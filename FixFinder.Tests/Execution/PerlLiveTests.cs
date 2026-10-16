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
/// Perl programs checked whole, as the window checks them: compiled by perl -wc before they run, run by perl, their
/// compilation errors, what stopped them and every warning Perl carried on past found at their lines - in a module the
/// program uses too - with changes checked in copies, and every example a Perl guide shows compiled and run. A computer
/// without Perl passes over them.
/// </summary>
public class PerlLiveTests(ITestOutputHelper output) : IDisposable
{
    private const string Strict = "use strict;\nuse warnings;\n\n";

    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string? Perl()
    {
        if (TargetFactory.FindOnPath("perl") is { } perl)
        {
            output.WriteLine($"Checked with {ScriptLanguages.VersionOf(ScriptLanguages.Perl)}: {perl}");
            return perl;
        }

        output.WriteLine("Not run on this computer: no Perl is here.");
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
        var report = await new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Perl }.CheckAsync(launch);

        foreach (var note in report.Notes) output.WriteLine($"note: {note}");
        foreach (var finding in report.Findings)
        {
            output.WriteLine($"{Path.GetFileName(finding.File)}:{finding.Line}: {finding.Kind} {finding.Severity} {finding.RuleId}: {finding.Title} | {finding.Verified.Summary}");
            if (finding.Fix is { } fix) output.WriteLine($"  change line {fix.StartLine} to: {string.Join(" / ", fix.NewLines)}");
            if (finding.Error is { } error) output.WriteLine("  as Perl said it:\n    " + error.RawText.Replace("\n", "\n    "));
        }

        foreach (var line in report.Run?.Run?.Lines ?? []) output.WriteLine($"  | {line.Text}");
        return report;
    }

    /// <summary>Runs Perl code here, typing <paramref name="input"/>, and gives back what it printed, failing on anything Perl said.</summary>
    private async Task<string> RunAsync(string perl, string code, string name, string input = "")
    {
        var file = Write(Path.Combine("changed", name, "main.pl"), code);
        using var running = Process.Start(new ProcessStartInfo(perl, "main.pl")
        {
            WorkingDirectory = Path.GetDirectoryName(file)!, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        })!;
        await running.StandardInput.WriteAsync(input);
        running.StandardInput.Close();
        var printed = await running.StandardOutput.ReadToEndAsync();
        var said = await running.StandardError.ReadToEndAsync();
        await running.WaitForExitAsync();

        Assert.True(running.ExitCode == 0 && said.Length == 0, $"{name}: perl said:\n{said}");
        return printed.Trim().ReplaceLineEndings("\n");
    }

    private static string Changed(string code, Finding found)
    {
        var lines = code.TrimEnd('\n').Split('\n').ToList();
        lines.RemoveRange(found.Fix!.StartLine - 1, found.Fix.RemoveCount);
        lines.InsertRange(found.Fix.StartLine - 1, found.Fix.NewLines);
        return string.Join("\n", lines) + "\n";
    }

    [Theory]
    [InlineData(Strict + "my $total = 10\nprint \"$total\\n\";\n", "perl-missing-semicolon", 5, 4, "my $total = 10;", "10")]
    [InlineData(Strict + "my $mark = 50;\nif $mark > 40 {\n    print \"pass\\n\";\n}\n", "perl-condition-in-brackets", 5, 5, "if ($mark > 40) {", "pass")]
    [InlineData(Strict + "my $total = 10;\nprint $totl + 1, \"\\n\";\n", "perl-undeclared-variable", 5, 5, "print $total + 1, \"\\n\";", "11")]
    [InlineData(Strict + "$total = 10;\nprint \"$total\\n\";\n", "perl-undeclared-variable", 4, 4, "my $total = 10;", "10")]
    [InlineData(Strict + "my $mark = 50;\nif ($mark > 70) {\n    print \"high\\n\";\n} elseif ($mark > 40) {\n    print \"pass\\n\";\n}\n", "perl-elsif", 7, 7, "} elsif ($mark > 40) {", "pass")]
    public async Task ACompilationErrorIsFoundBeforeItRunsAndItsChangeCompilesAndRuns(string code, string rule, int line, int changedLine, string changedTo, string fixedPrints)
    {
        if (Perl() is not { } perl) return;

        var report = await CheckAsync(Write(Path.Combine($"compile{code.Length}", "main.pl"), code));

        var found = Assert.Single(report.Findings, finding => finding.RuleId == rule);
        Assert.Equal((FindingKind.Syntax, line), (found.Kind, found.Line!.Value));
        Assert.Equal(changedLine, found.Fix?.StartLine);
        Assert.Equal(changedTo, found.Fix?.NewLines.Single());
        Assert.Contains("checked it with perl -c", found.FixCheckedBy);
        Assert.Equal(fixedPrints, await RunAsync(perl, Changed(code, found), $"compile{code.Length}"));
    }

    [Theory]
    [InlineData(Strict + "my @marks = ();\nmy $count = scalar(@marks);\nprint 100 / $count, \"\\n\";\n", "perl-division-guard", 6, "print $count == 0 ? 0 : 100 / $count, \"\\n\";", "0")]
    [InlineData(Strict + "sub average {\n    my @marks = @_;\n    return scalar(@marks);\n}\nprint averge(70, 80), \"\\n\";\n", "perl-nearest-sub", 8, "print average(70, 80), \"\\n\";", "2")]
    public async Task WhatStoppedTheProgramGetsItsChangeAndTheChangedProgramRunsClean(string code, string rule, int line, string changedTo, string? fixedPrints)
    {
        if (Perl() is not { } perl) return;

        var report = await CheckAsync(Write(Path.Combine($"stopped{code.Length}", "main.pl"), code));

        var found = Assert.Single(report.Findings, finding => finding.RuleId == rule);
        Assert.Equal((FindingKind.Runtime, Severity.Error, line), (found.Kind, found.Severity, found.Line!.Value));
        Assert.Equal(changedTo, found.Fix?.NewLines.Single());
        Assert.True(found.Verified.IsVerified, found.Verified.Summary);
        var printed = await RunAsync(perl, Changed(code, found), $"stopped{code.Length}");
        if (fixedPrints is not null) Assert.Equal(fixedPrints, printed);
    }

    [Theory]
    [InlineData(Strict + "my $mark = 70;\nif ($mark = 40) {\n    print \"forty\\n\";\n}\nprint \"$mark\\n\";\n", "perl-compare-in-condition", 5, "if ($mark == 40) {", "70")]
    [InlineData(Strict + "my $answer = \"yes\";\nif ($answer == \"no\") {\n    print \"no\\n\";\n} else {\n    print \"yes\\n\";\n}\n", "logic-perl-text-compared-as-numbers", 5, "if ($answer eq \"no\") {", "yes")]
    [InlineData(Strict + "my $name = \"bob\";\nif ($name < \"carol\") {\n    print \"before\\n\";\n} else {\n    print \"after\\n\";\n}\n", "perl-text-comparison", 5, "if ($name lt \"carol\") {", "before")]
    [InlineData(Strict + "my $total = 12;\nprint \"Total: \" + $total, \"\\n\";\n", "perl-join-text", 5, "print \"Total: \" . $total, \"\\n\";", "Total: 12")]
    [InlineData(Strict + "my @marks = (70, 80, 90, 65, 72, 88, 91, 55, 60, 77, 82, 95);\nprint length(@marks), \"\\n\";\n", "logic-perl-length-of-array", 5, "print scalar(@marks), \"\\n\";", "12")]
    public async Task AWarningPerlCarriedOnPastGetsItsChangeAndTheChangedProgramRunsClean(string code, string rule, int line, string changedTo, string fixedPrints)
    {
        if (Perl() is not { } perl) return;

        var report = await CheckAsync(Write(Path.Combine($"warned{code.Length}", "main.pl"), code));

        var found = Assert.Single(report.Findings, finding => finding.Fix?.NewLines.SequenceEqual([changedTo]) == true);
        Assert.Equal(line, found.Line);
        Assert.Equal(rule, found.RuleId);
        Assert.Equal(fixedPrints, await RunAsync(perl, Changed(code, found), $"warned{code.Length}"));
    }

    [Fact]
    public async Task EveryWarningARunPrintsIsAFindingOfItsOwnOnce()
    {
        if (Perl() is null) return;

        var report = await CheckAsync(Write(Path.Combine("warnings", "main.pl"),
            Strict + "my %ages = (Ada => 36);\nfor my $round (1 .. 3) {\n    print $ages{Bob} + 1, \"\\n\";\n}\nmy $typed = \"twenty\";\nprint $typed * 2, \"\\n\";\n"));

        var warned = report.Findings.Where(finding => finding.Error?.ExceptionType == "warning").OrderBy(finding => finding.Line).ToList();
        Assert.Equal(new int?[] { 6, 9 }, warned.Select(finding => finding.Line));
        Assert.All(warned, finding => Assert.StartsWith("It printed a warning and carried on: ", finding.Title, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(Strict + "my @marks = (70, 80, 90);\nfor (my $i = 0; $i <= scalar(@marks); $i++) {\n    print $marks[$i], \"\\n\";\n}\n", "logic-perl-loop-to-count", 5, "", "70\n80\n90")]
    [InlineData(Strict + "print \"Type 5: \";\nmy $typed = <STDIN>;\nif ($typed eq \"5\") {\n    print \"five\\n\";\n} else {\n    print \"not five\\n\";\n}\n", "logic-perl-line-not-chomped", 5, "5\n", "Type 5: five")]
    [InlineData(Strict + "my $total = 0.1 + 0.2;\nif ($total == 0.3) {\n    print \"equal\\n\";\n} else {\n    print \"not equal\\n\";\n}\n", "logic-perl-float-equality", 5, "", "equal")]
    public async Task ALogicMistakeIsFoundAndItsChangePrintsTheRightAnswer(string code, string check, int line, string input, string fixedPrints)
    {
        if (Perl() is not { } perl) return;

        var report = await CheckAsync(Write(Path.Combine($"logic{code.Length}", "main.pl"), code));

        var found = Assert.Single(report.Findings, finding => finding.RuleId == check);
        Assert.Equal(line, found.Line);
        Assert.Equal(fixedPrints, await RunAsync(perl, Changed(code, found), $"logic{code.Length}", input));
    }

    [Fact]
    public async Task TextComparedAsNumbersIsOneFindingThoughPerlWarnedOfItTwice()
    {
        if (Perl() is null) return;

        var report = await CheckAsync(Write(Path.Combine("compared", "main.pl"),
            Strict + "my $answer = \"yes\";\nif ($answer == \"no\") {\n    print \"no\\n\";\n}\n"));

        var comparison = Assert.Single(report.Findings, finding => finding.Fix?.NewLines.SequenceEqual(["if ($answer eq \"no\") {"]) == true);
        Assert.Equal(5, comparison.Line);
    }

    [Fact]
    public async Task WhatStoppedTheProgramInAModuleItUsesIsPlacedThere()
    {
        if (Perl() is null) return;

        var module = Write(Path.Combine("program", "lib", "Marks.pm"),
            "package Marks;\nuse strict;\nuse warnings;\n\nsub average {\n    my @marks = @_;\n    return 100 / scalar(@marks);\n}\n\n1;\n");
        var main = Write(Path.Combine("program", "main.pl"), Strict + "use lib 'lib';\nuse Marks;\n\nprint Marks::average(), \"\\n\";\n");

        var report = await CheckAsync(main);

        var crash = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Runtime && finding.Severity == Severity.Error);
        Assert.Equal((module, 7), (crash.File, crash.Line!.Value));
        Assert.Equal("perl-division-guard", crash.RuleId);
    }

    [Fact]
    public async Task ACompilationErrorInAModuleItUsesIsFoundThereBeforeItRuns()
    {
        if (Perl() is null) return;

        var module = Write(Path.Combine("compileprogram", "lib", "Marks.pm"),
            "package Marks;\nuse strict;\nuse warnings;\n\nsub total {\n    my $sum = 0\n    $sum += $_ for @_;\n    return $sum;\n}\n\n1;\n");
        var main = Write(Path.Combine("compileprogram", "main.pl"), Strict + "use lib 'lib';\nuse Marks;\n\nprint Marks::total(70, 80), \"\\n\";\n");

        var report = await CheckAsync(main);

        var syntax = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Syntax && finding.RuleId == "perl-missing-semicolon");
        Assert.Equal((module, 7), (syntax.File, syntax.Line!.Value));
        Assert.Equal(6, syntax.Fix?.StartLine);
    }

    /// <summary>Every whole program a Perl guide shows compiles with perl -wc, warning of nothing, and runs with nothing from Perl but its output.</summary>
    [Fact]
    public async Task EveryExampleAPerlGuideShowsCompilesAndRunsClean()
    {
        if (Perl() is not { } perl) return;

        var examples = PerlGuides.All.Concat(PerlPatternGuides.All).Select(entry => entry.Guide.Example).Where(example => example.Length > 0).Distinct().ToList();

        foreach (var (example, number) in examples.Select((example, index) => (example, index + 1)))
        {
            var file = Write(Path.Combine("examples", $"example{number}.pl"), example + "\n");

            using var compiling = Process.Start(new ProcessStartInfo(perl, $"-wc \"{Path.GetFileName(file)}\"")
                { WorkingDirectory = Path.GetDirectoryName(file)!, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true })!;
            var said = (await compiling.StandardOutput.ReadToEndAsync() + await compiling.StandardError.ReadToEndAsync()).Trim();
            await compiling.WaitForExitAsync();
            Assert.True(compiling.ExitCode == 0 && said == $"{Path.GetFileName(file)} syntax OK", $"example {number}:\n{example}\nperl -wc said:\n{said}");

            await RunAsync(perl, example + "\n", $"example{number}");
        }

        output.WriteLine($"{examples.Count} examples compiled and run.");
    }
}
