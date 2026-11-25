using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// A program that stops at an assert of its own - Java's, run with -ea; node:assert's; a Python program's that reads what
/// is typed in, so is not run as one that only checks itself - is given the change that lets it run to the end, the assert
/// holding, found by running a copy with each small change as the program was run. An assert is never changed.
/// </summary>
public class AssertRepairLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Write(string relative, string content)
    {
        var path = Path.Combine(_temp.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.ReplaceLineEndings("\n"));
        return path;
    }

    private async Task<CheckReport> CheckAsync(string file, CodeLanguage language, string? typed = null)
    {
        var launch = TargetFactory.FromFile(file);
        Assert.True(launch.Ok, launch.Problem);
        if (typed is not null) launch = launch with { Spec = launch.Spec!.WithInput(typed) };
        output.WriteLine($"how: {launch.Explanation}");

        using var http = new FixFinderHttpClient();
        var checker = new ProgramChecker(http, new FixSourceRegistry()) { Language = language };
        checker.Log += message => output.WriteLine($"log: {message}");
        var report = await checker.CheckAsync(launch);

        output.WriteLine($"syntax: {report.SyntaxSummary}");
        foreach (var note in report.Notes) output.WriteLine($"note: {note}");
        foreach (var finding in report.Findings) output.WriteLine($"[{finding.RuleId}] {finding.Line}: {finding.Title} :: {finding.Fix?.Title} :: {finding.Verified.Summary}");
        return report;
    }

    /// <summary>The finding for where the program stopped, with the change that lets its assert hold - and the program's file as it was.</summary>
    private static void FixedAt(CheckReport report, string program, string content, int assertLine, int changedLine, string changedTo)
    {
        var stopped = Assert.Single(report.Findings, finding => finding.Line == assertLine && finding.Fix is not null);
        Assert.Equal(changedLine, stopped.Fix!.StartLine);
        Assert.Equal([changedTo], stopped.Fix.NewLines);
        Assert.True(stopped.Verified.IsVerified, stopped.Verified.Summary);
        Assert.StartsWith($"With this change the program runs to the end, the assert on line {assertLine} holding - it stopped there before.", stopped.FixCheckedBy, StringComparison.Ordinal);
        Assert.Equal(content.ReplaceLineEndings("\n"), File.ReadAllText(program));
    }

    private const string JavaMarks = """
        public class Marks {
            static int total(int[] marks) {
                int sum = 0;
                for (int i = 1; i < marks.length; i++) {
                    sum += marks[i];
                }
                return sum;
            }

            public static void main(String[] args) {
                int[] marks = {60, 70, 80};
                assert total(marks) == 210 : "the total of 60, 70 and 80";
                System.out.println("Total: " + total(marks));
            }
        }
        """;

    [Fact]
    public async Task AJavaProgramsFailingAssertIsMadeToHold()
    {
        if (!LocalFixLiveTests.Available("java")) return;

        var program = Write(@"marks\Marks.java", JavaMarks);
        var report = await CheckAsync(program, CodeLanguage.Java);

        FixedAt(report, program, JavaMarks, 12, 4, "        for (int i = 0; i < marks.length; i++) {");
    }

    private const string NodeTotal = """
        const assert = require("node:assert");

        function total(marks) {
          let sum = 0;
          for (let i = 1; i < marks.length; i++) {
            sum += marks[i];
          }
          return sum;
        }

        assert.strictEqual(total([60, 70, 80]), 210, "the total of 60, 70 and 80");
        console.log("done");
        """;

    [Fact]
    public async Task ANodeProgramsFailingAssertIsReadAndMadeToHold()
    {
        if (!LocalFixLiveTests.Available("node")) return;

        var program = Write(@"marks\total.js", NodeTotal);
        var report = await CheckAsync(program, CodeLanguage.JavaScript);

        if (report.SyntaxSummary == "It could not be started") return;
        Assert.Equal("No syntax errors, but it stops with an error when run", report.SyntaxSummary);
        FixedAt(report, program, NodeTotal, 11, 5, "  for (let i = 0; i < marks.length; i++) {");
    }

    private const string CMarks = """
        #include <assert.h>
        #include <stdio.h>

        int total(const int marks[], int count) {
            int sum = 0;
            for (int i = 1; i < count; i++) {
                sum += marks[i];
            }
            return sum;
        }

        int main(void) {
            int marks[] = {60, 70, 80};
            assert(total(marks, 3) == 210);
            printf("Total: %d\n", total(marks, 3));
            return 0;
        }
        """;

    /// <summary>assert.h's assert prints what it checked and where, then aborts: read as the program stopping at that assert.</summary>
    [Fact]
    public async Task ACProgramsFailingAssertIsReadAndMadeToHold()
    {
        if (!LocalFixLiveTests.Available("c")) return;

        var program = Write(@"marks\marks.c", CMarks);
        var report = await CheckAsync(program, CodeLanguage.C);

        if (report.SyntaxSummary == "It could not be started") return;
        var stopped = Assert.Single(report.Findings, finding => finding.Line == 14);
        Assert.Equal("It crashed: Assertion failed: total(marks, 3) == 210", stopped.Title);
        FixedAt(report, program, CMarks, 14, 6, "    for (int i = 0; i < count; i++) {");
    }

    private const string PythonDouble = """
        def double(x):
            return x * 3


        number = int(input("Number: "))
        assert double(number) == number * 2, "doubling"
        print(double(number))
        """;

    /// <summary>It reads what is typed in, so it runs as a program; each change is run with what was typed.</summary>
    [Fact]
    public async Task APythonProgramsFailingAssertIsMadeToHoldWithWhatWasTyped()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var program = Write(@"double\double.py", PythonDouble);
        var report = await CheckAsync(program, CodeLanguage.Python, "7\n");

        if (report.SyntaxSummary == "It could not be started") return;
        FixedAt(report, program, PythonDouble, 6, 2, "    return x * 2");
    }
}
