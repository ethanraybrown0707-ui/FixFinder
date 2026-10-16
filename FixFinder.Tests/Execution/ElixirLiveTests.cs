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
/// Elixir programs checked whole, as the window checks them: compiled by FixFinder's check before they run, run by elixir,
/// what Elixir refused to compile, what stopped them and every warning it gave found at their lines - in a file the program
/// brings in too - with changes checked in copies, and every example an Elixir guide shows compiled and run. A computer
/// without Elixir passes over them.
/// </summary>
public class ElixirLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string? Elixir()
    {
        if (TargetFactory.FindOnPath("elixir") is { } elixir)
        {
            output.WriteLine($"Checked with Elixir {ScriptLanguages.VersionOf(ScriptLanguages.Elixir)}: {elixir}");
            return elixir;
        }

        output.WriteLine("Not run on this computer: no Elixir is here.");
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
        var report = await new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Elixir }.CheckAsync(launch);

        foreach (var note in report.Notes) output.WriteLine($"note: {note}");
        foreach (var finding in report.Findings)
        {
            output.WriteLine($"{Path.GetFileName(finding.File)}:{finding.Line}: {finding.Kind} {finding.Severity} {finding.RuleId}: {finding.Title} | {finding.Verified.Summary}");
            if (finding.Fix is { } fix) output.WriteLine($"  change line {fix.StartLine} ({fix.RemoveCount} removed) to: {string.Join(" / ", fix.NewLines)}");
            if (finding.Error is { } error) output.WriteLine("  as Elixir said it:\n    " + error.RawText.Replace("\n", "\n    "));
        }

        foreach (var line in report.Run?.Run?.Lines ?? []) output.WriteLine($"  | {line.Text}");
        return report;
    }

    /// <summary>Runs Elixir code here, typing <paramref name="input"/>, and gives back what it printed, failing on anything Elixir said.</summary>
    private async Task<string> RunAsync(string elixir, string code, string name, string input = "")
    {
        var file = Write(Path.Combine("changed", name, "main.exs"), code);
        using var running = Process.Start(new ProcessStartInfo(elixir, "main.exs")
        {
            WorkingDirectory = Path.GetDirectoryName(file)!, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        })!;
        await running.StandardInput.WriteAsync(input);
        running.StandardInput.Close();
        var printed = await running.StandardOutput.ReadToEndAsync();
        var said = await running.StandardError.ReadToEndAsync();
        await running.WaitForExitAsync();

        Assert.True(running.ExitCode == 0 && said.Trim().Length == 0, $"{name}: elixir said:\n{said}");
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
    [InlineData("marks = [70, 80]\ntotal = Enum.sum(marks)\nIO.puts(totl)\n", "elixir-nearest-variable", 3, 3, "IO.puts(total)", "150")]
    [InlineData("defmodule Marks do\n  def average(marks), do: Enum.sum(marks) / length(marks)\n  def report(marks), do: IO.puts(avrage(marks))\nend\n\nMarks.report([70, 80])\n",
        "elixir-nearest-function", 3, 3, "  def report(marks), do: IO.puts(average(marks))", "75.0")]
    [InlineData("defmodule Marks do\n  def total(marks)\n    Enum.sum(marks)\n  end\nend\n\nIO.puts(Marks.total([70, 80]))\n", "elixir-missing-do", 5, 2, "  def total(marks) do", "150")]
    [InlineData("defmodule Marks do\n  def total(marks) do\n    Enum.sum(marks)\nend\n\nIO.puts(Marks.total([70, 80]))\n", "elixir-missing-end", 6, 4, "  end", "150")]
    public async Task WhatElixirRefusesToCompileIsFoundBeforeItRunsAndItsChangeCompilesAndRuns(string code, string rule, int line, int changedLine, string changedTo, string fixedPrints)
    {
        if (Elixir() is not { } elixir) return;

        var report = await CheckAsync(Write(Path.Combine($"refused{code.Length}", "main.exs"), code));

        var refused = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Syntax);
        Assert.Equal((rule, line), (refused.RuleId, refused.Line!.Value));
        Assert.Equal(changedLine, refused.Fix?.StartLine);
        Assert.Equal(changedTo, refused.Fix?.NewLines.Single());
        Assert.Equal(fixedPrints, await RunAsync(elixir, Changed(code, refused), $"refused{code.Length}"));
    }

    [Fact]
    public async Task EveryErrorElixirWritesForAModuleIsAFindingWithItsOwnChange()
    {
        if (Elixir() is not { } elixir) return;

        // Apart by a line: a change is checked only when no other error is on a line next to it.
        const string code = "defmodule Marks do\n  def total(marks), do: Enum.sum(marks)\n  def average(marks), do: totl(marks) / length(marks)\n\n" +
                            "  def report(marks), do: IO.puts(avrage(marks))\nend\n\nMarks.report([70, 80])\n";
        var report = await CheckAsync(Write(Path.Combine("module", "main.exs"), code));

        var refused = report.Findings.Where(finding => finding.Kind == FindingKind.Syntax).OrderBy(finding => finding.Line).ToList();
        Assert.Equal([3, 5], refused.Select(finding => finding.Line!.Value));
        Assert.All(refused, finding => Assert.Equal("elixir-nearest-function", finding.RuleId));

        var changed = Changed(Changed(code, refused[1]), refused[0]);
        Assert.Equal("75.0", await RunAsync(elixir, changed, "module"));
    }

    [Theory]
    [InlineData("person = %{name: \"Ada\", age: 36}\nIO.puts(person.agee)\n", "elixir-did-you-mean-key", 2, "IO.puts(person.age)", "36")]
    [InlineData("IO.puts(Enum.summ([70, 80]))\n", "elixir-did-you-mean-function", 1, "IO.puts(Enum.sum([70, 80]))", "150")]
    [InlineData("total = 10\nIO.puts(\"Total: \" + total)\n", "elixir-join-text", 2, "IO.puts(\"Total: \" <> to_string(total))", "Total: 10")]
    [InlineData("marks = []\nIO.puts(Enum.sum(marks) / length(marks))\n", "elixir-division-guard", 2, "IO.puts((if length(marks) == 0, do: 0, else: Enum.sum(marks) / length(marks)))", "0")]
    [InlineData("count = 0\nIO.puts(div(10, count))\n", "elixir-division-guard", 2, "IO.puts((if count == 0, do: 0, else: div(10, count)))", "0")]
    [InlineData("marks = [70, 80]\nIO.puts(marks[0])\n", "elixir-list-index", 2, "IO.puts(Enum.at(marks, 0))", "70")]
    [InlineData("typed = \"12\\n\"\nIO.puts(String.to_integer(typed) + 1)\n", "elixir-trim-input", 2, "IO.puts(String.to_integer(String.trim(typed)) + 1)", "13")]
    public async Task WhatStoppedTheProgramGetsItsChangeAndTheChangedProgramRunsClean(string code, string rule, int line, string changedTo, string fixedPrints)
    {
        if (Elixir() is not { } elixir) return;

        var report = await CheckAsync(Write(Path.Combine($"stopped{code.Length}", "main.exs"), code));

        var stopped = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Runtime && finding.Severity == Severity.Error);
        Assert.Equal((rule, line), (stopped.RuleId, stopped.Line!.Value));
        Assert.Equal(changedTo, stopped.Fix?.NewLines.Single());
        Assert.Equal(fixedPrints, await RunAsync(elixir, Changed(code, stopped), $"stopped{code.Length}"));
    }

    [Theory]
    // A total built up inside Enum.each's fn, on one line and over three.
    [InlineData("total = 0\nmarks = [70, 80, 90]\nEnum.each(marks, fn mark -> total = total + mark end)\nIO.puts(total)\n", 3, 1, "240")]
    [InlineData("total = 0\nmarks = [70, 80, 90]\nEnum.each(marks, fn mark ->\n  total = total + mark\nend)\nIO.puts(total)\n", 3, 2, "240")]
    // A total built up inside a for.
    [InlineData("total = 0\nfor mark <- [70, 80, 90] do\n  total = total + mark\nend\nIO.puts(total)\n", 2, 2, "240")]
    // A value set inside an if.
    [InlineData("mark = 70\ngrade = \"fail\"\nif mark >= 40 do\n  grade = \"pass\"\nend\nIO.puts(grade)\n", 3, 3, "pass")]
    public async Task AVariableGivenANewValueInsideABlockIsOneFindingWithElixirsWarningAndItsChangeKeepsTheValue(string code, int changedLine, int removed, string fixedPrints)
    {
        if (Elixir() is not { } elixir) return;

        var report = await CheckAsync(Write(Path.Combine($"block{code.Length}", "main.exs"), code));

        var kept = Assert.Single(report.Findings, finding => finding.Fix is not null);
        Assert.Equal("logic-elixir-new-variable-in-block", kept.RuleId);
        Assert.Equal((changedLine, removed), (kept.Fix!.StartLine, kept.Fix.RemoveCount));
        Assert.Equal(fixedPrints, await RunAsync(elixir, Changed(code, kept), $"block{code.Length}"));
    }

    [Theory]
    [InlineData("marks = [70, 80, 90]\nfor i <- 0..length(marks) do\n  IO.puts(\"Mark: #{Enum.at(marks, i)}\")\nend\n", "logic-elixir-range-to-length", 2, "", "Mark: 70\nMark: 80\nMark: 90")]
    [InlineData("answer = IO.gets(\"Continue? \")\nif answer == \"y\" do\n  IO.puts(\"yes\")\nelse\n  IO.puts(\"no\")\nend\n", "logic-elixir-input-not-trimmed", 1, "y\n", "Continue? yes")]
    [InlineData("ages = %{\"Ada\" => 36}\nMap.put(ages, \"Bob\", 20)\nIO.inspect(ages)\n", "logic-elixir-result-discarded", 2, "", "%{\"Ada\" => 36, \"Bob\" => 20}")]
    [InlineData("total = 0.1 + 0.2\nif total == 0.3 do\n  IO.puts(\"equal\")\nelse\n  IO.puts(\"not equal\")\nend\n", "logic-elixir-float-equality", 2, "", "equal")]
    [InlineData("mark = 70\nif mark = 40 do\n  IO.puts(\"forty\")\nend\nIO.puts(mark)\n", "logic-elixir-assignment-in-condition", 2, "", "70")]
    public async Task ALogicMistakeIsFoundAndItsChangePrintsTheRightAnswer(string code, string check, int line, string input, string fixedPrints)
    {
        if (Elixir() is not { } elixir) return;

        var report = await CheckAsync(Write(Path.Combine($"logic{code.Length}", "main.exs"), code));

        var found = Assert.Single(report.Findings, finding => finding.RuleId == check);
        Assert.Equal(line, found.Line);
        Assert.Equal(fixedPrints, await RunAsync(elixir, Changed(code, found), $"logic{code.Length}", input));
    }

    [Fact]
    public async Task AWarningIsAFindingOfItsOwnOnceThoughTheCheckAndTheRunBothGaveIt()
    {
        if (Elixir() is null) return;

        var report = await CheckAsync(Write(Path.Combine("unused", "main.exs"), "count = 5\nIO.puts(\"hi\")\n"));

        var unused = Assert.Single(report.Findings);
        Assert.Equal(1, unused.Line);
        Assert.StartsWith("variable \"count\" is unused", unused.Error?.Message, StringComparison.Ordinal);
        Assert.Equal("hi", string.Join("\n", report.Run?.Run?.Lines.Where(line => line.Stream == StreamKind.StdOut).Select(line => line.Text) ?? []).Trim());
    }

    [Fact]
    public async Task WhatStoppedTheProgramInAFileItRequiresIsPlacedThere()
    {
        if (Elixir() is null) return;

        var helpers = Write(Path.Combine("program", "helpers.exs"), "defmodule Helpers do\n  def average(marks), do: Enum.sum(marks) / length(marks)\nend\n");
        var main = Write(Path.Combine("program", "main.exs"), "Code.require_file(\"helpers.exs\", __DIR__)\n\nIO.puts(Helpers.average([]))\n");

        var report = await CheckAsync(main);

        var crash = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Runtime && finding.Severity == Severity.Error);
        Assert.Equal((helpers, 2), (crash.File, crash.Line!.Value));
        Assert.Equal("elixir-division-guard", crash.RuleId);
    }

    [Fact]
    public async Task WhatElixirRefusesInAFileItRequiresIsFoundThereBeforeItRuns()
    {
        if (Elixir() is null) return;

        var helpers = Write(Path.Combine("compileprogram", "helpers.exs"),
            "defmodule Helpers do\n  def average(marks), do: Enum.sum(marks) / lenght(marks)\nend\n");
        var main = Write(Path.Combine("compileprogram", "main.exs"), "Code.require_file(\"helpers.exs\", __DIR__)\n\nIO.puts(Helpers.average([70, 80]))\n");

        var report = await CheckAsync(main);

        var refused = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Syntax);
        Assert.Equal((helpers, 2), (refused.File, refused.Line!.Value));
        Assert.StartsWith("undefined function lenght/1", refused.Error?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AScriptThatInstallsPackagesIsNotRun()
    {
        var script = Write(Path.Combine("installs", "main.exs"), "Mix.install([:jason])\nIO.puts(Jason.encode!(%{a: 1}))\n");

        var launch = TargetFactory.FromFile(script);

        Assert.False(launch.Ok);
        Assert.Contains("Mix.install", launch.Problem, StringComparison.Ordinal);
    }

    /// <summary>Every whole program an Elixir guide shows compiles with FixFinder's check, saying nothing, and runs with nothing from Elixir but its output.</summary>
    [Fact]
    public async Task EveryExampleAnElixirGuideShowsCompilesAndRunsClean()
    {
        if (Elixir() is not { } elixir) return;

        var examples = ElixirGuides.All.Concat(ElixirPatternGuides.All).Select(entry => entry.Guide.Example).Where(example => example.Length > 0).Distinct().ToList();

        foreach (var (example, number) in examples.Select((example, index) => (example, index + 1)))
        {
            var file = Write(Path.Combine("examples", $"example{number}.exs"), example + "\n");
            var check = ScriptLanguages.CheckOf(ScriptLanguages.Elixir, file, TimeSpan.FromMinutes(1))!;

            using var compiling = Process.Start(new ProcessStartInfo(check.ExecutablePath, check.Arguments)
                { WorkingDirectory = check.WorkingDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true })!;
            var said = (await compiling.StandardOutput.ReadToEndAsync() + await compiling.StandardError.ReadToEndAsync()).Trim();
            await compiling.WaitForExitAsync();
            Assert.True(compiling.ExitCode == 0 && said.Length == 0, $"example {number}:\n{example}\nthe check said:\n{said}");

            await RunAsync(elixir, example + "\n", $"example{number}");
        }

        output.WriteLine($"{examples.Count} examples compiled and run.");
    }
}
