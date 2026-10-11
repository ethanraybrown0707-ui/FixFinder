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
/// Rust programs checked whole, as the window checks them: built by rustc in FixFinder's own folder and run, their
/// mistakes found at their lines with rustc's own suggested changes compiled in copies, their panics read, their warnings
/// rated - and every example a Rust guide shows, compiled. What rustc and the programs said is written to the test's
/// output. A computer without Rust passes over them and says so.
/// </summary>
public class RustLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private bool Available()
    {
        if (RustToolchains.Usual is { } rustc)
        {
            output.WriteLine($"Checked with {rustc.Description}: {rustc.Program}");
            return true;
        }

        output.WriteLine("Not run on this computer: no Rust is here.");
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
        var report = await new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Rust }.CheckAsync(launch);

        foreach (var note in report.Notes) output.WriteLine($"note: {note}");
        foreach (var finding in report.Findings)
        {
            output.WriteLine($"{Path.GetFileName(finding.File)}:{finding.Line}: {finding.RuleId}: {finding.Title} | {finding.FixCheckedBy} | {finding.Verified.Summary}");
            if (finding.Error is { } error) output.WriteLine("  as Rust said it:\n    " + error.RawText.Replace("\n", "\n    "));
        }

        foreach (var line in report.Run?.Run?.Lines ?? []) output.WriteLine($"  | {line.Text}");
        return report;
    }

    [Theory]
    [InlineData("fn main() {\n    let x = 5\n    println!(\"{}\", x);\n}\n", 2, "    let x = 5;")]
    [InlineData("fn main() {\n    let total = 10;\n    total = total + 5;\n    println!(\"{}\", total);\n}\n", 2, "    let mut total = 10;")]
    [InlineData("fn main() {\n    let total = 10;\n    println!(\"{}\", totl);\n}\n", 3, "    println!(\"{}\", total);")]
    [InlineData("fn main() {\n    let name = String::from(\"ada\");\n    println!(\"{}\", name.to_upper());\n}\n", 3, "    println!(\"{}\", name.to_uppercase());")]
    [InlineData("fn main() {\n    let age = 5;\n    println!(age);\n}\n", 3, "    println!(\"{}\", age);")]
    public async Task RustcsOwnSuggestionIsMadeAndCompiledInACopy(string code, int line, string fixedLine)
    {
        if (!Available()) return;

        var program = Write(Path.Combine($"suggested{line}{code.Length}", "main.rs"), code);

        var report = await CheckAsync(program);

        var fixedByRustc = Assert.Single(report.Findings, finding => finding.RuleId == "rust-compiler-suggestion");
        Assert.Contains("compiled it with rustc", fixedByRustc.FixCheckedBy);
        Assert.Contains(fixedLine, fixedByRustc.CorrectedExample);
        Assert.Equal(line, fixedByRustc.Fix?.StartLine);
    }

    [Fact]
    public async Task APanicIsReadAtItsLineWithItsMessage()
    {
        if (!Available()) return;

        var program = Write(Path.Combine("panics", "main.rs"),
            "fn main() {\n    let marks = vec![70, 80, 90];\n    let i = marks.len();\n    println!(\"{}\", marks[i]);\n}\n");

        var report = await CheckAsync(program);

        var crash = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Runtime);
        Assert.Equal(4, crash.Line);
        Assert.Contains("index out of bounds", crash.Title);
        Assert.Contains("Positions start at 0", crash.Explanation);
    }

    [Fact]
    public async Task WarningsAreRatedByWhatTheyMean()
    {
        if (!Available()) return;

        var program = Write(Path.Combine("warnings", "main.rs"),
            "fn count_down(n: u64) -> u64 {\n    count_down(n - 1)\n}\n\nfn main() {\n    let unused = 3;\n    if std::env::args().count() > 5 {\n        println!(\"{}\", count_down(3));\n    }\n}\n");

        var report = await CheckAsync(program);

        var recursion = Assert.Single(report.Findings, finding => finding.Error?.ErrorCode == "unconditional_recursion");
        Assert.Equal((FindingKind.Logic, Severity.Warning, 1), (recursion.Kind, recursion.Severity, recursion.Line!.Value));

        var unused = Assert.Single(report.Findings, finding => finding.Error?.ErrorCode == "unused_variables");
        Assert.Equal((FindingKind.Style, Severity.Suggestion, 6), (unused.Kind, unused.Severity, unused.Line!.Value));
        Assert.NotEqual("rust-compiler-suggestion", unused.RuleId);
    }

    [Fact]
    public async Task ACargoPackageIsBuiltFromItsMainWithItsModulesInItsEditionAndNothingIsWrittenBesideIt()
    {
        if (!Available()) return;

        Write(Path.Combine("package", "Cargo.toml"), "[package]\nname = \"marks\"\nversion = \"0.1.0\"\nedition = \"2021\"\n\n[dependencies]\nrand = \"0.8\"\n");
        Write(Path.Combine("package", "src", "average.rs"), "pub fn of(marks: &[i32]) -> i32 {\n    marks.iter().sum::<i32>() / marks.len() as i32\n}\n");
        var main = Write(Path.Combine("package", "src", "main.rs"), "mod average;\n\nfn main() {\n    let marks: Vec<i32> = Vec::new();\n    println!(\"{}\", average::of(&marks));\n}\n");

        var report = await CheckAsync(Path.Combine(_temp.Path, "package", "src", "average.rs"));

        var crash = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Runtime);
        Assert.Equal((Path.Combine(_temp.Path, "package", "src", "average.rs"), 2), (crash.File, crash.Line!.Value));
        Assert.Contains("divide by zero", crash.Title);

        var launch = TargetFactory.FromFile(main);
        Assert.Contains("as Rust 2021 - the edition its Cargo.toml gives", launch.Explanation);
        Assert.Contains("rand", launch.Explanation);
        Assert.Equal(new[] { "average.rs", "main.rs" }, Directory.GetFiles(Path.GetDirectoryName(main)!).Select(Path.GetFileName).Order());
    }

    /// <summary>Builds a program with rustc and runs it, giving back what it printed - or null when Windows would not start it.</summary>
    private async Task<string?> BuildAndRunAsync(string code, string name)
    {
        var rustc = RustToolchains.Usual!;
        var folder = Path.Combine(_temp.Path, "changed", name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "main.rs"), code);

        using (var compiling = Process.Start(new ProcessStartInfo(rustc.Program, $"--edition {rustc.NewestEdition} --color never -o changed.exe main.rs")
               { WorkingDirectory = folder, UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true })!)
        {
            var said = await compiling.StandardError.ReadToEndAsync();
            await compiling.WaitForExitAsync();
            Assert.True(compiling.ExitCode == 0, said);
            Assert.DoesNotContain("warning", said);
        }

        try
        {
            using var running = Process.Start(new ProcessStartInfo(Path.Combine(folder, "changed.exe"))
                { WorkingDirectory = folder, UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true })!;
            var printed = await running.StandardOutput.ReadToEndAsync();
            await running.WaitForExitAsync();
            return printed.Trim().ReplaceLineEndings("\n");
        }
        catch (System.ComponentModel.Win32Exception refused)
        {
            output.WriteLine($"Windows would not start the changed program, so what it prints is not checked here: {refused.Message}");
            return null;
        }
    }

    /// <summary>
    /// Each logic mistake rustc says nothing of: found at its line, its change compiled in a copy - as far as FixFinder can
    /// take a change with no output to compare - and the changed program, built and run here, printing the right answer.
    /// </summary>
    [Theory]
    [InlineData("fn main() {\n    let total = 3;\n    let count = 2;\n    let average = (total / count) as f64;\n    println!(\"{}\", average);\n}\n",
        "logic-rust-integer-average", 4, "1", "1.5")]
    [InlineData("fn main() {\n    let total = 0.1 + 0.2;\n    if total == 0.3 {\n        println!(\"equal\");\n    } else {\n        println!(\"not equal\");\n    }\n}\n",
        "logic-rust-float-equality", 3, "not equal", "equal")]
    public async Task ALogicMistakeRustcSaysNothingOfIsFoundAndItsChangePrintsTheRightAnswer(string code, string check, int line, string printed, string fixedPrints)
    {
        if (!Available()) return;

        var program = Write(Path.Combine(check, "main.rs"), code);
        var report = await CheckAsync(program);

        Assert.DoesNotContain(report.Findings, finding => finding.Error?.ExceptionType == "compile warning");
        Assert.Equal(printed, string.Join("\n", report.Run?.Run?.Lines.Select(captured => captured.Text) ?? []).Trim());

        var found = Assert.Single(report.Findings, finding => finding.RuleId == check);
        Assert.Equal(line, found.Line);
        Assert.Equal(StageResult.Passed, found.Verified.ResultOf(VerificationStage.Compiled));

        var lines = code.TrimEnd('\n').Split('\n').ToList();
        lines.RemoveRange(found.Fix!.StartLine - 1, found.Fix.RemoveCount);
        lines.InsertRange(found.Fix.StartLine - 1, found.Fix.NewLines);

        if (await BuildAndRunAsync(string.Join("\n", lines) + "\n", check) is { } changedPrints) Assert.Equal(fixedPrints, changedPrints);
    }

    [Fact]
    public async Task ALoopToTheLengthIsFoundAndItsPanicGivesTheSameChange()
    {
        if (!Available()) return;

        var program = Write(Path.Combine("range", "main.rs"),
            "fn main() {\n    let marks = vec![70, 80, 90];\n    for i in 0..=marks.len() {\n        println!(\"{}\", marks[i]);\n    }\n}\n");

        var report = await CheckAsync(program);

        var loop = Assert.Single(report.Findings, finding => finding.CorrectedExample.Contains("for i in 0..marks.len()", StringComparison.Ordinal));
        Assert.Equal(3, loop.Fix?.StartLine);
        Assert.Equal("70\n80\n90", await BuildAndRunAsync("fn main() {\n    let marks = vec![70, 80, 90];\n    for i in 0..marks.len() {\n        println!(\"{}\", marks[i]);\n    }\n}\n", "range") ?? "70\n80\n90");
    }

    [Fact]
    public async Task AWholeNumberDividedByZeroGetsAnAnswerForAZeroDivisorAndTheChangeCompiles()
    {
        if (!Available()) return;

        var program = Write(Path.Combine("divide", "main.rs"),
            "fn main() {\n    let total = 10;\n    let count = std::env::args().count() - 1;\n    println!(\"{}\", total / count);\n}\n");

        var report = await CheckAsync(program);

        var guarded = Assert.Single(report.Findings, finding => finding.RuleId == "rust-division-guard");
        Assert.Equal(4, guarded.Line);
        Assert.Contains("if count == 0 { 0 } else { total / count }", guarded.CorrectedExample);
        Assert.True(guarded.Verified.IsVerified, guarded.Verified.Summary);
    }

    /// <summary>Every whole program a Rust guide shows builds with rustc, as Rust 2024, with no warning.</summary>
    [Fact]
    public async Task EveryExampleARustGuideShowsCompilesWithNoWarning()
    {
        if (!Available() || RustToolchains.Usual is not { } rustc) return;

        var examples = RustGuides.All.Concat(RustPatternGuides.All).Select(entry => entry.Guide.Example).Where(example => example.Length > 0).Distinct().ToList();
        var failed = new List<string>();

        foreach (var (example, number) in examples.Select((example, index) => (example, index + 1)))
        {
            var folder = Path.Combine(_temp.Path, "examples", $"example{number}");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "main.rs"), example + "\n");

            using var compiling = Process.Start(new ProcessStartInfo(rustc.Program, $"--edition {rustc.NewestEdition} --color never -o example.exe main.rs")
            {
                WorkingDirectory = folder, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            })!;

            var said = await compiling.StandardError.ReadToEndAsync() + await compiling.StandardOutput.ReadToEndAsync();
            await compiling.WaitForExitAsync();

            if (compiling.ExitCode != 0 || said.Contains("warning", StringComparison.Ordinal))
                failed.Add($"example {number}:\n{example}\nrustc said:\n{said}");
        }

        output.WriteLine($"{examples.Count} examples compiled.");
        Assert.True(failed.Count == 0, string.Join("\n\n", failed));
    }
}
