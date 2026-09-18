using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;

namespace FixFinder.Tests;

/// <summary>
/// Picks a crashing file, launches it for real, and reads the error back - the whole loop.
/// </summary>
/// <remarks>
/// Everything else about these languages is tested against captured output. This is the part that
/// captured output cannot prove: that <see cref="TargetFactory"/> knows how to start the thing,
/// that <see cref="TargetRunner"/> gets its output back without deadlocking, and that the parser
/// then recognises what a real runtime on a real machine actually printed.
/// <para>
/// <b>Each case skips when its runtime is absent</b>, so this suite still passes on a machine - or
/// a CI runner - with none of them installed. A skipped case proves nothing and says so by not
/// running, which is better than a fixture pretending to be a live result.
/// </para>
/// <para>
/// <b>The trap that comes with that:</b> a skip is indistinguishable from a pass in the output. A
/// process inherits PATH at the moment it starts, so a test host launched from a shell that was
/// already open when a runtime was installed cannot see it, and every case here goes green without
/// running a thing. It looked exactly like eight passing end-to-end tests. If these are meant to
/// prove something, check the durations first - a real run of any of these is tens to hundreds of
/// milliseconds, and single digits means nothing happened.
/// </para>
/// </remarks>
public class LiveRuntimeTests
{
    /// <summary>Language, the program to run, the file to save it as, and what must come back.</summary>
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

    /// <summary>
    /// The claim behind every "exercised end to end" row in the README.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ACrashingProgramIsLaunchedAndItsErrorRead(
        string language, string runtime, string fileName, string source, string expected)
    {
        if (TargetFactory.FindOnPath(runtime) is null) return;

        using var temp = new TempFolder();

        var path = Path.Combine(temp.Path, fileName);
        File.WriteAllText(path, source);

        var plan = TargetFactory.FromFile(path, TimeSpan.FromSeconds(120));
        Assert.True(plan.Ok, plan.Problem);

        var run = await new TargetRunner(new ParserRegistry()).RunAsync(plan.Spec!, CancellationToken.None);
        if (ApplicationControl.Refused(run)) return;

        Assert.NotNull(run.Error);
        Assert.Equal(language, run.Error!.LanguageId);
        Assert.Contains(expected, run.Error.RawText, StringComparison.Ordinal);

        // A crash FixFinder cannot place is a crash it cannot help with, so the location matters
        // as much as the message.
        Assert.NotEmpty(run.Error.Frames);
    }
}
