using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;

namespace FixFinder.Tests;

/// <summary>Picks a crashing file, launches it for real, and reads the error back - the whole loop.</summary>
public class LiveRuntimeTests
{
    public static TheoryData<string, string, string, string, string> Cases => new()
    {
        {
            "node", "node", "crash.js",
            "console.log(\"starting\");\nconst cart = null;\nconsole.log(cart.total);\n",
            "Cannot read properties of null"
        },
        {
            "go", "go", "crash.go",
            "package main\n\nimport \"fmt\"\n\nfunc main() {\n\titems := []int{1, 2, 3}\n\tfmt.Println(\"starting\")\n\tfmt.Println(items[5])\n}\n",
            "index out of range"
        },
        {
            "ruby", "ruby", "crash.rb",
            "puts \"starting\"\nputs 1 / 0\n",
            "divided by 0"
        },
        {
            "php", "php", "crash.php",
            "<?php\nfunction total(string $a): int { return $a + 1; }\necho \"starting\\n\";\necho total(\"twelve\");\n",
            "Unsupported operand types"
        },
        {
            "perl", "perl", "crash.pl",
            "use strict;\nuse warnings;\nprint \"starting\\n\";\nmy $obj = bless {}, 'Manifest';\n$obj->parse();\n",
            "Can't locate object method"
        },
        {
            "lua", "lua", "crash.lua",
            "print(\"starting\")\nlocal settings = nil\nprint(settings.debug)\n",
            "attempt to index a nil value"
        },
        {
            "dart", "dart", "crash.dart",
            "void main() {\n  final items = [1, 2, 3];\n  print('starting');\n  print(items[5]);\n}\n",
            "Invalid value"
        },
        {
            "elixir", "elixir", "crash.exs",
            "defmodule Ledger do\n  def average(total, count), do: total / count\nend\n\nIO.puts(\"starting\")\nIO.puts(Ledger.average(1, 0))\n",
            "bad argument in arithmetic expression"
        },
        {
            "powershell", "powershell", "crash.ps1",
            "Write-Output \"starting\"\nGet-Content -Path 'C:\\definitely\\missing.csv'\n",
            "Cannot find path"
        },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ACrashingProgramIsLaunchedAndItsErrorRead(
        string language, string runtime, string fileName, string source, string expected)
    {
        if (TargetFactory.FindOnPath(runtime) is null) return;

        using var temp = new TempFolder();

        var path = Path.Combine(temp.Path, fileName);
        File.WriteAllText(path, source);

        // A crashing program prints its error the moment it runs, so the wait here is for the runtime to
        // get that far - and Go gets there by compiling the standard library first on a machine that never
        // has. Two minutes was not enough for that on a cold runner (run 35531473398, 2026-09-20): the
        // program was killed before it printed anything. Taking the allowance from TargetFactory means this
        // also fails if the wait the GUI gives a real target is ever wrong again.
        var allowance = TargetFactory.TimeoutFor(Path.GetExtension(fileName));
        if (allowance < TimeSpan.FromSeconds(120)) allowance = TimeSpan.FromSeconds(120);

        var plan = TargetFactory.FromFile(path, allowance);
        Assert.True(plan.Ok, plan.Problem);

        var run = await new TargetRunner(new ParserRegistry()).RunAsync(plan.Spec!, CancellationToken.None);
        if (ApplicationControl.Refused(run)) return;

        Assert.True(run.Error is not null, Said(run));
        Assert.Equal(language, run.Error!.LanguageId);
        Assert.Contains(expected, run.Error.RawText, StringComparison.Ordinal);

        Assert.NotEmpty(run.Error.Frames);
    }

    /// <summary>What the run actually did, so a failure can be read without opening the machine it ran on.</summary>
    /// <remarks>
    /// Left to itself this assertion fails with "value is null" and nothing else, which says only that the
    /// error was not read and never why - worth an afternoon of digging through a CI log to answer.
    /// </remarks>
    private static string Said(TargetRunResult run)
    {
        var printed = run.Lines.TakeLast(20).Select(l => $"  [{l.Stream}] {l.Text}").ToList();

        return $"No error was read: the run {run.Outcome} after {run.Duration.TotalSeconds:0.0}s"
            + $" (exit code {run.ExitCode?.ToString() ?? "none"}). {run.Explanation}"
            + (printed.Count == 0
                ? " It printed nothing at all."
                : Environment.NewLine + "It printed:" + Environment.NewLine + string.Join(Environment.NewLine, printed));
    }
}
