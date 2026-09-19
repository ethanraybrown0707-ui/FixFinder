using FixFinder.Core.Analysis.Diffing;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Tests;

/// <summary>
/// Semantic diffing of a fix against the original: both versions are followed path by path with the same symbols for the
/// same inputs, so the report can say which inputs the fix changes the outcome for, and whether it does the same as
/// before for every other one - or that it could not compare them all.
/// </summary>
public class SemanticDiffTests : IDisposable
{
    private const string CouldNotCompare = "FixFinder could not compare the two versions for every other input.";
    private const string SameOtherwise = "For every other input it behaves exactly as before.";

    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>What replacing <paramref name="removeCount"/> lines at <paramref name="startLine"/> changes; null when the language cannot be read here.</summary>
    private async Task<IReadOnlyList<string>?> Changes(string file, string code, int startLine, int removeCount, params string[] newLines)
    {
        var python = PythonFrontend.FindInterpreter();
        if (file.EndsWith(".py", StringComparison.Ordinal) && python is null) return null;
        if (file.EndsWith(".java", StringComparison.Ordinal) && JavaFrontend.FindTools() is null) return null;

        var path = Path.Combine(_temp.Path, file);
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        var fix = new LocalFix
        {
            RuleId = "test", Title = "test", Explanation = "test", File = path,
            StartLine = startLine, RemoveCount = removeCount, NewLines = newLines,
        };

        return await FixDiffs.DescribeAsync(fix, python, CancellationToken.None);
    }

    [Fact]
    public async Task AGuardChangesOnlyTheEmptyCase()
    {
        const string code = "def average(values):\n    total = 0\n    for v in values:\n        total += v\n    return total / len(values)\n\n\nprint(average([1, 2]))\n";
        if (await Changes("sample.py", code, 2, 0, "    if not values:", "        return 0") is not { } changes) return;

        Assert.Equal(["When `values` is empty: before, `average` stopped with ZeroDivisionError on line 5; now it returns 0", CouldNotCompare], changes);
    }

    [Fact]
    public async Task ABoundaryMovedChangesOneInputAndNoOther()
    {
        const string code = "def grade(score):\n    if score > 50:\n        return 'pass'\n    return 'fail'\n\n\nprint(grade(3))\n";
        if (await Changes("sample.py", code, 2, 1, "    if score >= 50:") is not { } changes) return;

        Assert.Equal(["When `score` is 50: before, `grade` returned \"fail\"; now it returns \"pass\"", SameOtherwise], changes);
    }

    [Fact]
    public async Task WhatIsTypedIsTheSameInputInBothVersions()
    {
        if (await Changes("sample.py", "count = int(input())\nprint(100 / count)\n", 2, 1, "print(100 / count if count != 0 else 0)") is not { } changes) return;

        Assert.Equal(["When the number typed at line 1 is 0: before, the program stopped with ZeroDivisionError on line 2; now it prints 0", SameOtherwise], changes);
    }

    [Fact]
    public async Task ARewriteThatChangesNothingIsSaidToChangeNothing()
    {
        if (await Changes("sample.py", "def double(n: int):\n    return n + n\n", 2, 1, "    return 2 * n") is not { } changes) return;

        Assert.Equal(["`double` behaves exactly as before for every input - only the code changes."], changes);
    }

    [Fact]
    public async Task TheSameDivisionInBothVersionsIsTheSameValue()
    {
        const string code = "class Program\n{\n    static int Share(int total, int people)\n    {\n        return total / people;\n    }\n\n" +
                            "    static void Main() => System.Console.WriteLine(Share(10, 2));\n}\n";
        var changes = await Changes("Program.cs", code, 5, 1, "        return people == 0 ? 0 : total / people;");

        Assert.Equal(["When `people` is 0: before, `Program.Share` stopped with a DivideByZeroException on line 5; now it returns 0", SameOtherwise], changes);
    }

    [Fact]
    public async Task JavaFixesAreComparedToo()
    {
        const string code = "public class Main {\n    static int share(int total, int people) {\n        return total / people;\n    }\n\n" +
                            "    public static void main(String[] args) {\n        System.out.println(share(10, 2));\n    }\n}\n";
        if (await Changes("Main.java", code, 3, 1, "        if (people == 0) return 0;", "        return total / people;") is not { } changes) return;

        Assert.Equal(["When `people` is 0: before, `Main.share` stopped with an ArithmeticException on line 3; now it returns 0", SameOtherwise], changes);
    }

    [Fact]
    public async Task AValueThatCannotBeFollowedIsNeverCalledTheSame()
    {
        // Indexing a parameter that may be a dictionary is not followed, so the change cannot be shown - but it must not be called "the same".
        if (await Changes("sample.py", "def last(items):\n    return items[len(items)]\n", 2, 1, "    return items[len(items) - 1]") is not { } changes) return;

        Assert.Equal(["FixFinder found no input for which `last` behaves differently, but could not compare every input."], changes);
    }

    [Fact]
    public async Task ANoneGuardChangesTheNoneCase()
    {
        if (await Changes("sample.py", "def shout(text=None):\n    return text.upper()\n", 2, 0, "    if text is None:", "        return ''") is not { } changes) return;

        Assert.Equal("When `text` is None: before, `shout` stopped with an error from using None on line 2; now it returns \"\"", changes[0]);
        Assert.DoesNotContain(SameOtherwise, changes);
    }

    [Fact]
    public async Task AFixThatBreaksTheFileIsNotCompared()
    {
        if (PythonFrontend.FindInterpreter() is null) return;

        Assert.Null(await Changes("sample.py", "def f(x):\n    return x\n", 2, 1, "    return (x"));
    }

    [Fact]
    public void NothingIsSaidOfAFunctionNoPathOfWhichCouldBeCompared() =>
        Assert.Empty(SemanticDiff.Describe([new FunctionDiff("f", [], Complete: false, Followed: false)]));
}
