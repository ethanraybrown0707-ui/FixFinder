using FixFinder.Core.Execution;

namespace FixFinder.Tests;

/// <summary>
/// Reading what doctest.testmod() prints of the examples that failed - as Python 3.13.14 printed it here - and leaving its
/// reports out of what is read for the program's own error, so the traceback of an example that raised is not taken for
/// the program stopping.
/// </summary>
public class DoctestReportTests
{
    /// <summary>A program's doctests run by doctest.testmod(): an example that raised, and one that gave another answer.</summary>
    private static readonly string[] RaisedAndWrong =
    [
        "**********************************************************************",
        @"File ""C:\work\doubler.py"", line 13, in __main__.average",
        "Failed example:",
        "    average([])",
        "Exception raised:",
        "    Traceback (most recent call last):",
        @"      File ""C:\Python313\Lib\doctest.py"", line 1398, in __run",
        "        exec(compile(example.source, filename, \"single\",",
        "        ~~~~^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^",
        "                     compileflags, True), test.globs)",
        "                     ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^",
        @"      File ""<doctest __main__.average[0]>"", line 1, in <module>",
        "        average([])",
        "        ~~~~~~~^^^^",
        @"      File ""C:\work\doubler.py"", line 16, in average",
        "        return sum(marks) / len(marks)",
        "               ~~~~~~~~~~~^~~~~~~~~~~~",
        "    ZeroDivisionError: division by zero",
        "**********************************************************************",
        @"File ""C:\work\doubler.py"", line 5, in __main__.double",
        "Failed example:",
        "    double(5)",
        "Expected:",
        "    10",
        "Got:",
        "    15",
        "**********************************************************************",
        "2 items had failures:",
        "   1 of   1 in __main__.average",
        "   1 of   2 in __main__.double",
        "42",
    ];

    [Fact]
    public void EachFailedExampleIsReadWithWhereItIsAndWhatItGave()
    {
        var failures = DoctestReport.FailuresIn(RaisedAndWrong);

        Assert.Equal(2, failures.Count);
        Assert.Equal((@"C:\work\doubler.py", 13, "average", "average([])", "ZeroDivisionError: division by zero"),
            (failures[0].File, failures[0].Line, failures[0].Name, failures[0].Example, failures[0].Exception));
        Assert.Equal((5, "double", "double(5)", "10", "15", (string?)null),
            (failures[1].Line, failures[1].Name, failures[1].Example, failures[1].Expected, failures[1].Got, failures[1].Exception));
    }

    [Fact]
    public void AnExampleThatShowsNothingOrGivesNothingIsReadAsNothing()
    {
        string[] printed =
        [
            "**********************************************************************",
            @"File ""C:\work\nothing.py"", line 10, in __main__.loud",
            "Failed example:",
            "    loud()",
            "Expected nothing",
            "Got:",
            "    7",
            "**********************************************************************",
            @"File ""C:\work\nothing.py"", line 3, in __main__.quiet",
            "Failed example:",
            "    quiet()",
            "Expected:",
            "    5",
            "Got nothing",
            "**********************************************************************",
        ];

        var failures = DoctestReport.FailuresIn(printed);

        Assert.Equal([("loud", "nothing", "7"), ("quiet", "5", "nothing")], failures.Select(failure => (failure.Name, failure.Expected, failure.Got)));
    }

    /// <summary>A method's docstring is named with its class, and the module's own name is left off as __main__ is.</summary>
    [Fact]
    public void AMethodsDocstringIsNamedWithItsClass()
    {
        string[] printed = [@"File ""C:\work\stack.py"", line 9, in stack.Stack.pop", "Failed example:", "    s.pop()", "Expected:", "    2", "Got:", "    1"];

        Assert.Equal("Stack.pop", Assert.Single(DoctestReport.FailuresIn(printed)).Name);
    }

    /// <summary>The traceback in a report is doctest's account of an example; what the program printed after it is kept.</summary>
    [Fact]
    public void TheReportsAreLeftOutOfWhatIsReadForTheProgramsOwnError()
    {
        var captured = RaisedAndWrong.Select((text, index) => new CapturedLine(index, StreamKind.StdOut, text, TimeSpan.Zero)).ToList();

        var left = DoctestReport.WithoutReports(captured).Select(line => line.Text).ToList();

        Assert.DoesNotContain(left, text => text.Contains("Traceback", StringComparison.Ordinal) || text.Contains("ZeroDivisionError", StringComparison.Ordinal));
        Assert.Contains("42", left);
        Assert.Contains("2 items had failures:", left);
    }

    /// <summary>
    /// A program that ran its doctests with doctest.testmod() and then crashed in main(): doctest's report on its standard
    /// output and the crash's traceback on its standard error, as TargetRunner captured them in one of 600 runs of it with
    /// Python 3.13.14 here - the traceback's lines in among the report's - with the paths shortened as in <see cref="RaisedAndWrong"/>.
    /// </summary>
    private static readonly (StreamKind Stream, string Text)[] CapturedInAmongEachOther =
    [
        (StreamKind.StdErr, "Traceback (most recent call last):"),
        (StreamKind.StdOut, "**********************************************************************"),
        (StreamKind.StdOut, @"File ""C:\work\crasher.py"", line 13, in __main__.average"),
        (StreamKind.StdOut, "Failed example:"),
        (StreamKind.StdOut, "    average([])"),
        (StreamKind.StdOut, "Exception raised:"),
        (StreamKind.StdOut, "    Traceback (most recent call last):"),
        (StreamKind.StdOut, @"      File ""C:\Python313\Lib\doctest.py"", line 1398, in __run"),
        (StreamKind.StdOut, "        exec(compile(example.source, filename, \"single\","),
        (StreamKind.StdOut, "        ~~~~^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^"),
        (StreamKind.StdOut, "                     compileflags, True), test.globs)"),
        (StreamKind.StdOut, "                     ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^"),
        (StreamKind.StdOut, @"      File ""<doctest __main__.average[0]>"", line 1, in <module>"),
        (StreamKind.StdOut, "        average([])"),
        (StreamKind.StdOut, "        ~~~~~~~^^^^"),
        (StreamKind.StdOut, @"      File ""C:\work\crasher.py"", line 16, in average"),
        (StreamKind.StdOut, "        return sum(marks) / len(marks)"),
        (StreamKind.StdOut, "               ~~~~~~~~~~~^~~~~~~~~~~~"),
        (StreamKind.StdOut, "    ZeroDivisionError: division by zero"),
        (StreamKind.StdErr, @"  File ""C:\work\crasher.py"", line 26, in <module>"),
        (StreamKind.StdOut, "**********************************************************************"),
        (StreamKind.StdOut, @"File ""C:\work\crasher.py"", line 5, in __main__.double"),
        (StreamKind.StdOut, "Failed example:"),
        (StreamKind.StdErr, "    main()"),
        (StreamKind.StdOut, "    double(5)"),
        (StreamKind.StdOut, "Expected:"),
        (StreamKind.StdErr, "    ~~~~^^"),
        (StreamKind.StdErr, @"  File ""C:\work\crasher.py"", line 20, in main"),
        (StreamKind.StdOut, "    10"),
        (StreamKind.StdErr, "    print(average([]))"),
        (StreamKind.StdErr, "          ~~~~~~~^^^^"),
        (StreamKind.StdErr, @"  File ""C:\work\crasher.py"", line 16, in average"),
        (StreamKind.StdErr, "    return sum(marks) / len(marks)"),
        (StreamKind.StdOut, "Got:"),
        (StreamKind.StdErr, "           ~~~~~~~~~~~^~~~~~~~~~~~"),
        (StreamKind.StdOut, "    15"),
        (StreamKind.StdErr, "ZeroDivisionError: division by zero"),
        (StreamKind.StdOut, "**********************************************************************"),
        (StreamKind.StdOut, "2 items had failures:"),
        (StreamKind.StdOut, "   1 of   1 in __main__.average"),
        (StreamKind.StdOut, "   1 of   2 in __main__.double"),
        (StreamKind.StdOut, "***Test Failed*** 2 failures."),
    ];

    private static List<CapturedLine> Captured(IEnumerable<(StreamKind Stream, string Text)> lines) =>
        lines.Select((line, index) => new CapturedLine(index, line.Stream, line.Text, TimeSpan.Zero)).ToList();

    private static string[] PrintedTo(StreamKind stream) =>
        CapturedInAmongEachOther.Where(line => line.Stream == stream).Select(line => line.Text).ToArray();

    private static readonly (int Line, string Name, string Example, string Expected, string Got, string? Exception)[] TheTwoFailures =
    [
        (13, "average", "average([])", "nothing", "nothing", "ZeroDivisionError: division by zero"),
        (5, "double", "double(5)", "10", "15", null),
    ];

    private static (int, string, string, string, string, string?)[] Read(IReadOnlyList<CapturedLine> captured) =>
        DoctestReport.FailuresIn(captured).Select(failure => (failure.Line, failure.Name, failure.Example, failure.Expected, failure.Got, failure.Exception)).ToArray();

    /// <summary>
    /// A crash's traceback captured in among doctest's report is not read as part of it: on the runner, a program that ran its
    /// doctests and then crashed was found with one failed example of its two.
    /// </summary>
    [Fact]
    public void ACrashsTracebackCapturedInAmongTheReportIsNotReadAsPartOfIt()
    {
        Assert.Equal(TheTwoFailures, Read(Captured(CapturedInAmongEachOther)));
    }

    /// <summary>Wherever the lines of the traceback are captured among the report's, all at once or one by one, the report reads the same.</summary>
    [Fact]
    public void TheReportReadsTheSameWhereverTheTracebackIsCapturedAmongIt()
    {
        var report = PrintedTo(StreamKind.StdOut);
        var traceback = PrintedTo(StreamKind.StdErr);

        for (var at = 0; at <= report.Length; at++)
        {
            var allAtOnce = report.Take(at).Select(text => (StreamKind.StdOut, text))
                .Concat(traceback.Select(text => (StreamKind.StdErr, text)))
                .Concat(report.Skip(at).Select(text => (StreamKind.StdOut, text)));
            Assert.True(TheTwoFailures.SequenceEqual(Read(Captured(allAtOnce))), $"the whole traceback after {at} lines of the report");

            foreach (var line in traceback)
            {
                var oneLine = report.Take(at).Select(text => (StreamKind.StdOut, text))
                    .Append((StreamKind.StdErr, line))
                    .Concat(report.Skip(at).Select(text => (StreamKind.StdOut, text)));
                Assert.True(TheTwoFailures.SequenceEqual(Read(Captured(oneLine))), $"\"{line}\" after {at} lines of the report");
            }
        }
    }

    /// <summary>
    /// What is read for the program's own error keeps every line of its traceback, in order, even those captured in among the
    /// report, and leaves out only the report's own lines.
    /// </summary>
    [Fact]
    public void TheCrashsOwnTracebackIsKeptWholeWhenItIsCapturedInAmongTheReport()
    {
        const string stars = "**********************************************************************";

        var left = DoctestReport.WithoutReports(Captured(CapturedInAmongEachOther));

        Assert.Equal(PrintedTo(StreamKind.StdErr), left.Where(line => line.IsError).Select(line => line.Text));
        Assert.Equal([stars, stars, stars, "2 items had failures:", "   1 of   1 in __main__.average", "   1 of   2 in __main__.double", "***Test Failed*** 2 failures."],
            left.Where(line => !line.IsError).Select(line => line.Text));
    }

    [Fact]
    public void WhatIsNotDoctestsReportIsLeftAsItIs()
    {
        var captured = new[] { "Traceback (most recent call last):", @"  File ""C:\work\app.py"", line 3, in <module>", "ZeroDivisionError: division by zero" }
            .Select((text, index) => new CapturedLine(index, StreamKind.StdErr, text, TimeSpan.Zero)).ToList();

        Assert.Empty(DoctestReport.FailuresIn(captured.Select(line => line.Text).ToList()));
        Assert.Same(captured, DoctestReport.WithoutReports(captured));
    }
}
