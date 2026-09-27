using System.Text.Json;
using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.Engine;
using FixFinder.Core.Analysis.Frontends;

namespace FixFinder.Tests;

/// <summary>
/// FixFinder's findings as the lines an editor reads. What matters is that an editor really does read them, so the
/// MSBuild lines are held to the pattern VS Code itself uses, copied from its source rather than written from memory.
/// </summary>
/// <remarks>
/// In the collection that runs on its own, because a check from the command line applies the language versions saved
/// in the preferences, which every other test shares - and somebody's saved Java 8 must never reach a Java test running
/// alongside.
/// </remarks>
[Collection(SharedLanguageStandards.Name)]
public class CommandLineTests : IDisposable
{
    /// <summary>
    /// VS Code's built-in $msCompile pattern, verbatim from src/vs/workbench/contrib/tasks/common/problemMatcher.ts in
    /// microsoft/vscode (fetched 2026-09-25). Groups: 1 file, 2 location, 4 severity, 5 code, 6 message. Visual Studio
    /// and Rider read the same MSBuild format.
    /// </summary>
    private static readonly Regex MsCompile = new(
        @"^\s*(?:\s*\d+>)?(\S.*?)(?:\((\d+|\d+,\d+|\d+,\d+,\d+,\d+)\))?\s*:\s+(?:(\S+)\s+)?((?:fatal +)?error|warning|info)\s+(\w+\d+)?\s*:\s*(.*)$");

    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private static Finding Found(Severity severity, string file = @"C:\work\marks.py", string title = "Dividing by something that can be zero",
        string explanation = "`len(marks)` can still be 0 here") => new()
    {
        Kind = FindingKind.Runtime,
        Severity = severity,
        Confidence = Confidence.Possible,
        File = file,
        Line = 12,
        Title = title,
        Explanation = explanation,
        WhyItMatters = "why",
        SuggestedFix = "fix",
        CorrectedExample = "",
        RuleId = "analysis-division-by-zero",
    };

    [Theory]
    [InlineData(Severity.Error, "error")]
    [InlineData(Severity.Warning, "warning")]
    [InlineData(Severity.Suggestion, "info")]
    public void VsCodeReadsEveryFindingAsTheProblemItIs(Severity severity, string expected)
    {
        var line = DiagnosticLines.Line(Found(severity), DiagnosticFormat.MsBuild);
        var read = MsCompile.Match(line);

        Assert.True(read.Success, $"VS Code's $msCompile does not read: {line}");
        Assert.Equal(@"C:\work\marks.py", read.Groups[1].Value);
        Assert.Equal("12", read.Groups[2].Value);
        Assert.Equal(expected, read.Groups[4].Value);
        Assert.Equal(DiagnosticLines.Code("analysis-division-by-zero"), read.Groups[5].Value);
        Assert.Contains("Dividing by something that can be zero", read.Groups[6].Value, StringComparison.Ordinal);
        Assert.Contains("[analysis-division-by-zero]", read.Groups[6].Value, StringComparison.Ordinal);
    }

    /// <summary>OneDrive folders and "My Documents" have spaces in them, and the file still has to be found.</summary>
    [Fact]
    public void APathWithSpacesInItIsStillReadAsTheWholePath()
    {
        var file = @"C:\Users\student\OneDrive\My Coursework\week 3\marks.py";
        var read = MsCompile.Match(DiagnosticLines.Line(Found(Severity.Error, file), DiagnosticFormat.MsBuild));

        Assert.True(read.Success);
        Assert.Equal(file, read.Groups[1].Value);
    }

    /// <summary>An editor reads one line at a time, so an explanation over several lines would be cut at the first break.</summary>
    [Fact]
    public void AnExplanationOverSeveralLinesBecomesOne()
    {
        var line = DiagnosticLines.Line(Found(Severity.Error, explanation: "First part.\nSecond part.\r\nThird part."), DiagnosticFormat.MsBuild);

        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
        Assert.Contains("Third part", MsCompile.Match(line).Groups[6].Value, StringComparison.Ordinal);
    }

    [Fact]
    public void ATitleThatAlreadyEndsASentenceIsNotGivenASecondFullStop()
    {
        var line = DiagnosticLines.Line(Found(Severity.Error, title: "Something went wrong."), DiagnosticFormat.MsBuild);

        Assert.DoesNotContain("..", line, StringComparison.Ordinal);
    }

    /// <summary>The code is worked out from the rule's name, so a rule keeps its code from one run and one version to the next.</summary>
    [Fact]
    public void ARuleHasTheSameCodeEveryTime()
    {
        var codes = Enumerable.Range(0, 5).Select(_ => DiagnosticLines.Code("analysis-null-used")).Distinct().ToList();

        Assert.Single(codes);
        Assert.Matches(@"^\w+\d+$", codes[0]);
        Assert.NotEqual(DiagnosticLines.Code("analysis-null-used"), DiagnosticLines.Code("analysis-division-by-zero"));
    }

    [Theory]
    [InlineData(Severity.Error, "error")]
    [InlineData(Severity.Warning, "warning")]
    [InlineData(Severity.Suggestion, "note")]
    public void TheGccFormIsFileLineColumnSeverityMessage(Severity severity, string expected)
    {
        var line = DiagnosticLines.Line(Found(severity), DiagnosticFormat.Gcc);
        var read = Regex.Match(line, @"^(?<file>.+):(?<line>\d+):(?<column>\d+): (?<severity>error|warning|note): (?<message>.+)$");

        Assert.True(read.Success, line);
        Assert.Equal(@"C:\work\marks.py", read.Groups["file"].Value);
        Assert.Equal("12", read.Groups["line"].Value);
        Assert.Equal(expected, read.Groups["severity"].Value);
    }

    [Fact]
    public void TheJsonCarriesEverythingAFindingSays()
    {
        using var read = JsonDocument.Parse(DiagnosticLines.Json([Found(Severity.Warning)]));
        var finding = read.RootElement[0];

        Assert.Equal(@"C:\work\marks.py", finding.GetProperty("file").GetString());
        Assert.Equal(12, finding.GetProperty("line").GetInt32());
        Assert.Equal("warning", finding.GetProperty("severity").GetString());
        Assert.Equal("analysis-division-by-zero", finding.GetProperty("rule").GetString());
        Assert.Equal("Not tested", finding.GetProperty("verified").GetString());
    }

    [Theory]
    [InlineData]
    [InlineData("--help")]
    public async Task AskingForHelpPrintsHowToUseIt(params string[] args)
    {
        var output = new StringWriter();
        var errors = new StringWriter();

        var code = await CommandLine.RunAsync(args, output, errors);

        Assert.Equal(CommandLine.CouldNotCheck, code);
        Assert.Contains("usage: fixfinder", errors.ToString(), StringComparison.Ordinal);
        Assert.Equal("", output.ToString());
    }

    /// <summary>Anything wrong with the command line is said plainly, and never printed where an editor reads problems.</summary>
    [Theory]
    [InlineData("marks.py", "--format", "xml")]
    [InlineData("marks.py", "--level", "expert")]
    [InlineData("marks.py", "--language", "cobol")]
    [InlineData("marks.py", "--format")]
    [InlineData("one.py", "two.py")]
    [InlineData("marks.py", "--colour", "blue")]
    public async Task AMistakenCommandLineIsExplainedNotChecked(params string[] args)
    {
        var output = new StringWriter();
        var errors = new StringWriter();

        var code = await CommandLine.RunAsync(args, output, errors);

        Assert.Equal(CommandLine.CouldNotCheck, code);
        Assert.StartsWith("fixfinder: ", errors.ToString(), StringComparison.Ordinal);
        Assert.Equal("", output.ToString());
    }

    /// <summary>
    /// A check applies the saved language versions to the setting everything shares, and has to put it back: anything
    /// running it in-process would otherwise be left compiling under whatever the preferences happened to say.
    /// </summary>
    [Fact]
    public async Task ACheckPutsTheSharedLanguageVersionsBackAsItFoundThem()
    {
        var before = Core.Execution.LanguageStandards.Current;
        var distinctive = new Core.Execution.LanguageStandards { C = "c89", Cpp = "c++11", Java = "11" };

        try
        {
            Core.Execution.LanguageStandards.Current = distinctive;

            await CommandLine.RunAsync([Path.Combine(_temp.Path, "not-there.py")], new StringWriter(), new StringWriter());

            Assert.Equal(distinctive, Core.Execution.LanguageStandards.Current);
        }
        finally
        {
            Core.Execution.LanguageStandards.Current = before;
        }
    }

    [Fact]
    public async Task AProgramThatIsNotThereIsSaidToBeUncheckable()
    {
        var errors = new StringWriter();

        var code = await CommandLine.RunAsync([Path.Combine(_temp.Path, "not-there.py")], new StringWriter(), errors);

        Assert.Equal(CommandLine.CouldNotCheck, code);
        Assert.Contains("There is no file", errors.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The whole thing, for real: a program checked, its mistake printed as a line VS Code reads, and the exit code a
    /// build step can stop on. Only findings reach standard output; what is said about the run goes to standard error.
    /// </summary>
    [Fact]
    public async Task ARealProgramsMistakeArrivesAsALineVsCodeReads()
    {
        if (PythonFrontend.FindInterpreter() is null) return;

        var file = Path.Combine(_temp.Path, "share.py");
        await File.WriteAllTextAsync(file, "def share(prize, winners):\n    return prize / winners\n\nprint(share(120, 0))\n");

        var output = new StringWriter();
        var errors = new StringWriter();

        var code = await CommandLine.RunAsync([file], output, errors);

        Assert.Equal(CommandLine.FoundErrors, code);

        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();
        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.True(MsCompile.IsMatch(line), $"not a line VS Code reads: {line}"));
        Assert.Contains(lines, line => MsCompile.Match(line).Groups[4].Value == "error");

        Assert.Contains("syntax:", errors.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AProgramWithNothingWrongExitsCleanly()
    {
        if (PythonFrontend.FindInterpreter() is null) return;

        var file = Path.Combine(_temp.Path, "fine.py");
        await File.WriteAllTextAsync(file, "print('hello')\n");

        var output = new StringWriter();

        var code = await CommandLine.RunAsync([file], output, new StringWriter());

        Assert.Equal(CommandLine.NothingWrong, code);
        Assert.Equal("", output.ToString().Trim());
    }
}
