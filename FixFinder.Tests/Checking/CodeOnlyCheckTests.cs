using FixFinder.Core;
using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>
/// Reading the code alone, as Check on save does after every save: it finds what the code shows, runs nothing and says
/// so, never passes quietly over code it could not read, and reuses what it found in functions that have not changed.
/// </summary>
public class CodeOnlyCheckTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string Average = """
        def average(values):
            count = 0
            for value in values:
                count += 1
            return sum(values) / count


        print("starting")
        print(average([]))
        """;

    /// <summary>The report, and how many lines the program printed while it was made - null when Python is not installed.</summary>
    private async Task<(CheckReport Report, int LinesPrinted)?> CheckCodeAsync(string code, AnalysisCache? cache = null)
    {
        if (PythonFrontend.FindInterpreter() is null) return null;

        var path = Path.Combine(_temp.Path, "average.py");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));

        using var http = new FixFinderHttpClient();
        var launch = TargetFactory.FromFile(path);
        Assert.True(launch.Ok, launch.Problem);

        var checker = new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Of(path) ?? CodeLanguage.Any, Cache = cache };
        var printed = 0;
        checker.LineCaptured += _ => Interlocked.Increment(ref printed);

        var report = await checker.CheckCodeAsync(launch);
        return (report, printed);
    }

    [Fact]
    public async Task TheCodeIsReadButNothingRuns()
    {
        if (await CheckCodeAsync(Average) is not { } result) return;

        Assert.Contains(result.Report.Findings, f => f.RuleId == "analysis-division-by-zero");
        Assert.Equal(0, result.LinesPrinted);
        Assert.Null(result.Report.Run);
        Assert.StartsWith("Not compiled or run", result.Report.SyntaxSummary);
    }

    /// <summary>Code that does not read as Python would otherwise be reported as having no mistakes in it.</summary>
    [Fact]
    public async Task CodeThatCannotBeReadIsNotedRatherThanPassedOver()
    {
        if (await CheckCodeAsync("def broken(:\n    return 1\n") is not { } result) return;

        Assert.Contains(result.Report.Notes, note => note.StartsWith("Part of the code could not be read", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SavingAgainWithoutAChangeReusesWhatWasFound()
    {
        var cache = new AnalysisCache();
        if (await CheckCodeAsync(Average, cache) is not { } first) return;

        var again = (await CheckCodeAsync(Average, cache))!.Value;

        Assert.Contains("unchanged since the last check", again.Report.LogicSummary);
        Assert.Equal(
            first.Report.Findings.Select(f => (f.RuleId, f.Line, f.Explanation)),
            again.Report.Findings.Select(f => (f.RuleId, f.Line, f.Explanation)));
    }
}
