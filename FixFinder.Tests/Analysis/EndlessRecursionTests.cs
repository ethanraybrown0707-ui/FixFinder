using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;

namespace FixFinder.Tests;

/// <summary>
/// Recursion that never ends: a function that cannot return without calling itself, or a ring of them, found in Python, Java,
/// C#, JavaScript and Go - each said with what that language was seen to do: RecursionError, StackOverflowError, Stack
/// overflow., RangeError, fatal error: stack overflow. A call that may not be made - in a conditional expression, after an and,
/// behind a base case - a program that ends with sys.exit, a generator and a method a subclass can replace are left alone.
/// </summary>
public class EndlessRecursionTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<IReadOnlyList<AnalysisFinding>?> Analyse(string language, string code)
    {
        switch (language)
        {
            case "Python":
                if (PythonFrontend.FindInterpreter() is not { } python) return null;
                return AbstractChecks.Run(await PythonFrontend.ReadAsync([Write("sample.py", code)], python), new SourceText());

            case "Java":
                if (JavaFrontend.FindTools() is not { } tools) return null;
                return AbstractChecks.Run(await JavaFrontend.ReadAsync([Write("App.java", code)], tools.Javac, tools.Java), new SourceText());

            case "JavaScript":
                return AbstractChecks.Run(await JavaScriptFrontend.ReadAsync([Write("main.js", code)]), new SourceText());

            case "Go":
                var file = Write("main.go", code);
                if (GoFrontend.FindGo(file) is not { } go) return null;
                return AbstractChecks.Run(await GoFrontend.ReadAsync([file], go), new SourceText());

            default:
                return AbstractChecks.Run(await CSharpFrontend.ReadAsync([Write("Sample.cs", code)]), new SourceText());
        }
    }

    private string Write(string name, string code)
    {
        var path = Path.Combine(_temp.Path, name);
        File.WriteAllText(path, code);
        return path;
    }

    private static string Summary(IEnumerable<AnalysisFinding> findings) =>
        string.Join("; ", findings.Select(f => $"{f.CheckId} line {f.Span.Line}: {f.Message}"));

    [Theory]
    [InlineData("Python", "def countdown(n):\n    print(n)\n    return countdown(n - 1)\n", 3,
        "`countdown` cannot return without calling itself - every way through it reaches `countdown(n - 1)` - so each call makes another, and they never end: Python stops it with RecursionError: maximum recursion depth exceeded")]
    [InlineData("Python", "def is_even(n):\n    return is_odd(n - 1)\n\n\ndef is_odd(n):\n    return is_even(n - 1)\n", 2,
        "`is_even` cannot return without calling `is_odd`, `is_odd` without calling `is_even` - so the calls go round and never end: Python stops it with RecursionError: maximum recursion depth exceeded")]
    [InlineData("Java", "public class App {\n    static int countdown(int n) {\n        return countdown(n - 1);\n    }\n}\n", 3, "Java stops it with a StackOverflowError")]
    [InlineData("C#", "class Program\n{\n    static int Countdown(int n)\n    {\n        return Countdown(n - 1);\n    }\n}\n", 5, ".NET ends the whole program with Stack overflow.")]
    [InlineData("JavaScript", "function countdown(n) {\n  console.log(n);\n  return countdown(n - 1);\n}\n", 3, "Node.js stops it with RangeError: Maximum call stack size exceeded")]
    [InlineData("Go", "package main\n\nfunc countdown(n int) int {\n\treturn countdown(n-1) + 1\n}\n\nfunc main() {\n\tcountdown(3)\n}\n", 4, "Go stops it with fatal error: stack overflow")]
    public async Task ARecursionWithNoWayOutIsFoundOnceAtItsCall(string language, string code, int line, string said)
    {
        if (await Analyse(language, code) is not { } findings) return;

        var endless = Assert.Single(findings, f => f.CheckId == EndlessRecursion.CheckId);
        Assert.Equal(line, endless.Span.Line);
        Assert.Contains(said, endless.Message);
    }

    [Theory]
    [InlineData("Python", "def factorial(n):\n    return 1 if n <= 1 else n * factorial(n - 1)\n")]
    [InlineData("Python", "def total(values):\n    if not values:\n        return 0\n    return values[0] + total(values[1:])\n")]
    [InlineData("Python", "def ask():\n    return input('> ') == 'y' and ask()\n")]
    [InlineData("Python", "import sys\n\ndef menu():\n    if input('> ') == 'q':\n        sys.exit()\n    return menu()\n")]
    [InlineData("Python", "def numbers(n):\n    yield n\n    yield from numbers(n + 1)\n")]
    [InlineData("Python", "def check(n):\n    if n < 0:\n        raise ValueError('negative')\n    return check(n - 1)\n")]
    [InlineData("Python", "class Node:\n    def depth(self):\n        return self.depth()\n")]
    [InlineData("Java", "public class App {\n    static int factorial(int n) {\n        return n <= 1 ? 1 : n * factorial(n - 1);\n    }\n}\n")]
    [InlineData("C#", "class Program\n{\n    static int Factorial(int n) => n <= 1 ? 1 : n * Factorial(n - 1);\n}\n")]
    [InlineData("JavaScript", "function factorial(n) {\n  if (n <= 1) {\n    return 1;\n  }\n  return n * factorial(n - 1);\n}\n")]
    public async Task ARecursionWithAWayOutIsLeftAlone(string language, string code)
    {
        if (await Analyse(language, code) is not { } findings) return;

        Assert.True(findings.All(f => f.CheckId != EndlessRecursion.CheckId), Summary(findings));
    }
}
