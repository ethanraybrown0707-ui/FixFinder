using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Python files of doctests, checked as a student would: the examples in each docstring are run with doctest itself, the
/// first that does not give what it shows is a failure on its own line, the change to the code that makes every example
/// give what it shows is found - never a change to a docstring, which is the tests - and the file's own testmod options count.
/// </summary>
public class DoctestLiveTests(ITestOutputHelper output) : IDisposable
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

    /// <summary>A homework function in the style of a first programming course: n // 100 takes two digits off where one was meant.</summary>
    private const string DoubleEights = """"
        def double_eights(n):
            """Return true if n has two eights in a row.

            >>> double_eights(8)
            False
            >>> double_eights(88)
            True
            >>> double_eights(2882)
            True
            >>> double_eights(12345)
            False
            """
            previous = None
            while n > 0:
                digit = n % 10
                if digit == 8 and previous == 8:
                    return True
                previous = digit
                n = n // 100
            return False
        """";

    [Fact]
    public async Task TheFailingExampleIsSaidOnItsOwnLineAndTheCodeIsFixed()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var homework = Write(@"hw\hw02.py", DoubleEights);
        var report = await CheckAsync(homework);

        if (report.SyntaxSummary == "It could not be started") return;
        Assert.Equal("No syntax errors; 1 of 1 doctest failed", report.SyntaxSummary);

        var failed = Assert.Single(report.Findings, finding => finding.RuleId == "test-failed");
        Assert.Equal(6, failed.Line);
        Assert.Equal("An example in the docstring of double_eights failed: double_eights(88): expected True, got False", failed.Title);
        Assert.NotNull(failed.Fix);
        Assert.Equal(19, failed.Fix!.StartLine);
        Assert.Equal(["        n = n // 10"], failed.Fix.NewLines);
        Assert.True(failed.Verified.IsVerified, failed.Verified.Summary);
        Assert.StartsWith("With this change the doctest in hw02.py passes.", failed.FixCheckedBy, StringComparison.Ordinal);

        // The changes were tried in copies: the file itself is as it was.
        Assert.Equal(DoubleEights.ReplaceLineEndings("\n"), File.ReadAllText(homework));
    }

    [Fact]
    public async Task AnExampleThatRaisesIsSaidOnItsOwnLineWithWhereItWasRaised()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var marks = Write(@"marks\marks.py", """"
            def average(marks):
                """The mean of the marks.

                >>> average([60, 80])
                70.0
                >>> average([])
                0
                """
                return sum(marks) / len(marks)
            """");
        var report = await CheckAsync(marks);

        if (report.SyntaxSummary == "It could not be started") return;
        var failed = Assert.Single(report.Findings, finding => finding.RuleId == "test-failed");
        Assert.Equal(6, failed.Line);
        Assert.Equal("An example in the docstring of average stopped with ZeroDivisionError: division by zero", failed.Title);
        Assert.Equal("doctest ran the examples in the docstring of average, and the one on line 6 stopped with ZeroDivisionError (division by zero), " +
                     "raised in marks.average on line 9 of marks.py.", failed.Explanation);
    }

    /// <summary>Two mistakes in a class's methods, each failing its own docstring: the fix is built a change at a time.</summary>
    [Fact]
    public async Task TwoMistakesInAClassAreFixedAChangeAtATime()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        const string Stack = """"
            class Stack:
                def __init__(self):
                    self.items = []

                def push(self, item):
                    self.items.append(item)

                def pop(self):
                    """
                    >>> s = Stack()
                    >>> s.push(1)
                    >>> s.push(2)
                    >>> s.pop()
                    2
                    """
                    return self.items.pop(0)

                def peek(self):
                    """
                    >>> s = Stack()
                    >>> s.push(1)
                    >>> s.push(2)
                    >>> s.peek()
                    2
                    """
                    return self.items[len(self.items)]
            """";
        var stack = Write(@"stack\stack.py", Stack);
        var report = await CheckAsync(stack);

        if (report.SyntaxSummary == "It could not be started") return;
        Assert.Equal("No syntax errors; 2 of 2 doctests failed", report.SyntaxSummary);

        var fixedFirst = Assert.Single(report.Findings, finding => finding is { RuleId: "test-failed", Fix: not null });
        Assert.Equal("An example in the docstring of Stack.pop failed: s.pop(): expected 2, got 1", fixedFirst.Title);
        Assert.Equal(16, fixedFirst.Fix!.StartLine);
        Assert.Equal("        return self.items.pop()", fixedFirst.Fix.NewLines[0]);
        Assert.Equal("        return self.items[len(self.items) - 1]", fixedFirst.Fix.NewLines[^1]);
        Assert.True(fixedFirst.Verified.IsVerified, fixedFirst.Verified.Summary);

        // Every line between the two changes - the next docstring among them - is as it was.
        var lines = Stack.ReplaceLineEndings("\n").Split('\n');
        Assert.Equal(lines[16..25], fixedFirst.Fix.NewLines.Skip(1).Take(fixedFirst.Fix.NewLines.Count - 2));
        Assert.Equal(Stack.ReplaceLineEndings("\n"), File.ReadAllText(stack));
    }

    /// <summary>The file's own doctest.testmod(optionflags=doctest.ELLIPSIS) lets ... stand for the rest of what is shown, here as there.</summary>
    [Fact]
    public async Task TheFilesOwnTestmodOptionsCount()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var squares = Write(@"squares\squares.py", """"
            import doctest


            def squares(n):
                """
                >>> squares(10)
                [0, 1, 4, ..., 81]
                """
                return [i * i for i in range(n)]


            if __name__ == "__main__":
                doctest.testmod(optionflags=doctest.ELLIPSIS)
            """");
        var report = await CheckAsync(squares);

        if (report.SyntaxSummary == "It could not be started") return;
        Assert.Equal("No syntax errors, and its doctest passes", report.SyntaxSummary);
        Assert.DoesNotContain(report.Findings, finding => finding.RuleId == "test-failed");
    }

    /// <summary>A program that runs its doctests and then does work of its own is checked as the program it is.</summary>
    [Fact]
    public async Task AProgramWithDoctestsIsCheckedAsAProgram()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var program = Write(@"doubler\doubler.py", """"
            def double(x):
                """
                >>> double(2)
                4
                """
                return x + x


            def main():
                print(double(21))


            if __name__ == "__main__":
                import doctest
                doctest.testmod()
                main()
            """");

        var launch = TargetFactory.FromFile(program);
        Assert.True(launch.Ok, launch.Problem);
        Assert.StartsWith("Running it with", launch.Explanation, StringComparison.Ordinal);

        var report = await CheckAsync(program);
        if (report.SyntaxSummary == "It could not be started") return;
        Assert.Equal("No syntax errors, and it runs to the end", report.SyntaxSummary);
    }

    private const string Doubler = """"
        def double(x):
            """
            >>> double(2)
            4
            >>> double(5)
            10
            """
            return x * 2 if x < 5 else x * 3


        def average(marks):
            """
            >>> average([])
            0
            """
            return sum(marks) / len(marks)


        def main():
            print(double(21))


        if __name__ == "__main__":
            import doctest
            doctest.testmod()
            main()
        """";

    /// <summary>
    /// A program that runs its doctests and then its own work carries on past the examples that fail, as doctest only prints
    /// them: each is a finding on its own line, and the traceback doctest printed for the one that raised is not the program stopping.
    /// </summary>
    [Fact]
    public async Task TheExamplesAProgramsOwnDoctestsFailedAreEachAFinding()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var report = await CheckAsync(Write(@"doubler\doubler.py", Doubler));

        if (report.SyntaxSummary == "It could not be started") return;
        Assert.Equal("No syntax errors, and it runs to the end, but 2 examples of its doctests failed", report.SyntaxSummary);

        var failed = report.Findings.Where(finding => finding.RuleId == "doctest-failed").OrderBy(finding => finding.Line).ToList();
        Assert.Equal([5, 13], failed.Select(finding => finding.Line));
        Assert.Equal("An example in the docstring of double failed: double(5): expected 10, got 15", failed[0].Title);
        Assert.Equal("An example in the docstring of average stopped with ZeroDivisionError: division by zero", failed[1].Title);
        Assert.DoesNotContain(report.Findings, finding => finding.Title.StartsWith("It ran to the end, but printed an exception", StringComparison.Ordinal));
    }

    /// <summary>A crash after the doctests is still the program stopping, found as any crash is.</summary>
    [Fact]
    public async Task ACrashAfterTheDoctestsIsStillFound()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var report = await CheckAsync(Write(@"crasher\crasher.py", Doubler.Replace("print(double(21))", "print(average([]))")));

        if (report.SyntaxSummary == "It could not be started") return;
        Assert.Equal("No syntax errors, but it stops with an error when run", report.SyntaxSummary);
        Assert.Contains(report.Findings, finding => finding.Line == 16 && finding.Title == "It crashed: ZeroDivisionError: division by zero");
        Assert.Equal(2, report.Findings.Count(finding => finding.RuleId == "doctest-failed"));
    }
}
