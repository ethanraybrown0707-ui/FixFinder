using FixFinder.Core.Checking;
using FixFinder.Core.Reporting;

namespace FixFinder.Tests;

/// <summary>
/// The health of a check's findings, out of 100: each problem takes off the points the table gives it - an error 20, 15 or
/// 10, a warning 8, 6 or 4, a suggestion 2, 2 or 1, by how sure FixFinder is of it - efficiency findings take nothing off,
/// the score is never below 0, and what each kind took off is said with it. A saved report says it only when it is given.
/// </summary>
public class HealthScoreTests
{
    private static Finding Found(Severity severity, Confidence confidence, FindingKind kind = FindingKind.Logic) => new()
    {
        Kind = kind,
        Severity = severity,
        Confidence = confidence,
        File = "main.py",
        Title = "A problem",
        Explanation = "",
        WhyItMatters = "",
        SuggestedFix = "",
        CorrectedExample = "",
    };

    [Theory]
    [InlineData(Severity.Error, Confidence.Certain, 20)]
    [InlineData(Severity.Error, Confidence.Likely, 15)]
    [InlineData(Severity.Error, Confidence.Possible, 10)]
    [InlineData(Severity.Warning, Confidence.Certain, 8)]
    [InlineData(Severity.Warning, Confidence.Likely, 6)]
    [InlineData(Severity.Warning, Confidence.Possible, 4)]
    [InlineData(Severity.Suggestion, Confidence.Certain, 2)]
    [InlineData(Severity.Suggestion, Confidence.Likely, 2)]
    [InlineData(Severity.Suggestion, Confidence.Possible, 1)]
    public void EachProblemTakesOffThePointsTheTableGivesIt(Severity severity, Confidence confidence, int points)
    {
        Assert.Equal(points, HealthScore.PointsFor(severity, confidence));
        Assert.Equal(100 - points, HealthScore.Of([Found(severity, confidence)]).Score);
    }

    [Fact]
    public void NoProblemIs100()
    {
        Assert.Equal("100 out of 100: no problem was found.", HealthScore.Of([]).Said);
    }

    /// <summary>A certain error, two likely warnings and a possible suggestion, with an efficiency finding that takes nothing off.</summary>
    [Fact]
    public void WhatEachKindTookOffIsSaidTheWorstFirst()
    {
        var health = HealthScore.Of([
            Found(Severity.Suggestion, Confidence.Possible),
            Found(Severity.Warning, Confidence.Likely),
            Found(Severity.Error, Confidence.Certain),
            Found(Severity.Warning, Confidence.Likely),
            Found(Severity.Warning, Confidence.Certain, FindingKind.Performance),
        ]);

        Assert.Equal(67, health.Score);
        Assert.Equal("67 out of 100: 1 error FixFinder is certain of took off 20; 2 warnings FixFinder thinks likely took off 12, 6 each; " +
                     "1 suggestion FixFinder thinks possible took off 1.", health.Said);
    }

    [Fact]
    public void FiveCertainErrorsAre0AndMoreAreNoLower()
    {
        Assert.Equal(0, HealthScore.Of([.. Enumerable.Range(0, 5).Select(_ => Found(Severity.Error, Confidence.Certain))]).Score);
        Assert.Equal(0, HealthScore.Of([.. Enumerable.Range(0, 7).Select(_ => Found(Severity.Error, Confidence.Certain))]).Score);
    }

    [Fact]
    public void TheSavedReportSaysTheHealthOnlyWhenItIsGiven()
    {
        ReportPage Page(string? health) => new()
        {
            Program = "main.py", Language = "Python", CheckedAt = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero),
            SyntaxSummary = "No syntax errors", LogicSummary = "1 possible mistake in the code", Findings = [], Health = health,
        };

        Assert.Contains("<dt>Health</dt><dd>98 out of 100: 1 suggestion FixFinder is certain of took off 2.</dd>",
            Page("98 out of 100: 1 suggestion FixFinder is certain of took off 2.").ToHtml(), StringComparison.Ordinal);
        Assert.DoesNotContain("<dt>Health</dt>", Page(null).ToHtml(), StringComparison.Ordinal);
    }
}
