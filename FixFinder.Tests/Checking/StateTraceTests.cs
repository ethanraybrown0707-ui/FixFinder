using FixFinder.Core.Checking;

namespace FixFinder.Tests;

/// <summary>
/// The table of what a line actually did. Everything in it came from a run; the rules here are about what is left out
/// - variables nobody asked about, passes past the point a reader can take in, and tables that would say nothing.
/// </summary>
public class StateTraceTests
{
    private static IReadOnlyList<IReadOnlyDictionary<string, string>> Visits(params (string I, string Item)[] passes) =>
        [.. passes.Select(p => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["i"] = p.I,
            ["item"] = p.Item,
            ["unrelated"] = "'noise'",
        })];

    [Fact]
    public void EachPassBecomesARowOfTheVariablesAskedAbout()
    {
        var trace = StateTrace.From(7, Visits(("0", "12"), ("1", "15"), ("2", "9")), ["i", "item"], failedOnLastPass: false);

        Assert.NotNull(trace);
        Assert.Equal(["i", "item"], trace.Columns);
        Assert.Equal(3, trace.Rows.Count);
        Assert.Equal([1, 2, 3], trace.Rows.Select(r => r.Number).ToArray());
        Assert.Equal(["1", "15"], trace.Rows[1].Values);
    }

    [Fact]
    public void AVariableNobodyAskedAboutIsNotAColumn()
    {
        var trace = StateTrace.From(7, Visits(("0", "12")), ["i"], failedOnLastPass: false);

        Assert.NotNull(trace);
        Assert.Equal(["i"], trace.Columns);
    }

    [Fact]
    public void AVariableTheRunNeverSawIsNotAnEmptyColumn()
    {
        var trace = StateTrace.From(7, Visits(("0", "12")), ["i", "neverHappened"], failedOnLastPass: false);

        Assert.NotNull(trace);
        Assert.Equal(["i"], trace.Columns);
    }

    [Fact]
    public void ThePassTheProgramStoppedOnIsMarked()
    {
        var trace = StateTrace.From(7, Visits(("0", "12"), ("1", "15")), ["i"], failedOnLastPass: true);

        Assert.NotNull(trace);
        Assert.False(trace.Rows[0].IsWhereItFailed);
        Assert.True(trace.Rows[1].IsWhereItFailed);
    }

    [Fact]
    public void ALongRunIsCutAndSaysSo()
    {
        var many = Enumerable.Range(0, 400).Select(n => (n.ToString(), n.ToString())).ToArray();

        var trace = StateTrace.From(7, Visits(many), ["i"], failedOnLastPass: false);

        Assert.NotNull(trace);
        Assert.Equal(StateTrace.MostRowsShown, trace.Rows.Count);
        Assert.True(trace.WasCut);
        Assert.Contains("400", trace.CutNote);
    }

    /// <summary>
    /// When the run itself stopped recording, the last row kept is not the pass the program failed on - the failing
    /// pass happened later and was never seen. Marking it would be inventing which pass went wrong.
    /// </summary>
    [Fact]
    public void WhenTheRunStoppedRecordingNoPassIsCalledTheFailingOne()
    {
        var trace = StateTrace.From(7, Visits(("0", "12"), ("1", "15")), ["i"], failedOnLastPass: true, moreThanWereKept: true);

        Assert.NotNull(trace);
        Assert.All(trace.Rows, row => Assert.False(row.IsWhereItFailed));
        Assert.True(trace.WasCut);
    }

    [Fact]
    public void ARunThatRecordedNothingGivesNoTable()
    {
        Assert.Null(StateTrace.From(7, [], ["i"], failedOnLastPass: false));
    }

    [Fact]
    public void ATableWithNothingWorthShowingIsNotATable()
    {
        Assert.Null(StateTrace.From(7, Visits(("0", "12")), [], failedOnLastPass: false));
    }
}
