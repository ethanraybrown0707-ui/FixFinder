using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;
using FixFinder.Core.Parsing;

namespace FixFinder.Tests;

/// <summary>The rules for crashes that have one conventional fix, and for characters pasted in from a word processor.</summary>
public class GuardRuleTests : IDisposable
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
        File.WriteAllText(path, body.ReplaceLineEndings("\n"));
        return path;
    }

    private LocalFixContext Context(string language, string type, string message, string file, int line, string? code = null) => new()
    {
        Error = new ParsedError
        {
            LanguageId = language,
            Confidence = 90,
            RawText = message,
            FirstLineSequence = 0,
            ExceptionType = type,
            ErrorCode = code,
            Message = message,
            Frames = [new ErrorFrame { Order = 0, File = file, Line = line, RawLine = "" }],
        },
        SourceRoot = _temp.Path,
    };

    private static string FixedLine(LocalFix? fix)
    {
        Assert.NotNull(fix);
        return fix!.ApplyTo(SourceFile.Read(fix.File)!)![fix.StartLine - 1];
    }

    [Theory]
    [InlineData("def average(xs):\n    return sum(xs) / len(xs)\n", 2, "    return (sum(xs) / len(xs) if len(xs) else 0)")]
    [InlineData("average = total / count\n", 1, "average = (total / count if count else 0)")]
    [InlineData("half = 10 / 2\n", 1, null)]
    public void PythonDivisionIsGuarded(string code, int line, string? expected)
    {
        var file = Write("app.py", code);
        var fix = new PythonDivisionGuard().Propose(Context("python", "ZeroDivisionError", "division by zero", file, line));

        if (expected is null) Assert.Null(fix);
        else Assert.Equal(expected, FixedLine(fix));
    }

    [Fact]
    public void JavaAndCSharpDivisionsAreGuarded()
    {
        var java = Write("App.java", "        System.out.println(10 / count);\n");
        var cs = Write("Program.cs", "var average = total / items.Count;\n");

        Assert.Equal("        System.out.println((count == 0 ? 0 : 10 / count));",
            FixedLine(new JavaDivisionGuard().Propose(Context("java", "java.lang.ArithmeticException", "/ by zero", java, 1))));
        Assert.Equal("var average = (items.Count == 0 ? 0 : total / items.Count);",
            FixedLine(new CSharpDivisionGuard().Propose(Context("csharp", "System.DivideByZeroException", "Attempted to divide by zero.", cs, 1))));
    }

    [Fact]
    public void AMissingKeyIsReadWithADefault()
    {
        var py = Write("app.py", "print(prices['pear'])\n");
        var cs = Write("Program.cs", "Console.WriteLine(prices[\"pear\"]);\n");

        Assert.Equal("print(prices.get('pear'))", FixedLine(new PythonMissingKeyGet().Propose(Context("python", "KeyError", "'pear'", py, 1))));
        Assert.Equal("Console.WriteLine(prices.GetValueOrDefault(\"pear\"));",
            FixedLine(new CSharpMissingKeyDefault().Propose(Context("csharp", "System.Collections.Generic.KeyNotFoundException",
                "The given key 'pear' was not present in the dictionary.", cs, 1))));
    }

    [Fact]
    public void AKeyBeingSetIsNotTouched()
    {
        var py = Write("app.py", "prices['pear'] = 3\n");

        Assert.Null(new PythonMissingKeyGet().Propose(Context("python", "KeyError", "'pear'", py, 1)));
    }

    [Fact]
    public void AnEmptySequenceGetsADefault()
    {
        var cs = Write("Program.cs", "Console.WriteLine(xs.First());\nConsole.WriteLine(xs.Max());\n");

        Assert.Equal("Console.WriteLine(xs.FirstOrDefault());",
            FixedLine(new CSharpEmptySequenceDefault().Propose(Context("csharp", "System.InvalidOperationException", "Sequence contains no elements", cs, 1))));
        Assert.Equal("Console.WriteLine(xs.DefaultIfEmpty().Max());",
            FixedLine(new CSharpEmptySequenceDefault().Propose(Context("csharp", "System.InvalidOperationException", "Sequence contains no elements", cs, 2))));
    }

    [Fact]
    public void CurlyQuotesAreStraightened()
    {
        var py = Write("app.py", "print(\u201CHello\u201D)\n");
        var java = Write("App.java", "        System.out.println(\u201CHi\u201D);\n");

        Assert.Equal("print(\"Hello\")", FixedLine(new PythonSmartQuotes().Propose(Context("python", "SyntaxError", "invalid character '\u201C' (U+201C)", py, 1))));
        Assert.Equal("        System.out.println(\"Hi\");", FixedLine(new JavaSmartQuotes().Propose(Context("java", "compile error", "illegal character: '\\u201c'", java, 1))));
    }

    [Fact]
    public void ARecursionThatCountsDownGetsABaseCase()
    {
        var py = Write("app.py", "def count_down(n):\n    print(n)\n    count_down(n - 1)\n\ncount_down(3)\n");
        var java = Write("App.java", "class App {\n    static void countDown(int n) {\n        System.out.println(n);\n        countDown(n - 1);\n    }\n}\n");

        var pythonFix = new PythonRecursionBaseCase().Propose(Context("python", "RecursionError", "maximum recursion depth exceeded", py, 2));
        var javaFix = new JavaRecursionBaseCase().Propose(Context("java", "java.lang.StackOverflowError", "", java, 3));

        Assert.Equal(["    if n <= 0:", "        return"], pythonFix!.NewLines);
        Assert.Equal(2, pythonFix.StartLine);
        Assert.Equal(["        if (n <= 0) return;"], javaFix!.NewLines);
        Assert.Equal(3, javaFix.StartLine);
    }

    [Fact]
    public void ARecursionThatAlreadyHasABaseCaseIsLeftAlone()
    {
        var py = Write("app.py", "def fact(n):\n    if n <= 1:\n        return 1\n    return n * fact(n - 1)\n");

        Assert.Null(new PythonRecursionBaseCase().Propose(Context("python", "RecursionError", "maximum recursion depth exceeded", py, 4)));
    }

    [Fact]
    public void AFallthroughWarningGetsABreak()
    {
        var java = Write("App.java", "        switch (d) {\n            case 1:\n                System.out.println(\"one\");\n            case 2:\n                break;\n        }\n");
        var fix = new JavaFallthroughBreak().Propose(Context("java", "compile warning", "[fallthrough] possible fall-through into case", java, 4));

        Assert.NotNull(fix);
        Assert.Equal(4, fix!.StartLine);
        Assert.Equal(["                break;"], fix.NewLines);
        Assert.Equal("[fallthrough]", fix.ResolvesWarning);
    }
}
