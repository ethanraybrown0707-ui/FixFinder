using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Flow;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Checking;

namespace FixFinder.Tests;

/// <summary>
/// Java read by javac's own parser into the shared IR, and analysed like Python: each mistake is found on its line, and
/// each shape of correct code that once raised a false alarm in the JDK stays quiet.
/// </summary>
public class JavaAnalysisTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<IrProgram?> Read(string body)
    {
        if (JavaFrontend.FindTools() is not { } tools) return null;

        var path = Path.Combine(_temp.Path, "App.java");
        await File.WriteAllTextAsync(path, $"import java.util.*;\n\npublic class App {{\n{body}\n}}\n".ReplaceLineEndings("\n"));
        var program = await JavaFrontend.ReadAsync([path], tools.Javac, tools.Java);
        Assert.Empty(program.Problems);
        return program;
    }

    private async Task<IReadOnlyList<AnalysisFinding>?> Analyse(string body) =>
        await Read(body) is { } program ? AbstractChecks.Run(program, new SourceText()) : null;

    private static int LineOf(string body, string marker) =>
        body.ReplaceLineEndings("\n").Split('\n').Select((text, i) => (text, i)).First(l => l.text.Contains(marker, StringComparison.Ordinal)).i + 4;

    [Fact]
    public async Task ClassesMethodsAndFieldsAreRead()
    {
        if (await Read("    private int count = 0;\n    static int twice(int x) { return x * 2; }\n    App(int start) { this.count = start; }") is not { } program) return;

        var app = program.Classes.Single();
        Assert.Equal("App", app.Name);
        Assert.Equal(["count"], app.Fields.Select(f => f.Name));
        Assert.Contains(app.Methods, m => m.Name == "twice" && m.IsStatic && m.Parameters.Single().Type.Name == "int");
        Assert.Contains(app.Methods, m => m.IsConstructor);
    }

    [Fact]
    public async Task ALabeledBreakLeavesTheLabeledBlock()
    {
        const string body = "    static int f(String s) {\n        check: {\n            if (s == null) break check;\n            return s.length();\n        }\n        return 0;\n    }";
        if (await Read(body) is not { } program) return;

        var text = IrText.Of(CfgBuilder.Build(program.Classes.Single().Methods.Single()));
        Assert.DoesNotContain("s.length()", text.Split('\n').Single(l => l.Contains("if s == null", StringComparison.Ordinal)));
        Assert.Empty(AbstractChecks.Run(program, new SourceText()));
    }

    [Theory]
    [InlineData("analysis-division-by-zero", "Possible", "    static int average(int[] values) {\n        int total = 0;\n        int count = 0;\n        for (int v : values) {\n            total += v;\n            count++;\n        }\n        return total / count;\n    }", "total / count")]
    [InlineData("analysis-division-by-zero", "Certain", "    static int f() {\n        int n = 0;\n        return 10 / n;\n    }", "10 / n")]
    [InlineData("analysis-not-a-number", "Certain", "    static double f() {\n        return Double.parseDouble(\"inf\");\n    }", "Double.parseDouble")]
    [InlineData("analysis-null-used", "Possible", "    static String label(int score) {\n        String message = null;\n        if (score > 90) message = \"top\";\n        return message.toUpperCase();\n    }", "message.toUpperCase()")]
    [InlineData("analysis-index-out-of-range", "Certain", "    static int f() {\n        int[] points = {3, 5, 8};\n        return points[3];\n    }", "points[3]")]
    [InlineData("analysis-never-true", "Likely", "    static int grade(int mark) {\n        if (mark > 100 && mark < 0) return -1;\n        return 0;\n    }", "mark > 100")]
    [InlineData("analysis-loop-never-runs", "Likely", "    static int f() {\n        int n = 0;\n        while (n > 0) n--;\n        return n;\n    }", "while (n > 0)")]
    // A key put in a map is found again only while it is the same key in the same map: not another key, not once it is
    // taken out, and not once the variable it was written with has moved on.
    [InlineData("analysis-null-used", "Possible", "    static int f(int i) {\n        Map<Integer, String> names = new HashMap<>();\n        names.put(i, \"a\");\n        return names.get(i + 1).length();\n    }", "names.get(i + 1)")]
    [InlineData("analysis-null-used", "Possible", "    static int f(int i) {\n        Map<Integer, String> names = new HashMap<>();\n        names.put(i, \"a\");\n        names.remove(i);\n        return names.get(i).length();\n    }", "return names.get(i)")]
    [InlineData("analysis-null-used", "Possible", "    static int f(int i) {\n        Map<Integer, String> names = new HashMap<>();\n        names.put(i, \"a\");\n        i++;\n        return names.get(i).length();\n    }", "return names.get(i)")]
    public async Task EachMistakeIsFoundOnItsLine(string check, string confidence, string body, string marker)
    {
        if (await Analyse(body) is not { } findings) return;

        var finding = findings.SingleOrDefault(f => f.CheckId == check);
        Assert.True(finding is not null, $"{check} not found; found: {string.Join("; ", findings.Select(f => f.CheckId))}");
        Assert.Equal(LineOf(body, marker), finding!.Span.Line);
        Assert.Equal(Enum.Parse<Confidence>(confidence), finding.Confidence);
    }

    [Theory]
    [InlineData("an overflow check", "    static void f(char[] buf, int offset, int length) {\n        if (offset < 0 || length < 0 || (offset + length) < 0) throw new IndexOutOfBoundsException();\n    }")]
    [InlineData("a doubled length that can overflow", "    static int f(String s) {\n        int len = s.length();\n        int bufLen = len * 2;\n        if (bufLen < 0) { bufLen = Integer.MAX_VALUE; }\n        return bufLen;\n    }")]
    [InlineData("the result of adding to a set", "    static int f(Set<String> seen, String item) {\n        boolean added = seen.add(item);\n        if (!added) { return 1; }\n        return 0;\n    }")]
    [InlineData("an assignment inside a method call's target", "    static Object f(Object first) {\n        String t = null;\n        int n = (t = first.toString()).length();\n        if (t != null) { return t; }\n        return n;\n    }")]
    [InlineData("an increment inside an index", "    static long f(long[] groups, boolean more) {\n        int count = 0;\n        while (more) {\n            groups[count++] = 1;\n            more = false;\n        }\n        if (count == 0) return 0;\n        return groups[count - 1];\n    }")]
    [InlineData("a pre-increment compared once", "    static boolean f() {\n        int zeros = 0;\n        return ++zeros == 1;\n    }")]
    [InlineData("a field assigned through this", "    private List<String> items;\n    void ensure() {\n        if (items == null) {\n            this.items = new ArrayList<>();\n            items.add(\"first\");\n        }\n    }")]
    [InlineData("a field changed by another call", "    private Throwable error;\n    void run() {\n        error = null;\n        work();\n        if (error != null) { System.out.println(error); }\n    }\n    void work() { error = new RuntimeException(); }")]
    [InlineData("a guard that only throws", "    static int f(List<String> list) {\n        int size = list.size();\n        if (size < 0) { throw new IllegalStateException(\"size\"); }\n        return size;\n    }")]
    [InlineData("the last case of a switch", "    static int f(int kind) {\n        if (kind < 1 || kind > 2) return 0;\n        switch (kind) {\n            case 1: return 10;\n            case 2: return 20;\n        }\n        return 0;\n    }")]
    [InlineData("a guarded division by a size", "    static int f(List<String> items) {\n        if (items.size() > 0) { return 10 / items.size(); }\n        return 0;\n    }")]
    [InlineData("a whole number kept in a double", "    static double f() {\n        double d = 0;\n        return 10 / d;\n    }")]
    [InlineData("a double that a method returns", "    static double zero() { return 0; }\n    static double f() { return 10 / zero(); }")]
    [InlineData("doubles written with a type letter and in hexadecimal", "    static double f() {\n        return Double.parseDouble(\"1.5f\") + Double.parseDouble(\"0x1p3\") + Double.parseDouble(\" -Infinity \");\n    }")]
    [InlineData("a Stack class of the program's own", "    static class Stack {\n        private final int[] items = new int[10];\n        private int size;\n        void push(int value) { items[size++] = value; }\n        int pop() { return size == 0 ? -1 : items[--size]; }\n    }\n    static int f() {\n        Stack stack = new Stack();\n        stack.push(1);\n        stack.pop();\n        return stack.pop();\n    }")]
    // QuixBugs' LCS_LENGTH (MIT licence), whose lookups were each taken for one that can find nothing.
    [InlineData("a lookup guarded by containsKey", "    static int f(Map<Integer, Map<Integer, Integer>> dp, int i, int j) {\n        if (dp.containsKey(i - 1) && dp.get(i - 1).containsKey(j - 1)) {\n            return dp.get(i - 1).get(j - 1) + 1;\n        }\n        return 1;\n    }")]
    [InlineData("a lookup of the key just put", "    static int f(int n) {\n        Map<Integer, List<Integer>> rows = new HashMap<>();\n        for (int i = 0; i < n; i++) {\n            rows.put(i, new ArrayList<>());\n            for (int j = 0; j < i; j++) {\n                List<Integer> row = rows.get(i);\n                row.add(j);\n            }\n        }\n        return rows.size();\n    }")]
    public async Task CorrectCodeIsLeftAlone(string shape, string body)
    {
        if (await Analyse(body) is not { } findings) return;

        Assert.True(findings.Count == 0, $"{shape}: {string.Join("; ", findings.Select(f => $"{f.CheckId} line {f.Span.Line}: {f.Message}"))}");
    }

    /// <summary>
    /// A loop that fills a list, then a search whose answer starts at -1 and is set inside the loop. What the search loop
    /// sets is carried round it: best &lt; 0 was once said to be always true, because the code after the first loop was
    /// not looked at again once that loop could be left - the second loop was gone round only once.
    /// </summary>
    [Fact]
    public async Task WhatALoopSetsIsCarriedRoundItAfterAnEarlierLoopFillsAList()
    {
        const string body = "    static int largest() {\n        List<Integer> seen = new ArrayList<>();\n        for (int i = 0; i < 4; i++) seen.add(i);\n        int best = -1;\n        for (int i = 0; i < 4; i++) {\n            if (best < 0 || seen.get(i) > best) best = seen.get(i);\n        }\n        return best;\n    }";
        if (await Analyse(body) is not { } findings) return;

        Assert.True(findings.Count == 0, string.Join("; ", findings.Select(f => $"{f.CheckId} line {f.Span.Line}: {f.Message}")));
    }

    private const string ShortestPaths = """
            static final int INF = 1000000000;

            static int[] shortest() {
                List<List<int[]>> graph = new ArrayList<>();
                for (int i = 0; i < 4; i++) graph.add(new ArrayList<>());
                graph.get(0).add(new int[] {1, 4});
                graph.get(0).add(new int[] {2, 1});
                graph.get(1).add(new int[] {3, 1});
                graph.get(2).add(new int[] {1, 2});
                graph.get(2).add(new int[] {3, 5});
                int[] dist = {0, INF, INF, INF};
                boolean[] done = new boolean[4];
                for (int round = 0; round < 4; round++) {
                    int node = -1;
                    for (int i = 0; i < 4; i++) {
                        if (!done[i] && (node < 0 || dist[i] < dist[node])) node = i;
                    }
                    done[node] = true;
                    for (int[] edge : graph.get(node)) {
                        int next = edge[0];
                        int length = edge[1];
                        if (dist[node] + length < dist[next]) {
                            dist[next] = dist[node] + length;
                        }
                    }
                }
                return dist;
            }
        """;

    /// <summary>
    /// A shortest-path search that takes the nearest place not yet done each round; it prints 0 3 1 4. node &lt; 0 is not
    /// always true - node is set inside the loop. done[node] after the search fails only if every done[i] is true, which four
    /// rounds over four places never make so; FixFinder does not follow what done holds by then, so that is said as
    /// possible, never as a failure on one way through the code.
    /// </summary>
    [Fact]
    public async Task ASearchThatCanFindNothingOnlyIfItsOwnListSaysSoIsSaidAsPossible()
    {
        if (await Analyse(ShortestPaths) is not { } findings) return;

        Assert.DoesNotContain(findings, f => f.CheckId == "analysis-always-true");
        var finding = Assert.Single(findings, f => f.CheckId == "analysis-index-out-of-range");
        Assert.Equal(LineOf(ShortestPaths, "done[node] = true"), finding.Span.Line);
        Assert.Equal(Confidence.Possible, finding.Confidence);
        Assert.Equal("`node` is outside the list when `done[i]` is true every time it is tested, so `done[node]` fails with an " +
            "IndexOutOfBoundsException - FixFinder cannot tell what `done` holds by then, so this may never happen", finding.Message);
    }

    /// <summary>The same search over a list it is given: what is passed in can be all true, so that way stays likely.</summary>
    [Fact]
    public async Task ASearchThatFindsNothingInAListItIsGivenStaysLikely()
    {
        const string body = "    static int pick(int[] dist, boolean[] done) {\n        int node = -1;\n        for (int i = 0; i < 4; i++) {\n            if (!done[i] && (node < 0 || dist[i] < dist[node])) node = i;\n        }\n        return dist[node];\n    }";
        if (await Analyse(body) is not { } findings) return;

        var finding = Assert.Single(findings, f => f.CheckId == "analysis-index-out-of-range");
        Assert.Equal(LineOf(body, "return dist[node]"), finding.Span.Line);
        Assert.Equal(Confidence.Likely, finding.Confidence);
    }
}
