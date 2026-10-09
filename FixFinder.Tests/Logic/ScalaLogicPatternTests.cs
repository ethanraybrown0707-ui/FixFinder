using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;

namespace FixFinder.Tests;

/// <summary>
/// The mistakes Scala programs make that still compile - each found where it is, with its change, and left alone where
/// the same words are correct: a range that stops in time, a List compared with ==, a copy that is a block's value, a
/// .get on an Option the line above checked.
/// </summary>
public class ScalaLogicPatternTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private IReadOnlyList<LogicFinding> Scan(string code)
    {
        var path = Path.Combine(_temp.Path, "Program.scala");
        File.WriteAllText(path, code);
        return LogicPatterns.Scan(SourceFile.Read(path)!);
    }

    private static string Main(string body) => "object Program {\n  def main(args: Array[String]): Unit = {\n" + body + "  }\n}\n";

    [Theory]
    [InlineData("    val marks = Array(70, 80, 90)\n    for (i <- 0 to marks.length) println(marks(i))\n", 4, "    for (i <- 0 until marks.length) println(marks(i))")]
    [InlineData("    val marks = List(70, 80, 90)\n    for (i <- 0 to marks.size) {\n      println(marks(i))\n    }\n", 4, "    for (i <- 0 until marks.size) {")]
    public void ALoopOverZeroToTheLengthThatIndexesWithItIsFound(string body, int line, string fixedLine)
    {
        var finding = Assert.Single(Scan(Main(body)), finding => finding.PatternId == "logic-scala-range-to-length");

        Assert.Equal(line, finding.Line);
        Assert.Equal(fixedLine, finding.Fix?.NewLines.Single());
    }

    [Theory]
    [InlineData("    val marks = Array(70, 80, 90)\n    for (i <- 0 until marks.length) println(marks(i))\n")]
    [InlineData("    val marks = Array(70, 80, 90)\n    for (i <- 0 to marks.length - 1) println(marks(i))\n")]
    [InlineData("    val marks = Array(70, 80, 90)\n    for (count <- 0 to marks.length) println(\"seen \" + count)\n")]
    public void ARangeThatStopsInTimeOrIndexesNothingIsLeftAlone(string body) =>
        Assert.DoesNotContain(Scan(Main(body)), finding => finding.PatternId == "logic-scala-range-to-length");

    [Fact]
    public void TwoArraysComparedWithEqualsAreFoundAndCompareTheirItemsInstead()
    {
        var finding = Assert.Single(Scan(Main("    val first = Array(1, 2, 3)\n    val second = Array(1, 2, 3)\n    if (first == second) println(\"same\")\n")),
            finding => finding.PatternId == "logic-scala-array-equals");

        Assert.Equal(5, finding.Line);
        Assert.Equal("    if (first.sameElements(second)) println(\"same\")", finding.Fix?.NewLines.Single());
    }

    [Fact]
    public void ListsComparedWithEqualsAreLeftAlone() =>
        Assert.DoesNotContain(Scan(Main("    val first = List(1, 2, 3)\n    val second = List(1, 2, 3)\n    if (first == second) println(\"same\")\n")),
            finding => finding.PatternId == "logic-scala-array-equals");

    [Theory]
    [InlineData("    val scores = List(1, 2)\n    val average: Double = scores.sum / scores.length\n", "    val average: Double = scores.sum.toDouble / scores.length")]
    [InlineData("    val total = 7\n    val count = 2\n    val share: Double = total / count\n", "    val share: Double = total.toDouble / count")]
    public void AWholeNumberQuotientGivenToADoubleIsFound(string body, string fixedLine)
    {
        var finding = Assert.Single(Scan(Main(body)), finding => finding.PatternId == "logic-scala-integer-average");

        Assert.Equal(fixedLine, finding.Fix?.NewLines.Single());
    }

    [Theory]
    [InlineData("    val scores = List(1.5, 2.5)\n    val average: Double = scores.sum / scores.length\n")]
    [InlineData("    val total: Double = 7\n    val count = 2\n    val share: Double = total / count\n")]
    [InlineData("    val scores = List(1, 2)\n    val average: Double = scores.sum.toDouble / scores.length\n")]
    public void ADivisionThatIsAlreadyADecimalOneIsLeftAlone(string body) =>
        Assert.DoesNotContain(Scan(Main(body)), finding => finding.PatternId == "logic-scala-integer-average");

    [Fact]
    public void ACopyNobodyKeepsIsFoundAndAVarIsGivenIt()
    {
        var found = Scan(Main("    var marks = List(3, 1, 2)\n    marks.sorted\n    println(marks)\n    val name = \" Ada \"\n    name.trim\n    println(name)\n"))
            .Where(finding => finding.PatternId == "logic-scala-result-discarded")
            .ToList();

        Assert.Equal(new[] { 4, 7 }, found.Select(finding => finding.Line));
        Assert.Equal("    marks = marks.sorted", found[0].Fix?.NewLines.Single());
        Assert.Null(found[1].Fix);
    }

    [Theory]
    [InlineData("object Program {\n  def ordered(marks: List[Int]): List[Int] = {\n    marks.sorted\n  }\n}\n")]
    [InlineData("object Program {\n  def ordered(marks: List[Int]): List[Int] =\n    marks.sorted\n}\n")]
    [InlineData("object Program {\n  def main(args: Array[String]): Unit = {\n    val marks = List(3, 1, 2)\n    val sortedMarks = marks.sorted\n    println(sortedMarks)\n  }\n}\n")]
    public void ACopyThatIsKeptOrIsABlocksValueIsLeftAlone(string code) =>
        Assert.DoesNotContain(Scan(code), finding => finding.PatternId == "logic-scala-result-discarded");

    [Fact]
    public void AValueTakenOutOfAnOptionWithGetIsFoundUnlessTheLineAboveChecked()
    {
        var takenBlindly = Scan(Main("    val ages = Map(\"Ada\" -> 36)\n    println(ages.get(\"Bob\").get)\n"));
        var checkedFirst = Scan(Main("    val ages = Map(\"Ada\" -> 36)\n    if (ages.contains(\"Bob\"))\n      println(ages.get(\"Bob\").get)\n"));

        var finding = Assert.Single(takenBlindly, finding => finding.PatternId == "logic-scala-option-get");
        Assert.Equal(4, finding.Line);
        Assert.Contains("`.get(\"Bob\").get`", finding.Message);
        Assert.DoesNotContain(checkedFirst, finding => finding.PatternId == "logic-scala-option-get");
    }
}
