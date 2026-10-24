using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;

namespace FixFinder.Tests;

/// <summary>
/// Numbers put in order by Perl's sort with nothing to compare by: found where the array was given only numbers, with the
/// order they come out in, and left alone where sort is told how to compare, or the array holds text.
/// </summary>
public class PerlLogicPatternTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private IReadOnlyList<LogicFinding> Scan(string code)
    {
        var path = Path.Combine(_temp.Path, "main.pl");
        File.WriteAllText(path, "use strict;\nuse warnings;\n" + code);
        return LogicPatterns.Scan(SourceFile.Read(path)!);
    }

    private const string Check = "logic-perl-numbers-sorted-as-text";

    [Theory]
    [InlineData("my @marks = (10, 9, 100);\nmy @sorted = sort @marks;\n", "my @sorted = sort { $a <=> $b } @marks;",
        "so 10, 9, 100, the numbers in @marks, come out as 10 100 9, not 9 10 100")]
    [InlineData("my @marks = (5, 30, 200);\nprint join(\" \", sort(@marks)), \"\\n\";\n", "print join(\" \", sort { $a <=> $b } (@marks)), \"\\n\";",
        "so 5, 30, 200, the numbers in @marks, come out as 200 30 5, not 5 30 200")]
    [InlineData("my @marks = (-1, 10, 2);\n@marks = sort @marks;\n", "@marks = sort { $a <=> $b } @marks;",
        "so -1, 10, 2, the numbers in @marks, come out as -1 10 2, not -1 2 10")]
    // In order as text by chance: still sorted as text.
    [InlineData("my @marks = (70, 80, 90);\nmy @sorted = sort @marks;\n", "my @sorted = sort { $a <=> $b } @marks;",
        "70, 80, 90, the numbers in @marks, happen to come out in order, but as text 100 comes before 9, so other numbers would not")]
    // Decimals, which Perl may write back differently, and an array given more numbers later: the order is not worked out.
    [InlineData("my @prices = (2.50, 10, 1.5);\nmy @sorted = sort @prices;\n", "my @sorted = sort { $a <=> $b } @prices;",
        "and @prices holds numbers, so 10, 9 and 100 would come out as 10 100 9")]
    [InlineData("my @marks = (3, 1);\npush @marks, 20;\nmy @sorted = sort @marks;\n", "my @sorted = sort { $a <=> $b } @marks;",
        "and @marks holds numbers, so 10, 9 and 100 would come out as 10 100 9")]
    public void NumbersSortedAsTextAreFoundWithTheOrderTheyComeOutIn(string code, string changedTo, string messageEnds)
    {
        var found = Assert.Single(Scan(code), finding => finding.PatternId == Check);

        Assert.Equal(changedTo, found.Fix!.NewLines.Single());
        Assert.EndsWith(messageEnds, found.Message, StringComparison.Ordinal);
    }

    [Theory]
    // Told how to compare.
    [InlineData("my @marks = (10, 9, 100);\nmy @sorted = sort { $a <=> $b } @marks;\n")]
    [InlineData("my @marks = (10, 9, 100);\nmy @sorted = reverse sort { $a <=> $b } @marks;\n")]
    // Text, as sort is for.
    [InlineData("my @names = (\"Bob\", \"Ada\");\nmy @sorted = sort @names;\n")]
    [InlineData("my @words = qw(10 9);\nmy @sorted = sort @words;\n")]
    // Numbers, then text pushed on.
    [InlineData("my @marks = (10, 9);\npush @marks, \"Ada\";\nmy @sorted = sort @marks;\n")]
    public void SortToldHowToCompareOrGivenTextIsLeftAlone(string code) =>
        Assert.DoesNotContain(Scan(code), finding => finding.PatternId == Check);
}
