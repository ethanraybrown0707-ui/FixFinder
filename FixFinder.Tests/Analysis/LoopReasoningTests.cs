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

    /// <summary>
    /// TheAlgorithms' C# FibonacciSearcher (MIT licence) climbs the Fibonacci numbers until one passes the length. Each of
    /// its numbers stays at least what it started as only because another does - fibonacciNum >= 1 keeps
    /// fibonacciNumPrevious >= 1, and the other way about - and fibonacciNum starts as what fibonacciNumPrevious was given.
    /// Proven one at a time, and with fibonacciNum's start unknown, nothing ruled out all three being 0, and the loop was
    /// said to be able to get stuck.
    /// </summary>
    [Fact]
    public async Task InCSharpFibonacciNumbersClimbingPastTheLengthAlwaysEnd()
    {
        const string code = """
            class Program
            {
                static int Beyond(int arrayLength)
                {
                    var fibonacciNumberBeyondPrevious = 0;
                    var fibonacciNumPrevious = 1;
                    var fibonacciNum = fibonacciNumPrevious;

                    while (fibonacciNum <= arrayLength)
                    {
                        fibonacciNumberBeyondPrevious = fibonacciNumPrevious;
                        fibonacciNumPrevious = fibonacciNum;
                        fibonacciNum = fibonacciNumberBeyondPrevious + fibonacciNumPrevious;
                    }
                    return fibonacciNum;
                }
            }
            """;

        var findings = await CSharp(code);
        Assert.True(findings.Count == 0, Summary(findings));
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

    // -------------------------------------------------------------------------------------------------------------
    // Reads that wait on a test: c ? a[i - 1] : 0, a[i + 1] if c else 0, c && a[i - 1] > 0. Only the branch the test
    // chooses is evaluated, so the read counts only where the test lets it through.

    /// <summary>
    /// A 0/1 knapsack table with a row 0 for no items: items[i - 1] is read only when i > 0, so i being 0 on the first time
    /// round never reaches it. It prints 90, and was said to ask for position -1 on lines 5 and 6.
    /// </summary>
    private const string Knapsack = """
        public class Knapsack {
            static int knapsack(int capacity, int[][] items) {
                int[][] table = new int[items.length + 1][capacity + 1];
                for (int i = 0; i <= items.length; i++) {
                    int weight = i > 0 ? items[i - 1][0] : 0;
                    int value = i > 0 ? items[i - 1][1] : 0;
                    for (int j = 0; j <= capacity; j++) {
                        if (i == 0 || j == 0) {
                            table[i][j] = 0;
                        } else if (weight <= j) {
                            table[i][j] = Math.max(table[i - 1][j], value + table[i - 1][j - weight]);
                        } else {
                            table[i][j] = table[i - 1][j];
                        }
                    }
                }
                return table[items.length][capacity];
            }

            public static void main(String[] args) {
                System.out.println(knapsack(7, new int[][] {{5, 10}, {4, 40}, {6, 30}, {3, 50}}));
            }
        }
        """;

    [Theory]
    [InlineData("i > 0 ? items[i - 1][0] : 0", "i > 0 ? items[i - 1][1] : 0")]
    [InlineData("i == 0 ? 0 : items[i - 1][0]", "i == 0 ? 0 : items[i - 1][1]")]
    public async Task InJavaAReadTheTestKeepsFromTheFirstTimeRoundIsLeftAlone(string weight, string value)
    {
        var code = Knapsack.Replace("i > 0 ? items[i - 1][0] : 0", weight, StringComparison.Ordinal).Replace("i > 0 ? items[i - 1][1] : 0", value, StringComparison.Ordinal);
        if (await Java(code) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    /// <summary>The same table read without the test fails on the first time round - ArrayIndexOutOfBoundsException: Index -1 out of bounds for length 4.</summary>
    [Fact]
    public async Task InJavaTheSameReadWithoutTheTestStillGoesBeforeTheStart()
    {
        var code = Knapsack.Replace("i > 0 ? items[i - 1][0] : 0", "items[i - 1][0]", StringComparison.Ordinal).Replace("i > 0 ? items[i - 1][1] : 0", "items[i - 1][1]", StringComparison.Ordinal);
        if (await Java(code) is not { } findings) return;

        Assert.Equal(new[] { 5, 6 }, findings.Where(f => f.CheckId == "analysis-index-out-of-range").Select(f => f.Span.Line).Order());
        foreach (var found in findings)
            Says("On the first time round the loop, `i` is `0`, so `items[i - 1]` asks for position `-1` - before the start of `items` - and fails with an ArrayIndexOutOfBoundsException", found);
    }

    /// <summary>Counts the marks higher than the one before: 3 for 3, 8, 7, 17, 18.</summary>
    private const string Rises = """
        public class Rises {
            static int rises(int[] marks) {
                int count = 0;
                for (int i = 0; i < marks.length; i++) {
                    boolean counted = i > 0 && marks[i] > marks[i - 1];
                    count += counted ? 1 : 0;
                }
                return count;
            }

            public static void main(String[] args) {
                System.out.println(rises(new int[] {3, 8, 7, 17, 18}));
            }
        }
        """;

    /// <summary>The right of && is evaluated only when the left is true, and the right of || only when the left is false.</summary>
    [Theory]
    [InlineData("i > 0 && marks[i] > marks[i - 1]")]
    [InlineData("i == 0 || marks[i] < marks[i - 1]")]
    [InlineData("i + 1 < marks.length && marks[i + 1] > marks[i]")]
    public async Task InJavaAReadOnTheRightOfAndOrOrWaitsOnTheLeft(string test)
    {
        if (await Java(Rises.Replace("i > 0 && marks[i] > marks[i - 1]", test, StringComparison.Ordinal)) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    /// <summary>The left of && is read every time round - ArrayIndexOutOfBoundsException: Index -1 out of bounds for length 5.</summary>
    [Fact]
    public async Task InJavaAReadOnTheLeftOfAndIsStillReadEveryTimeRound()
    {
        if (await Java(Rises.Replace("i > 0 && marks[i] > marks[i - 1]", "marks[i] > marks[i - 1] && i > 0", StringComparison.Ordinal)) is not { } findings) return;

        var found = Assert.Single(findings);
        Assert.Equal(5, found.Span.Line);
        Says("On the first time round the loop, `i` is `0`, so `marks[i - 1]` asks for position `-1` - before the start of `marks`", found);
    }

    /// <summary>
    /// The test keeps the first time round out, but not the next: when i is 1, marks[i - 2] is marks[-1] -
    /// ArrayIndexOutOfBoundsException: Index -1 out of bounds for length 5. The failure is put on that time round, not
    /// on i being 0, where nothing is read.
    /// </summary>
    [Fact]
    public async Task InJavaATestThatStillLetsABadIndexThroughIsFoundWhereItLetsItThrough()
    {
        const string code = """
            public class StepTwoBack {
                static int total(int[] marks) {
                    int sum = 0;
                    for (int i = 0; i < marks.length; i++) {
                        int other = i > 0 ? marks[i - 2] : 0;
                        sum += other;
                    }
                    return sum;
                }

                public static void main(String[] args) {
                    System.out.println(total(new int[] {3, 8, 7, 17, 18}));
                }
            }
            """;
        if (await Java(code) is not { } findings) return;

        var found = Assert.Single(findings);
        Assert.Equal(5, found.Span.Line);
        Says("Even with `i > 0` being true, `marks[i - 2]` is read when `i` is `1`, where it asks for position `-1` - before the start of `marks`", found);
    }

    /// <summary>The C# table prints 90; without the test it stops with an IndexOutOfRangeException on line 11.</summary>
    private const string CSharpKnapsack = """
        using System;

        public static class Program
        {
            static int Knapsack(int capacity, int[][] items)
            {
                var table = new int[items.Length + 1][];
                for (int i = 0; i <= items.Length; i++)
                {
                    table[i] = new int[capacity + 1];
                    int weight = i > 0 ? items[i - 1][0] : 0;
                    int value = i > 0 ? items[i - 1][1] : 0;
                    for (int j = 0; j <= capacity; j++)
                    {
                        if (i == 0 || j == 0)
                        {
                            table[i][j] = 0;
                        }
                        else if (weight <= j)
                        {
                            table[i][j] = Math.Max(table[i - 1][j], value + table[i - 1][j - weight]);
                        }
                        else
                        {
                            table[i][j] = table[i - 1][j];
                        }
                    }
                }
                return table[items.Length][capacity];
            }

            public static void Main()
            {
                Console.WriteLine(Knapsack(7, new[] { new[] { 5, 10 }, new[] { 4, 40 }, new[] { 6, 30 }, new[] { 3, 50 } }));
            }
        }
        """;

    [Fact]
    public async Task InCSharpAReadTheTestKeepsFromTheFirstTimeRoundIsLeftAlone()
    {
        var findings = await CSharp(CSharpKnapsack);
        Assert.True(findings.Count == 0, Summary(findings));
    }

    [Fact]
    public async Task InCSharpTheSameReadWithoutTheTestStillGoesBeforeTheStart()
    {
        var code = CSharpKnapsack.Replace("i > 0 ? items[i - 1][0] : 0", "items[i - 1][0]", StringComparison.Ordinal).Replace("i > 0 ? items[i - 1][1] : 0", "items[i - 1][1]", StringComparison.Ordinal);
        var findings = await CSharp(code);

        Assert.Equal(new[] { 11, 12 }, findings.Select(f => f.Span.Line).Order());
        foreach (var found in findings)
            Says("On the first time round the loop, `i` is `0`, so `items[i - 1]` asks for position `-1` - before the start of `items` - and fails with an IndexOutOfRangeException", found);
    }

    [Theory]
    [InlineData("a[i + 1] if i + 1 < len(a) else a[i]")]
    [InlineData("a[i] if i == len(a) - 1 else a[i + 1]")]
    [InlineData("i + 1 < len(a) and a[i + 1]")]
    [InlineData("not (i + 1 >= len(a)) and a[i + 1]")]
    public async Task InPythonAReadTheTestKeepsFromTheLastTimeRoundIsLeftAlone(string read)
    {
        if (await Python(Pairs.Replace("a[i + 1]", read, StringComparison.Ordinal)) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    /// <summary>i + 1 < len(a) keeps i from the last position, but a[i + 2] is still past the end one before it - IndexError.</summary>
    [Fact]
    public async Task InPythonATestThatStillLetsABadIndexThroughIsFoundWhereItLetsItThrough()
    {
        if (await Python(Pairs.Replace("a[i + 1]", "a[i + 2] if i + 1 < len(a) else 0", StringComparison.Ordinal)) is not { } findings) return;

        var found = Assert.Single(findings);
        Assert.Equal(4, found.Span.Line);
        Says("`a[i + 2]` is read when `i` is `len(a) - 2`, where it asks for position `len(a)` - past the end of `a` - and fails with IndexError", found);
    }

    /// <summary>
    /// C, C++ and JavaScript write i++ and i += 1 as expressions, so their loops are not read as counted here: the guarded
    /// table was never reported in them, and still is not.
    /// </summary>
    [Theory]
    [InlineData("knapsack.c", """
        #include <stdio.h>

        static int knapsack(int capacity, int items[][2], int count) {
            int table[5][8];
            for (int i = 0; i <= count; i++) {
                int weight = i > 0 ? items[i - 1][0] : 0;
                int value = i > 0 ? items[i - 1][1] : 0;
                for (int j = 0; j <= capacity; j++) {
                    if (i == 0 || j == 0) {
                        table[i][j] = 0;
                    } else if (weight <= j) {
                        int taken = value + table[i - 1][j - weight];
                        table[i][j] = table[i - 1][j] > taken ? table[i - 1][j] : taken;
                    } else {
                        table[i][j] = table[i - 1][j];
                    }
                }
            }
            return table[count][capacity];
        }
        """)]
    [InlineData("knapsack.js", """
        function knapsack(capacity, items) {
          const table = [];
          for (let i = 0; i <= items.length; i++) {
            table.push(new Array(capacity + 1).fill(0));
            const weight = i > 0 ? items[i - 1][0] : 0;
            const value = i > 0 ? items[i - 1][1] : 0;
            for (let j = 0; j <= capacity; j++) {
              if (i === 0 || j === 0) {
                table[i][j] = 0;
              } else if (weight <= j) {
                table[i][j] = Math.max(table[i - 1][j], value + table[i - 1][j - weight]);
              } else {
                table[i][j] = table[i - 1][j];
              }
            }
          }
          return table[items.length][capacity];
        }
        """)]
    public async Task InCAndJavaScriptTheGuardedTableIsLeftAlone(string file, string code)
    {
        var path = Path.Combine(_temp.Path, file);
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        var program = file.EndsWith(".js", StringComparison.Ordinal) ? await JavaScriptFrontend.ReadAsync([path]) : await CFrontend.ReadAsync([path]);

        var findings = Loops(AbstractChecks.Run(program, new SourceText()));
        Assert.True(findings.Count == 0, Summary(findings));
    }
}
