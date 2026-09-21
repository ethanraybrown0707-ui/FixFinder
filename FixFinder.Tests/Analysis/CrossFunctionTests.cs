using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Checking;

namespace FixFinder.Tests;

/// <summary>
/// Contracts, cross-function consistency, type-driven checks and temporal properties: what one function says it needs,
/// returns or does, held against the calls and the order of events in the rest of the program.
/// </summary>
public class CrossFunctionTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<IReadOnlyList<AnalysisFinding>?> Python(string code)
    {
        if (PythonFrontend.FindInterpreter() is not { } python) return null;

        var path = Path.Combine(_temp.Path, "sample.py");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        return AbstractChecks.Run(await PythonFrontend.ReadAsync([path], python), new SourceText());
    }

    private async Task<IReadOnlyList<AnalysisFinding>> CSharp(string code)
    {
        var path = Path.Combine(_temp.Path, "Sample.cs");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        return AbstractChecks.Run(await CSharpFrontend.ReadAsync([path]), new SourceText());
    }

    private async Task<IReadOnlyList<AnalysisFinding>?> Java(string code)
    {
        if (JavaFrontend.FindTools() is not { } tools) return null;

        var path = Path.Combine(_temp.Path, "App.java");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        return AbstractChecks.Run(await JavaFrontend.ReadAsync([path], tools.Javac, tools.Java), new SourceText());
    }

    private static string Summary(IEnumerable<AnalysisFinding> findings) =>
        string.Join("; ", findings.Select(f => $"{f.CheckId} line {f.Span.Line}: {f.Message}"));

    [Theory]
    [InlineData("def root(x):\n    if x < 0:\n        raise ValueError('negative')\n    return x ** 0.5\n\nprint(root(-4))\n", 6, "when `x < 0` it raises ValueError")]
    [InlineData("def double(n):\n    if not isinstance(n, int):\n        raise TypeError('whole numbers only')\n    return n * 2\n\ndouble('3')\n", 6, "it raises TypeError")]
    [InlineData("def share(total, people):\n    assert people > 0\n    return total / people\n\nshare(10, 0)\n", 5, "AssertionError")]
    public async Task ACallThatBreaksWhatAFunctionChecksIsFound(string code, int line, string said)
    {
        if (await Python(code) is not { } findings) return;

        var broken = Assert.Single(findings, f => f.CheckId == "analysis-contract-broken");
        Assert.Equal(line, broken.Span.Line);
        Assert.Contains(said, broken.Message);
    }

    [Fact]
    public async Task ACallThatKeepsTheContractIsLeftAlone()
    {
        if (await Python("def root(x):\n    if x < 0:\n        raise ValueError('negative')\n    return x ** 0.5\n\nprint(root(4))\nprint(root(int(input())))\n") is not { } findings) return;

        Assert.DoesNotContain(findings, f => f.CheckId == "analysis-contract-broken");
    }

    [Fact]
    public async Task AJavaGuardThatThrowsIsAContractToo()
    {
        if (await Java("public class App {\n    static int half(int n) {\n        if (n < 0) throw new IllegalArgumentException(\"n\");\n        return n / 2;\n    }\n    static int run() { return half(-6); }\n}\n") is not { } findings) return;

        var broken = Assert.Single(findings, f => f.CheckId == "analysis-contract-broken");
        Assert.Contains("IllegalArgumentException", broken.Message);
    }

    [Theory]
    [InlineData("def area(width, height):\n    return width * height\n\nprint(area(3))\n", "leaves out `height`")]
    [InlineData("def area(width, height):\n    return width * height\n\nprint(area(1, 2, 3))\n", "gives 3 arguments, but `area` takes 2")]
    [InlineData("def area(width, height):\n    return width * height\n\nprint(area(3, h=4))\n", "names `h`, which `area` does not have")]
    [InlineData("def area(width, height=1):\n    return width * height\n\nprint(area(3, width=4))\n", "gives `width` twice")]
    [InlineData("class Point:\n    def __init__(self, x, y):\n        self.x = x\n        self.y = y\n\np = Point(1)\n", "leaves out `y`, which `Point` needs")]
    public async Task ACallWithTheWrongArgumentsIsCaughtBeforeItRuns(string code, string said)
    {
        if (await Python(code) is not { } findings) return;

        var wrong = Assert.Single(findings, f => f.CheckId == "analysis-wrong-arguments");
        Assert.Contains(said, wrong.Message);
        Assert.Equal(Confidence.Certain, wrong.Confidence);
    }

    [Theory]
    [InlineData("def area(width, height=1, *more, scale, **options):\n    return width * height\n\nprint(area(3, 4, 5, 6, scale=2, colour='red'))\n")]
    [InlineData("class Shape:\n    def area(self, width, height):\n        return width * height\n    def square(self, side):\n        return self.area(side, side)\n")]
    [InlineData("def area(width, height):\n    return width * height\n\nsizes = (3, 4)\nprint(area(*sizes))\n")]
    public async Task CallsThatFitAreLeftAlone(string code)
    {
        if (await Python(code) is not { } findings) return;

        Assert.True(findings.All(f => f.CheckId != "analysis-wrong-arguments"), Summary(findings));
    }

    [Fact]
    public async Task TheNoneAFunctionAlwaysReturnsIsFollowedIntoItsCaller()
    {
        const string code = "def describe(name):\n    print('Hello, ' + name)\n\nmessage = describe('Sam')\nprint(message.upper())\n";
        if (await Python(code) is not { } findings) return;

        var nullUse = Assert.Single(findings, f => f.CheckId == "analysis-null-used");
        Assert.Equal((5, Confidence.Certain), (nullUse.Span.Line, nullUse.Confidence));
    }

    [Fact]
    public async Task ANoneAFunctionOnlySometimesReturnsIsNotBlamedOnItsCallers()
    {
        const string code = "def find(names, wanted):\n    for name in names:\n        if name == wanted:\n            return name\n    return None\n\nfound = find(['a'], 'a')\nprint(found.upper())\n";
        if (await Python(code) is not { } findings) return;

        Assert.DoesNotContain(findings, f => f.CheckId == "analysis-null-used");
    }

    [Fact]
    public async Task WhatAMethodReturnsIsNotAssumedBecauseASubclassCanReplaceIt()
    {
        const string code = "class Reader:\n    def ready(self):\n        return False\n    def read(self):\n        if self.ready():\n            return 'data'\n        return ''\n";
        if (await Python(code) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    [Theory]
    [InlineData("def label(score: int) -> str:\n    if score > 50:\n        return 'pass'\n    return score\n", 4, "says it returns `str`, but here it returns a number")]
    [InlineData("def label(score: int) -> str:\n    return 'pass' if score > 50 else 'fail'\n\nlabel('80')\n", 4, "passes text for `score`")]
    [InlineData("def show(message: str) -> None:\n    print(message)\n    return len(message)\n", 3, "says it returns `None`, but here it returns a number")]
    public async Task AValueThatCanNeverMatchItsHintIsFound(string code, int line, string said)
    {
        if (await Python(code) is not { } findings) return;

        var broken = Assert.Single(findings, f => f.CheckId == "analysis-type-hint-broken");
        Assert.Equal(line, broken.Span.Line);
        Assert.Contains(said, broken.Message);
    }

    [Fact]
    public async Task HintsThatAllowTheValueAreLeftAlone()
    {
        const string code = "from typing import Optional\n\ndef average(values: list) -> float:\n    if not values:\n        return 0\n    return sum(values) / len(values)\n\ndef first(values: list) -> Optional[str]:\n    return None\n";
        if (await Python(code) is not { } findings) return;

        Assert.DoesNotContain(findings, f => f.CheckId == "analysis-type-hint-broken");
    }

    [Fact]
    public async Task AFileUsedAfterItsWithBlockIsCaught()
    {
        const string code = "def first_line(path):\n    with open(path) as handle:\n        pass\n    return handle.readline()\n";
        if (await Python(code) is not { } findings) return;

        var closed = Assert.Single(findings, f => f.CheckId == "analysis-used-after-close");
        Assert.Equal(4, closed.Span.Line);
        Assert.Contains("closed on line 2", closed.Message);
    }

    [Theory]
    [InlineData("def first_line(path):\n    with open(path) as handle:\n        return handle.readline()\n")]
    [InlineData("def lines(path, again):\n    handle = open(path)\n    if again:\n        handle.close()\n    return handle.readlines()\n")]
    [InlineData("def lines(path):\n    handle = open(path)\n    handle.close()\n    handle = open(path)\n    return handle.readlines()\n")]
    public async Task AFileStillOpenOnSomeWayIsLeftAlone(string code)
    {
        if (await Python(code) is not { } findings) return;

        Assert.DoesNotContain(findings, f => f.CheckId == "analysis-used-after-close");
    }

    [Fact]
    public async Task AJavaLockHeldAtAReturnIsReported()
    {
        const string code = "import java.util.concurrent.locks.*;\npublic class App {\n    private final Lock lock = new ReentrantLock();\n    private int count;\n    void add() {\n        lock.lock();\n        count++;\n        if (count > 10) return;\n        lock.unlock();\n    }\n}\n";
        if (await Java(code) is not { } findings) return;

        var held = Assert.Single(findings, f => f.CheckId == "analysis-lock-not-released");
        Assert.Equal(6, held.Span.Line);
        Assert.Contains("returns on line 8", held.Message);
    }

    [Fact]
    public async Task ALockReleasedInFinallyInsideAnOuterTryIsLeftAlone()
    {
        const string code = "import java.util.concurrent.locks.*;\npublic class App {\n    private final Lock lock = new ReentrantLock();\n    private boolean failed;\n    void write(int b) {\n        try {\n            if (lock != null) {\n                lock.lock();\n                try {\n                    work(b);\n                } finally {\n                    lock.unlock();\n                }\n            }\n        } catch (RuntimeException x) {\n            failed = true;\n        }\n    }\n    void work(int b) { }\n}\n";
        if (await Java(code) is not { } findings) return;

        Assert.DoesNotContain(findings, f => f.CheckId == "analysis-lock-not-released");
    }

    [Fact]
    public async Task AJavaLockReleasedInFinallyIsLeftAlone()
    {
        const string code = "import java.util.concurrent.locks.*;\npublic class App {\n    private final Lock lock = new ReentrantLock();\n    private int count;\n    void add() {\n        lock.lock();\n        try {\n            count++;\n            if (count > 10) return;\n            System.out.println(count);\n        } finally {\n            lock.unlock();\n        }\n    }\n}\n";
        if (await Java(code) is not { } findings) return;

        Assert.DoesNotContain(findings, f => f.CheckId == "analysis-lock-not-released");
    }

    [Theory]
    [InlineData("class App\n{\n    static bool F(byte b)\n    {\n        if (b < 0) return true;\n        return false;\n    }\n}\n", "analysis-never-true")]
    [InlineData("class App\n{\n    static int F(uint u)\n    {\n        if (u >= 0) return 1;\n        return 0;\n    }\n}\n", "analysis-always-true")]
    public async Task ATypesOwnRangeSettlesAComparison(string code, string check)
    {
        var findings = await CSharp(code);

        Assert.Single(findings, f => f.CheckId == check);
    }
}
