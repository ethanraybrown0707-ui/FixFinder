using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Tests;

/// <summary>
/// The before-and-after a finding shows, built from the fix itself. What matters here is that nothing is shown that
/// FixFinder did not work out: no fix means no change, and a fix that leaves the file as it was is not a change either.
/// </summary>
public class CodeChangeTests
{
    private static SourceFile FileOf(params string[] lines) =>
        new() { Path = @"C:\work\Readings.cs", Lines = lines };

    private static LocalFix Replacing(int line, string text) =>
        LocalFix.ReplaceLine("rule", "Title", "Explanation", @"C:\work\Readings.cs", line, text);

    [Fact]
    public void OnlyThePartOfTheLineThatChangedIsMarked()
    {
        var source = FileOf(
            "int total = 0;",
            "for (int i = 0; i <= readings.Length; i++)",
            "{",
            "    total += readings[i];",
            "}");

        var change = CodeChange.From(Replacing(2, "for (int i = 0; i < readings.Length; i++)"), source);

        Assert.NotNull(change);

        var removed = Assert.Single(change.Lines, l => l.Kind == ChangeKind.Removed);
        var added = Assert.Single(change.Lines, l => l.Kind == ChangeKind.Added);

        Assert.Equal("=", removed.Changed);
        Assert.Equal("", added.Changed);

        Assert.Equal("for (int i = 0; i <", removed.Before);
        Assert.Equal(" readings.Length; i++)", removed.After);
    }

    [Fact]
    public void AChangedWordIsMarkedWholeRatherThanByItsLetters()
    {
        var source = FileOf("total = reading + 1;");

        var change = CodeChange.From(Replacing(1, "total = readings + 1;"), source);

        Assert.NotNull(change);

        // The letters in common are "reading" and "s" is the only difference, but marking one letter of a name reads
        // as a mistake in the marking rather than as the edit.
        Assert.Equal("reading", Assert.Single(change.Lines, l => l.Kind == ChangeKind.Removed).Changed);
        Assert.Equal("readings", Assert.Single(change.Lines, l => l.Kind == ChangeKind.Added).Changed);
    }

    [Fact]
    public void TheLinesEitherSideComeWithItSoTheChangeCanBePlaced()
    {
        var source = FileOf("one", "two", "three", "four", "five", "six", "seven");

        var change = CodeChange.From(Replacing(4, "FOUR"), source);

        Assert.NotNull(change);

        Assert.Equal(
            ["two", "three", "four", "FOUR", "five", "six"],
            change.Lines.Select(l => l.Text).ToArray());

        Assert.Equal([2, 3], change.Lines.Where(l => l.Kind == ChangeKind.Context).Take(2).Select(l => l.Number).ToArray());
    }

    [Fact]
    public void AChangeAtTheTopOfTheFileDoesNotRunOffTheStart()
    {
        var source = FileOf("first", "second");

        var change = CodeChange.From(Replacing(1, "FIRST"), source);

        Assert.NotNull(change);
        Assert.Equal(ChangeKind.Removed, change.Lines[0].Kind);
        Assert.Equal(1, change.Lines[0].Number);
    }

    [Fact]
    public void NothingIsShownWithoutTheFileToCompareAgainst()
    {
        Assert.Null(CodeChange.From(Replacing(1, "anything"), null));
    }

    [Fact]
    public void AFixThatLeavesTheLineAsItWasIsNotAChange()
    {
        var source = FileOf("total = 0;");

        Assert.Null(CodeChange.From(Replacing(1, "total = 0;"), source));
    }

    [Fact]
    public void AddedLinesAreCountedAndCarryTheNumbersTheyWillHave()
    {
        var source = FileOf("def share(prize, winners):", "    return prize // winners");

        var fix = LocalFix.Insert("rule", "Title", "Explanation", @"C:\work\Readings.cs", 2,
            ["    if winners == 0:", "        return 0"]);

        var change = CodeChange.From(fix, source);

        Assert.NotNull(change);
        Assert.Equal(2, change.AddedCount);
        Assert.Equal(0, change.RemovedCount);
        Assert.Equal([2, 3], change.Lines.Where(l => l.Kind == ChangeKind.Added).Select(l => l.Number).ToArray());
        Assert.Equal("2 lines added", change.Summary);
    }

    [Fact]
    public void ManyLinesAtOnceAreShownWholeRatherThanMarkedInside()
    {
        var source = FileOf("a = 1;", "b = 2;", "c = 3;");

        var fix = new LocalFix
        {
            RuleId = "rule", Title = "Title", Explanation = "Explanation", File = @"C:\work\Readings.cs",
            StartLine = 1, RemoveCount = 2, NewLines = ["a = 10;", "b = 20;"],
        };

        var change = CodeChange.From(fix, source);

        Assert.NotNull(change);
        Assert.Equal(2, change.RemovedCount);
        Assert.Equal(2, change.AddedCount);
        Assert.All(change.Lines, line => Assert.False(line.HasHighlight));
        Assert.Equal("2 lines replaced by 2", change.Summary);
    }

    [Fact]
    public void ALineReplacedBySomethingEntirelyDifferentIsNotMarkedInside()
    {
        var source = FileOf("counts[word] += 1");

        var change = CodeChange.From(Replacing(1, "raise ValueError('no')"), source);

        Assert.NotNull(change);
        Assert.All(change.Lines, line => Assert.False(line.HasHighlight));
    }
}
