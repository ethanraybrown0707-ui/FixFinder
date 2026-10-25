using System.Diagnostics;
using System.Text.RegularExpressions;
using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Checking.Guides;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Erlang programs checked whole, as the window checks them: compiled by erlc -Wall into FixFinder's own build folder, run
/// from their main by FixFinder's starting module, what erlc refused and what stopped them found at their lines - in a module
/// the program calls too - with changes checked in copies, and every example an Erlang guide shows compiled and run. A
/// computer without Erlang/OTP passes over them.
/// </summary>
public class ErlangLiveTests(ITestOutputHelper output) : IDisposable
{
    private const string Head = "-module(marks).\n-export([main/0]).\n\n";

    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private (string Erlc, string Erl)? Erlang()
    {
        if (ErlangBuild.Erlc() is { } erlc && ErlangBuild.Erl() is { } erl)
        {
            output.WriteLine($"Checked with {ErlangBuild.Version()}: {erlc}");
            return (erlc, erl);
        }

        output.WriteLine("Not run on this computer: no Erlang/OTP is here.");
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
        var report = await new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Erlang }.CheckAsync(launch);

        foreach (var note in report.Notes) output.WriteLine($"note: {note}");
        foreach (var finding in report.Findings)
        {
            output.WriteLine($"{Path.GetFileName(finding.File)}:{finding.Line}: {finding.Kind} {finding.Severity} {finding.RuleId}: {finding.Title} | {finding.Verified.Summary}");
            if (finding.Fix is { } fix) output.WriteLine($"  change line {fix.StartLine} ({fix.RemoveCount} removed) to: {string.Join(" / ", fix.NewLines)}");
            if (finding.Error is { } error) output.WriteLine("  as Erlang said it:\n    " + error.RawText.Replace("\n", "\n    "));
        }

        foreach (var line in report.Run?.Run?.Lines ?? []) output.WriteLine($"  | {line.Text}");
        return report;
    }

    private static async Task<(int ExitCode, string Printed, string Said)> ProcessAsync(string program, string arguments, string folder, string input = "")
    {
        var start = new ProcessStartInfo(program, arguments)
        {
            WorkingDirectory = folder, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var (name, value) in ErlangBuild.Environment) start.Environment[name] = value;

        using var running = Process.Start(start)!;
        await running.StandardInput.WriteAsync(input);
        running.StandardInput.Close();
        var printed = await running.StandardOutput.ReadToEndAsync();
        var said = await running.StandardError.ReadToEndAsync();
        await running.WaitForExitAsync();
        return (running.ExitCode, printed, said);
    }

    /// <summary>
    /// Compiles an Erlang module here with erlc -Wall and runs it from its main, typing <paramref name="input"/>, and gives back
    /// what it printed - failing on anything erlc or Erlang said.
    /// </summary>
    private async Task<string> RunAsync((string Erlc, string Erl) erlang, string code, string name, string input = "")
    {
        var module = Regex.Match(code, @"-module\((\w+)\)").Groups[1].Value;
        var folder = Path.GetDirectoryName(Write(Path.Combine("changed", name, module + ".erl"), code))!;
        var build = Path.Combine(folder, "build");
        Directory.CreateDirectory(build);

        var compiled = await ProcessAsync(erlang.Erlc, ErlangBuild.CompileArguments(build, [ErlangBuild.Runner(), module + ".erl"]), folder);
        Assert.True(compiled.ExitCode == 0 && (compiled.Printed + compiled.Said).Trim().Length == 0, $"{name}: erlc said:\n{compiled.Printed}{compiled.Said}");

        var ran = await ProcessAsync(erlang.Erl, $"-noshell -pa \"{build}\" -run {ErlangBuild.RunnerModule} main {module}", folder, input);
        Assert.True(ran.ExitCode == 0 && ran.Said.Trim().Length == 0, $"{name}: Erlang said:\n{ran.Said}");
        return ran.Printed.Trim().ReplaceLineEndings("\n");
    }

    private static string Changed(string code, Finding found)
    {
        var lines = code.TrimEnd('\n').Split('\n').ToList();
        lines.RemoveRange(found.Fix!.StartLine - 1, found.Fix.RemoveCount);
        lines.InsertRange(found.Fix.StartLine - 1, found.Fix.NewLines);
        return string.Join("\n", lines) + "\n";
    }

    [Theory]
    [InlineData(Head + "main() ->\n    Marks = [70, 80],\n    Total = lists:sum(Marks),\n    io:format(\"~p~n\", [Totl]).\n",
        "erlang-did-you-mean", 7, 7, "    io:format(\"~p~n\", [Total]).", "150")]
    [InlineData(Head + "average(Marks) -> lists:sum(Marks) / length(Marks).\n\nmain() ->\n    io:format(\"~p~n\", [avrage([70, 80])]).\n",
        "erlang-did-you-mean", 7, 7, "    io:format(\"~p~n\", [average([70, 80])]).", "75.0")]
    [InlineData(Head + "total(Marks) -> lists:sum(Marks)\n\nmain() ->\n    io:format(\"~p~n\", [total([70, 80])]).\n",
        "erlang-missing-period", 6, 4, "total(Marks) -> lists:sum(Marks).", "150")]
    [InlineData(Head + "main() ->\n    Marks = [70, 80]\n    io:format(\"~p~n\", [lists:sum(Marks)]).\n",
        "erlang-missing-comma", 6, 5, "    Marks = [70, 80],", "150")]
    [InlineData(Head + "grade(Mark) ->\n    case Mark of\n        70 -> \"A\"\n        40 -> \"C\"\n    end.\n\nmain() ->\n    io:format(\"~s~n\", [grade(70)]).\n",
        "erlang-missing-semicolon", 7, 6, "        70 -> \"A\";", "A")]
    [InlineData(Head + "check(Mark) ->\n    if\n        Mark > 40 and Mark < 70 -> \"pass\";\n        true -> \"other\"\n    end.\n\nmain() ->\n    io:format(\"~s~n\", [check(50)]).\n",
        "erlang-andalso", 6, 6, "        Mark > 40 andalso Mark < 70 -> \"pass\";", "pass")]
    public async Task WhatErlcRefusesIsFoundBeforeItRunsAndItsChangeCompilesAndRuns(string code, string rule, int line, int changedLine, string changedTo, string fixedPrints)
    {
        if (Erlang() is not { } erlang) return;

        var report = await CheckAsync(Write(Path.Combine($"refused{code.Length}", "marks.erl"), code));

        var refused = Assert.Single(report.Findings, finding => finding.RuleId == rule);
        Assert.Equal((FindingKind.Syntax, line), (refused.Kind, refused.Line!.Value));
        Assert.Equal(changedLine, refused.Fix?.StartLine);
        Assert.Equal(changedTo, refused.Fix?.NewLines.Single());
        Assert.Equal(fixedPrints, await RunAsync(erlang, Changed(code, refused), $"refused{code.Length}"));
    }

    [Theory]
    [InlineData(Head + "next(Typed) -> list_to_integer(Typed) + 1.\n\nmain() ->\n    io:format(\"~p~n\", [next(\"12\\n\")]).\n",
        "erlang-trim-input", 4, "next(Typed) -> list_to_integer(string:trim(Typed)) + 1.", "13")]
    [InlineData(Head + "average(Marks) -> lists:sum(Marks) / length(Marks).\n\nmain() ->\n    io:format(\"~p~n\", [average([])]).\n",
        "erlang-division-guard", 4, "average(Marks) -> (case length(Marks) of 0 -> 0; _ -> lists:sum(Marks) / length(Marks) end).", "0")]
    // io:format's value given on its own: Erlang names no line of the program, as the call was main's last.
    [InlineData(Head + "main() ->\n    Total = 150,\n    io:format(\"Total: ~p~n\", Total).\n", "erlang-format-values-list", 6, "    io:format(\"Total: ~p~n\", [Total]).", "Total: 150")]
    public async Task WhatStoppedTheProgramGetsItsChangeAndTheChangedProgramRunsClean(string code, string rule, int line, string changedTo, string fixedPrints)
    {
        if (Erlang() is not { } erlang) return;

        var report = await CheckAsync(Write(Path.Combine($"stopped{code.Length}", "marks.erl"), code));

        var stopped = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Runtime && finding.Severity == Severity.Error);
        Assert.Equal((rule, line), (stopped.RuleId, stopped.Line!.Value));
        Assert.Equal(changedTo, stopped.Fix?.NewLines.Single());
        Assert.Equal(fixedPrints, await RunAsync(erlang, Changed(code, stopped), $"stopped{code.Length}"));
    }

    [Fact]
    public async Task ANameWithASmallFirstLetterIsMadeAVariableToTheEndOfItsFunction()
    {
        if (Erlang() is not { } erlang) return;

        const string code = Head + "main() ->\n    total = 10,\n    io:format(\"~p~n\", [total]).\n";
        var report = await CheckAsync(Write(Path.Combine("atom", "marks.erl"), code));

        var capital = report.Findings.First(finding => finding.RuleId == "erlang-capital-variable");
        Assert.Equal(5, capital.Line);
        Assert.Equal(["    Total = 10,", "    io:format(\"~p~n\", [Total])."], capital.Fix!.NewLines);
        Assert.Equal("10", await RunAsync(erlang, Changed(code, capital), "atom"));
    }

    [Theory]
    [InlineData(Head + "main() ->\n    Typed = io:get_line(\"Age? \"),\n    if\n        Typed > 17 -> io:format(\"adult~n\");\n        true -> io:format(\"child~n\")\n    end.\n",
        "logic-erlang-text-compared-with-number", 5, "7\n", "Age? child")]
    [InlineData(Head + "main() ->\n    Answer = io:get_line(\"Continue? \"),\n    if\n        Answer == \"y\" -> io:format(\"yes~n\");\n        true -> io:format(\"no~n\")\n    end.\n",
        "logic-erlang-input-not-trimmed", 5, "y\n", "Continue? yes")]
    [InlineData("-module(marks).\n-export([main/0]).\n\ndescribe(Price) ->\n    Total = Price + 0.2,\n    if\n        Total == 0.3 -> \"equal\";\n        true -> \"not equal\"\n    end.\n\nmain() ->\n    io:format(\"~s~n\", [describe(0.1)]).\n",
        "logic-erlang-float-equality", 7, "", "equal")]
    [InlineData(Head + "main() ->\n    Marks = [80, 70, 90],\n    io:format(\"~p~n\", [Marks]).\n",
        "logic-erlang-list-printed-as-text", 6, "", "[80,70,90]")]
    [InlineData(Head + "main() ->\n    Total = lists:sum([70, 80]),\n    io:format(\"Total: \" ++ Total).\n",
        "logic-erlang-number-joined-to-text", 6, "", "Total: 150")]
    [InlineData(Head + "main() ->\n    Marks = [80, 70, 90],\n    lists:sort(Marks),\n    io:format(\"~w~n\", [Marks]).\n",
        "logic-erlang-result-discarded", 6, "", "[70,80,90]")]
    public async Task ALogicMistakeIsFoundAndItsChangePrintsTheRightAnswer(string code, string check, int line, string input, string fixedPrints)
    {
        if (Erlang() is not { } erlang) return;

        var report = await CheckAsync(Write(Path.Combine($"logic{code.Length}", "marks.erl"), code));

        var found = Assert.Single(report.Findings, finding => finding.RuleId == check);
        Assert.Equal(line, found.Line);
        Assert.Equal(fixedPrints, await RunAsync(erlang, Changed(code, found), $"logic{code.Length}", input));
    }

    [Fact]
    public async Task AWarningIsAFindingOfItsOwnAndTheProgramRuns()
    {
        if (Erlang() is null) return;

        var report = await CheckAsync(Write(Path.Combine("unused", "marks.erl"), Head + "main() ->\n    Count = 5,\n    io:format(\"hi~n\").\n"));

        var unused = Assert.Single(report.Findings);
        Assert.Equal(5, unused.Line);
        Assert.Equal("variable 'Count' is unused", unused.Error?.Message);
        Assert.Equal("hi", string.Join("\n", report.Run?.Run?.Lines.Where(line => line.Stream == StreamKind.StdOut).Select(line => line.Text) ?? []).Trim());
    }

    [Fact]
    public async Task WhatStoppedTheProgramInAModuleItCallsIsPlacedThere()
    {
        if (Erlang() is null) return;

        var helpers = Write(Path.Combine("program", "helpers.erl"), "-module(helpers).\n-export([average/1]).\n\naverage(Marks) -> lists:sum(Marks) / length(Marks).\n");
        var main = Write(Path.Combine("program", "marks.erl"), Head + "main() ->\n    io:format(\"~p~n\", [helpers:average([])]).\n");

        var report = await CheckAsync(main);

        var crash = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Runtime && finding.Severity == Severity.Error);
        Assert.Equal((helpers, 4), (crash.File, crash.Line!.Value));
        Assert.Equal("erlang-division-guard", crash.RuleId);
    }

    [Fact]
    public async Task WhatErlcRefusesInAModuleItCallsIsFoundThere()
    {
        if (Erlang() is null) return;

        var helpers = Write(Path.Combine("compileprogram", "helpers.erl"),
            "-module(helpers).\n-export([average/1]).\n\naverage(Marks) -> lists:sum(Marks) / lenght(Marks).\n");
        var main = Write(Path.Combine("compileprogram", "marks.erl"), Head + "main() ->\n    io:format(\"~p~n\", [helpers:average([70, 80])]).\n");

        var report = await CheckAsync(main);

        var refused = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Syntax && finding.Severity == Severity.Error);
        Assert.Equal((helpers, 4), (refused.File, refused.Line!.Value));
        Assert.StartsWith("function lenght/1 undefined", refused.Error?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AModuleWithNothingToStartItIsNotRun()
    {
        var library = Write(Path.Combine("library", "marks.erl"), "-module(marks).\n-export([total/1]).\n\ntotal(Marks) -> lists:sum(Marks).\n");

        var launch = TargetFactory.FromFile(library);

        Assert.False(launch.Ok);
        Assert.Contains("main/0, main/1 or start/0", launch.Problem, StringComparison.Ordinal);
    }

    /// <summary>Every whole module an Erlang guide shows compiles with erlc -Wall, saying nothing, and runs with nothing from Erlang but its output.</summary>
    [Fact]
    public async Task EveryExampleAnErlangGuideShowsCompilesAndRunsClean()
    {
        if (Erlang() is not { } erlang) return;

        var examples = ErlangGuides.All.Concat(ErlangPatternGuides.All).Select(entry => entry.Guide.Example).Where(example => example.Length > 0).Distinct().ToList();

        foreach (var (example, number) in examples.Select((example, index) => (example, index + 1)))
            output.WriteLine($"example {number}: {await RunAsync(erlang, example + "\n", $"example{number}")}");

        output.WriteLine($"{examples.Count} examples compiled and run.");
    }
}
