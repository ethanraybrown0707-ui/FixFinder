using System.Diagnostics;
using FixFinder.Core.Engine;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Parsing;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>The C# mistakes a beginner makes: each rule's fix and refusals from Roslyn's own messages, then the same mistakes
/// built for real by the .NET SDK.</summary>
public class CSharpBasicsTests : IDisposable
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
        File.WriteAllText(path, body);
        return path;
    }

    private static ParsedError Roslyn(string code, string message, string file, int line, int column) => new()
    {
        LanguageId = "msvc",
        Confidence = 90,
        RawText = $"{file}({line},{column}): error {code}: {message}",
        FirstLineSequence = 0,
        ExceptionType = "compile error",
        ErrorCode = code,
        Message = message,
        Frames = [new ErrorFrame { Order = 0, File = file, Line = line, Column = column, RawLine = "" }],
    };

    private string? Propose(string rule, ParsedError error)
    {
        var fix = LocalFixEngine.Rules.Single(r => r.Id == rule).Propose(new LocalFixContext { Error = error, SourceRoot = _temp.Path });
        return fix is null ? null : string.Join("|", fix.NewLines);
    }

    [Theory]
    [InlineData("csharp-elif", "CS1002", "; expected", "if (x > 1) { } elif (x == 1) { }\n", 1, 21, "if (x > 1) { } else if (x == 1) { }")]
    [InlineData("csharp-java-print", "CS1001", "Identifier expected", "System.out.println(\"hi\");\n", 1, 11, "Console.WriteLine(\"hi\");")]
    [InlineData("csharp-java-print", "CS1001", "Identifier expected", "System.out.println(1); System.out.print(2);\n", 1, 11, null)]
    [InlineData("csharp-condition-parentheses", "CS1003", "Syntax error, '(' expected", "        if x > 5 {\n", 1, 12, "        if (x > 5) {")]
    [InlineData("csharp-condition-parentheses", "CS1003", "Syntax error, '(' expected", "} else if x == 1 {\n", 1, 11, "} else if (x == 1) {")]
    [InlineData("csharp-condition-parentheses", "CS1003", "Syntax error, '(' expected", "if (x > 5) {\n", 1, 4, null)]
    [InlineData("csharp-missing-semicolon", "CS1002", "; expected", "        int x = 3\n", 1, 18, "        int x = 3;")]
    [InlineData("csharp-missing-semicolon", "CS1002", "; expected", "        if (ready) {\n", 1, 21, null)]
    [InlineData("csharp-missing-closing-brace", "CS1513", "} expected", "class P {\n    static void Main() {\n    }\n", 3, 6, "}")]
    [InlineData("csharp-missing-closing-brace", "CS1513", "} expected", "class P {\n    void M() {\n", 2, 15, null)]
    [InlineData("csharp-char-literal-string", "CS1012", "Too many characters in character literal", "var s = 'Ethan';\n", 1, 9, "var s = \"Ethan\";")]
    [InlineData("csharp-foreach-type", "CS0230", "Type and identifier are both required in a foreach statement", "foreach (item in items) { }\n", 1, 10, "foreach (var item in items) { }")]
    [InlineData("csharp-name-missing", "CS0103", "The name 'True' does not exist in the current context", "bool ok = True;\n", 1, 11, "bool ok = true;")]
    [InlineData("csharp-name-missing", "CS0103", "The name 'None' does not exist in the current context", "string s = None;\n", 1, 12, "string s = null;")]
    [InlineData("csharp-name-missing", "CS0103", "The name 'print' does not exist in the current context", "print(\"hi\");\n", 1, 1, "Console.WriteLine(\"hi\");")]
    [InlineData("csharp-name-missing", "CS0103", "The name 'len' does not exist in the current context", "int n = len(name);\n", 1, 9, "int n = name.Length;")]
    [InlineData("csharp-name-missing", "CS0103", "The name 'i' does not exist in the current context", "for (i = 0; i < 3; i++) { }\n", 1, 6, "for (int i = 0; i < 3; i++) { }")]
    [InlineData("csharp-name-missing", "CS0103", "The name 'avarage' does not exist in the current context", "int average = 3;\nConsole.WriteLine(avarage);\n", 2, 19, "Console.WriteLine(average);")]
    [InlineData("csharp-name-missing", "CS0103", "The name 'zzz' does not exist in the current context", "Console.WriteLine(zzz);\n", 1, 19, null)]
    [InlineData("csharp-missing-member", "CS0117", "'Console' does not contain a definition for 'WriteLin'", "Console.WriteLin(\"hi\");\n", 1, 9, "Console.WriteLine(\"hi\");")]
    [InlineData("csharp-missing-member", "CS1061", "'List<int>' does not contain a definition for 'Length' and no accessible extension method 'Length' accepting a first argument of type 'List<int>' could be found (are you missing a using directive or an assembly reference?)", "var n = items.Length;\n", 1, 15, "var n = items.Count;")]
    [InlineData("csharp-missing-member", "CS1061", "'string' does not contain a definition for 'length' and no accessible extension method 'length' accepting a first argument of type 'string' could be found (are you missing a using directive or an assembly reference?)", "var n = name.length();\n", 1, 14, "var n = name.Length;")]
    [InlineData("csharp-missing-member", "CS1061", "'string' does not contain a definition for 'equals' and no accessible extension method 'equals' accepting a first argument of type 'string' could be found (are you missing a using directive or an assembly reference?)", "if (name.equals(\"x\")) { }\n", 1, 10, "if (name.Equals(\"x\")) { }")]
    [InlineData("csharp-missing-member", "CS0117", "'Math' does not contain a definition for 'sqrt'", "var r = Math.sqrt(4);\n", 1, 14, "var r = Math.Sqrt(4);")]
    [InlineData("csharp-missing-member", "CS1061", "'Widget' does not contain a definition for 'Foo'", "w.Foo();\n", 1, 3, null)]
    [InlineData("csharp-non-invocable", "CS1955", "Non-invocable member 'string.Length' cannot be used like a method.", "var n = name.Length();\n", 1, 14, "var n = name.Length;")]
    [InlineData("csharp-non-invocable", "CS1955", "Non-invocable member 'List<T>' cannot be used like a method.", "var items = List<int>();\n", 1, 13, "var items = new List<int>();")]
    [InlineData("csharp-implicit-conversion", "CS0029", "Cannot implicitly convert type 'string' to 'int'", "int age = \"18\";\n", 1, 11, "int age = 18;")]
    [InlineData("csharp-implicit-conversion", "CS0029", "Cannot implicitly convert type 'string' to 'int'", "int next = input + 1;\n", 1, 12, "int next = int.Parse(input) + 1;")]
    [InlineData("csharp-implicit-conversion", "CS0029", "Cannot implicitly convert type 'string' to 'int'", "int n = Console.ReadLine();\n", 1, 9, "int n = int.Parse(Console.ReadLine());")]
    [InlineData("csharp-implicit-conversion", "CS0029", "Cannot implicitly convert type 'string' to 'int'", "int n = a + b;\n", 1, 9, null)]
    [InlineData("csharp-implicit-conversion", "CS0029", "Cannot implicitly convert type 'string' to 'char'", "char c = \"a\";\n", 1, 10, "char c = 'a';")]
    [InlineData("csharp-implicit-conversion", "CS0029", "Cannot implicitly convert type 'int' to 'string'", "string s = 5;\n", 1, 12, "string s = \"5\";")]
    [InlineData("csharp-implicit-conversion", "CS0029", "Cannot implicitly convert type 'int' to 'string'", "string s = total;\n", 1, 12, "string s = total.ToString();")]
    [InlineData("csharp-implicit-conversion", "CS0029", "Cannot implicitly convert type 'int' to 'bool'", "if (x = 5) { }\n", 1, 5, "if (x == 5) { }")]
    [InlineData("csharp-implicit-conversion", "CS0029", "Cannot implicitly convert type 'int' to 'bool'", "if (count) { }\n", 1, 5, "if (count != 0) { }")]
    [InlineData("csharp-implicit-conversion", "CS0266", "Cannot implicitly convert type 'double' to 'int'. An explicit conversion exists (are you missing a cast?)", "int x = 5.5;\n", 1, 9, "int x = (int)5.5;")]
    [InlineData("csharp-non-static-member", "CS0120", "An object reference is required for the non-static field, method, or property 'Program.Total()'", "class Program {\n    int Total() { return 3; }\n    static void Main() { System.Console.WriteLine(Total()); }\n}\n", 3, 51, "    static int Total() { return 3; }")]
    [InlineData("csharp-type-not-found", "CS0246", "The type or namespace name 'StringBuilder' could not be found (are you missing a using directive or an assembly reference?)", "using System;\nvar sb = new StringBuilder();\n", 2, 14, "using System.Text;")]
    [InlineData("csharp-type-not-found", "CS0246", "The type or namespace name 'Lsit<>' could not be found (are you missing a using directive or an assembly reference?)", "var items = new Lsit<int>();\n", 1, 17, "var items = new List<int>();")]
    [InlineData("csharp-type-not-found", "CS0246", "The type or namespace name 'boolean' could not be found (are you missing a using directive or an assembly reference?)", "boolean ok = true;\n", 1, 1, "bool ok = true;")]
    [InlineData("csharp-namespace-typo", "CS0234", "The type or namespace name 'Collection' does not exist in the namespace 'System' (are you missing an assembly reference?)", "using System.Collection.Generic;\n", 1, 14, "using System.Collections.Generic;")]
    [InlineData("csharp-await-without-async", "CS4033", "The 'await' operator can only be used within an async method. Consider marking this method with the 'async' modifier and changing its return type to 'Task'.", "class P {\n    static void Main() {\n        await Task.Delay(10);\n    }\n}\n", 3, 9, "    static async Task Main() {")]
    [InlineData("csharp-inaccessible", "CS0122", "'Counter.Next()' is inaccessible due to its protection level", "class Counter {\n    int Next() { return 1; }\n}\nclass P { static void Main() { new Counter().Next(); } }\n", 4, 46, "    public int Next() { return 1; }")]
    [InlineData("csharp-unassigned-local", "CS0165", "Use of unassigned local variable 'total'", "int total;\nConsole.WriteLine(total);\n", 2, 19, "int total = 0;")]
    [InlineData("csharp-unassigned-local", "CS0165", "Use of unassigned local variable 'name'", "string name;\nConsole.WriteLine(name);\n", 2, 19, null)]
    [InlineData("csharp-missing-return-type", "CS1520", "Method must have a return type", "class P {\n    static Add(int a, int b) {\n        return a + b;\n    }\n}\n", 2, 12, "    static int Add(int a, int b) {")]
    [InlineData("csharp-missing-return-type", "CS1520", "Method must have a return type", "class P {\n    static Hello() {\n        System.Console.WriteLine(1);\n    }\n}\n", 2, 12, "    static void Hello() {")]
    [InlineData("csharp-missing-return-type", "CS1520", "Method must have a return type", "class Program {\n    Progam() {\n    }\n}\n", 2, 5, null)]
    public void EachConstructGetsItsFixOrARefusal(string rule, string code, string message, string source, int line, int column, string? expected)
    {
        var file = Write("Program.cs", source);

        Assert.Equal(expected, Propose(rule, Roslyn(code, message, file, line, column)));
    }

    [Theory]
    [InlineData("for (int i = 0; i <= items.Length; i++)", "for (int i = 0; i < items.Length; i++)")]
    [InlineData("for (int i = 0; i <= 10; i++)", null)]
    public void AnOffByOneLoopStopsAtTheLastElement(string loop, string? expected)
    {
        var file = Write("Program.cs", $"var items = new int[3];\n{loop}\n    Console.WriteLine(items[i]);\n");

        var error = new ParsedError
        {
            LanguageId = "csharp",
            Confidence = 90,
            RawText = "Unhandled exception. System.IndexOutOfRangeException: Index was outside the bounds of the array.",
            FirstLineSequence = 0,
            ExceptionType = "System.IndexOutOfRangeException",
            Message = "Index was outside the bounds of the array.",
            Frames = [new ErrorFrame { Order = 0, File = file, Line = 3, RawLine = "" }],
        };

        Assert.Equal(expected, Propose("csharp-off-by-one-loop", error));
    }

    private static readonly Lazy<bool> RunsSingleFiles = new(() =>
    {
        if (TargetFactory.FindOnPath("dotnet") is not { } dotnet) return false;

        try
        {
            using var process = Process.Start(new ProcessStartInfo(dotnet, "--version")
            {
                UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true,
                WorkingDirectory = Path.GetTempPath(),
            });

            var version = process!.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(30_000);

            return int.TryParse(version.Split('.')[0], out var major) && major >= 10;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    });

    private static string Program(string body, string usings = "using System;\n") =>
        usings + "class Program {\n    static void Main() {\n" + body + "    }\n}\n";

    public static TheoryData<string, string, string, string> Constructs => new()
    {
        { "missing semicolon", Program("        int x = 3\n        Console.WriteLine(x);\n"), "local:csharp-missing-semicolon", "int x = 3;" },
        { "elif", Program("        int x = 1;\n        if (x > 1) {\n            Console.WriteLine(\"big\");\n        } elif (x == 1) {\n            Console.WriteLine(\"one\");\n        }\n"), "local:csharp-elif", "} else if (x == 1) {" },
        { "python print", Program("        print(\"hi\");\n"), "local:csharp-name-missing", "Console.WriteLine(\"hi\");" },
        { "python True", Program("        bool ready = True;\n        Console.WriteLine(ready);\n"), "local:csharp-name-missing", "bool ready = true;" },
        { "string to int", Program("        int age = \"18\";\n        Console.WriteLine(age);\n"), "local:csharp-implicit-conversion", "int age = 18;" },
        { "text plus a number", Program("        string input = \"17\";\n        int next = input + 1;\n        Console.WriteLine(next);\n"), "local:csharp-implicit-conversion", "int next = int.Parse(input) + 1;" },
        { "Length on a List", Program("        var items = new System.Collections.Generic.List<int> { 1, 2 };\n        Console.WriteLine(items.Length);\n"), "local:csharp-missing-member", "items.Count" },
        { "Length with brackets", Program("        string name = \"Ethan\";\n        Console.WriteLine(name.Length());\n"), "local:csharp-non-invocable", "Console.WriteLine(name.Length);" },
        { "missing new", Program("        var items = System.Collections.Generic.List<int>();\n        Console.WriteLine(items.Count);\n"), "local:csharp-non-invocable", "new System.Collections.Generic.List<int>()" },
        { "foreach without a type", Program("        string[] names = { \"a\", \"b\" };\n        foreach (n in names) {\n            Console.WriteLine(n);\n        }\n"), "local:csharp-foreach-type", "foreach (var n in names) {" },
        { "java println", Program("        System.out.println(\"hi\");\n"), "local:csharp-java-print", "Console.WriteLine(\"hi\");" },
        { "if without brackets", Program("        int x = 3;\n        if x > 1 {\n            Console.WriteLine(x);\n        }\n"), "local:csharp-condition-parentheses", "if (x > 1) {" },
        { "unassigned local", Program("        int total;\n        Console.WriteLine(total);\n"), "local:csharp-unassigned-local", "int total = 0;" },
        { "single-quoted string", Program("        string name = 'Ethan';\n        Console.WriteLine(name);\n"), "local:csharp-char-literal-string", "string name = \"Ethan\";" },
        { "type typo", Program("        var items = new System.Collections.Generic.Lsit<int>();\n        Console.WriteLine(items.Count);\n"), "local:csharp-namespace-typo", "System.Collections.Generic.List<int>" },
        { "missing return type", "using System;\nclass Program {\n    static Add(int a, int b) {\n        return a + b;\n    }\n    static void Main() {\n        Console.WriteLine(Add(1, 2));\n    }\n}\n", "local:csharp-missing-return-type", "static int Add(int a, int b) {" },
        { "await without async", "using System;\nusing System.Threading.Tasks;\nclass Program {\n    static void Main() {\n        await Task.Delay(10);\n        Console.WriteLine(\"done\");\n    }\n}\n", "local:csharp-await-without-async", "static async Task Main() {" },
        { "non-static method", "using System;\nclass Program {\n    int Total() { return 3; }\n    static void Main() {\n        Console.WriteLine(Total());\n    }\n}\n", "local:csharp-non-static-member", "static int Total() { return 3; }" },
    };

    [Theory]
    [MemberData(nameof(Constructs))]
    public async Task TheConstructIsFixedLive(string construct, string source, string id, string expected)
    {
        if (!RunsSingleFiles.Value) return;

        var plan = TargetFactory.FromFile(Write("Program.cs", source), TimeSpan.FromMinutes(3));
        Assert.True(plan.Ok, plan.Problem);

        using var http = new FixFinderHttpClient();
        var outcome = await new FixFinderSession(http, new FixSourceRegistry()).RunAsync(plan, new SearchBudget(Cache: CacheMode.CacheOnly));

        if (ApplicationControl.Refused(outcome)) return;
        Assert.True(outcome.Result == SessionResult.FoundFix, $"{construct}: {outcome.Result} - {outcome.Headline}");
        Assert.Equal(id, outcome.Best!.Id);

        var copied = PasteableFix.For(new ExaminedCandidate(outcome.Best, 1, outcome.Candidates.Count, outcome.Harvest, outcome.Plan));

        Assert.NotNull(copied);
        Assert.Contains(expected, copied!.Text, StringComparison.Ordinal);
    }
}
