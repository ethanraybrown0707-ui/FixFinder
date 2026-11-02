using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Python programs that check themselves with assert, checked as a student would: each assert at the top is a check of its
/// own, so the first that fails does not hide the rest; one that fails says what its left side was, one that raised what
/// and where; and the change to the code that makes every assert hold is found - never a change to an assert.
/// </summary>
public class AssertChecksLiveTests(ITestOutputHelper output) : IDisposable
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

    private async Task<CheckReport> CheckAsync(string file)
    {
        var launch = TargetFactory.FromFile(file);
        Assert.True(launch.Ok, launch.Problem);
        output.WriteLine($"how: {launch.Explanation}");

        using var http = new FixFinderHttpClient();
        var checker = new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Python };
        checker.Log += message => output.WriteLine($"log: {message}");
        var report = await checker.CheckAsync(launch);

        output.WriteLine($"syntax: {report.SyntaxSummary}");
        foreach (var note in report.Notes) output.WriteLine($"note: {note}");
        foreach (var finding in report.Findings) output.WriteLine($"[{finding.RuleId}] {finding.Line}: {finding.Title} :: {finding.Fix?.Title} :: {finding.Verified.Summary}");
        return report;
    }

    private const string Average = """
        def average(marks):
            total = 0
            for mark in marks:
                total += mark
            return total / (len(marks) + 1)


        def highest(marks):
            best = marks[0]
            for mark in marks:
                if mark > best:
                    best = mark
            return best


        assert highest([3, 9, 4]) == 9
        assert average([60, 80]) == 70
        assert average([50]) == 50
        print("all checks passed")
        """;

    [Fact]
    public async Task EveryAssertIsCheckedAndTheChangeThatMakesThemAllHoldIsFound()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var program = Write(@"marks\average.py", Average);
        var report = await CheckAsync(program);

        if (report.SyntaxSummary == "It could not be started") return;
        Assert.Equal("No syntax errors; 2 of 3 asserts failed", report.SyntaxSummary);

        var failed = report.Findings.Where(finding => finding.RuleId == "test-failed").OrderBy(finding => finding.Line).ToList();
        Assert.Equal([17, 18], failed.Select(finding => finding.Line));
        Assert.Equal("The assert on line 17 failed: average([60, 80]) is 46.666666666666664, not 70", failed[0].Title);
        Assert.Equal("The assert on line 18 failed: average([50]) is 25.0, not 50", failed[1].Title);

        Assert.NotNull(failed[0].Fix);
        Assert.Equal(5, failed[0].Fix!.StartLine);
        Assert.Equal(["    return total / (len(marks))"], failed[0].Fix!.NewLines);
        Assert.True(failed[0].Verified.IsVerified, failed[0].Verified.Summary);
        Assert.StartsWith("With this change every one of the 3 asserts in average.py passes - 2 of which failed before.", failed[0].FixCheckedBy, StringComparison.Ordinal);
        Assert.Equal(Average.ReplaceLineEndings("\n"), File.ReadAllText(program));
    }

    /// <summary>Asserts in the __main__ block, one with a message of its own and one whose code raised: each said as what it is.</summary>
    [Fact]
    public async Task AnAssertsMessageAndWhatAnAssertRaisedAreSaid()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var program = Write(@"marks\mainblock.py", """
            def mean(marks):
                return sum(marks) / len(marks)


            def top(marks):
                return sorted(marks)[0]


            if __name__ == "__main__":
                assert mean([2, 4]) == 3
                assert mean([]) == 0, "an empty list has a mean of 0"
                assert top([3, 9, 4]) == 9, "the top mark is the largest"
                print("done")
            """);
        var report = await CheckAsync(program);

        if (report.SyntaxSummary == "It could not be started") return;
        Assert.Equal("No syntax errors; 2 of 3 asserts failed", report.SyntaxSummary);

        var raised = Assert.Single(report.Findings, finding => finding is { RuleId: "test-failed", Line: 11 });
        Assert.Equal("The assert on line 11 stopped with ZeroDivisionError: division by zero", raised.Title);
        Assert.Equal("The program checks itself with assert, and the assert on line 11 stopped with ZeroDivisionError (division by zero), raised in mean on line 2 of mainblock.py.",
            raised.Explanation);

        var said = Assert.Single(report.Findings, finding => finding is { RuleId: "test-failed", Line: 12 });
        Assert.Equal("The assert on line 12 failed: the top mark is the largest - top([3, 9, 4]) is 3, not 9", said.Title);
    }

    /// <summary>The largest read from the wrong end of a sorted list: sorted(marks)[0] where [-1] was meant.</summary>
    [Fact]
    public async Task AnItemReadFromTheWrongEndIsFixed()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        const string Top = """
            def top(marks):
                return sorted(marks)[0]


            assert top([3, 9, 4]) == 9
            assert top([5]) == 5
            """;
        var program = Write(@"marks\top.py", Top);
        var report = await CheckAsync(program);

        if (report.SyntaxSummary == "It could not be started") return;
        var failed = Assert.Single(report.Findings, finding => finding.RuleId == "test-failed");
        Assert.Equal(["    return sorted(marks)[-1]"], failed.Fix?.NewLines);
        Assert.True(failed.Verified.IsVerified, failed.Verified.Summary);
        Assert.Equal(Top.ReplaceLineEndings("\n"), File.ReadAllText(program));
    }

    [Fact]
    public async Task WhenEveryAssertHoldsThatIsSaid()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var report = await CheckAsync(Write(@"double\double.py", "def double(x):\n    return x * 2\n\n\nassert double(2) == 4\nassert double(0) == 0\nprint(\"all good\")\n"));

        if (report.SyntaxSummary == "It could not be started") return;
        Assert.Equal("No syntax errors, and all 2 of its asserts pass", report.SyntaxSummary);
        Assert.DoesNotContain(report.Findings, finding => finding.RuleId == "test-failed");
    }
}
