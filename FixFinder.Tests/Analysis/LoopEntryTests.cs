using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Checking;

namespace FixFinder.Tests;

/// <summary>
/// A loop whose test holds the first time it is made - i = 0 against i &lt; 4, range(4) - goes round at least once, so a
/// list it adds to is not empty after it and dividing by its size is safe. A loop that may not go round at all - over a
/// collection that may be empty, up to a bound that may be 0, left by a break before the add - still leaves the list
/// possibly empty, and the division is still reported.
/// </summary>
/// <remarks>
/// Each program that must go round ends with a loop of 400 passes that uses the list, as the programs that showed this
/// did. The search that follows each path cannot finish that loop, so it cannot drop the false alarm by itself: what these
/// tests hold is that the values followed through the code know the list is not empty.
/// </remarks>
public class LoopEntryTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<string> WriteAsync(string name, string code)
    {
        var path = Path.Combine(_temp.Path, name);
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        return path;
    }

    private static bool DividesByZero(IEnumerable<AnalysisFinding> findings) => findings.Any(f => f.CheckId == "analysis-division-by-zero");

    private static string Shown(IEnumerable<AnalysisFinding> findings) => string.Join("; ", findings.Select(f => $"{f.CheckId} line {f.Span.Line}: {f.Message}"));

    private async Task<IReadOnlyList<AnalysisFinding>?> PythonAsync(string code)
    {
        if (PythonFrontend.FindInterpreter() is not { } python) return null;
        return AbstractChecks.Run(await PythonFrontend.ReadAsync([await WriteAsync("fill.py", code)], python), new SourceText());
    }

    private async Task<IReadOnlyList<AnalysisFinding>?> JavaAsync(string body)
    {
        if (JavaFrontend.FindTools() is not { } tools) return null;

        var code = $"import java.util.*;\n\npublic class Fill {{\n{body}\n}}\n";
        return AbstractChecks.Run(await JavaFrontend.ReadAsync([await WriteAsync("Fill.java", code)], tools.Javac, tools.Java), new SourceText());
    }

    private async Task<IReadOnlyList<AnalysisFinding>> CSharpAsync(string body)
    {
        var code = $"using System;\nusing System.Collections.Generic;\n\npublic static class Fill\n{{\n{body}\n}}\n";
        return AbstractChecks.Run(await CSharpFrontend.ReadAsync([await WriteAsync("Fill.cs", code)]), new SourceText());
    }

    private const string PythonLongLoop = "for number in range(400):\n    print(number % len(values))\n";

    [Theory]
    [InlineData("a range of four", "values = []\nfor i in range(4):\n    values.append(i)\nprint(100 / len(values))\n")]
    [InlineData("a list written out", "values = []\nfor mark in [70, 80]:\n    values.append(mark)\nprint(100 / len(values))\n")]
    [InlineData("a counter against a fixed bound", "values = []\ncount = 0\nwhile count < 3:\n    values.append(count)\n    count += 1\nprint(100 / len(values))\n")]
    public async Task APythonLoopThatMustGoRoundLeavesItsListFilled(string shape, string code)
    {
        if (await PythonAsync(code + PythonLongLoop) is not { } found) return;

        Assert.False(DividesByZero(found), $"{shape}: {Shown(found)}");
    }

    [Theory]
    [InlineData("a parameter that may be empty", "def average(items):\n    values = []\n    for item in items:\n        values.append(item)\n    return 100 / len(values)\n")]
    [InlineData("a bound that may be 0", "def fill(n):\n    values = []\n    for i in range(n):\n        values.append(i)\n    return 100 / len(values)\n")]
    [InlineData("a range that is empty", "values = []\nfor i in range(0):\n    values.append(i)\nprint(100 / len(values))\n")]
    [InlineData("a break before the add", "def fill(stop):\n    values = []\n    for i in range(4):\n        if stop:\n            break\n        values.append(i)\n    return 100 / len(values)\n")]
    public async Task APythonLoopThatMayNotGoRoundStillLeavesItsListPossiblyEmpty(string shape, string code)
    {
        if (await PythonAsync(code) is not { } found) return;

        Assert.True(DividesByZero(found), $"{shape}: {Shown(found)}");
    }

    [Fact]
    public async Task AJavaCountedLoopLeavesItsListFilledAndAnUnknownBoundDoesNot()
    {
        const string counted = """
                static int share() {
                    List<Integer> values = new ArrayList<>();
                    for (int i = 0; i < 4; i++) {
                        values.add(i * 10);
                    }
                    int total = 0;
                    for (int number = 0; number < 400; number++) {
                        total += number % values.size();
                    }
                    return total + 100 / values.size();
                }
            """;

        if (await JavaAsync(counted) is not { } found) return;
        Assert.False(DividesByZero(found), Shown(found));

        var unknown = counted.Replace("i < 4", "i < limit").Replace("share()", "share(int limit)");
        Assert.True(DividesByZero((await JavaAsync(unknown))!), "an unknown bound must still leave the list possibly empty");
    }

    [Fact]
    public async Task ACSharpCountedLoopLeavesItsListFilledAndAnUnknownBoundDoesNot()
    {
        const string counted = """
                public static int Share()
                {
                    var values = new List<int>();
                    for (var i = 0; i < 4; i++)
                    {
                        values.Add(i * 10);
                    }
                    var total = 0;
                    for (var number = 0; number < 400; number++)
                    {
                        total += number % values.Count;
                    }
                    return total + 100 / values.Count;
                }
            """;

        Assert.False(DividesByZero(await CSharpAsync(counted)), Shown(await CSharpAsync(counted)));
        Assert.True(DividesByZero(await CSharpAsync(counted.Replace("i < 4", "i < limit").Replace("Share()", "Share(int limit)"))));
    }
}
