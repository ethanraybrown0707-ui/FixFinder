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
/// PowerShell scripts checked whole, as the window checks them: read by PowerShell's own parser before they run, run, and
/// every error a run prints found at its line - in a script it dot-sources too - with changes checked in copies, and every
/// example a PowerShell guide shows parsed and run. A computer without Windows PowerShell passes over them.
/// </summary>
[Collection(BuildsRunAlone.Name)]
public class PowerShellLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string? PowerShell()
    {
        if (TargetFactory.FindOnPath("powershell") is { } powershell)
        {
            output.WriteLine($"Checked with PowerShell {ScriptLanguages.VersionOf(ScriptLanguages.PowerShell)}: {powershell}");
            return powershell;
        }

        output.WriteLine("Not run on this computer: no Windows PowerShell is here.");
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
        var report = await new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.PowerShell }.CheckAsync(launch);

        foreach (var note in report.Notes) output.WriteLine($"note: {note}");
        foreach (var finding in report.Findings)
        {
            output.WriteLine($"{Path.GetFileName(finding.File)}:{finding.Line}: {finding.Kind} {finding.Severity} {finding.RuleId}: {finding.Title} | {finding.Verified.Summary}");
            if (finding.Fix is { } fix) output.WriteLine($"  change line {fix.StartLine} to: {string.Join(" / ", fix.NewLines)}");
            if (finding.Error is { } error) output.WriteLine("  as PowerShell said it:\n    " + error.RawText.Replace("\n", "\n    "));
        }

        foreach (var line in report.Run?.Run?.Lines ?? []) output.WriteLine($"  | {line.Text}");
        return report;
    }

    /// <summary>Runs a script here and gives back what it printed, failing on any error PowerShell printed.</summary>
    private async Task<string> RunAsync(string powershell, string code, string name)
    {
        var file = Write(Path.Combine("changed", name, "main.ps1"), code);
        using var running = Process.Start(new ProcessStartInfo(powershell, "-NoProfile -ExecutionPolicy Bypass -File main.ps1")
            { WorkingDirectory = Path.GetDirectoryName(file)!, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true })!;
        var printed = await running.StandardOutput.ReadToEndAsync();
        var said = await running.StandardError.ReadToEndAsync();
        await running.WaitForExitAsync();

        Assert.True(running.ExitCode == 0 && said.Trim().Length == 0, $"{name}: PowerShell said:\n{said}");
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
    [InlineData("$mark = 50\nif ($mark -gt 70) {\n    Write-Output \"high\"\n} else if ($mark -gt 40) {\n    Write-Output \"pass\"\n}\n", "powershell-elseif", 4, "} elseif ($mark -gt 40) {", "pass")]
    [InlineData("$mark = 30\nif ($mark < 40) {\n    Write-Output \"fail\"\n}\n", "powershell-comparison-words", 2, "if ($mark -lt 40) {", "fail")]
    [InlineData("$mark = 50\nif ($mark != 40) {\n    Write-Output \"not forty\"\n}\n", "powershell-comparison-words", 2, "if ($mark -ne 40) {", "not forty")]
    [InlineData("$mark = 50\nif ($mark >= 40) {\n    Write-Output \"pass\"\n}\n", "powershell-comparison-words", 2, "if ($mark -ge 40) {", "pass")]
    [InlineData("$mark = 50\nif ($mark -gt 40 && $mark -lt 70) {\n    Write-Output \"middle\"\n}\n", "powershell-comparison-words", 2, "if ($mark -gt 40 -and $mark -lt 70) {", "middle")]
    // PowerShell names the function's { on line 1; the { left open is the foreach's, and its } goes after the foreach's body.
    [InlineData("function Get-Total($marks) {\n    $sum = 0\n    foreach ($mark in $marks) {\n        $sum += $mark\n    return $sum\n}\n\nWrite-Output (Get-Total @(70, 80))\n",
        "powershell-closing-brace", 1, "    }", "150")]
    [InlineData("$name = \"Ada\nWrite-Output $name\n", "powershell-closing-quote", 1, "$name = \"Ada\"", "Ada")]
    [InlineData("$total = (70 + 80\nWrite-Output $total\n", "powershell-closing-bracket", 1, "$total = (70 + 80)", "150")]
    public async Task WhatThePowerShellParserRefusesIsFoundBeforeItRunsAndItsChangeRuns(string code, string rule, int line, string changedTo, string fixedPrints)
    {
        if (PowerShell() is not { } powershell) return;

        var report = await CheckAsync(Write(Path.Combine($"refused{code.Length}", "main.ps1"), code));

        var found = Assert.Single(report.Findings, finding => finding.RuleId == rule);
        Assert.Equal((FindingKind.Syntax, line), (found.Kind, found.Line!.Value));
        Assert.Equal(changedTo, found.Fix?.NewLines.Single());
        Assert.Contains("checked it with PowerShell's parser", found.FixCheckedBy);
        Assert.Equal(fixedPrints, await RunAsync(powershell, Changed(code, found), $"refused{code.Length}"));
    }

    [Theory]
    [InlineData("$mark = 50\nif ($mark == 40) {\n    Write-Output \"forty\"\n}\nWrite-Output \"done\"\n", "powershell-comparison-words", 2, "if ($mark -eq 40) {", "done")]
    [InlineData("$total = 10\nWrite-Ouput $total\n", "powershell-nearest-command", 2, "Write-Output $total", "10")]
    [InlineData("$marks = @()\n$marks += 70\n$marks.Add(80)\nWrite-Output $marks.Count\n", "powershell-fixed-size-array", 3, "$marks += 80", "2")]
    [InlineData("$marks = @()\n$average = 100 / $marks.Count\nWrite-Output $average\n", "powershell-division-guard", 2, "$average = $(if ($marks.Count -eq 0) { 0 } else { 100 / $marks.Count })", "0")]
    [InlineData("$marks = @(70, 80)\ntotal = ($marks | Measure-Object -Sum).Sum\nWrite-Output $total\n", "powershell-missing-dollar", 2, "$total = ($marks | Measure-Object -Sum).Sum", "150")]
    public async Task AnErrorTheScriptCarriedOnPastGetsItsChangeAndTheChangedScriptRunsClean(string code, string rule, int line, string changedTo, string fixedPrints)
    {
        if (PowerShell() is not { } powershell) return;

        var report = await CheckAsync(Write(Path.Combine($"printed{code.Length}", "main.ps1"), code));

        var found = Assert.Single(report.Findings, finding => finding.RuleId == rule);
        Assert.Equal((FindingKind.Runtime, line), (found.Kind, found.Line!.Value));
        Assert.StartsWith("It printed an error and carried on: ", found.Title, StringComparison.Ordinal);
        Assert.Equal(changedTo, found.Fix?.NewLines.Single());
        Assert.Equal(fixedPrints, await RunAsync(powershell, Changed(code, found), $"printed{code.Length}"));
    }

    [Fact]
    public async Task EveryErrorARunPrintsIsAFindingOfItsOwn()
    {
        if (PowerShell() is null) return;

        var report = await CheckAsync(Write(Path.Combine("errors", "main.ps1"),
            "$name = $null\nWrite-Output $name.ToUpper()\n$marks = @()\nWrite-Output (100 / $marks.Count)\nWrite-Output \"end\"\n"));

        var printed = report.Findings.Where(finding => finding.Kind == FindingKind.Runtime).OrderBy(finding => finding.Line).ToList();
        Assert.Equal(new int?[] { 2, 4 }, printed.Select(finding => finding.Line));
        Assert.Equal(["InvokeMethodOnNull", "RuntimeException"], printed.Select(finding => finding.Error?.ErrorCode));
    }

    [Theory]
    [InlineData("$mark = 50\nif ($mark > 40) {\n    Write-Output \"pass\"\n}\nWrite-Output \"done\"\n", "logic-powershell-greater-than-writes-a-file", 2, "pass\ndone")]
    [InlineData("$mark = 70\nif ($mark = 40) {\n    Write-Output \"forty\"\n}\nWrite-Output $mark\n", "logic-powershell-assignment-in-condition", 2, "70")]
    [InlineData("$marks = 70, 80, 90\nfor ($i = 0; $i -le $marks.Count; $i++) {\n    Write-Output (\"Mark: \" + $marks[$i])\n}\n", "logic-powershell-loop-to-count", 2, "Mark: 70\nMark: 80\nMark: 90")]
    [InlineData("function Area($width, $height) {\n    return $width * $height\n}\nWrite-Output (Area(3, 4))\n", "logic-powershell-function-called-with-brackets", 4, "12")]
    // In double quotes only $marks is put in - "Marks counted: 70 80 90.Count" - and in single quotes nothing is.
    [InlineData("$marks = @(70, 80, 90)\nWrite-Output \"Marks counted: $marks.Count\"\n", "logic-powershell-property-in-text", 2, "Marks counted: 3")]
    [InlineData("$marks = @(70, 80, 90)\nWrite-Output \"First: $marks[0]\"\n", "logic-powershell-property-in-text", 2, "First: 70")]
    [InlineData("$name = \"Ada\"\nWrite-Output 'Hello, $name'\n", "logic-powershell-variable-in-single-quotes", 2, "Hello, Ada")]
    public async Task ALogicMistakePowerShellRunsWithoutAWordIsFoundAndItsChangePrintsTheRightAnswer(string code, string check, int line, string fixedPrints)
    {
        if (PowerShell() is not { } powershell) return;

        var report = await CheckAsync(Write(Path.Combine($"logic{code.Length}", "main.ps1"), code));

        var found = Assert.Single(report.Findings, finding => finding.RuleId == check);
        Assert.Equal(line, found.Line);
        Assert.Equal(fixedPrints, await RunAsync(powershell, Changed(code, found), $"logic{code.Length}"));
    }

    [Fact]
    public async Task AnErrorInADotSourcedScriptIsPlacedThere()
    {
        if (PowerShell() is null) return;

        var helpers = Write(Path.Combine("program", "helpers.ps1"), "function Get-Average($marks) {\n    return 100 / $marks.Count\n}\n");
        var main = Write(Path.Combine("program", "main.ps1"), ". $PSScriptRoot\\helpers.ps1\n\nWrite-Output (Get-Average @())\n");

        var report = await CheckAsync(main);

        var printed = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Runtime);
        Assert.Equal((helpers, 2), (printed.File, printed.Line!.Value));
        Assert.Equal("powershell-division-guard", printed.RuleId);
    }

    /// <summary>Every whole script a PowerShell guide shows reads with no parse error and runs with no error printed.</summary>
    [Fact]
    public async Task EveryExampleAPowerShellGuideShowsParsesAndRunsClean()
    {
        if (PowerShell() is not { } powershell) return;

        var examples = PowerShellGuides.All.Concat(PowerShellPatternGuides.All).Select(entry => entry.Guide.Example).Where(example => example.Length > 0).Distinct().ToList();

        foreach (var (example, number) in examples.Select((example, index) => (example, index + 1)))
        {
            var file = Write(Path.Combine("examples", $"example{number}.ps1"), example + "\n");
            var check = ScriptLanguages.CheckOf(ScriptLanguages.PowerShell, file, TimeSpan.FromMinutes(1))!;

            using var parsing = Process.Start(new ProcessStartInfo(check.ExecutablePath, check.Arguments)
                { WorkingDirectory = check.WorkingDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true })!;
            var said = await parsing.StandardOutput.ReadToEndAsync() + await parsing.StandardError.ReadToEndAsync();
            await parsing.WaitForExitAsync();
            Assert.True(parsing.ExitCode == 0 && said.Trim().Length == 0, $"example {number}:\n{example}\nthe parser said:\n{said}");

            await RunAsync(powershell, example + "\n", $"example{number}");
        }

        output.WriteLine($"{examples.Count} examples parsed and run.");
    }
}
