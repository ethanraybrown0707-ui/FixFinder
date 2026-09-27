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
    public async Task CorrectCodeIsLeftAlone(string shape, string body)
    {
        if (await Analyse(body) is not { } findings) return;

        Assert.True(findings.Count == 0, $"{shape}: {string.Join("; ", findings.Select(f => $"{f.CheckId} line {f.Span.Line}: {f.Message}"))}");
    }
}
