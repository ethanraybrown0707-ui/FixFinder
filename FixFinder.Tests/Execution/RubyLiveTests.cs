using System.Diagnostics;
using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Checking.Guides;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Ruby programs checked whole, as the window checks them: read by ruby -wc before they run, run by ruby, their mistakes
/// found at their lines, Ruby's own "Did you mean?" made a change, every change checked in a copy - and every example a
/// Ruby guide shows, checked. What Ruby said is written to the test's output. A computer without Ruby passes over them.
/// </summary>
public class RubyLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string? Ruby()
    {
        if (TargetFactory.FindOnPath("ruby") is { } ruby)
        {
            output.WriteLine($"Checked with {ScriptLanguages.VersionOf(ScriptLanguages.Ruby)}: {ruby}");
            return ruby;
        }

        output.WriteLine("Not run on this computer: no Ruby is here.");
        return null;
    }

    private string Write(string relative, string code)
    {
        var path = Path.Combine(_temp.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, code);
        return path;
    }

    private async Task<CheckReport> CheckAsync(string file)
    {
        var launch = TargetFactory.FromFile(file, LiveAllowance.For(file, TimeSpan.FromMinutes(2)));
        Assert.True(launch.Ok, launch.Problem);

        using var http = new FixFinderHttpClient();
        var report = await new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Ruby }.CheckAsync(launch);

        foreach (var note in report.Notes) output.WriteLine($"note: {note}");
        foreach (var finding in report.Findings)
        {
            output.WriteLine($"{Path.GetFileName(finding.File)}:{finding.Line}: {finding.Kind} {finding.Severity} {finding.RuleId}: {finding.Title} | {finding.FixCheckedBy} | {finding.Verified.Summary}");
            if (finding.Error is { } error) output.WriteLine("  as Ruby said it:\n    " + error.RawText.Replace("\n", "\n    "));
        }

        foreach (var line in report.Run?.Run?.Lines ?? []) output.WriteLine($"  | {line.Text}");
        return report;
    }

    private async Task<string> RunAsync(string ruby, string code, string name)
    {
        var file = Write(Path.Combine("changed", name, "main.rb"), code);
        using var running = Process.Start(new ProcessStartInfo(ruby, "-w main.rb")
            { WorkingDirectory = Path.GetDirectoryName(file)!, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true })!;
        var printed = await running.StandardOutput.ReadToEndAsync();
        var said = await running.StandardError.ReadToEndAsync();
        await running.WaitForExitAsync();
        Assert.True(said.Trim().Length == 0, said);
        return printed.Trim().ReplaceLineEndings("\n");
    }

    private static string Changed(string code, Finding found)
    {
        var lines = code.TrimEnd('\n').Split('\n').ToList();
        lines.RemoveRange(found.Fix!.StartLine - 1, found.Fix.RemoveCount);
        lines.InsertRange(found.Fix.StartLine - 1, found.Fix.NewLines);
        return string.Join("\n", lines) + "\n";
    }

    [Fact]
    public async Task AMissingEndIsFoundBeforeTheProgramRuns()
    {
        if (Ruby() is null) return;

        var program = Write(Path.Combine("syntax", "main.rb"), "def total(marks)\n  marks.sum\n\nputs total([1, 2])\n");

        var report = await CheckAsync(program);

        var refused = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Syntax);
        Assert.Equal("SyntaxError", refused.Error?.ExceptionType);
        Assert.Contains("closed by its own end", refused.Explanation);
        Assert.Empty(report.Run?.Run?.Lines.Where(line => line.Text.Contains('3')) ?? []);
    }

    [Theory]
    [InlineData("total = 10\nputs totl\n", 2, "puts total")]
    [InlineData("name = \"ada\"\nputs name.upcse\n", 2, "puts name.upcase")]
    public async Task RubysOwnDidYouMeanIsMadeAndCheckedInACopy(string code, int line, string fixedLine)
    {
        if (Ruby() is not { } ruby) return;

        var program = Write(Path.Combine($"named{code.Length}", "main.rb"), code);
        var report = await CheckAsync(program);

        // The change is run in a copy - the language's own suggestion is taken through FixFinder's run of the program - so it
        // is verified by the program getting past the line, not only by ruby -c.
        var renamed = Assert.Single(report.Findings, finding => finding.RuleId == "ruby-did-you-mean");
        Assert.Equal(line, renamed.Line);
        Assert.Contains(fixedLine, renamed.CorrectedExample);
        Assert.True(renamed.CameFrom?.IsTheLanguagesOwn, renamed.CameFrom?.ToString());
        Assert.True(renamed.Verified.IsVerified, renamed.Verified.Summary);
        Assert.Equal(code.Contains("upcse") ? "ADA" : "10", await RunAsync(ruby, Changed(code, renamed), $"named{line}{code.Length}"));
    }

    [Theory]
    [InlineData("marks = []\ntotal = marks.sum\naverage = total / marks.length\nputs average\n", "ruby-division-guard", 3, "0")]
    [InlineData("age = 36\nputs \"Age: \" + age\n", "ruby-number-to-text", 2, "Age: 36")]
    public async Task ACrashGetsItsChangeAndTheChangedProgramRuns(string code, string rule, int line, string fixedPrints)
    {
        if (Ruby() is not { } ruby) return;

        var program = Write(Path.Combine(rule, "main.rb"), code);
        var report = await CheckAsync(program);

        var crash = Assert.Single(report.Findings, finding => finding.RuleId == rule);
        Assert.Equal((FindingKind.Runtime, line), (crash.Kind, crash.Line!.Value));
        Assert.True(crash.Verified.IsVerified, crash.Verified.Summary);
        Assert.Equal(fixedPrints, await RunAsync(ruby, Changed(code, crash), rule));
    }

    [Fact]
    public async Task WarningsAreRatedByWhatTheyMean()
    {
        if (Ruby() is null) return;

        var program = Write(Path.Combine("warnings", "main.rb"), "def check(mark)\n  unused = 5\n  if mark = 40\n    puts \"pass\"\n  end\nend\n\ncheck(70)\n");
        var report = await CheckAsync(program);

        var assigned = Assert.Single(report.Findings, finding => finding.Error?.Message?.Contains("in conditional") == true);
        Assert.Equal((FindingKind.Logic, Severity.Warning, 3), (assigned.Kind, assigned.Severity, assigned.Line!.Value));

        var unused = Assert.Single(report.Findings, finding => finding.Error?.Message?.StartsWith("assigned but unused") == true);
        Assert.Equal((FindingKind.Style, Severity.Suggestion, 2), (unused.Kind, unused.Severity, unused.Line!.Value));
    }

    [Theory]
    [InlineData("total = 3\ncount = 2\naverage = (total / count).to_f\nputs average\n", "logic-ruby-integer-average", 3, "1.0", "1.5")]
    [InlineData("sum = 0.1 + 0.2\nif sum == 0.3\n  puts \"equal\"\nelse\n  puts \"not equal\"\nend\n", "logic-ruby-float-equality", 2, "not equal", "equal")]
    [InlineData("name = \"ada\"\nname.upcase\nputs name\n", "logic-ruby-result-discarded", 2, "ada", "ADA")]
    public async Task ALogicMistakeRubySaysNothingOfIsFoundAndItsChangePrintsTheRightAnswer(string code, string check, int line, string printed, string fixedPrints)
    {
        if (Ruby() is not { } ruby) return;

        var program = Write(Path.Combine(check, "main.rb"), code);
        var report = await CheckAsync(program);

        Assert.DoesNotContain(report.Findings, finding => finding.Error?.ExceptionType == "compile warning");
        Assert.Equal(printed, string.Join("\n", report.Run?.Run?.Lines.Select(captured => captured.Text) ?? []).Trim());

        var found = Assert.Single(report.Findings, finding => finding.RuleId == check);
        Assert.Equal(line, found.Line);
        Assert.Equal(fixedPrints, await RunAsync(ruby, Changed(code, found), check));
    }

    [Fact]
    public async Task ACrashInAFileBroughtInWithRequireRelativeIsPlacedThere()
    {
        if (Ruby() is null) return;

        var helper = Write(Path.Combine("program", "average.rb"), "def average(marks)\n  marks.sum / marks.length\nend\n");
        var main = Write(Path.Combine("program", "main.rb"), "require_relative \"average\"\n\nputs average([])\n");

        var report = await CheckAsync(main);

        var crash = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Runtime);
        Assert.Equal((helper, 2), (crash.File, crash.Line!.Value));
        Assert.Equal("ruby-division-guard", crash.RuleId);
    }

    /// <summary>Every whole program a Ruby guide shows reads with ruby -wc, with no warning.</summary>
    [Fact]
    public async Task EveryExampleARubyGuideShowsChecksWithNoWarning()
    {
        if (Ruby() is not { } ruby) return;

        var examples = RubyGuides.All.Concat(RubyPatternGuides.All).Select(entry => entry.Guide.Example).Where(example => example.Length > 0).Distinct().ToList();
        var failed = new List<string>();

        foreach (var (example, number) in examples.Select((example, index) => (example, index + 1)))
        {
            var file = Write(Path.Combine("examples", $"example{number}.rb"), example + "\n");

            using var checking = Process.Start(new ProcessStartInfo(ruby, $"-wc \"{Path.GetFileName(file)}\"")
                { WorkingDirectory = Path.GetDirectoryName(file)!, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true })!;
            var said = await checking.StandardError.ReadToEndAsync() + await checking.StandardOutput.ReadToEndAsync();
            await checking.WaitForExitAsync();

            if (checking.ExitCode != 0 || said.Trim() != "Syntax OK") failed.Add($"example {number}:\n{example}\nruby said:\n{said}");
        }

        output.WriteLine($"{examples.Count} examples checked.");
        Assert.True(failed.Count == 0, string.Join("\n\n", failed));
    }
}
