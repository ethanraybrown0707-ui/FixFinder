using FixFinder.Core.LocalFixes;
using FixFinder.Core.Parsing;

namespace FixFinder.Tests;

/// <summary>
/// A print given a printf code and values, which Java and Dart refuse to compile: the change each rule works out from the
/// compiler's words, and where it works out none.
/// </summary>
public class FormatFixRuleTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private LocalFix? Fix(string rule, string language, string? code, string message, string fileName, string source, int line)
    {
        var path = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(path, source);
        var error = new ParsedError
        {
            LanguageId = language, Confidence = 90, RawText = message, FirstLineSequence = 0, ExceptionType = "compile error", ErrorCode = code,
            Message = message, Frames = [new ErrorFrame { Order = 0, File = path, Line = line, RawLine = "" }],
        };
        return LocalFixEngine.Rules.Single(each => each.Id == rule).Propose(new LocalFixContext { Error = error, SourceRoot = _temp.Path });
    }

    private const string JavaRule = "java-printf-for-println";

    private static string InMain(string statement) =>
        $"public class Main {{\n    public static void main(String[] args) {{\n        {statement}\n    }}\n}}\n";

    [Theory]
    [InlineData("System.out.println(\"Total: %d\", total);", "println(String,int)", "        System.out.printf(\"Total: %d%n\", total);")]
    // print gave no new line, so none is added; nor where the text already ends with one.
    [InlineData("System.out.print(\"%s scored %d\\n\", name, total);", "print(String,String,int)", "        System.out.printf(\"%s scored %d\\n\", name, total);")]
    [InlineData("System.out.println(\"Total: %d%n\", total);", "println(String,int)", "        System.out.printf(\"Total: %d%n\", total);")]
    public void JavasPrintlnGivenACodeAndValuesBecomesPrintf(string statement, string signature, string changedTo)
    {
        var fix = Fix(JavaRule, "java", null, $"no suitable method found for {signature}", "Main.java", InMain(statement), 3);

        Assert.Equal(changedTo, fix?.NewLines.Single());
    }

    [Theory]
    // No code in the text: the values were meant to be joined, not filled in.
    [InlineData("System.out.println(\"Total: \", total);", "println(String,int)")]
    // The text is not the first thing given.
    [InlineData("System.out.println(total, \"Total: %d\");", "println(int,String)")]
    public void JavasPrintlnWithNoCodeInItsTextGetsNoPrintf(string statement, string signature) =>
        Assert.Null(Fix(JavaRule, "java", null, $"no suitable method found for {signature}", "Main.java", InMain(statement), 3));

    private const string DartRule = "dart-format-into-text";

    [Theory]
    [InlineData("print(\"Total: %d\", total);", "  print(\"Total: $total\");")]
    // A name with letters straight after its code is put in with braces, as is anything more than a name.
    [InlineData("print(\"%dpts\", total);", "  print(\"${total}pts\");")]
    [InlineData("print(\"Next: %d\", total + 1);", "  print(\"Next: ${total + 1}\");")]
    [InlineData("print(\"%s: %.2f%%\", name, rate);", "  print(\"$name: ${rate.toStringAsFixed(2)}%\");")]
    public void DartsPrintGivenACodeAndValuesPutsTheValuesIntoTheText(string statement, string changedTo)
    {
        var fix = Fix(DartRule, "dart", "extra_positional_arguments", "Too many positional arguments: 1 expected, but 2 found.", "main.dart",
            $"void main() {{\n  {statement}\n}}\n", 2);

        Assert.Equal(changedTo, fix?.NewLines.Single());
    }

    [Theory]
    // A width, or a code Dart's text has no form for.
    [InlineData("print(\"%5d\", total);")]
    [InlineData("print(\"%x\", total);")]
    // A $ already in the text, which Dart would read as a value to put in.
    [InlineData("print(\"$name %d\", total);")]
    // More values than codes.
    [InlineData("print(\"Total: %d\", total, count);")]
    // A function of the program's own, given too many values.
    [InlineData("show(\"Total: %d\", total);")]
    public void DartsPrintWithNoPlaceForEachValueGetsNoChange(string statement) =>
        Assert.Null(Fix(DartRule, "dart", "extra_positional_arguments", "Too many positional arguments: 1 expected, but 2 found.", "main.dart",
            $"void main() {{\n  {statement}\n}}\n", 2));
}
