using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Checking;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Loops reasoned about with relations between variables and the solver: a counter tied to its limit, so a[i + 1] is
/// found past the end on the last time round whatever the list's length; and a while loop's inductive invariants, inside
/// which a state one time round leaves unchanged is a loop that never ends. Each is checked beside its repair.
/// </summary>
public class LoopReasoningTests(ITestOutputHelper output) : IDisposable
{
    private static readonly string[] LoopChecks = ["analysis-index-out-of-range", LoopReasoning.StuckRule];

    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<IReadOnlyList<AnalysisFinding>?> Python(string code)
    {
        if (PythonFrontend.FindInterpreter() is not { } python) return null;

        var path = Path.Combine(_temp.Path, "loops.py");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        return Loops(AbstractChecks.Run(await PythonFrontend.ReadAsync([path], python), new SourceText()));
    }

    private async Task<IReadOnlyList<AnalysisFinding>?> Java(string code)
    {
        if (JavaFrontend.FindTools() is not { } tools) return null;

        var path = Path.Combine(_temp.Path, code.Split("public class ")[1].Split(' ', '{')[0] + ".java");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        return Loops(AbstractChecks.Run(await JavaFrontend.ReadAsync([path], tools.Javac, tools.Java), new SourceText()));
    }

    private async Task<IReadOnlyList<AnalysisFinding>> CSharp(string code)
    {
        var path = Path.Combine(_temp.Path, "Program.cs");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        return Loops(AbstractChecks.Run(await CSharpFrontend.ReadAsync([path]), new SourceText()));
    }

    private List<AnalysisFinding> Loops(IEnumerable<AnalysisFinding> findings)
    {
        var found = findings.Where(f => LoopChecks.Contains(f.CheckId)).ToList();
        foreach (var finding in found) output.WriteLine($"line {finding.Span.Line} {finding.CheckId}: {finding.Message}");
        return found;
    }

    private static string Summary(IEnumerable<AnalysisFinding> findings) => string.Join("; ", findings.Select(f => $"line {f.Span.Line}: {f.Message}"));

    private static void Says(string expected, AnalysisFinding finding) =>
        Assert.True(finding.Message.Contains(expected, StringComparison.Ordinal), $"expected \"{expected}\" in: {finding.Message}");

    private const string Pairs = """
        def pairs(a):
            total = 0
            for i in range(len(a)):
                total += a[i + 1]
            return total
        """;

    /// <summary>Nothing is known about a's length - and nothing needs to be: i + 1 reaches len(a) on the last time round, whatever it is.</summary>
    [Fact]
    public async Task ReadingOnePastTheCounterGoesPastTheEndOnTheLastTimeRound()
    {
        if (await Python(Pairs) is not { } findings) return;

        var found = Assert.Single(findings);
        Assert.Equal(4, found.Span.Line);
        Says("On the last time round the loop, `i` is `len(a) - 1`, so `a[i + 1]` asks for position `len(a)` - past the end of `a` - and fails with IndexError", found);
    }

    [Theory]
    [InlineData("for i in range(len(a)):", "for i in range(len(a) - 1):")]
    [InlineData("        total += a[i + 1]", "        if i + 1 < len(a):\n            total += a[i + 1]")]
    public async Task StoppingOneSoonerOrCheckingFirstIsFine(string original, string repaired)
    {
        var fixedCode = Pairs.ReplaceLineEndings("\n").Replace(original, repaired, StringComparison.Ordinal);
        Assert.NotEqual(Pairs.ReplaceLineEndings("\n"), fixedCode);

        if (await Python(fixedCode) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    [Fact]
    public async Task ALoopStartingAtOneReadingTheOneBeforeIsFine()
    {
        const string code = """
            def steps(a):
                for i in range(1, len(a)):
                    print(a[i] - a[i - 1])
            """;
        if (await Python(code) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    [Fact]
    public async Task AWhileLoopCountingThroughTheListGoesPastTheEndToo()
    {
        const string code = """
            def diffs(a):
                i = 0
                out = []
                while i < len(a):
                    out.append(a[i + 1] - a[i])
                    i += 1
                return out
            """;
        if (await Python(code) is not { } findings) return;

        Assert.Equal(5, Assert.Single(findings).Span.Line);
    }

    /// <summary>A loop that can stop early might never reach its last time round, so nothing is claimed about it.</summary>
    [Fact]
    public async Task ALoopThatCanStopEarlyIsLeftAlone()
    {
        const string code = """
            def first_rise(a):
                for i in range(len(a)):
                    if a[i] > 10:
                        break
                    print(a[i + 1])
            """;
        if (await Python(code) is not { } findings) return;

        Assert.DoesNotContain(findings, f => f.CheckId == "analysis-index-out-of-range" && f.Span.Line == 5);
    }

    private const string Search = """
        def find(a, x):
            lo = 0
            hi = len(a)
            while lo < hi:
                mid = (lo + hi) // 2
                if a[mid] < x:
                    lo = mid
                else:
                    hi = mid
            return lo
        """;

    /// <summary>
    /// When hi is lo + 1, mid is lo, and lo = mid changes nothing: the loop comes back in the same state for ever. The
    /// state is inside the invariants the solver proved inductive, so it is one the loop can reach.
    /// </summary>
    [Fact]
    public async Task ABinarySearchThatSetsLoToMidCanGetStuck()
    {
        if (await Python(Search) is not { } findings) return;

        var found = Assert.Single(findings);
        Assert.Equal(LoopReasoning.StuckRule, found.CheckId);
        Assert.Equal(4, found.Span.Line);
        Says("When `lo` is 0 and `hi` is 1", found);
        Says("`a[mid] < x` being true and `lo = mid`", found);
        Says("leaves `lo` and `hi` exactly as they were, so the loop comes back to the same state and never ends", found);
        Says("it keeps `lo <= hi`, `lo >= 0` and `hi <= len(a)`", found);
    }

    [Fact]
    public async Task ABinarySearchThatMovesPastMidAlwaysEnds()
    {
        if (await Python(Search.Replace("lo = mid\n", "lo = mid + 1\n", StringComparison.Ordinal).ReplaceLineEndings("\n")
                .Replace("            lo = mid\n", "            lo = mid + 1\n", StringComparison.Ordinal)) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    [Fact]
    public async Task InJavaLessThanOrEqualToTheLengthReadsPastTheEnd()
    {
        const string code = """
            public class Sum {
                static int sum(int[] marks) {
                    int total = 0;
                    for (int i = 0; i <= marks.length; i++) {
                        total += marks[i];
                    }
                    return total;
                }
            }
            """;
        if (await Java(code) is not { } findings) return;

        var found = Assert.Single(findings);
        Assert.Equal(5, found.Span.Line);
        Says("`i` is `marks.length`", found);
        Says("ArrayIndexOutOfBoundsException", found);
    }

    /// <summary>Counting down from the length reads a[a.length] on the very first time round.</summary>
    [Fact]
    public async Task InJavaCountingDownFromTheLengthStartsPastTheEnd()
    {
        const string code = """
            public class Backwards {
                static void print(int[] marks) {
                    for (int i = marks.length; i >= 0; i--) {
                        System.out.println(marks[i]);
                    }
                }
            }
            """;
        if (await Java(code) is not { } findings) return;

        var found = Assert.Single(findings);
        Assert.Equal(4, found.Span.Line);
        Says("On the first time round the loop, `i` is `marks.length`", found);
    }

    /// <summary>A negative index is an error in Java, where Python would count back from the end instead.</summary>
    [Fact]
    public async Task InJavaReadingTheOneBeforeTheFirstGoesBeforeTheStart()
    {
        const string code = """
            public class Steps {
                static void print(int[] marks) {
                    for (int i = 0; i < marks.length; i++) {
                        System.out.println(marks[i] - marks[i - 1]);
                    }
                }
            }
            """;
        if (await Java(code) is not { } findings) return;

        var found = Assert.Single(findings);
        Says("On the first time round the loop, `i` is `0`, so `marks[i - 1]` asks for position `-1` - before the start of `marks`", found);
    }

    private const string JavaSearch = """
        public class Search {
            static int find(int[] a, int x) {
                int lo = 0;
                int hi = a.length;
                while (lo < hi) {
                    int mid = (lo + hi) / 2;
                    if (a[mid] < x) {
                        lo = mid;
                    } else {
                        hi = mid;
                    }
                }
                return lo;
            }
        }
        """;

    /// <summary>int mid = ... is a declaration each time round, and its value - whole-number division - is what makes the state repeat.</summary>
    [Fact]
    public async Task InJavaAStuckBinarySearchIsFoundWithWholeNumberDivision()
    {
        if (await Java(JavaSearch) is not { } findings) return;

        var found = Assert.Single(findings, f => f.CheckId == LoopReasoning.StuckRule);
        Assert.Equal(5, found.Span.Line);
        Says("`mid = (lo + hi) / 2`", found);
        Says("`lo = mid`", found);
    }

    /// <summary>The declared mid is followed, not left free - so the correct search, moving past mid, is not claimed to get stuck.</summary>
    [Fact]
    public async Task InJavaABinarySearchThatMovesPastMidAlwaysEnds()
    {
        if (await Java(JavaSearch.Replace("lo = mid;", "lo = mid + 1;", StringComparison.Ordinal)) is not { } findings) return;

        Assert.DoesNotContain(findings, f => f.CheckId == LoopReasoning.StuckRule);
    }

    [Fact]
    public async Task InCSharpLessThanOrEqualToTheLengthReadsPastTheEnd()
    {
        const string code = """
            class Program
            {
                static int Sum(int[] numbers)
                {
                    var total = 0;
                    for (var i = 0; i <= numbers.Length; i++)
                    {
                        total += numbers[i];
                    }
                    return total;
                }
            }
            """;

        var found = Assert.Single(await CSharp(code));
        Assert.Equal(8, found.Span.Line);
        Says("IndexOutOfRangeException", found);
    }
}
