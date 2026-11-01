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

    [Fact]
    public void WhatIsNotDoctestsReportIsLeftAsItIs()
    {
        var captured = new[] { "Traceback (most recent call last):", @"  File ""C:\work\app.py"", line 3, in <module>", "ZeroDivisionError: division by zero" }
            .Select((text, index) => new CapturedLine(index, StreamKind.StdErr, text, TimeSpan.Zero)).ToList();

        Assert.Empty(DoctestReport.FailuresIn(captured.Select(line => line.Text).ToList()));
        Assert.Same(captured, DoctestReport.WithoutReports(captured));
    }
}
