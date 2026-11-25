using FixFinder.Core.Checking;
using FixFinder.Core.Engine;
using FixFinder.Core.Reporting;

namespace FixFinder.Tests;

/// <summary>
/// A program's history, read for more than the last check: how many checks in a row have found each finding still there,
/// and the program's problems over its last checks - each read from the counts and fingerprints the history keeps, and said
/// in the window's tooltip, the panel beside the report and the saved report.
/// </summary>
public class CheckTrendTests
{
    private const string Program = @"C:\work\grades.py";

    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    private static Finding Found(string rule, int line, Severity severity = Severity.Warning, FindingKind kind = FindingKind.Logic) => new()
    {
        Kind = kind,
        Severity = severity,
        Confidence = Confidence.Likely,
        File = Program,
        Line = line,
        Title = rule,
        Explanation = "",
        WhyItMatters = "",
        SuggestedFix = "",
        CorrectedExample = "",
        RuleId = rule,
    };

    private static CheckRecord Check(DateTimeOffset when, bool ran, params Finding[] findings) => CheckRecord.Of(Program, findings, when, ran);

    private static readonly Finding LoopOneShort = Found("logic-python-loop-one-short", 4);
    private static readonly Finding Unused = Found("logic-python-unused-variable", 9, Severity.Suggestion);
    private static readonly Finding Crash = Found("ZeroDivisionError", 12, Severity.Error, FindingKind.Runtime);

    [Fact]
    public void AFindingEachOfTheLastChecksFoundHasBeenThereSinceTheFirstOfThem()
    {
        var earlier = new[] { Check(Now.AddDays(-1), true, LoopOneShort), Check(Now.AddDays(-2), true, LoopOneShort, Unused), Check(Now.AddDays(-3), true, Unused) };

        var comparison = CheckComparison.Of(earlier[0], [LoopOneShort, Unused], ranNow: true, earlier: earlier[1..]);

        Assert.NotNull(comparison);
        Assert.Equal(new FindingStreak(3, Now.AddDays(-2)), comparison.StreakOf(LoopOneShort));
        Assert.Equal("Found by each of the last 3 checks of this program, since 2 days ago.", comparison.StreakOf(LoopOneShort)!.Said(Now));
        Assert.Null(comparison.StreakOf(Unused));
        Assert.Equal(SinceLastCheck.New, comparison.StatusOf(Unused));
    }

    /// <summary>Only a run finds a crash: a check in which the program did not run neither counts for it nor ends its row.</summary>
    [Fact]
    public void ACheckWithoutARunNeitherCountsNorEndsTheRowOfACrash()
    {
        var earlier = new[] { Check(Now.AddHours(-1), true, Crash), Check(Now.AddHours(-2), false), Check(Now.AddHours(-3), true, Crash), Check(Now.AddHours(-4), true) };

        var comparison = CheckComparison.Of(earlier[0], [Crash], ranNow: true, earlier: earlier[1..]);

        Assert.Equal(new FindingStreak(3, Now.AddHours(-3)), comparison!.StreakOf(Crash));
    }

    [Fact]
    public void WithoutTheEarlierChecksAFindingStillThereSaysOnlyThatTheLastCheckFoundIt()
    {
        var comparison = CheckComparison.Of(Check(Now.AddDays(-1), true, LoopOneShort), [LoopOneShort], ranNow: true);

        Assert.Equal(new FindingStreak(2, Now.AddDays(-1)), comparison!.StreakOf(LoopOneShort));
    }

    [Fact]
    public void TheProblemsOfTheLastChecksAreSaidOldestFirstOnceThereAreThreeOfThem()
    {
        var problems = new[] { 7, 5, 5, 3 };
        var before = problems.Select((count, index) => Check(Now.AddDays(index - problems.Length), true,
            [.. Enumerable.Range(1, count).Select(line => Found("logic-python-loop-one-short", line))])).Reverse().ToList();
        var now = Check(Now, true, Found("logic-python-loop-one-short", 1), Found("logic-python-loop-one-short", 2));

        Assert.Equal("Problems in the last 5 checks of this program, oldest first: 7, 5, 5, 3, 2.", CheckHistory.Trend(before, now));
        Assert.Null(CheckHistory.Trend(before[..1], now));
    }

    [Fact]
    public void OnlyTheLastEightChecksMakeTheTrend()
    {
        var before = Enumerable.Range(1, 12).Select(day => Check(Now.AddDays(-day), true, [.. Enumerable.Range(1, day).Select(line => Found("logic-python-loop-one-short", line))])).ToList();

        Assert.Equal("Problems in the last 8 checks of this program, oldest first: 7, 6, 5, 4, 3, 2, 1, 0.", CheckHistory.Trend(before, Check(Now, true)));
    }

    /// <summary>The trend, and how long each finding has been there, are in the report saved as a web page.</summary>
    [Fact]
    public void TheSavedReportSaysTheTrendAndHowLongAFindingHasBeenThere()
    {
        var earlier = new[] { Check(Now.AddDays(-1), true, LoopOneShort), Check(Now.AddDays(-2), true, LoopOneShort) };
        var comparison = CheckComparison.Of(earlier[0], [LoopOneShort], ranNow: true, earlier: earlier[1..])! with
        {
            Trend = CheckHistory.Trend(earlier, Check(Now, true, LoopOneShort)),
        };

        var page = new ReportPage
        {
            Program = "grades.py", Language = "Python", CheckedAt = Now, SyntaxSummary = "No syntax errors", LogicSummary = "1 possible mistake in the code",
            Findings = [LoopOneShort], SinceLastCheck = comparison,
        }.ToHtml();

        Assert.Contains("<p class=\"trend\">Problems in the last 3 checks of this program, oldest first: 1, 1, 1.</p>", page, StringComparison.Ordinal);
        Assert.Contains("<span class=\"streak\">Found by each of the last 3 checks of this program, since 2 days ago.</span>", page, StringComparison.Ordinal);
    }
}
