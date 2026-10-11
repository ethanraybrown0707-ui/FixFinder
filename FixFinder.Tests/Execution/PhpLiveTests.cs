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
/// PHP programs checked whole, as the window checks them: read by php -l before they run, run by php, their parse errors,
/// fatal errors and every warning PHP carried on past found at their lines, with changes checked in copies - and every
/// example a PHP guide shows, linted and run. A computer without PHP passes over them.
/// </summary>
public class PhpLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string? Php()
    {
        if (TargetFactory.FindOnPath("php") is { } php)
        {
            output.WriteLine($"Checked with {ScriptLanguages.VersionOf(ScriptLanguages.Php)}: {php}");
            return php;
        }

        output.WriteLine("Not run on this computer: no PHP is here.");
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
        var report = await new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Php }.CheckAsync(launch);

        foreach (var note in report.Notes) output.WriteLine($"note: {note}");
        foreach (var finding in report.Findings)
        {
            output.WriteLine($"{Path.GetFileName(finding.File)}:{finding.Line}: {finding.Kind} {finding.Severity} {finding.RuleId}: {finding.Title} | {finding.Verified.Summary}");
            if (finding.Error is { } error) output.WriteLine("  as PHP said it:\n    " + error.RawText.Replace("\n", "\n    "));
        }

        foreach (var line in report.Run?.Run?.Lines ?? []) output.WriteLine($"  | {line.Text}");
        return report;
    }

    /// <summary>Runs PHP code here and gives back what it printed, failing on any warning or error it printed.</summary>
    private async Task<string> RunAsync(string php, string code, string name)
    {
        var file = Write(Path.Combine("changed", name, "main.php"), code);
        using var running = Process.Start(new ProcessStartInfo(php, "main.php")
            { WorkingDirectory = Path.GetDirectoryName(file)!, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true })!;
        var printed = await running.StandardOutput.ReadToEndAsync() + await running.StandardError.ReadToEndAsync();
        await running.WaitForExitAsync();

        Assert.DoesNotContain("Warning:", printed);
        Assert.DoesNotContain("error:", printed);
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
    public async Task AMissingSemicolonIsFoundBeforeItRunsAndPutAtTheEndOfTheLineBefore()
    {
        if (Php() is not { } php) return;

        const string code = "<?php\n$total = 10\necho $total;\n";
        var report = await CheckAsync(Write(Path.Combine("semicolon", "main.php"), code));

        var fixedLine = Assert.Single(report.Findings, finding => finding.RuleId == "php-missing-semicolon");
        Assert.Equal((FindingKind.Syntax, 3), (fixedLine.Kind, fixedLine.Line!.Value));
        Assert.Equal(2, fixedLine.Fix?.StartLine);
        Assert.Equal("$total = 10;", fixedLine.Fix?.NewLines.Single());
        Assert.Contains("checked it with php -l", fixedLine.FixCheckedBy);
        Assert.Equal("10", await RunAsync(php, Changed(code, fixedLine), "semicolon"));
    }

    [Theory]
    [InlineData("<?php\n$total = 10;\necho $totl + 1;\n", "php-nearest-variable", 3, "1", "11")]
    [InlineData("<?php\n$marks = [];\n$average = array_sum($marks) / count($marks);\necho $average;\n", "php-division-guard", 3, null, "0")]
    public async Task AWarningOrAFatalErrorGetsItsChangeAndTheChangedProgramRunsClean(string code, string rule, int line, string? printed, string fixedPrints)
    {
        if (Php() is not { } php) return;

        var report = await CheckAsync(Write(Path.Combine(rule, "main.php"), code));

        var found = Assert.Single(report.Findings, finding => finding.RuleId == rule);
        Assert.Equal((FindingKind.Runtime, line), (found.Kind, found.Line!.Value));
        if (printed is not null) Assert.Equal(printed, report.Run?.Run?.Lines.Where(captured => captured.Stream == StreamKind.StdOut).Select(captured => captured.Text).LastOrDefault()?.Trim());
        Assert.True(found.Verified.IsVerified, found.Verified.Summary);
        Assert.Equal(fixedPrints, await RunAsync(php, Changed(code, found), rule));
    }

    [Fact]
    public async Task EveryWarningARunPrintsIsAFindingOfItsOwn()
    {
        if (Php() is null) return;

        var report = await CheckAsync(Write(Path.Combine("warnings", "main.php"),
            "<?php\n$ages = [\"Ada\" => 36];\necho $ages[\"Bob\"];\necho $ages[\"Cy\"];\necho \"done\";\n"));

        var warned = report.Findings.Where(finding => finding.Error?.ExceptionType == "Warning").OrderBy(finding => finding.Line).ToList();
        Assert.Equal(new int?[] { 3, 4 }, warned.Select(finding => finding.Line));
        Assert.All(warned, finding => Assert.Equal((FindingKind.Runtime, Severity.Warning), (finding.Kind, finding.Severity)));
        Assert.Equal(["It printed a warning and carried on: Undefined array key \"Bob\"", "It printed a warning and carried on: Undefined array key \"Cy\""],
            warned.Select(finding => finding.Title));
    }

    /// <summary>
    /// A loop to &lt;= count() is found twice - by the logic check, and by the warning its run prints - with the same change,
    /// so it is one finding, and the changed program reads every mark with no warning.
    /// </summary>
    [Fact]
    public async Task ALoopPastTheEndIsOneFindingThoughItsRunWarnedOfIt()
    {
        if (Php() is not { } php) return;

        const string code = "<?php\n$marks = [70, 80, 90];\n$total = 0;\nfor ($i = 0; $i <= count($marks); $i++) {\n    $total += $marks[$i];\n}\necho $total;\n";
        var report = await CheckAsync(Write(Path.Combine("loop-warned", "main.php"), code));

        Assert.Contains(report.Run?.Run?.Lines ?? [], captured => captured.Text.StartsWith("Warning: Undefined array key 3", StringComparison.Ordinal));
        var loop = Assert.Single(report.Findings);
        Assert.Equal(4, loop.Fix?.StartLine);
        Assert.Equal("for ($i = 0; $i < count($marks); $i++) {", loop.Fix?.NewLines.Single());
        Assert.Equal("240", await RunAsync(php, Changed(code, loop), "loop-warned"));
    }

    [Theory]
    [InlineData("<?php\n$marks = [70, 80, 90];\nfor ($i = 0; $i <= count($marks); $i++) {\n    echo $marks[$i] . \"\\n\";\n}\n", 3, "70\n80\n90")]
    [InlineData("<?php\n$mark = 70;\nif ($mark = 40) {\n    echo \"forty\\n\";\n}\necho $mark;\n", 3, "70")]
    [InlineData("<?php\n$total = 0.1 + 0.2;\nif ($total == 0.3) {\n    echo \"equal\";\n} else {\n    echo \"not equal\";\n}\n", 3, "equal")]
    public async Task ALogicMistakeIsFoundAndItsChangePrintsTheRightAnswer(string code, int line, string fixedPrints)
    {
        if (Php() is not { } php) return;

        var report = await CheckAsync(Write(Path.Combine($"logic{line}{code.Length}", "main.php"), code));

        var found = Assert.Single(report.Findings, finding => finding.RuleId.StartsWith("logic-php-", StringComparison.Ordinal) && finding.Fix?.StartLine == line);
        Assert.Equal(fixedPrints, await RunAsync(php, Changed(code, found), $"logic{line}{code.Length}"));
    }

    [Fact]
    public async Task AFatalErrorInAnIncludedFileIsPlacedThere()
    {
        if (Php() is null) return;

        var helper = Write(Path.Combine("program", "average.php"), "<?php\nfunction average($marks) {\n    return array_sum($marks) / count($marks);\n}\n");
        var main = Write(Path.Combine("program", "main.php"), "<?php\nrequire_once __DIR__ . '/average.php';\necho average([]);\n");

        var report = await CheckAsync(main);

        var crash = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Runtime && finding.Severity == Severity.Error);
        Assert.Equal((helper, 3), (crash.File, crash.Line!.Value));
        Assert.Equal("php-division-guard", crash.RuleId);
    }

    [Fact]
    public async Task AParseErrorInARequiredFileIsFoundThereBeforeItRuns()
    {
        if (Php() is null) return;

        var helper = Write(Path.Combine("required", "settings.php"), "<?php\n$passMark = 40\necho $passMark;\n");
        var main = Write(Path.Combine("required", "main.php"), "<?php\nrequire_once __DIR__ . '/settings.php';\necho \"done\";\n");

        var report = await CheckAsync(main);

        var syntax = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Syntax);
        Assert.Equal((helper, 3), (syntax.File, syntax.Line!.Value));
        Assert.Equal(("php-missing-semicolon", 2), (syntax.RuleId, syntax.Fix?.StartLine));
    }

    /// <summary>Every whole program a PHP guide shows reads with php -l and runs with nothing from PHP but its output.</summary>
    [Fact]
    public async Task EveryExampleAPhpGuideShowsLintsAndRunsClean()
    {
        if (Php() is not { } php) return;

        var examples = PhpGuides.All.Concat(PhpPatternGuides.All).Select(entry => entry.Guide.Example).Where(example => example.Length > 0).Distinct().ToList();

        foreach (var (example, number) in examples.Select((example, index) => (example, index + 1)))
        {
            var file = Write(Path.Combine("examples", $"example{number}.php"), example + "\n");

            using var linting = Process.Start(new ProcessStartInfo(php, $"-l \"{Path.GetFileName(file)}\"")
                { WorkingDirectory = Path.GetDirectoryName(file)!, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true })!;
            var said = await linting.StandardOutput.ReadToEndAsync() + await linting.StandardError.ReadToEndAsync();
            await linting.WaitForExitAsync();
            Assert.True(linting.ExitCode == 0, $"example {number}:\n{example}\nphp -l said:\n{said}");

            await RunAsync(php, example + "\n", $"example{number}");
        }

        output.WriteLine($"{examples.Count} examples linted and run.");
    }
}
