using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Reporting;

namespace FixFinder.Tests;

/// <summary>
/// What a fix removes besides its own finding: the lines a fix really changes, how far it moves those after them, which
/// mistakes found before are not found with it made - never one on a line it changes, nor an analysis's when either
/// reading could not follow the values - and how the report says so.
/// </summary>
public class FixEffectsTests
{
    private static SourceFile Lines(int count) => new()
    {
        Path = @"C:\work\marks.py",
        Lines = [.. Enumerable.Range(1, count).Select(number => $"line {number}")],
    };

    private static LocalFix Fix(int startLine, int removeCount, params string[] newLines) => new()
    {
        RuleId = "logic-edit", Title = "Change it", Explanation = "", File = @"C:\work\marks.py",
        StartLine = startLine, RemoveCount = removeCount, NewLines = newLines,
    };

    private static CodeReading Reading(bool analysesRead, params (string RuleId, int Line)[] found) =>
        new(found.Select(place => new CodeFindingPlace(place.RuleId, place.Line)).ToHashSet(), analysesRead);

    [Fact]
    public void AOneLineChangeChangesThatLineAndMovesNothing()
    {
        Assert.Equal((4, 4, 0), FixEffects.ChangedLines(Fix(4, 1, "changed"), Lines(10)));
    }

    [Fact]
    public void LinesOnlyAddedChangeNoLineAndMoveTheRestDown()
    {
        Assert.Equal((4, 3, 2), FixEffects.ChangedLines(Fix(4, 0, "added", "added too"), Lines(10)));
    }

    /// <summary>A fix made longer to be found in one place only keeps the lines around its change as they were.</summary>
    [Fact]
    public void LinesAFixKeepsAsTheyWereAtEitherEndAreNotChanged()
    {
        Assert.Equal((5, 5, 0), FixEffects.ChangedLines(Fix(4, 3, "line 4", "changed", "line 6"), Lines(10)));
    }

    [Fact]
    public void ALineTakenOutOfABlockMovesTheLinesAfterItUp()
    {
        Assert.Equal((5, 5, -1), FixEffects.ChangedLines(Fix(4, 3, "line 4", "line 6"), Lines(10)));
    }

    [Fact]
    public void AMistakeNoLongerFoundWithTheFixIsRemovedAndOneStillFoundIsNot()
    {
        var before = Reading(true, ("logic-python-loop-one-short", 2), ("analysis-division-by-zero", 9), ("logic-python-result-discarded", 7));
        var after = Reading(true, ("logic-python-result-discarded", 8));

        var removed = FixEffects.RemovedBy(Fix(4, 0, "added"), Lines(10), before, after);

        Assert.Equal([new CodeFindingPlace("logic-python-loop-one-short", 2), new CodeFindingPlace("analysis-division-by-zero", 9)], removed);
    }

    [Fact]
    public void AMistakeOnALineTheFixChangesIsNeverSaidToBeRemoved()
    {
        var before = Reading(true, ("logic-python-loop-one-short", 4), ("logic-python-result-discarded", 6));
        var after = Reading(true);

        var removed = FixEffects.RemovedBy(Fix(4, 1, "changed"), Lines(10), before, after);

        Assert.Equal([new CodeFindingPlace("logic-python-result-discarded", 6)], removed);
    }

    [Fact]
    public void AnAnalysisFindingIsNotJudgedWhenNeitherReadingCouldFollowTheValues()
    {
        var before = Reading(false, ("analysis-division-by-zero", 9), ("logic-python-loop-one-short", 2));
        var after = Reading(false);

        var removed = FixEffects.RemovedBy(Fix(4, 1, "changed"), Lines(10), before, after);

        Assert.Equal([new CodeFindingPlace("logic-python-loop-one-short", 2)], removed);
    }

    /// <summary>A fix that leaves the file its parser can no longer read, or read all of, hides mistakes rather than removing them.</summary>
    [Fact]
    public void AFixAfterWhichTheFileReadsLessWellRemovesNothing()
    {
        var before = Reading(true, ("analysis-division-by-zero", 9), ("logic-python-loop-one-short", 2));

        Assert.Empty(FixEffects.RemovedBy(Fix(4, 1, "changed"), Lines(10), before, Reading(false)));
        Assert.Empty(FixEffects.RemovedBy(Fix(4, 1, "changed"), Lines(10), before, Reading(true) with { PartsNotRead = 1 }));
    }

    private static Finding Found(int line, string rule, Relation? causedBy = null) => new()
    {
        Kind = FindingKind.Logic,
        Severity = Severity.Warning,
        Confidence = Confidence.Likely,
        File = @"C:\work\marks.py",
        Line = line,
        Title = rule,
        Explanation = "",
        WhyItMatters = "",
        SuggestedFix = "",
        CorrectedExample = "",
        RuleId = rule,
        CausedBy = causedBy,
    };

    [Fact]
    public void AFindingAFixRemovesSaysSoAsSureAsItIsKnown()
    {
        var root = Found(4, "logic-python-loop-one-short");
        var removed = Found(9, "analysis-division-by-zero", new Relation(root.Id, RelationKind.RemovedByItsFix, Confidence.Certain));

        Assert.Equal("Fixing the problem on line 4 removes this one too: with that fix made, in a copy, the file was read again and this was no longer found.",
                     FindingText.Follows(root, removed));
        Assert.Equal("Its fix removes the problem on line 9 too: with the fix made, in a copy, the file was read again and that problem was no longer found.",
                     FindingText.Explains([removed]));
    }

    [Fact]
    public void FindingsAFixRemovesAreSaidApartFromThoseThatOnlyLookLikeConsequences()
    {
        var root = Found(4, "logic-python-loop-one-short");
        var removed = new[] { 9, 12 }.Select(line => Found(line, "analysis-division-by-zero", new Relation(root.Id, RelationKind.RemovedByItsFix, Confidence.Certain))).ToList();
        var alike = Found(15, "NameError", new Relation(root.Id, RelationKind.SameMissingName, Confidence.Certain));

        Assert.Equal("Its fix removes the problems on lines 9 and 12 too: with the fix made, in a copy, the file was read again and those problems were no longer found. " +
                     "The problem on line 15 looks like a consequence of this one, so fixing this may remove it too.",
                     FindingText.Explains([.. removed, alike]));
        Assert.Equal("Follows from the problem on line 4 - fixing that one should remove this.", FindingText.Follows(root, alike));
    }
}
