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
/// Lua programs checked whole, as the window checks them: read by luac -p before they run, run by lua, their syntax and
/// runtime errors found at their lines - in a file brought in with require too - with changes checked in copies, and every
/// example a Lua guide shows read and run. A computer without Lua passes over them.
/// </summary>
public class LuaLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string? Lua()
    {
        if (TargetFactory.FindOnPath("lua") is { } lua && TargetFactory.FindOnPath("luac") is not null)
        {
            output.WriteLine($"Checked with {ScriptLanguages.VersionOf(ScriptLanguages.Lua)}: {lua}");
            return lua;
        }

        output.WriteLine("Not run on this computer: no lua and luac are here.");
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
        var report = await new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Lua }.CheckAsync(launch);

        foreach (var note in report.Notes) output.WriteLine($"note: {note}");
        foreach (var finding in report.Findings)
        {
            output.WriteLine($"{Path.GetFileName(finding.File)}:{finding.Line}: {finding.Kind} {finding.Severity} {finding.RuleId}: {finding.Title} | {finding.Verified.Summary}");
            if (finding.Fix is { } fix) output.WriteLine($"  change line {fix.StartLine} to: {string.Join(" / ", fix.NewLines)}");
            if (finding.Error is { } error) output.WriteLine("  as Lua said it:\n    " + error.RawText.Replace("\n", "\n    "));
        }

        foreach (var line in report.Run?.Run?.Lines ?? []) output.WriteLine($"  | {line.Text}");
        return report;
    }

    /// <summary>Runs Lua code here and gives back what it printed, failing on any error Lua printed.</summary>
    private async Task<string> RunAsync(string lua, string code, string name)
    {
        var file = Write(Path.Combine("changed", name, "main.lua"), code);
        using var running = Process.Start(new ProcessStartInfo(lua, "main.lua")
            { WorkingDirectory = Path.GetDirectoryName(file)!, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true })!;
        var printed = await running.StandardOutput.ReadToEndAsync();
        var said = await running.StandardError.ReadToEndAsync();
        await running.WaitForExitAsync();

        Assert.True(running.ExitCode == 0 && said.Length == 0, $"{name}: lua said:\n{said}");
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
    [InlineData("local mark = 50\nif mark != 40 then\n  print(\"not forty\")\nend\n", "lua-not-equal", 2, 2, "if mark ~= 40 then", "not forty")]
    [InlineData("local mark = 70\nif mark = 40 then\n  print(\"forty\")\nend\nprint(mark)\n", "lua-compare-with-double-equals", 2, 2, "if mark == 40 then", "70")]
    [InlineData("local total = 10\ntotal += 5\nprint(total)\n", "lua-compound-assignment", 2, 2, "total = total + 5", "15")]
    [InlineData("local count = 0\ncount++\nprint(count)\n", "lua-compound-assignment", 2, 2, "count = count + 1", "1")]
    [InlineData("local mark = 50\nif mark > 40\n  print(\"pass\")\nend\n", "lua-missing-then-or-do", 3, 2, "if mark > 40 then", "pass")]
    [InlineData("local count = 0\nwhile count < 3\n  count = count + 1\nend\nprint(count)\n", "lua-missing-then-or-do", 3, 2, "while count < 3 do", "3")]
    public async Task ASyntaxErrorIsFoundBeforeItRunsAndItsChangeReadsAndRuns(string code, string rule, int line, int changedLine, string changedTo, string fixedPrints)
    {
        if (Lua() is not { } lua) return;

        var report = await CheckAsync(Write(Path.Combine($"syntax{code.Length}", "main.lua"), code));

        var found = Assert.Single(report.Findings, finding => finding.RuleId == rule);
        Assert.Equal((FindingKind.Syntax, line), (found.Kind, found.Line!.Value));
        Assert.Equal(changedLine, found.Fix?.StartLine);
        Assert.Equal(changedTo, found.Fix?.NewLines.Single());
        Assert.Contains("checked it with luac -p", found.FixCheckedBy);
        Assert.Equal(fixedPrints, await RunAsync(lua, Changed(code, found), $"syntax{code.Length}"));
    }

    [Theory]
    [InlineData("local total = 10\nprint(totl + 1)\n", "lua-nearest-name", 2, "print(total + 1)", "11")]
    [InlineData("local function average(marks)\n  return (marks[1] + marks[2]) / 2\nend\nprint(averge({70, 80}))\n", "lua-nearest-name", 4, "print(average({70, 80}))", "75.0")]
    [InlineData("local total = 12\nprint(\"Total: \" + total)\n", "lua-join-text", 2, "print(\"Total: \" .. total)", "Total: 12")]
    [InlineData("local sweets = 7\nlocal people = 0\nprint(sweets // people)\n", "lua-division-guard", 3, "print((people == 0 and 0 or sweets // people))", "0")]
    [InlineData("local sweets = 7\nlocal people = 0\nprint(sweets % people)\n", "lua-division-guard", 3, "print((people == 0 and 0 or sweets % people))", "0")]
    public async Task ARuntimeErrorGetsItsChangeAndTheChangedProgramRunsClean(string code, string rule, int line, string changedTo, string fixedPrints)
    {
        if (Lua() is not { } lua) return;

        var report = await CheckAsync(Write(Path.Combine($"runtime{code.Length}", "main.lua"), code));

        var found = Assert.Single(report.Findings, finding => finding.RuleId == rule);
        Assert.Equal((FindingKind.Runtime, line), (found.Kind, found.Line!.Value));
        Assert.Equal(changedTo, found.Fix?.NewLines.Single());
        Assert.True(found.Verified.IsVerified, found.Verified.Summary);
        Assert.Equal(fixedPrints, await RunAsync(lua, Changed(code, found), $"runtime{code.Length}"));
    }

    [Theory]
    [InlineData("local marks = {70, 80, 90}\nfor i = 0, #marks do\n  print(marks[i])\nend\n", "logic-lua-loop-from-zero", 2, "70\n80\n90")]
    [InlineData("local marks = {70, 80, 90}\nfor i = 0, #marks - 1 do\n  print(marks[i])\nend\n", "logic-lua-loop-from-zero", 2, "70\n80\n90")]
    [InlineData("local typed = \"5\"\nif typed == 5 then\n  print(\"five\")\nelse\n  print(\"not five\")\nend\n", "logic-lua-text-compared-with-number", 2, "five")]
    [InlineData("local total = 0.1 + 0.2\nif total == 0.3 then\n  print(\"equal\")\nelse\n  print(\"not equal\")\nend\n", "logic-lua-float-equality", 2, "equal")]
    public async Task ALogicMistakeLuaSaysNothingOfIsFoundAndItsChangePrintsTheRightAnswer(string code, string check, int line, string fixedPrints)
    {
        if (Lua() is not { } lua) return;

        var report = await CheckAsync(Write(Path.Combine($"logic{code.Length}", "main.lua"), code));

        Assert.DoesNotContain(report.Findings, finding => finding.Kind is FindingKind.Syntax or FindingKind.Runtime);
        var found = Assert.Single(report.Findings, finding => finding.RuleId == check);
        Assert.Equal(line, found.Line);
        Assert.Equal(fixedPrints, await RunAsync(lua, Changed(code, found), $"logic{code.Length}"));
    }

    [Fact]
    public async Task ARuntimeErrorInARequiredFileIsPlacedThere()
    {
        if (Lua() is null) return;

        var stats = Write(Path.Combine("program", "stats.lua"),
            "local stats = {}\n\nfunction stats.average(marks)\n  local total = 0\n  for _, mark in ipairs(marks) do\n    total = total + mark\n  end\n  return total // #marks\nend\n\nreturn stats\n");
        var main = Write(Path.Combine("program", "main.lua"), "local stats = require(\"stats\")\n\nprint(stats.average({}))\n");

        var report = await CheckAsync(main);

        var crash = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Runtime);
        Assert.Equal((stats, 8), (crash.File, crash.Line!.Value));
        Assert.Equal("lua-division-guard", crash.RuleId);
    }

    [Fact]
    public async Task ASyntaxErrorInARequiredFileIsFoundThereBeforeItRuns()
    {
        if (Lua() is null) return;

        var helpers = Write(Path.Combine("syntaxprogram", "lib", "helpers.lua"),
            "local helpers = {}\n\nfunction helpers.passed(mark)\n  return mark != 0\nend\n\nreturn helpers\n");
        var main = Write(Path.Combine("syntaxprogram", "main.lua"), "local helpers = require \"lib.helpers\"\n\nprint(helpers.passed(50))\n");

        var report = await CheckAsync(main);

        var syntax = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Syntax);
        Assert.Equal((helpers, 4), (syntax.File, syntax.Line!.Value));
        Assert.Equal("lua-not-equal", syntax.RuleId);
    }

    /// <summary>ipairs given nil names no place of its own - only lua: and the message - so it is placed at the traceback's line in the program.</summary>
    [Fact]
    public async Task AnErrorThatNamesNoPlaceIsPlacedAtTheProgramsLineInTheTraceback()
    {
        if (Lua() is null) return;

        var report = await CheckAsync(Write(Path.Combine("placeless", "main.lua"), "local marks\nfor _, mark in ipairs(marks) do\n  print(mark)\nend\n"));

        var crash = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Runtime);
        Assert.Equal(2, crash.Line);
        Assert.Equal("attempt to index a nil value", crash.Error?.Message);
    }

    /// <summary>Every whole program a Lua guide shows reads with luac -p and runs with nothing from Lua but its output.</summary>
    [Fact]
    public async Task EveryExampleALuaGuideShowsReadsAndRunsClean()
    {
        if (Lua() is not { } lua) return;

        var examples = LuaGuides.All.Concat(LuaPatternGuides.All).Select(entry => entry.Guide.Example).Where(example => example.Length > 0).Distinct().ToList();
        var luac = TargetFactory.FindOnPath("luac")!;

        foreach (var (example, number) in examples.Select((example, index) => (example, index + 1)))
        {
            var file = Write(Path.Combine("examples", $"example{number}.lua"), example + "\n");

            using var reading = Process.Start(new ProcessStartInfo(luac, $"-p \"{Path.GetFileName(file)}\"")
                { WorkingDirectory = Path.GetDirectoryName(file)!, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true })!;
            var said = await reading.StandardOutput.ReadToEndAsync() + await reading.StandardError.ReadToEndAsync();
            await reading.WaitForExitAsync();
            Assert.True(reading.ExitCode == 0, $"example {number}:\n{example}\nluac -p said:\n{said}");

            await RunAsync(lua, example + "\n", $"example{number}");
        }

        output.WriteLine($"{examples.Count} examples read and run.");
    }
}
