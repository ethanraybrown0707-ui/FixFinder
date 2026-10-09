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
/// OCaml programs checked whole, as the window checks them: built by ocamlc in FixFinder's own folder and run by ocamlrun,
/// their mistakes found at their lines and their fixes compiled - and run - in copies; and every example a guide shows,
/// compiled. What OCaml printed is written to the test's output. A computer without OCaml passes over them and says so -
/// this one cannot run OCaml, so they run on GitHub's runners.
/// </summary>
public class OCamlLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private bool Available()
    {
        if (OCamlToolchains.Usual is { } ocaml)
        {
            output.WriteLine($"Checked with {ocaml.Description}: {ocaml.Ocamlc}");
            return true;
        }

        output.WriteLine("Not run on this computer: no OCaml is here.");
        return false;
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
        output.WriteLine(launch.Explanation);

        using var http = new FixFinderHttpClient();
        var report = await new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.OCaml }.CheckAsync(launch);

        foreach (var note in report.Notes) output.WriteLine($"note: {note}");
        foreach (var finding in report.Findings)
        {
            output.WriteLine($"{Path.GetFileName(finding.File)}:{finding.Line}: {finding.RuleId}: {finding.Title} | {finding.FixCheckedBy} | {finding.Verified.Summary}");
            if (finding.Error is { } error) output.WriteLine("  as OCaml said it:\n    " + error.RawText.Replace("\n", "\n    "));
        }

        foreach (var line in report.Run?.Run?.Lines ?? []) output.WriteLine($"  | {line.Text}");
        return report;
    }

    /// <summary>
    /// OCaml's bytecode names no place for the division itself, only the call of the function that divided: the crash is
    /// at that call, and the division the function makes is the one changed - and the change is run in a copy.
    /// </summary>
    [Fact]
    public async Task AWholeNumberDividedByZeroIsFoundAtTheCallOCamlNamesAndItsFixIsRunInACopy()
    {
        if (!Available()) return;

        var program = Write(Path.Combine("average", "average.ml"), "let average total count = total / count\n\nlet () = print_int (average 10 0)\n");

        var report = await CheckAsync(program);

        var crash = Assert.Single(report.Findings, finding => finding.RuleId == "ocaml-division-guard");
        Assert.Equal(3, crash.Line);
        Assert.Contains("Division_by_zero", crash.Title);
        Assert.Equal(1, crash.Fix?.StartLine);
        Assert.Contains("(if count = 0 then 0 else total / count)", crash.CorrectedExample);
        Assert.True(crash.Verified.IsVerified, crash.Verified.Summary);
    }

    [Fact]
    public async Task AFunctionThatCallsItselfWithoutRecGetsRecWhereOCamlSays()
    {
        if (!Available()) return;

        var program = Write(Path.Combine("facto", "facto.ml"), "let facto n =\n  if n = 0 then 1 else n * facto (n - 1)\n\nlet () = print_int (facto 5)\n");

        var report = await CheckAsync(program);

        var unbound = Assert.Single(report.Findings);
        Assert.Equal("ocaml-add-rec", unbound.RuleId);
        Assert.Contains("compiled it with ocamlc", unbound.FixCheckedBy);
        Assert.Contains("let rec facto n =", unbound.CorrectedExample);
    }

    [Fact]
    public async Task AMisspeltNameIsChangedToOCamlsOwnSuggestion()
    {
        if (!Available()) return;

        var program = Write(Path.Combine("values", "values.ml"), "let value1 = 3\n\nlet () = print_int (valeu1 + 1)\n");

        var report = await CheckAsync(program);

        var misspelt = Assert.Single(report.Findings);
        Assert.Equal(3, misspelt.Line);
        Assert.Equal("ocaml-hint", misspelt.RuleId);
        Assert.Contains("value1 + 1", misspelt.CorrectedExample);
    }

    [Fact]
    public async Task AnIntOperatorBetweenFloatsBecomesTheFloatOperator()
    {
        if (!Available()) return;

        var program = Write(Path.Combine("sum", "sum.ml"), "let total = 2.5 + 1.5\n\nlet () = print_float total\n");

        var report = await CheckAsync(program);

        var mismatch = Assert.Single(report.Findings);
        Assert.Equal("ocaml-float-operator", mismatch.RuleId);
        Assert.Contains("2.5 +. 1.5", mismatch.CorrectedExample);
    }

    [Fact]
    public async Task AMatchThatMissesACaseIsWarnedOfAndItsMatchFailureFound()
    {
        if (!Available()) return;

        var program = Write(Path.Combine("grades", "grade.ml"), """
            type mark = Pass | Fail | Merit

            let describe mark =
              match mark with
              | Pass -> "passed"
              | Fail -> "failed"

            let () = print_endline (describe Merit)
            """);

        var report = await CheckAsync(program);

        var warning = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Logic && finding.Severity == Severity.Warning);
        Assert.Equal("partial-match", warning.Error?.ErrorCode);
        Assert.Contains("Match_failure", warning.Explanation);

        Assert.Contains(report.Findings, finding => finding.Title.Contains("Match_failure", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ALoopToTheLengthOfAnArrayIsFoundAndStopsOneBeforeIt()
    {
        if (!Available()) return;

        var program = Write(Path.Combine("marks", "marks.ml"), "let marks = [| 70; 80; 90 |]\n\nlet () =\n  for i = 0 to Array.length marks do\n    print_int marks.(i)\n  done\n");

        var report = await CheckAsync(program);

        // The crash, on line 5, and the loop check, on line 4, make the same change, so they are one finding.
        var loop = Assert.Single(report.Findings, finding => finding.CorrectedExample.Contains("Array.length marks - 1", StringComparison.Ordinal));
        Assert.Contains("index out of bounds", loop.Title);
    }

    [Fact]
    public async Task ACrashInAnotherFileOfTheProgramIsFixedInThatFileAndNothingIsWrittenBesideIt()
    {
        if (!Available()) return;

        var helper = Write(Path.Combine("program", "helper.ml"), "let divide a b = a / b\n");
        var main = Write(Path.Combine("program", "main.ml"), "let () = print_int (Helper.divide 10 0)\n");

        var report = await CheckAsync(main);

        var crash = Assert.Single(report.Findings, finding => finding.Title.Contains("Division_by_zero", StringComparison.Ordinal));
        Assert.Equal((main, 1), (crash.File, crash.Line!.Value));
        Assert.Equal((helper, 1), (crash.Fix?.File, crash.Fix?.StartLine));
        Assert.True(crash.Verified.IsVerified, crash.Verified.Summary);
        Assert.Equal(new[] { "helper.ml", "main.ml" }, Directory.GetFiles(Path.GetDirectoryName(main)!).Select(Path.GetFileName).Order());
    }

    /// <summary>Every whole program a guide shows compiles with ocamlc, with no warning.</summary>
    [Fact]
    public async Task EveryExampleAnOCamlGuideShowsCompilesWithNoWarning()
    {
        if (!Available() || OCamlToolchains.Usual is not { } ocaml) return;

        var examples = OCamlGuides.All.Concat(OCamlPatternGuides.All).Select(entry => entry.Guide.Example).Where(example => example.Length > 0).Distinct().ToList();
        var failed = new List<string>();

        foreach (var (example, number) in examples.Select((example, index) => (example, index + 1)))
        {
            var folder = Path.Combine(_temp.Path, "examples", $"example{number}");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "example.ml"), example + "\n");

            using var compiling = Process.Start(new ProcessStartInfo(ocaml.Ocamlc, "-o example.byte example.ml")
            {
                WorkingDirectory = folder, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            })!;

            var said = await compiling.StandardError.ReadToEndAsync() + await compiling.StandardOutput.ReadToEndAsync();
            await compiling.WaitForExitAsync();

            if (compiling.ExitCode != 0 || said.Contains("Warning", StringComparison.Ordinal))
                failed.Add($"example {number}:\n{example}\nocamlc said:\n{said}");
        }

        output.WriteLine($"{examples.Count} examples compiled.");
        Assert.True(failed.Count == 0, string.Join("\n\n", failed));
    }
}
