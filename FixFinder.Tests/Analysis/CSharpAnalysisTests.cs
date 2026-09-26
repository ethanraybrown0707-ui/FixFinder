using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Checking;

namespace FixFinder.Tests;

/// <summary>
/// C# read by Roslyn into the shared IR: each mistake is found on its line, and each C# shape that could fool the analysis -
/// ?. and ??, out variables, patterns, lambdas that change what they capture - stays quiet when the code is correct.
/// </summary>
public class CSharpAnalysisTests : IDisposable
{
    private const string Header = "using System;\nusing System.Collections.Generic;\nusing System.Linq;\n\npublic class App\n{\n";

    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<IrProgram> ReadSource(string source)
    {
        var path = Path.Combine(_temp.Path, "App.cs");
        await File.WriteAllTextAsync(path, source.ReplaceLineEndings("\n"));
        var program = await CSharpFrontend.ReadAsync([path]);
        Assert.Empty(program.Problems);
        return program;
    }

    private Task<IrProgram> Read(string body) => ReadSource(Header + body + "\n}\n");

    private async Task<IReadOnlyList<AnalysisFinding>> Analyse(string body) => AbstractChecks.Run(await Read(body), new SourceText());

    private static int LineOf(string body, string marker) =>
        body.ReplaceLineEndings("\n").Split('\n').Select((text, i) => (text, i)).First(l => l.text.Contains(marker, StringComparison.Ordinal)).i
        + Header.Count(c => c == '\n') + 1;

    [Fact]
    public async Task ClassesMethodsPropertiesAndFieldsAreRead()
    {
        var program = await Read("    private int _count;\n    public string Name { get; set; } = \"\";\n    public int Twice => _count * 2;\n" +
                                 "    public App(int start) { _count = start; }\n    public static int Half(int x) => x / 2;");

        var app = program.Classes.Single();
        Assert.Equal(["_count", "Name"], app.Fields.Select(f => f.Name));
        Assert.Contains(app.Methods, m => m.Name == "Half" && m.IsStatic && m.Parameters.Single().Type.Name == "int");
        Assert.Contains(app.Methods, m => m.IsConstructor);
        Assert.Contains(app.Methods, m => m.Name == "get Twice");
    }

    [Fact]
    public async Task TopLevelStatementsAreTheModuleBody()
    {
        var program = await ReadSource("int total = 10;\nint count = 0;\nConsole.WriteLine(total / count);\n");

        Assert.Equal(IrFunction.ModuleBody, program.Functions.Single().Name);
        var finding = Assert.Single(AbstractChecks.Run(program, new SourceText()));
        Assert.Equal(("analysis-division-by-zero", 3, Confidence.Certain), (finding.CheckId, finding.Span.Line, finding.Confidence));
    }

    [Fact]
    public async Task ALambdaIsAFunctionEnclosedByTheMethodItIsWrittenIn()
    {
        var program = await Read("    static int Count(List<int> items)\n    {\n        int count = 0;\n        items.ForEach(i => count++);\n        return count;\n    }");

        var lambda = program.Functions.Single();
        Assert.Equal("App.Count", lambda.EnclosedBy);
        Assert.Contains("count", lambda.OuterNames);
    }

    [Theory]
    [InlineData("analysis-division-by-zero", "Possible", "    static int Average(int[] values)\n    {\n        int total = 0;\n        int count = 0;\n        foreach (var v in values)\n        {\n            total += v;\n            count++;\n        }\n        return total / count;\n    }", "total / count")]
    [InlineData("analysis-division-by-zero", "Certain", "    static int F()\n    {\n        int n = 0;\n        return 10 / n;\n    }", "10 / n")]
    [InlineData("analysis-division-by-zero", "Possible", "    static int Mean(List<int> scores)\n    {\n        int total = 0;\n        foreach (var s in scores) total += s;\n        return total / scores.Count;\n    }", "total / scores.Count")]
    [InlineData("analysis-division-by-zero", "Certain", "    static decimal F()\n    {\n        decimal price = 0;\n        return 10 / price;\n    }", "10 / price")]
    [InlineData("analysis-null-used", "Possible", "    static string Label(int score)\n    {\n        string message = null;\n        if (score > 90) message = \"top\";\n        return message.ToUpper();\n    }", "message.ToUpper()")]
    [InlineData("analysis-null-used", "Possible", "    static int NameLength(App person)\n    {\n        var name = person?.ToString();\n        return name.Length;\n    }", "name.Length")]
    [InlineData("analysis-index-out-of-range", "Certain", "    static int F()\n    {\n        int[] points = { 3, 5, 8 };\n        return points[3];\n    }", "points[3]")]
    [InlineData("analysis-index-out-of-range", "Certain", "    static int F()\n    {\n        var names = new List<string> { \"a\", \"b\" };\n        return names[2].Length;\n    }", "names[2]")]
    [InlineData("analysis-empty-collection", "Certain", "    static int F()\n    {\n        var stack = new Stack<int>();\n        return stack.Pop();\n    }", "stack.Pop()")]
    [InlineData("analysis-not-a-number", "Certain", "    static int F() => int.Parse(\"twelve\");", "int.Parse")]
    [InlineData("analysis-never-true", "Likely", "    static int Grade(int mark)\n    {\n        if (mark > 100 && mark < 0) return -1;\n        return 0;\n    }", "mark > 100")]
    [InlineData("analysis-never-true", "Likely", "    static int Grade(int mark)\n    {\n        if (mark < 0) return 0;\n        if (mark is < 0) return -1;\n        return 1;\n    }", "mark is < 0")]
    [InlineData("analysis-loop-never-runs", "Likely", "    static int F()\n    {\n        int n = 0;\n        while (n > 0) n--;\n        return n;\n    }", "while (n > 0)")]
    public async Task EachMistakeIsFoundOnItsLine(string check, string confidence, string body, string marker)
    {
        var findings = await Analyse(body);

        var finding = findings.SingleOrDefault(f => f.CheckId == check);
        Assert.True(finding is not null, $"{check} not found; found: {string.Join("; ", findings.Select(f => f.CheckId))}");
        Assert.Equal(LineOf(body, marker), finding!.Span.Line);
        Assert.Equal(Enum.Parse<Confidence>(confidence), finding.Confidence);
        Assert.Contains(check == "analysis-null-used" ? "NullReferenceException" : "", finding.Message);
    }

    [Theory]
    [InlineData("?. and ?? together", "    static int F(App person) => person?.ToString()?.Length ?? 0;")]
    [InlineData("?. then ?? a default", "    static int F(string text)\n    {\n        var extra = text?.Trim() ?? \"\";\n        return extra.Length;\n    }")]
    [InlineData("?. then ?? throw", "    static int F(App app)\n    {\n        var found = app?.ToString() ?? throw new InvalidOperationException();\n        return found.Length;\n    }")]
    [InlineData("?. on something that is not a variable", "    static string F(string a, string b) => (a ?? b)?.Trim().ToUpper();")]
    [InlineData("a value made safe with ??", "    static int F(bool flag)\n    {\n        string name = null;\n        if (flag) name = \"x\";\n        string label = name ?? \"none\";\n        return label.Length;\n    }")]
    [InlineData("?? throw", "    static int F(string input)\n    {\n        string s = input ?? throw new ArgumentNullException(nameof(input));\n        return s.Length;\n    }")]
    [InlineData("??=", "    static int F(bool flag)\n    {\n        string name = null;\n        if (flag) name = \"x\";\n        name ??= \"none\";\n        return name.Length;\n    }")]
    [InlineData("an out var set by TryParse", "    static int F(string text)\n    {\n        if (int.TryParse(text, out var n) && n != 0) return 10 / n;\n        return 0;\n    }")]
    [InlineData("an out argument set by TryParse", "    static int F(string text)\n    {\n        int n = 0;\n        if (!int.TryParse(text, out n)) return -1;\n        return 10 / n;\n    }")]
    [InlineData("a lambda that changes what it captures", "    static int F(List<int> items)\n    {\n        int count = 0;\n        items.ForEach(i => count++);\n        if (count == 0) return -1;\n        return count;\n    }")]
    [InlineData("a local function that changes what it captures", "    static int F()\n    {\n        int total = 0;\n        void Add(int x) { total += x; }\n        Add(5);\n        if (total == 0) return -1;\n        return 100 / total;\n    }")]
    [InlineData("a type pattern", "    static int F(object thing)\n    {\n        if (thing is string s) return s.Length;\n        return 0;\n    }")]
    [InlineData("a type pattern on something that can be null", "    static int F(bool flag)\n    {\n        object thing = null;\n        if (flag) thing = \"x\";\n        if (thing is string) return thing.GetHashCode();\n        return 0;\n    }")]
    [InlineData("is null", "    static int F(bool flag)\n    {\n        string s = null;\n        if (flag) s = \"x\";\n        if (s is null) return 0;\n        return s.Length;\n    }")]
    [InlineData("is not null", "    static int F(bool flag)\n    {\n        string s = null;\n        if (flag) s = \"x\";\n        if (s is not null) return s.Length;\n        return 0;\n    }")]
    [InlineData("a nullable int", "    static int F(bool flag)\n    {\n        int? x = null;\n        if (flag) x = 5;\n        if (x.HasValue) return x.Value;\n        return x.GetValueOrDefault();\n    }")]
    [InlineData("string.IsNullOrEmpty", "    static int F(bool flag)\n    {\n        string s = null;\n        if (flag) s = \"x\";\n        if (string.IsNullOrEmpty(s)) return 0;\n        return s.Length;\n    }")]
    [InlineData("a using declaration", "    static int F(string text)\n    {\n        using var reader = new System.IO.StringReader(text);\n        string line = reader.ReadLine();\n        return line == null ? 0 : line.Length;\n    }")]
    [InlineData("a goto that goes round again", "    static int F()\n    {\n        int tries = 0;\n    retry:\n        if (tries > 2) return -1;\n        tries++;\n        goto retry;\n    }")]
    [InlineData("a switch with patterns", "    static int F(object shape)\n    {\n        switch (shape)\n        {\n            case string s: return s.Length;\n            case null: return 0;\n            default: return 1;\n        }\n    }")]
    [InlineData("an object initializer naming a member like a local", "    public int x;\n    static int F()\n    {\n        int x = 3;\n        var box = new App { x = 0 };\n        return 10 / x;\n    }")]
    [InlineData("a guarded division by a count", "    static int F(List<int> items, int total)\n    {\n        if (items.Count > 0) return total / items.Count;\n        return 0;\n    }")]
    [InlineData("an item read from a filled list", "    static int F()\n    {\n        var list = new List<int> { 1, 2, 3 };\n        return list[2];\n    }")]
    [InlineData("new() given its type by the declaration", "    static int F()\n    {\n        List<int> list = new();\n        list.Add(1);\n        return list[0];\n    }")]
    [InlineData("a null-forgiving !", "    static string Find() => null;\n    static int F() => Find()!.Length;")]
    [InlineData("a field changed during an await", "    private string _state;\n    async System.Threading.Tasks.Task<int> F(System.Threading.Tasks.Task work)\n    {\n        _state = null;\n        await work;\n        if (_state != null) return _state.Length;\n        return 0;\n    }")]
    [InlineData("the last case of a switch", "    static int F(int kind)\n    {\n        if (kind < 1 || kind > 2) return 0;\n        switch (kind)\n        {\n            case 1: return 10;\n            case 2: return 20;\n        }\n        return 0;\n    }")]
    [InlineData("a dictionary initializer", "    static int F()\n    {\n        var ages = new Dictionary<string, int> { [\"a\"] = 1, { \"b\", 2 } };\n        return ages[\"a\"];\n    }")]
    [InlineData("a deconstructed tuple", "    static int F((int, int) pair)\n    {\n        var (a, b) = pair;\n        if (b == 0) return 0;\n        return a / b;\n    }")]
    [InlineData("a whole number kept in a double", "    static double F()\n    {\n        double d = 0;\n        return 10 / d;\n    }")]
    [InlineData("a whole number kept in a float", "    static float F()\n    {\n        float f = 0;\n        return 10 / f;\n    }")]
    [InlineData("a double that a method returns", "    static double Zero() { return 0; }\n    static double F() => 10 / Zero();")]
    [InlineData("a number written with thousands separators", "    static double F() => double.Parse(\"1,000.5\") + int.Parse(\" 42 \");")]
    public async Task CorrectCodeIsLeftAlone(string shape, string body)
    {
        var findings = await Analyse(body);

        Assert.True(findings.Count == 0, $"{shape}: {string.Join("; ", findings.Select(f => $"{f.CheckId} line {f.Span.Line}: {f.Message}"))}");
    }
}
