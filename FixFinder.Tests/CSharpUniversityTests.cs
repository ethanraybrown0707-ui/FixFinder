using System.Diagnostics;
using FixFinder.Core.Engine;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Parsing;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>
/// The C# mistakes of later years - interfaces, inheritance, properties, LINQ, exceptions, switch - each
/// rule's fix and refusals from Roslyn's own messages, then the same programs built for real.
/// </summary>
/// <remarks>Live cases need an SDK that runs a lone .cs file (10 or later) and skip without one.</remarks>
public class CSharpUniversityTests : IDisposable
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
        RawText = message,
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
    [InlineData("csharp-interface-member-public", "CS0737", "'Circle' does not implement interface member 'IShape.Area()'. 'Circle.Area()' cannot implement an interface member because it is not public.", "interface IShape\n{\n    double Area();\n}\n\nclass Circle : IShape\n{\n    double Area() { return 3.14; }\n}\n", 6, 16, "    public double Area() { return 3.14; }")]
    [InlineData("csharp-virtual-base", "CS0506", "'Dog.Speak()': cannot override inherited member 'Animal.Speak()' because it is not marked virtual, abstract, or override", "class Animal\n{\n    public string Speak() { return \"...\"; }\n}\n\nclass Dog : Animal\n{\n    public override string Speak() { return \"Woof\"; }\n}\n", 8, 28, "    public virtual string Speak() { return \"...\"; }")]
    [InlineData("csharp-override-typo", "CS0115", "'Point.ToStrin()': no suitable method found to override", "class Point\n{\n    public override string ToStrin() { return \"Point\"; }\n}\n", 3, 28, "    public override string ToString() { return \"Point\"; }")]
    [InlineData("csharp-override-typo", "CS0115", "'Point.Describe()': no suitable method found to override", "class Point\n{\n    public override string Describe() { return \"Point\"; }\n}\n", 3, 28, null)]
    [InlineData("csharp-constructor-return-type", "CS0542", "'Dog': member names cannot be the same as their enclosing type", "class Dog\n{\n    public void Dog(string name)\n    {\n    }\n}\n", 3, 17, "    public Dog(string name)")]
    [InlineData("csharp-inconsistent-accessibility", "CS0051", "Inconsistent accessibility: parameter type 'Item' is less accessible than method 'Store.Add(Item)'", "class Item\n{\n}\n\npublic class Store\n{\n    public void Add(Item item) { }\n}\n", 7, 17, "public class Item")]
    [InlineData("csharp-inconsistent-accessibility", "CS0050", "Inconsistent accessibility: return type 'List<Item>' is less accessible than method 'Store.All()'", "internal class Item\n{\n}\n", 1, 1, "public class Item")]
    [InlineData("csharp-read-only-property", "CS0200", "Property or indexer 'Person.Name' cannot be assigned to -- it is read only", "class Person\n{\n    public string Name { get; }\n}\n\nclass P { static void M(Person p) { p.Name = \"Ada\"; } }\n", 6, 37, "    public string Name { get; set; }")]
    [InlineData("csharp-static-through-instance", "CS0176", "Member 'Counter.Total' cannot be accessed with an instance reference; qualify it with a type name instead", "Console.WriteLine(c.Total);\n", 1, 19, "Console.WriteLine(Counter.Total);")]
    [InlineData("csharp-method-group", "CS0428", "Cannot convert method group 'ToString' to non-delegate type 'string'. Did you intend to invoke the method?", "string s = x.ToString;\n", 1, 12, "string s = x.ToString();")]
    [InlineData("csharp-redefinition", "CS0128", "A local variable or function named 'x' is already defined in this scope", "class P\n{\n    static void Main()\n    {\n        int x = 1;\n        int x = 2;\n    }\n}\n", 6, 13, "        x = 2;")]
    [InlineData("csharp-operand-types", "CS0019", "Operator '==' cannot be applied to operands of type 'char' and 'string'", "if (word[0] == \"a\") { }\n", 1, 5, "if (word[0] == 'a') { }")]
    [InlineData("csharp-operand-types", "CS0019", "Operator '-' cannot be applied to operands of type 'string' and 'int'", "int b = a - 1;\n", 1, 9, "int b = int.Parse(a) - 1;")]
    [InlineData("csharp-operand-types", "CS0019", "Operator '-' cannot be applied to operands of type 'string' and 'int'", "int b = \"10\" - 1;\n", 1, 9, "int b = 10 - 1;")]
    [InlineData("csharp-operand-types", "CS0019", "Operator '>' cannot be applied to operands of type 'T' and 'T'", "return a > b ? a : b;\n", 1, 8, null)]
    [InlineData("csharp-argument-conversion", "CS1503", "Argument 1: cannot convert from 'string' to 'int'", "Console.WriteLine(Square(input));\n", 1, 26, "Console.WriteLine(Square(int.Parse(input)));")]
    [InlineData("csharp-argument-conversion", "CS1503", "Argument 1: cannot convert from 'int' to 'string'", "Greet(age);\n", 1, 7, "Greet(age.ToString());")]
    [InlineData("csharp-collection-conversion", "CS0266", "Cannot implicitly convert type 'System.Collections.Generic.IEnumerable<int>' to 'System.Collections.Generic.List<int>'. An explicit conversion exists (are you missing a cast?)", "List<int> evens = numbers.Where(n => n % 2 == 0);\n", 1, 19, "List<int> evens = numbers.Where(n => n % 2 == 0).ToList();")]
    [InlineData("csharp-collection-conversion", "CS0029", "Cannot implicitly convert type 'System.Collections.Generic.List<int>' to 'int[]'", "int[] values = numbers;\n", 1, 16, "int[] values = numbers.ToArray();")]
    [InlineData("csharp-collection-conversion", "CS0029", "Cannot implicitly convert type 'System.Collections.Generic.List<string>' to 'int[]'", "int[] values = names;\n", 1, 16, null)]
    [InlineData("csharp-switch-fall-through", "CS0163", "Control cannot fall through from one case label ('case 1:') to another", "switch (day)\n{\n    case 1:\n        Console.WriteLine(\"Monday\");\n    case 2:\n        break;\n}\n", 3, 5, "        break;")]
    [InlineData("csharp-catch-order", "CS0160", "A previous catch clause already catches all exceptions of this or of a super type ('Exception')", "try\n{\n    f();\n}\ncatch (Exception)\n{\n    a();\n}\ncatch (FormatException)\n{\n    b();\n}\n", 9, 8, "catch (FormatException)|{|    b();|}|catch (Exception)|{|    a();|}")]
    [InlineData("csharp-if-semicolon", "CS8641", "'else' cannot start a statement.", "if (x > 3);\n{\n    a();\n}\nelse\n{\n    b();\n}\n", 5, 1, "if (x > 3)")]
    [InlineData("csharp-if-semicolon", "CS8641", "'else' cannot start a statement.", "if (x > 3);\n{\n    a();\n}\nelse\n{\n    b();\n}\n", 4, 2, "if (x > 3)")]
    public void EachConstructGetsItsFixOrARefusal(string rule, string code, string message, string source, int line, int column, string? expected)
    {
        var file = Write("Program.cs", source);

        Assert.Equal(expected, Propose(rule, Roslyn(code, message, file, line, column)));
    }

    [Theory]
    [InlineData("foreach (var n in numbers)\n{\n    if (n % 2 == 0) numbers.Remove(n);\n}\n", "numbers.RemoveAll(n => n % 2 == 0);")]
    [InlineData("foreach (var n in numbers)\n{\n    Console.WriteLine(n);\n    numbers.Remove(n);\n}\n", null)]
    public void RemovingInsideAForeachBecomesRemoveAll(string source, string? expected)
    {
        var file = Write("Program.cs", source);

        var error = new ParsedError
        {
            LanguageId = "csharp",
            Confidence = 90,
            RawText = "Unhandled exception. System.InvalidOperationException: Collection was modified; enumeration operation may not execute.",
            FirstLineSequence = 0,
            ExceptionType = "System.InvalidOperationException",
            Message = "Collection was modified; enumeration operation may not execute.",
            Frames = [new ErrorFrame { Order = 0, File = file, Line = 1, RawLine = "" }],
        };

        Assert.Equal(expected, Propose("csharp-remove-in-foreach", error));
    }

    // ------------------------------------------------------------------ live, through the .NET SDK

    private static readonly Lazy<bool> RunsSingleFiles = new(() =>
    {
        if (TargetFactory.FindOnPath("dotnet") is not { } dotnet) return false;

        try
        {
            using var process = Process.Start(new ProcessStartInfo(dotnet, "--version")
            {
                UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true, WorkingDirectory = Path.GetTempPath(),
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

    private static string Program(string body, string types = "") =>
        "using System;\nusing System.Collections.Generic;\nusing System.Linq;\n\n" + types +
        "class Program\n{\n    static void Main()\n    {\n" + body + "    }\n}\n";

    /// <summary>The construct, the program, the answer's id, and text the copied fix must contain.</summary>
    public static TheoryData<string, string, string, string> Constructs => new()
    {
        { "interface member not public", Program("        IShape s = new Circle();\n        Console.WriteLine(s.Area());\n", "interface IShape\n{\n    double Area();\n}\n\nclass Circle : IShape\n{\n    double Area() { return 3.14; }\n}\n\n"), "local:csharp-interface-member-public", "public double Area() { return 3.14; }" },
        { "override of a method that is not virtual", Program("        Animal a = new Dog();\n        Console.WriteLine(a.Speak());\n", "class Animal\n{\n    public string Speak() { return \"...\"; }\n}\n\nclass Dog : Animal\n{\n    public override string Speak() { return \"Woof\"; }\n}\n\n"), "local:csharp-virtual-base", "public virtual string Speak()" },
        { "override typo", Program("        Console.WriteLine(new Point());\n", "class Point\n{\n    public int X = 1;\n\n    public override string ToStrin() { return \"Point \" + X; }\n}\n\n"), "local:csharp-override-typo", "public override string ToString()" },
        { "read-only property assigned", Program("        var p = new Person();\n        p.Name = \"Ada\";\n        Console.WriteLine(p.Name);\n", "class Person\n{\n    public string Name { get; }\n}\n\n"), "local:csharp-read-only-property", "public string Name { get; set; }" },
        { "char compared with a string", Program("        string word = \"apple\";\n        if (word[0] == \"a\")\n        {\n            Console.WriteLine(\"starts with a\");\n        }\n"), "local:csharp-operand-types", "if (word[0] == 'a')" },
        { "string minus a number", Program("        string a = \"10\";\n        int b = a - 1;\n        Console.WriteLine(b);\n"), "local:csharp-operand-types", "int b = int.Parse(a) - 1;" },
        { "argument of the wrong type", "using System;\n\nclass Program\n{\n    static int Square(int x) { return x * x; }\n\n    static void Main()\n    {\n        string input = \"4\";\n        Console.WriteLine(Square(input));\n    }\n}\n", "local:csharp-argument-conversion", "Square(int.Parse(input))" },
        { "Where assigned to a List", Program("        var numbers = new List<int> { 1, 2, 3, 4 };\n        List<int> evens = numbers.Where(n => n % 2 == 0);\n        Console.WriteLine(evens.Count);\n"), "local:csharp-collection-conversion", "numbers.Where(n => n % 2 == 0).ToList();" },
        { "List assigned to an array", Program("        var numbers = new List<int> { 1, 2, 3 };\n        int[] values = numbers;\n        Console.WriteLine(values.Length);\n"), "local:csharp-collection-conversion", "int[] values = numbers.ToArray();" },
        { "static through an instance", Program("        var c = new Counter();\n        Console.WriteLine(c.Total);\n", "class Counter\n{\n    public static int Total = 0;\n}\n\n"), "local:csharp-static-through-instance", "Console.WriteLine(Counter.Total);" },
        { "inconsistent accessibility", Program("        new Store().Add(new Item());\n", "class Item\n{\n    public string Name = \"pen\";\n}\n\npublic class Store\n{\n    public void Add(Item item) { Console.WriteLine(item.Name); }\n}\n\n"), "local:csharp-inconsistent-accessibility", "public class Item" },
        { "method without brackets", Program("        int x = 5;\n        string s = x.ToString;\n        Console.WriteLine(s);\n"), "local:csharp-method-group", "string s = x.ToString();" },
        { "declared twice", Program("        int x = 1;\n        int x = 2;\n        Console.WriteLine(x);\n"), "local:csharp-redefinition", "        x = 2;" },
        { "catch order", Program("        try\n        {\n            int.Parse(\"x\");\n        }\n        catch (Exception)\n        {\n            Console.WriteLine(\"something went wrong\");\n        }\n        catch (FormatException)\n        {\n            Console.WriteLine(\"not a number\");\n        }\n"), "local:csharp-catch-order", "catch (FormatException)" },
        { "void on a constructor", Program("        Console.WriteLine(new Dog(\"Rex\").Name);\n", "class Dog\n{\n    public string Name;\n\n    public void Dog(string name)\n    {\n        Name = name;\n    }\n}\n\n"), "local:csharp-constructor-return-type", "public Dog(string name)" },
        { "if semicolon else", Program("        int x = 5;\n        if (x > 3);\n        {\n            Console.WriteLine(\"big\");\n        }\n        else\n        {\n            Console.WriteLine(\"small\");\n        }\n"), "local:csharp-if-semicolon", "if (x > 3)\n" },
        { "switch fall-through", Program("        int day = 1;\n        switch (day)\n        {\n            case 1:\n                Console.WriteLine(\"Monday\");\n            case 2:\n                Console.WriteLine(\"Tuesday\");\n                break;\n        }\n"), "local:csharp-switch-fall-through", "Console.WriteLine(\"Monday\");\n                break;" },
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
        Assert.Contains(expected, copied!.Text.ReplaceLineEndings("\n") + "\n", StringComparison.Ordinal);
    }
}
