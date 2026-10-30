using System.Diagnostics;
using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Checking.Guides;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Parsing.Parsers;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// F# scripts checked whole, as the window checks them: checked by dotnet fsi --typecheck-only before they run, run by
/// F# Interactive, what F# refused, warned of and what stopped them found at their lines - in a file a script loads too -
/// with changes checked in copies and run, each logic check's mistake and change run, and every example an F# guide shows
/// checked and run. A computer whose dotnet has no F# Interactive passes over them.
/// </summary>
[Collection(BuildsRunAlone.Name)]
public class FSharpLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string? Dotnet()
    {
        if (TargetFactory.FindOnPath("dotnet") is { } dotnet && ScriptLanguages.VersionOf(ScriptLanguages.FSharp) is { } version &&
            version.Contains("F# Interactive", StringComparison.Ordinal))
        {
            output.WriteLine($"Checked with {version}: {dotnet}");
            return dotnet;
        }

        output.WriteLine("Not run on this computer: no dotnet with F# Interactive is here.");
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
        var report = await new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.FSharp }.CheckAsync(launch);

        foreach (var note in report.Notes) output.WriteLine($"note: {note}");
        foreach (var finding in report.Findings)
        {
            output.WriteLine($"{Path.GetFileName(finding.File)}:{finding.Line}: {finding.Kind} {finding.Severity} {finding.RuleId}: {finding.Title} | {finding.Verified.Summary}");
            if (finding.Fix is { } fix) output.WriteLine($"  change line {fix.StartLine} ({fix.RemoveCount} removed) to: {string.Join(" / ", fix.NewLines)}");
            if (finding.Error is { } error) output.WriteLine("  as F# said it:\n    " + error.RawText.Replace("\n", "\n    "));
        }

        foreach (var line in report.Run?.Run?.Lines ?? []) output.WriteLine($"  | {line.Text}");
        return report;
    }

    /// <summary>
    /// Runs F# code here as FixFinder runs a script, typing <paramref name="input"/>, and gives back what it printed - failing on
    /// anything F# said, unless <paramref name="mayWarn"/>, when only a failure fails.
    /// </summary>
    private async Task<string> RunAsync(string dotnet, string code, string name, string input = "", bool mayWarn = false)
    {
        var file = Write(Path.Combine("ran", name, "main.fsx"), code);
        using var running = Process.Start(new ProcessStartInfo(dotnet, $"{FSharpScript.RunArguments} main.fsx")
        {
            WorkingDirectory = Path.GetDirectoryName(file)!, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        })!;
        await running.StandardInput.WriteAsync(input);
        running.StandardInput.Close();
        var printed = await running.StandardOutput.ReadToEndAsync();
        var said = await running.StandardError.ReadToEndAsync();
        await running.WaitForExitAsync();

        Assert.True(running.ExitCode == 0 && (mayWarn || said.Trim().Length == 0), $"{name}: F# said:\n{said}");
        return printed.Trim().ReplaceLineEndings("\n");
    }

    private static string Changed(string code, int startLine, int removeCount, IReadOnlyList<string> newLines)
    {
        var lines = code.TrimEnd('\n').Split('\n').ToList();
        lines.RemoveRange(startLine - 1, removeCount);
        lines.InsertRange(startLine - 1, newLines);
        return string.Join("\n", lines) + "\n";
    }

    private static string Changed(string code, Finding found) => Changed(code, found.Fix!.StartLine, found.Fix.RemoveCount, found.Fix.NewLines);

    [Theory]
    [InlineData("let total = 10\nlet count = 2\nprintfn \"%d\" (totl / count)\n", "fsharp-did-you-mean", 3, 3, "printfn \"%d\" (total / count)", "5")]
    [InlineData("let marks = [70; 80]\nprintfn \"%d\" (List.lenght marks)\n", "fsharp-did-you-mean", 2, 2, "printfn \"%d\" (List.length marks)", "2")]
    [InlineData("let factorial n =\n    if n <= 1 then 1 else n * factorial (n - 1)\nprintfn \"%d\" (factorial 5)\n", "fsharp-missing-rec", 2, 1, "let rec factorial n =", "120")]
    [InlineData("let count = 0\n\ncount <- count + 1\nprintfn \"%d\" count\n", "fsharp-not-mutable", 3, 1, "let mutable count = 0", "1")]
    [InlineData("let mark = 70\nif mark != 100 then printfn \"not full\"\n", "fsharp-comparison-operator", 2, 2, "if mark <> 100 then printfn \"not full\"", "not full")]
    [InlineData("let mark = 100\nif mark == 100 then printfn \"full\"\n", "fsharp-comparison-operator", 2, 2, "if mark = 100 then printfn \"full\"", "full")]
    [InlineData("let double x =\n    return x * 2\nprintfn \"%d\" (double 4)\n", "fsharp-return", 2, 2, "    x * 2", "8")]
    [InlineData("for mark in [70; 80]\n    printfn \"%d\" mark\n", "fsharp-missing-do", 1, 1, "for mark in [70; 80] do", "70\n80")]
    [InlineData("let mark = 70\nif mark > 50\n    printfn \"pass\"\n", "fsharp-missing-then", 2, 2, "if mark > 50 then", "pass")]
    [InlineData("let finished = false\nif !finished then printfn \"keep going\"\n", "fsharp-not", 2, 2, "if not finished then printfn \"keep going\"", "keep going")]
    [InlineData("let name = \"Ada\"\nprintfn \"Name: %d\" name\n", "fsharp-printf-format", 2, 2, "printfn \"Name: %s\" name", "Name: Ada")]
    [InlineData("let mark = 70\nprintfn \"Mark: %s\" mark\n", "fsharp-printf-format", 2, 2, "printfn \"Mark: %d\" mark", "Mark: 70")]
    [InlineData("let name = \"Ada\"\nlet average = 72.5\nprintfn \"%s has %d\" name average\n", "fsharp-printf-format", 3, 3, "printfn \"%s has %f\" name average", "Ada has 72.500000")]
    [InlineData("let mark = 70\nprintfn \"%s\" (\"Mark: \" + mark)\n", "fsharp-number-to-text", 2, 2, "printfn \"%s\" (\"Mark: \" + string mark)", "Mark: 70")]
    [InlineData("let marks = [70; 80; 90]\nprintfn \"%d\" (List.sum marks\nprintfn \"done\"\n", "fsharp-unmatched-bracket", 2, 2, "printfn \"%d\" (List.sum marks)", "240\ndone")]
    [InlineData("let area (width: int) (height: int) = width * height\nlet result = area(3, 4)\nprintfn \"%d\" result\n", "fsharp-curried-arguments", 2, 2, "let result = area 3 4", "12")]
    [InlineData("let area (width: int) (height: int) = width * height\nlet width = 2\nlet result = area (width + 1, 4)\nprintfn \"%d\" result\n", "fsharp-curried-arguments", 3, 3, "let result = area (width + 1) 4", "12")]
    public async Task WhatFSharpRefusesIsFoundBeforeItRunsAndItsChangeChecksAndRuns(string code, string rule, int line, int changedLine, string changedTo, string fixedPrints)
    {
        if (Dotnet() is not { } dotnet) return;

        var report = await CheckAsync(Write(Path.Combine($"refused-{rule}-{code.Length}", "main.fsx"), code));

        var refused = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Syntax);
        Assert.Equal((rule, line), (refused.RuleId, refused.Line!.Value));
        Assert.Equal(changedLine, refused.Fix?.StartLine);
        Assert.Equal(changedTo, refused.Fix?.NewLines.Single());
        Assert.Null(report.Run?.Run);
        Assert.Equal(fixedPrints, await RunAsync(dotnet, Changed(code, refused), $"refused-{rule}-{code.Length}"));
    }

    [Theory]
    [InlineData("let average total count = total / count\nlet marks : int list = []\nprintfn \"%d\" (average (List.sum marks) (List.length marks))\n",
        "fsharp-division-guard", 1, 1, "let average total count = (if count = 0 then 0 else total / count)", "0")]
    [InlineData("let marks = [|70; 80; 90|]\nlet mutable total = 0\nfor index in 0 .. marks.Length do\n    total <- total + marks.[index]\nprintfn \"%d\" total\n",
        "fsharp-loop-to-length", 4, 3, "for index in 0 .. marks.Length - 1 do", "240")]
    [InlineData("let marks = [|70; 80; 90|]\nlet mutable total = 0\nfor index = 0 to marks.Length do\n    total <- total + marks.[index]\nprintfn \"%d\" total\n",
        "fsharp-loop-to-length", 4, 3, "for index = 0 to marks.Length - 1 do", "240")]
    public async Task WhatStopsAScriptIsFoundAtItsLineAndItsChangeRuns(string code, string rule, int line, int changedLine, string changedTo, string fixedPrints)
    {
        if (Dotnet() is not { } dotnet) return;

        var report = await CheckAsync(Write(Path.Combine($"stopped-{rule}-{code.Length}", "main.fsx"), code));

        var stopped = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Runtime);
        Assert.Equal((rule, line), (stopped.RuleId, stopped.Line!.Value));
        Assert.Equal(changedLine, stopped.Fix?.StartLine);
        Assert.Equal(changedTo, stopped.Fix?.NewLines.Single());
        Assert.Equal(fixedPrints, await RunAsync(dotnet, Changed(code, stopped), $"stopped-{rule}-{code.Length}"));
    }

    [Theory]
    // F# warns of = used to give a new value inside a function, and runs on: count stays 0.
    [InlineData("let countUp () =\n    let mutable count = 0\n    count = count + 1\n    count\nprintfn \"%d\" (countUp ())\n",
        "fsharp-assign-with-arrow", 3, "    count <- count + 1", "0", "1")]
    // [70, 80, 90] is a list of one tuple; F# notes it, and runs on.
    [InlineData("let marks = [70, 80, 90]\nprintfn \"%A\" marks\n", "fsharp-list-semicolons", 1, "let marks = [70; 80; 90]", "[(70, 80, 90)]", "[70; 80; 90]")]
    public async Task WhatFSharpWarnsOfIsFoundAndItsChangeRunsWithoutTheWarning(string code, string rule, int line, string changedTo, string mistakePrints, string fixedPrints)
    {
        if (Dotnet() is not { } dotnet) return;

        var report = await CheckAsync(Write(Path.Combine($"warned-{rule}", "main.fsx"), code));

        var warned = Assert.Single(report.Findings, finding => finding.RuleId == rule);
        Assert.Equal(line, warned.Line);
        Assert.Equal(changedTo, warned.Fix?.NewLines.Single());
        Assert.Equal(mistakePrints, await RunAsync(dotnet, code, $"warned-{rule}-mistake", mayWarn: true));
        Assert.Equal(fixedPrints, await RunAsync(dotnet, Changed(code, warned), $"warned-{rule}-change"));
    }

    [Fact]
    public async Task ACrashInAFileTheScriptLoadsIsPlacedThereAndItsChangeIsCheckedWithTheScript()
    {
        if (Dotnet() is not { } dotnet) return;

        var helpers = Write(Path.Combine("loaded", "helpers.fsx"), "module Helpers\nlet average total count = total / count\n");
        var main = Write(Path.Combine("loaded", "main.fsx"), "#load \"helpers.fsx\"\nprintfn \"%d\" (Helpers.average 150 2)\nprintfn \"%d\" (Helpers.average 0 0)\n");

        var report = await CheckAsync(main);

        var stopped = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Runtime);
        Assert.Equal((helpers, 2, "fsharp-division-guard"), (stopped.File, stopped.Line!.Value, stopped.RuleId));
        Assert.Equal("75", report.Run?.Run?.Lines.First(line => line.Stream == StreamKind.StdOut).Text);

        File.WriteAllText(helpers, Changed(File.ReadAllText(helpers), stopped));
        Assert.Equal("75\n0", await RunLoaded(dotnet, main));
    }

    /// <summary>Runs a script that loads files beside it, in its own folder, and gives back what it printed.</summary>
    private static async Task<string> RunLoaded(string dotnet, string main)
    {
        using var running = Process.Start(new ProcessStartInfo(dotnet, $"{FSharpScript.RunArguments} {Path.GetFileName(main)}")
        {
            WorkingDirectory = Path.GetDirectoryName(main)!, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        })!;
        var printed = await running.StandardOutput.ReadToEndAsync();
        var said = await running.StandardError.ReadToEndAsync();
        await running.WaitForExitAsync();

        Assert.True(running.ExitCode == 0 && said.Trim().Length == 0, said);
        return printed.Trim().ReplaceLineEndings("\n");
    }

    [Fact]
    public async Task AScriptThatIsOneExpressionIsNotToldOfAnErrorAndRuns()
    {
        if (Dotnet() is null) return;

        // dotnet fsi --typecheck-only says "error FS0971: Undefined value 'it'" of this script, which runs as it should.
        var report = await CheckAsync(Write(Path.Combine("one-expression", "main.fsx"), "for mark in [70; 80] do\n    printfn \"%d\" mark\n"));

        Assert.Empty(report.Findings);
        Assert.Equal(["70", "80"], report.Run?.Run?.Lines.Where(line => line.Stream == StreamKind.StdOut).Select(line => line.Text.Trim()));
    }

    [Fact]
    public async Task AFunctionThatCallsItselfForeverIsFoundByTheCallItRepeated()
    {
        if (Dotnet() is null) return;

        var report = await CheckAsync(Write(Path.Combine("overflow", "main.fsx"), "let rec count n = 1 + count (n + 1)\nprintfn \"%d\" (count 0)\n"));

        var stopped = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Runtime);
        Assert.Equal(FSharpParser.StackOverflow, stopped.Error?.ExceptionType);
        Assert.EndsWith("FSI_0001.count(Int32)", stopped.Error?.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string, string, int, string, string, string> LogicMistakes => new()
    {
        { "let mutable total = 0\ntotal = total + 70\nprintfn \"%d\" total\n", "logic-fsharp-comparison-for-assignment", 2, "", "0", "70" },
        { "let marks = [80; 70; 90]\nList.sort marks\nprintfn \"%A\" marks\n", "logic-fsharp-result-discarded", 2, "", "[80; 70; 90]", "[70; 80; 90]" },
        { "let total = 7\nlet count = 2\nlet average = float (total / count)\nprintfn \"%.1f\" average\n", "logic-fsharp-whole-number-division", 3, "", "3.0", "3.5" },
        { "let price = 0.1 + 0.2\nif price = 0.3 then printfn \"equal\" else printfn \"not equal\"\n", "logic-fsharp-float-equality", 2, "", "not equal", "equal" },
        { "let answer = stdin.ReadLine()\nif answer > \"10\" then printfn \"big\" else printfn \"small\"\n", "logic-fsharp-input-compared-as-text", 2, "7\n", "big", "small" },
    };

    [Theory]
    [MemberData(nameof(LogicMistakes))]
    public async Task EachLogicMistakeRunsWithTheWrongAnswerAndItsChangeWithTheRightOne(string code, string check, int line, string input, string mistakePrints, string changePrints)
    {
        if (Dotnet() is not { } dotnet) return;

        var path = Write(Path.Combine($"logic-{check}", "main.fsx"), code);
        var found = Assert.Single(LogicPatterns.Scan(SourceFile.Read(path)!), finding => finding.PatternId == check);
        Assert.Equal(line, found.Line);

        Assert.Equal(mistakePrints, await RunAsync(dotnet, code, $"logic-{check}-mistake", input));
        Assert.Equal(changePrints, await RunAsync(dotnet, Changed(code, found.Fix!.StartLine, found.Fix.RemoveCount, found.Fix.NewLines), $"logic-{check}-change", input));
    }

    /// <summary>Every whole script an F# guide shows checks with nothing said, and runs with nothing printed to the error stream.</summary>
    [Fact]
    public async Task EveryExampleAnFSharpGuideShowsChecksAndRunsClean()
    {
        if (Dotnet() is not { } dotnet) return;

        var examples = FSharpGuides.All.Concat(FSharpPatternGuides.All).Select(entry => entry.Guide.Example).Where(example => example.Length > 0).Distinct().ToList();

        foreach (var (example, number) in examples.Select((example, index) => (example, index + 1)))
        {
            var file = Write(Path.Combine("examples", $"example{number}", "main.fsx"), example + "\n");
            var check = ScriptLanguages.CheckOf(ScriptLanguages.FSharp, file, TimeSpan.FromMinutes(1))!;

            using var checking = Process.Start(new ProcessStartInfo(check.ExecutablePath, check.Arguments)
                { WorkingDirectory = check.WorkingDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true })!;
            var said = await checking.StandardOutput.ReadToEndAsync() + await checking.StandardError.ReadToEndAsync();
            await checking.WaitForExitAsync();
            Assert.True(checking.ExitCode == 0 && said.Trim().Length == 0, $"example {number}:\n{example}\nthe check said:\n{said}");

            await RunAsync(dotnet, example + "\n", $"example{number}");
        }

        output.WriteLine($"{examples.Count} examples checked and run.");
    }
}

/// <summary>What F#'s logic checks leave alone - code that does what it says - and a script FixFinder will not run.</summary>
public class FSharpScriptTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Write(string code)
    {
        var path = Path.Combine(_temp.Path, $"main{code.Length}.fsx");
        File.WriteAllText(path, code);
        return path;
    }

    private static readonly string[] Checks =
    [
        "logic-fsharp-comparison-for-assignment", "logic-fsharp-result-discarded", "logic-fsharp-whole-number-division",
        "logic-fsharp-loop-to-length", "logic-fsharp-float-equality", "logic-fsharp-input-compared-as-text",
    ];

    [Theory]
    // <- gives the new value; and inside a function, where F# warns of =, the warning's own rule is the one that speaks.
    [InlineData("let mutable total = 0\ntotal <- total + 70\nprintfn \"%d\" total\n")]
    [InlineData("let countUp () =\n    let mutable count = 0\n    count = count + 1\n    count\n")]
    // A comparison kept, or a value that is not mutable.
    [InlineData("let total = 0\nlet same = total = 0\nprintfn \"%b\" same\n")]
    // The new list kept, or given back as a function's last line.
    [InlineData("let marks = [80; 70]\nlet sortedMarks = List.sort marks\nprintfn \"%A\" sortedMarks\n")]
    [InlineData("let sorted marks =\n    List.sort marks\n\nprintfn \"%A\" (sorted [80; 70])\n")]
    // Decimals divided as decimals.
    [InlineData("let total = 7.0\nlet count = 2.0\nlet average = float (total / count)\nprintfn \"%.1f\" average\n")]
    // A loop that stops at the last position, or one that never reads the array at its number.
    [InlineData("let marks = [|70; 80|]\nfor index in 0 .. marks.Length - 1 do\n    printfn \"%d\" marks.[index]\n")]
    [InlineData("let marks = [|70; 80|]\nfor count in 0 .. marks.Length do\n    printfn \"%d\" count\n")]
    // A decimal given as it is, not worked out.
    [InlineData("let price = 0.3\nif price = 0.3 then printfn \"equal\"\n")]
    // A line read and compared with text for equality, or turned into a number first.
    [InlineData("let answer = stdin.ReadLine()\nif answer = \"yes\" then printfn \"ok\"\n")]
    [InlineData("let answer = stdin.ReadLine()\nif int answer > 10 then printfn \"big\"\n")]
    public void CodeThatDoesWhatItSaysIsLeftAlone(string code) =>
        Assert.DoesNotContain(LogicPatterns.Scan(SourceFile.Read(Write(code))!), finding => Checks.Contains(finding.PatternId));

    [Fact]
    public void ALoopToTheLengthThatReadsTheArrayIsFound()
    {
        var found = LogicPatterns.Scan(SourceFile.Read(Write("let marks = [|70; 80|]\nfor index = 0 to marks.Length do\n    printfn \"%d\" marks.[index]\n"))!);
        Assert.Equal(2, Assert.Single(found, finding => finding.PatternId == "logic-fsharp-loop-to-length").Line);
    }

    [Fact]
    public void ANewListWhoseOldNameIsGivenAMeaningOfItsOwnLaterIsFoundWithNoChange()
    {
        var found = LogicPatterns.Scan(SourceFile.Read(Write("let marks = [80; 70; 90]\nList.sort marks\nlet best marks = List.max marks\nprintfn \"%A\" marks\n"))!);
        var discarded = Assert.Single(found, finding => finding.PatternId == "logic-fsharp-result-discarded");
        Assert.Null(discarded.Fix);
    }

    [Fact]
    public void AScriptThatFetchesANuGetPackageIsNotRun()
    {
        var launch = TargetFactory.FromFile(Write("#r \"nuget: Newtonsoft.Json\"\nprintfn \"hi\"\n"));

        Assert.False(launch.Ok);
        Assert.Contains("a check never downloads anything", launch.Problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("let total = List.fold (*) 1 [2; 3]", "let total = List.fold (*) 1 [2; 3]")]
    [InlineData("let total = 1 (* a comment *) + 2", "let total = 1                 + 2")]
    [InlineData("let total = 1 // a comment", "let total = 1             ")]
    [InlineData("let path = @\"C:\\marks\\\" + \"x\"", "let path = @\"         \" + \" \"")]
    [InlineData("let name' = 'a'", "let name' = ' '")]
    public void FSharpCodeIsReadWithoutItsTextAndComments(string line, string masked) =>
        Assert.Equal(masked, CodeText.Mask(line, Syntax.FSharp));
}
