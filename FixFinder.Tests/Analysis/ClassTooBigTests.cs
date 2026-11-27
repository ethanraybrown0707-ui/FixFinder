using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Checking;

namespace FixFinder.Tests;

/// <summary>
/// A class too big: one of 250 methods or more is a suggestion on the line the class starts on, saying how many it has; one
/// of 249 is not. Each class but Go's has a constructor beside its methods, and the C++ and C# ones a destructor too, which
/// are not counted - so the class of 249 methods says nothing. Written in each language whose reader reads classes - Go's
/// methods are its type's, by their receiver - as its programs write one.
/// </summary>
public class ClassTooBigTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>The program read by its own language's reader; null when this computer has no Python, JDK or Go to read it with.</summary>
    private async Task<IrProgram?> ReadAsync(string fileName, string code)
    {
        var path = Path.Combine(_temp.Path, fileName);
        await File.WriteAllTextAsync(path, code);

        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".py" => PythonFrontend.FindInterpreter() is { } python ? await PythonFrontend.ReadAsync([path], python) : null,
            ".java" => JavaFrontend.FindTools() is { } tools ? await JavaFrontend.ReadAsync([path], tools.Javac, tools.Java) : null,
            ".go" => GoFrontend.FindGo(path) is { } go ? await GoFrontend.ReadAsync([path], go) : null,
            ".js" => await JavaScriptFrontend.ReadAsync([path]),
            ".cs" => await CSharpFrontend.ReadAsync([path]),
            _ => await CFrontend.ReadAsync([path]),
        };
    }

    /// <summary>A class named Inventory with a constructor and as many methods as asked for, as the language writes one, and the line it starts on.</summary>
    private static (string FileName, string Code, int ClassLine) InventoryOf(string language, int methods)
    {
        var numbers = Enumerable.Range(0, methods).Select(number => number.ToString("000")).ToList();
        return language switch
        {
            "Python" => ("inventory.py",
                "class Inventory:\n    def __init__(self):\n        self.count = 0\n\n" +
                string.Concat(numbers.Select(number => $"    def method_{number}(self):\n        return {int.Parse(number)}\n\n")) +
                "\nprint(Inventory().method_000())\n", 1),
            "Java" => ("Inventory.java",
                "public class Inventory {\n    public Inventory() {\n    }\n\n" +
                string.Concat(numbers.Select(number => $"    int method{number}() {{\n        return {int.Parse(number)};\n    }}\n\n")) + "}\n", 1),
            "C#" => ("Inventory.cs",
                "class Inventory\n{\n    public Inventory()\n    {\n    }\n\n    ~Inventory()\n    {\n    }\n\n" +
                string.Concat(numbers.Select(number => $"    int Method{number}()\n    {{\n        return {int.Parse(number)};\n    }}\n\n")) + "}\n", 1),
            "JavaScript" => ("inventory.js",
                "class Inventory {\n    constructor() {\n        this.count = 0;\n    }\n\n" +
                string.Concat(numbers.Select(number => $"    method{number}() {{\n        return {int.Parse(number)};\n    }}\n\n")) +
                "}\n\nconsole.log(new Inventory().method000());\n", 1),
            "C++" => ("inventory.cpp",
                "class Inventory {\npublic:\n    Inventory() {\n    }\n\n    ~Inventory() {\n    }\n\n" +
                string.Concat(numbers.Select(number => $"    int method{number}() {{\n        return {int.Parse(number)};\n    }}\n\n")) +
                "};\n\nint main() {\n    Inventory inventory;\n    return inventory.method000();\n}\n", 1),
            // Go has no constructors: its methods are the type's, by their receiver, and a function makes one.
            "Go" => ("inventory.go",
                "package main\n\nimport \"fmt\"\n\ntype Inventory struct {\n\tcount int\n}\n\nfunc NewInventory() *Inventory {\n\treturn &Inventory{}\n}\n\n" +
                string.Concat(numbers.Select(number => $"func (inventory *Inventory) Method{number}() int {{\n\treturn {int.Parse(number)}\n}}\n\n")) +
                "func main() {\n\tfmt.Println(NewInventory().Method000())\n}\n", 5),
            _ => throw new ArgumentException(language),
        };
    }

    [Theory]
    [InlineData("Python")]
    [InlineData("Java")]
    [InlineData("C#")]
    [InlineData("JavaScript")]
    [InlineData("C++")]
    [InlineData("Go")]
    public async Task AClassOf250MethodsIsTooBigAndOneOf249IsNot(string language)
    {
        var (fileName, tooBig, classLine) = InventoryOf(language, ClassTooBig.MostMethods);
        if (await ReadAsync(fileName, tooBig) is not { } big) return;

        var found = Assert.Single(ClassTooBig.Find(big, null));
        Assert.Equal(ClassTooBig.CheckId, found.CheckId);
        Assert.Equal(classLine, found.Span.Line);
        Assert.Equal(Severity.Suggestion, found.Severity);
        Assert.Equal(Confidence.Certain, found.Confidence);
        Assert.Equal("`Inventory` has 250 methods, and 250 or more make a class too big: a class that size is usually doing the work of several, " +
                     "and each part of it is harder to find, change and test on its own", found.Message);

        var (_, smallEnough, _) = InventoryOf(language, ClassTooBig.MostMethods - 1);
        if (await ReadAsync(fileName, smallEnough) is not { } fewer) return;
        Assert.Empty(ClassTooBig.Find(fewer, null));
    }

    /// <summary>
    /// C# declares a property's accessors and an operator apart from its methods, so neither is counted as one: its reader
    /// reads a get, a set and an operator + beside a class's 249 methods, and the class is said to have 249 - too few to be
    /// said - while with 250 methods beside them it is said to have 250.
    /// </summary>
    [Fact]
    public async Task ACSharpPropertysAccessorsAndAnOperatorAreNotCountedAsMethods()
    {
        static string InventoryWith(int methods) =>
            "class Inventory\n{\n    private int _count;\n\n" +
            "    public int Count\n    {\n        get { return _count; }\n        set { _count = value; }\n    }\n\n" +
            "    public static Inventory operator +(Inventory left, Inventory right)\n    {\n        return left;\n    }\n\n" +
            string.Concat(Enumerable.Range(0, methods).Select(number => $"    int Method{number:000}()\n    {{\n        return {number};\n    }}\n\n")) + "}\n";

        if (await ReadAsync("Inventory.cs", InventoryWith(ClassTooBig.MostMethods - 1)) is not { } fewer) return;
        var type = Assert.Single(fewer.Classes);
        Assert.Equal(ClassTooBig.MostMethods - 1 + 3, type.Methods.Count);
        Assert.Equal(ClassTooBig.MostMethods - 1, ClassTooBig.MethodsOf(type));
        Assert.Empty(ClassTooBig.Find(fewer, null));

        if (await ReadAsync("Inventory.cs", InventoryWith(ClassTooBig.MostMethods)) is not { } big) return;
        Assert.StartsWith("`Inventory` has 250 methods,", Assert.Single(ClassTooBig.Find(big, null)).Message);
    }

    /// <summary>A class in another file than the one checked is that file's to say.</summary>
    [Fact]
    public async Task OnlyAClassInTheFileCheckedIsSaid()
    {
        var (fileName, code, _) = InventoryOf("JavaScript", ClassTooBig.MostMethods);
        if (await ReadAsync(fileName, code) is not { } program) return;

        Assert.Single(ClassTooBig.Find(program, Path.Combine(_temp.Path, fileName)));
        Assert.Empty(ClassTooBig.Find(program, Path.Combine(_temp.Path, "main.js")));
    }
}
