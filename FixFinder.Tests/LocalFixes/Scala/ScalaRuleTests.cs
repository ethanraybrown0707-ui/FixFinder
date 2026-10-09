using FixFinder.Core.LocalFixes;
using FixFinder.Core.Parsing;

namespace FixFinder.Tests;

/// <summary>
/// Each Scala rule's change, worked out from what Scala 3.8.4, Scala 2.13.18 or a Scala program's crash said - the
/// messages as they said them - and the changes each rule refuses to make.
/// </summary>
public class ScalaRuleTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Write(string name, string body)
    {
        var path = Path.Combine(_temp.Path, name);
        File.WriteAllText(path, body);
        return path;
    }

    private static ParsedError Error(string type, string message, string file, int line, int? column = null) => new()
    {
        LanguageId = "scala",
        Confidence = 85,
        RawText = message,
        FirstLineSequence = 0,
        ExceptionType = type,
        Message = message,
        Frames = [new ErrorFrame { Order = 0, File = file, Line = line, Column = column, RawLine = "", Origin = FrameOrigin.FirstParty }],
    };

    private LocalFix? Fix(string rule, ParsedError error) =>
        LocalFixEngine.Rules.Single(r => r.Id == rule).Propose(new LocalFixContext { Error = error, SourceRoot = _temp.Path });

    private const string Hello = "@main def hello(): Unit =\n  val name = \"Ada\"\n  prinln(\"Hello, \" + name)\n";
    private const string Words = "object NotMember {\n  def main(args: Array[String]): Unit = {\n    val words = List(\"a\", \"b\")\n    println(words.lenght)\n  }\n}\n";
    private const string Counting = "object ValReassign {\n  def main(args: Array[String]): Unit = {\n    val count = 0\n    count = count + 1\n    println(count)\n  }\n}\n";
    private const string Total = "object Total {\n  def main(args: Array[String]): Unit = {\n    val count: Int = \"three\"\n    val price: Double = 2.5\n    val total: Int = count * price\n    println(total)\n  }\n}\n";

    [Theory]
    [InlineData("Not found: prinln - did you mean println?", Hello, 3, 3, "  println(\"Hello, \" + name)")]
    [InlineData("value lenght is not a member of List[String] - did you mean words.length?", Words, 4, 19, "    println(words.length)")]
    [InlineData("value lenght is not a member of List[String]\ndid you mean length? or perhaps lengthIs?", Words, 4, 19, "    println(words.length)")]
    [InlineData("Not found: prinln", Hello, 3, 3, null)]
    public void ANameScalaDoesNotKnowIsChangedToTheOneScalaSuggests(string message, string code, int line, int column, string? expected)
    {
        var fix = Fix("scala-did-you-mean", Error("compile error", message, Write("Program.scala", code), line, column));

        Assert.Equal(expected, fix?.NewLines.Single());
    }

    [Theory]
    [InlineData("Reassignment to val count")]
    [InlineData("reassignment to val")]
    public void AValGivenANewValueIsMadeAVarWhereItIsDefined(string message)
    {
        var fix = Fix("scala-val-to-var", Error("compile error", message, Write("ValReassign.scala", Counting), 4));

        Assert.NotNull(fix);
        Assert.Equal(3, fix.StartLine);
        Assert.Equal("    var count = 0", fix.NewLines.Single());
    }

    [Fact]
    public void ANumberDeclaredAsAnotherKindOfNumberIsDeclaredAsTheKindItIs()
    {
        var file = Write("Total.scala", Total);

        var doubleForInt = Fix("scala-declared-number-type", Error("compile error", "Found:    Double\nRequired: Int", file, 5));
        var textForInt = Fix("scala-declared-number-type", Error("compile error", "Found:    (\"three\" : String)\nRequired: Int", file, 3));

        Assert.Equal("    val total: Double = count * price", doubleForInt?.NewLines.Single());
        Assert.Null(textForInt);
    }

    [Fact]
    public void AMethodInProcedureSyntaxIsGivenTheResultTypeAndEqualsScala3Needs()
    {
        var file = Write("Old.scala", "object Old {\n  def main(args: Array[String]) {\n    println(\"procedure syntax\")\n  }\n}\n");

        var fix = Fix("scala-procedure-syntax", Error("compile error", "'=' expected, but '{' found", file, 2, 33));

        Assert.Equal("  def main(args: Array[String]): Unit = {", fix?.NewLines.Single());
    }

    [Fact]
    public void AWholeNumberDividedByZeroIsGivenAnAnswerForWhenTheDivisorIsZero()
    {
        var file = Write("Marks.scala", "object Marks {\n  def average(scores: List[Int]): Int = scores.sum / scores.length\n}\n");

        var fix = Fix("scala-division-guard", Error("java.lang.ArithmeticException", "/ by zero", file, 2));

        Assert.Equal("  def average(scores: List[Int]): Int = (if (scores.length == 0) 0 else scores.sum / scores.length)", fix?.NewLines.Single());
    }

    [Fact]
    public void ARangeThatRunsOnePastTheEndOfWhatItIndexesStopsBeforeIt()
    {
        var file = Write("ArrayIndex.scala", "object ArrayIndex {\n  def main(args: Array[String]): Unit = {\n    val marks = Array(70, 80, 90)\n    for (i <- 0 to marks.length) println(marks(i))\n  }\n}\n");

        var fix = Fix("scala-range-until", Error("java.lang.ArrayIndexOutOfBoundsException", "Index 3 out of bounds for length 3", file, 4));
        var unrelated = Fix("scala-range-until", Error("java.lang.ArithmeticException", "/ by zero", file, 4));

        Assert.Equal("    for (i <- 0 until marks.length) println(marks(i))", fix?.NewLines.Single());
        Assert.Null(unrelated);
    }
}
