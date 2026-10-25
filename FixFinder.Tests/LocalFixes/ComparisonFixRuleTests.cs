using FixFinder.Core.LocalFixes;
using FixFinder.Core.Parsing;

namespace FixFinder.Tests;

/// <summary>
/// A second comparison with no left side, a comparison or a sum on a line of its own, and a C# foreach variable given a new
/// value: the change each rule works out from the compiler's words, and where it works out none.
/// </summary>
public class ComparisonFixRuleTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private LocalFix? Fix(string rule, string language, string type, string? code, string message, string fileName, string source, int line)
    {
        var path = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(path, source);
        var error = new ParsedError
        {
            LanguageId = language, Confidence = 90, RawText = message, FirstLineSequence = 0, ExceptionType = type, ErrorCode = code,
            Message = message, Frames = [new ErrorFrame { Order = 0, File = path, Line = line, RawLine = "" }],
        };
        return LocalFixEngine.Rules.Single(each => each.Id == rule).Propose(new LocalFixContext { Error = error, SourceRoot = _temp.Path });
    }

    private static string InJava(string statement) => $"public class Main {{\n    public static void main(String[] args) {{\n        {statement}\n    }}\n}}\n";

    [Theory]
    [InlineData("if (age > 12 && < 20) {", "        if (age > 12 && age < 20) {")]
    [InlineData("while (marks[i] >= 40 || == -1) {", "        while (marks[i] >= 40 || marks[i] == -1) {")]
    [InlineData("boolean teen = age > 12 && < 20 && != 15;", "        boolean teen = age > 12 && age < 20 && age != 15;")]
    public void JavasSecondComparisonIsGivenTheFirstsLeftSide(string statement, string changedTo) =>
        Assert.Equal(changedTo, Fix("java-repeat-left-side", "java", "compile error", null, "illegal start of expression", "Main.java", InJava(statement), 3)?.NewLines.Single());

    [Theory]
    [InlineData("if age > 12 and < 20:\n    print(age)\n", "if age > 12 and age < 20:")]
    [InlineData("if mark < 0 or > 100:\n    print(mark)\n", "if mark < 0 or mark > 100:")]
    public void PythonsSecondComparisonIsGivenTheFirstsLeftSide(string code, string changedTo) =>
        Assert.Equal(changedTo, Fix("python-repeat-left-side", "python", "SyntaxError", null, "invalid syntax", "app.py", code, 1)?.NewLines.Single());

    [Fact]
    public void ALineWithBothSidesOfEachComparisonIsLeftAlone()
    {
        Assert.Null(Fix("java-repeat-left-side", "java", "compile error", null, "illegal start of expression", "Main.java", InJava("if (age > 12 && age < 20) {"), 3));
        Assert.Null(Fix("python-repeat-left-side", "python", "SyntaxError", null, "invalid syntax", "app.py", "if age > 12 and age < 20 print(age)\n", 1));
    }

    [Theory]
    [InlineData("count == 0;", "        count = 0;")]
    [InlineData("total + mark;", "        total += mark;")]
    [InlineData("marks[i] * 2;   // double it", "        marks[i] *= 2;   // double it")]
    public void JavasComparisonOrSumOnALineOfItsOwnIsStored(string statement, string changedTo) =>
        Assert.Equal(changedTo, Fix("java-not-a-statement", "java", "compile error", null, "not a statement", "Main.java", InJava(statement), 3)?.NewLines.Single());

    [Theory]
    // A call, text, or ++, which are not a value worked out and lost in the same way.
    [InlineData("\"hello\";")]
    [InlineData("count++ + 1;")]
    public void ALineThatIsNotACompareOrASumGetsNoChange(string statement) =>
        Assert.Null(Fix("java-not-a-statement", "java", "compile error", null, "not a statement", "Main.java", InJava(statement), 3));

    private const string Foreach = "Cannot assign to 'mark' because it is a 'foreach iteration variable'";

    [Fact]
    public void ACSharpForeachOverAListBecomesAForOverItsPositions()
    {
        const string code = "var marks = new List<int> { 70, 80 };\nforeach (var mark in marks) {\n    mark = mark * 2;\n    Console.WriteLine(mark);\n}\n";
        var fix = Fix("csharp-foreach-variable-assigned", "msvc", "compile error", "CS1656", Foreach, "Program.cs", code, 3);

        Assert.Equal(2, fix?.StartLine);
        Assert.Equal(["for (int index = 0; index < marks.Count; index++) {", "    marks[index] = marks[index] * 2;", "    Console.WriteLine(marks[index]);"], fix!.NewLines);
    }

    [Fact]
    public void ACSharpForeachOverACollectionWithNoPositionsGetsNoChange()
    {
        const string code = "IEnumerable<int> marks = Read();\nforeach (var mark in marks)\n{\n    mark += 5;\n}\n";

        Assert.Null(Fix("csharp-foreach-variable-assigned", "msvc", "compile error", "CS1656", Foreach, "Program.cs", code, 4));
    }
}
