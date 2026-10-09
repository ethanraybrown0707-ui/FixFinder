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
/// The Go logic checks against Go itself: every example their guides show builds with no word from go vet, and each
/// mistake, checked whole as the window checks it, does what its check says - go vet says nothing of it, and the program
/// runs on and prints the wrong thing - and is found at its line. FixFinder compiles its change in a copy; with no output
/// to compare with, that is as far as it can take it, so the test runs the changed program and checks what it prints. A
/// computer without Go passes over them and says so.
/// </summary>
public class GoLogicLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string? Go()
    {
        if (TargetFactory.FindOnPath("go") is { } go) return go;

        output.WriteLine("Not run on this computer: no Go is here.");
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
        var launch = TargetFactory.FromFile(file, LiveAllowance.For(file, TimeSpan.FromMinutes(6)));
        Assert.True(launch.Ok, launch.Problem);

        using var http = new FixFinderHttpClient();
        var report = await new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Go }.CheckAsync(launch);

        foreach (var note in report.Notes) output.WriteLine($"note: {note}");
        foreach (var finding in report.Findings)
            output.WriteLine($"{Path.GetFileName(finding.File)}:{finding.Line}: {finding.RuleId}: {finding.Title} | {finding.Verified.Summary}");
        foreach (var line in report.Run?.Run?.Lines ?? []) output.WriteLine($"  | {line.Text}");

        return report;
    }

    /// <summary>Runs go with these arguments in a folder, and gives back everything it said and how it ended.</summary>
    private static async Task<(int ExitCode, string Said)> GoAsync(string go, string folder, string arguments)
    {
        using var running = Process.Start(new ProcessStartInfo(go, arguments)
        {
            WorkingDirectory = folder, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            Environment = { ["GOPROXY"] = "off", ["GOTOOLCHAIN"] = "local" },
        })!;

        var said = await running.StandardError.ReadToEndAsync() + await running.StandardOutput.ReadToEndAsync();
        await running.WaitForExitAsync();
        return (running.ExitCode, said);
    }

    [Fact]
    public async Task EveryExampleAGoLogicGuideShowsBuildsWithNothingFromGoVet()
    {
        if (Go() is not { } go) return;

        var failed = new List<string>();

        foreach (var (entry, number) in GoPatternGuides.All.Select((entry, index) => (entry, index + 1)))
        {
            var folder = Path.Combine(_temp.Path, "examples", $"example{number}");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "main.go"), entry.Guide.Example + "\n");

            var (exitCode, said) = await GoAsync(go, folder, "vet main.go");
            if (exitCode != 0 || said.Trim().Length > 0) failed.Add($"example {number}:\n{entry.Guide.Example}\ngo vet said:\n{said}");
        }

        Assert.True(failed.Count == 0, string.Join("\n\n", failed));
    }

    /// <summary>
    /// Each mistake: what running it prints, the check that finds it and on which line, and what the program prints with
    /// the check's change - null for a check that has none. The loop past the end is left out here: its panic is found by
    /// the crash rule as well, and JsGoLiveTests runs it.
    /// </summary>
    public static TheoryData<string, string, string, int, string?> Mistakes => new()
    {
        {
            "package main\n\nimport \"fmt\"\n\nfunc main() {\n\ttotal := 3\n\tcount := 2\n\taverage := float64(total / count)\n\tfmt.Println(average)\n}\n",
            "1", "logic-go-integer-average", 8, "1.5"
        },
        {
            "package main\n\nimport \"fmt\"\n\nfunc main() {\n\tmarks := []int{70, 80, 90}\n\tfor _, mark := range marks {\n\t\tmark = mark + 5\n\t}\n\tfmt.Println(marks)\n}\n",
            "[70 80 90]", "logic-go-range-value-changed", 8, "[75 85 95]"
        },
        {
            "package main\n\nimport (\n\t\"fmt\"\n\t\"strconv\"\n)\n\nfunc main() {\n\tage, _ := strconv.Atoi(\"twelve\")\n\tfmt.Println(age + 1)\n}\n",
            "1", "logic-go-parse-error-ignored", 9, null
        },
        {
            "package main\n\nimport (\n\t\"fmt\"\n\t\"strings\"\n)\n\nfunc main() {\n\tname := \"ada\"\n\tstrings.ToUpper(name)\n\tfmt.Println(name)\n}\n",
            "ada", "logic-go-result-discarded", 10, "ADA"
        },
    };

    [Theory]
    [MemberData(nameof(Mistakes))]
    public async Task AMistakeGoSaysNothingOfRunsOnAndIsFound(string code, string printed, string check, int line, string? fixedPrints)
    {
        if (Go() is not { } go) return;

        var program = Write(Path.Combine(check, "main.go"), code);

        var (vetExitCode, vetSaid) = await GoAsync(go, Path.GetDirectoryName(program)!, "vet main.go");
        Assert.True(vetExitCode == 0 && vetSaid.Trim().Length == 0, $"go vet said: {vetSaid}");

        var report = await CheckAsync(program);

        Assert.Equal(printed, string.Join("\n", report.Run?.Run?.Lines.Select(captured => captured.Text) ?? []).Trim());
        var found = Assert.Single(report.Findings, finding => finding.RuleId == check);
        Assert.Equal(line, found.Line);

        if (fixedPrints is null)
        {
            Assert.Null(found.Fix);
            return;
        }

        Assert.NotNull(found.Fix);
        Assert.Equal(StageResult.Passed, found.Verified.ResultOf(VerificationStage.Compiled));
        Assert.False(found.Verified.SomethingFailed, found.Verified.Summary);

        var fix = found.Fix!;
        var lines = code.TrimEnd('\n').Split('\n').ToList();
        lines.RemoveRange(fix.StartLine - 1, fix.RemoveCount);
        lines.InsertRange(fix.StartLine - 1, fix.NewLines);
        var changed = Write(Path.Combine(check + "-changed", "main.go"), string.Join("\n", lines) + "\n");

        var (exitCode, said) = await GoAsync(go, Path.GetDirectoryName(changed)!, "run main.go");
        if (ApplicationControl.Refused(said))
        {
            output.WriteLine($"Windows refused to start the changed program, so what it prints is not checked here: {said}");
            return;
        }

        Assert.Equal((0, fixedPrints), (exitCode, said.Trim()));
    }
}
