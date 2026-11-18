using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;

namespace FixFinder.Tests;

/// <summary>
/// A value passed in and never used: total price gives quantity 1 before anything reads the quantity it was passed - found in
/// Python, Java, C#, JavaScript, Go, C and C++. Left alone in each: each found program put right, a parameter read first, one
/// given another value only sometimes, a C# out or ref parameter and a C++ reference, a pointer's target given a value, one
/// a lambda keeps, and one a loop that may not run gives another.
/// </summary>
public class ParameterIgnoredTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<IReadOnlyList<AnalysisFinding>?> Analyse(string language, string code)
    {
        switch (language)
        {
            case "Python":
                if (PythonFrontend.FindInterpreter() is not { } python) return null;
                return AbstractChecks.Run(await PythonFrontend.ReadAsync([Write("sample.py", code)], python), new SourceText());

            case "Java":
                if (JavaFrontend.FindTools() is not { } tools) return null;
                var className = System.Text.RegularExpressions.Regex.Match(code, @"public class (\w+)").Groups[1].Value;
                return AbstractChecks.Run(await JavaFrontend.ReadAsync([Write($"{className}.java", code)], tools.Javac, tools.Java), new SourceText());

            case "JavaScript":
                return AbstractChecks.Run(await JavaScriptFrontend.ReadAsync([Write("main.js", code)]), new SourceText());

            case "Go":
                var file = Write("main.go", code);
                if (GoFrontend.FindGo(file) is not { } go) return null;
                return AbstractChecks.Run(await GoFrontend.ReadAsync([file], go), new SourceText());

            case "C":
                return AbstractChecks.Run(await CFrontend.ReadAsync([Write("main.c", code)]), new SourceText());

            case "C++":
                return AbstractChecks.Run(await CFrontend.ReadAsync([Write("main.cpp", code)]), new SourceText());

            default:
                return AbstractChecks.Run(await CSharpFrontend.ReadAsync([Write("Program.cs", code)]), new SourceText());
        }
    }

    private string Write(string name, string code)
    {
        var path = Path.Combine(_temp.Path, name);
        File.WriteAllText(path, code);
        return path;
    }

    /// <summary>The language, the program, the line giving the parameter its new value, and what the check said of it here.</summary>
    public static IEnumerable<object[]> Ignored =>
    [
        new object[] { "C", "#include <stdio.h>\n\nint total_price(int price, int quantity) {\n    quantity = 1;\n    return price * quantity;\n}\n\nint main(void) {\n    printf(\"%d\\n\", total_price(4, 3));\n    return 0;\n}\n", 4, "`quantity` is given `1` on line 4 before anything reads it, so the value the caller passes for `quantity` is never used" },
        new object[] { "C++", "#include <iostream>\n\nint totalPrice(int price, int quantity) {\n    quantity = 1;\n    return price * quantity;\n}\n\nint main() {\n    std::cout << totalPrice(4, 3) << \"\\n\";\n}\n", 4, "`quantity` is given `1` on line 4 before anything reads it, so the value the caller passes for `quantity` is never used" },
        new object[] { "C#", "using System;\n\nConsole.WriteLine(TotalPrice(4, 3));\n\nstatic int TotalPrice(int price, int quantity)\n{\n    quantity = 1;\n    return price * quantity;\n}\n", 7, "`quantity` is given `1` on line 7 before anything reads it, so the value the caller passes for `quantity` is never used" },
        new object[] { "Go", "package main\n\nimport \"fmt\"\n\nfunc totalPrice(price int, quantity int) int {\n\tquantity = 1\n\treturn price * quantity\n}\n\nfunc main() {\n\tfmt.Println(totalPrice(4, 3))\n}\n", 6, "`quantity` is given `1` on line 6 before anything reads it, so the value the caller passes for `quantity` is never used" },
        new object[] { "Java", "public class Price {\n    static int totalPrice(int price, int quantity) {\n        quantity = 1;\n        return price * quantity;\n    }\n\n    public static void main(String[] args) {\n        System.out.println(totalPrice(4, 3));\n    }\n}\n", 3, "`quantity` is given `1` on line 3 before anything reads it, so the value the caller passes for `quantity` is never used" },
        new object[] { "JavaScript", "function totalPrice(price, quantity) {\n  quantity = 1;\n  return price * quantity;\n}\n\nconsole.log(totalPrice(4, 3));\n", 2, "`quantity` is given `1` on line 2 before anything reads it, so the value the caller passes for `quantity` is never used" },
        new object[] { "Python", "def total_price(price, quantity):\n    quantity = 1\n    return price * quantity\n\n\nprint(total_price(4, 3))\n", 2, "`quantity` is given `1` on line 2 before anything reads it, so the value the caller passes for `quantity` is never used" },
    ];

    /// <summary>The language, and a program the check must leave alone: each found program put right, and the near misses.</summary>
    public static IEnumerable<object[]> NearMisses =>
    [
        new object[] { "C", "#include <stdio.h>\n\nint total_price(int price, int quantity) {\n    return price * quantity;\n}\n\nint main(void) {\n    printf(\"%d\\n\", total_price(4, 3));\n    return 0;\n}\n" },
        new object[] { "C++", "#include <iostream>\n\nint totalPrice(int price, int quantity) {\n    return price * quantity;\n}\n\nint main() {\n    std::cout << totalPrice(4, 3) << \"\\n\";\n}\n" },
        new object[] { "C#", "using System;\n\nConsole.WriteLine(TotalPrice(4, 3));\n\nstatic int TotalPrice(int price, int quantity)\n{\n    return price * quantity;\n}\n" },
        new object[] { "Go", "package main\n\nimport \"fmt\"\n\nfunc totalPrice(price int, quantity int) int {\n\treturn price * quantity\n}\n\nfunc main() {\n\tfmt.Println(totalPrice(4, 3))\n}\n" },
        new object[] { "Java", "public class Price {\n    static int totalPrice(int price, int quantity) {\n        return price * quantity;\n    }\n\n    public static void main(String[] args) {\n        System.out.println(totalPrice(4, 3));\n    }\n}\n" },
        new object[] { "JavaScript", "function totalPrice(price, quantity) {\n  return price * quantity;\n}\n\nconsole.log(totalPrice(4, 3));\n" },
        new object[] { "Python", "def total_price(price, quantity):\n    return price * quantity\n\n\nprint(total_price(4, 3))\n" },
        new object[] { "C", "#include <stdio.h>\n\nint at_least_one(int quantity) {\n    if (quantity < 1) {\n        quantity = 1;\n    }\n    return quantity;\n}\n\nvoid reset(int *count) {\n    *count = 0;\n}\n\nint main(void) {\n    int count = 5;\n    reset(&count);\n    printf(\"%d %d\\n\", at_least_one(0), count);\n    return 0;\n}\n" },
        new object[] { "C++", "#include <iostream>\n#include <string>\n\nint atLeastOne(int quantity) {\n    if (quantity < 1) {\n        quantity = 1;\n    }\n    return quantity;\n}\n\nvoid reset(int& count) {\n    count = 0;\n}\n\nstd::string cleaned(std::string name) {\n    name = name.substr(name.find_first_not_of(' '));\n    return name;\n}\n\nint main() {\n    int count = 5;\n    reset(count);\n    std::cout << atLeastOne(0) << \" \" << count << \" \" << cleaned(\"  ann\") << \"\\n\";\n}\n" },
        new object[] { "C#", "using System;\n\nConsole.WriteLine($\"{Cleaned(\"  ann \")} {AtLeastOne(0)} {(TryHalf(6, out var half) ? half : -1)} {Reset(ref Counter.Value)}\");\n\nstatic string Cleaned(string name)\n{\n    name = name.Trim();\n    return name.ToUpper();\n}\n\nstatic int AtLeastOne(int quantity)\n{\n    if (quantity < 1)\n    {\n        quantity = 1;\n    }\n    return quantity;\n}\n\nstatic bool TryHalf(int number, out int half)\n{\n    half = number / 2;\n    return number % 2 == 0;\n}\n\nstatic int Reset(ref int count)\n{\n    count = 0;\n    return count;\n}\n\nstatic class Counter\n{\n    public static int Value = 5;\n}\n" },
        new object[] { "Go", "package main\n\nimport (\n\t\"fmt\"\n\t\"strings\"\n)\n\nfunc cleaned(name string) string {\n\tname = strings.TrimSpace(name)\n\treturn strings.ToUpper(name)\n}\n\nfunc atLeastOne(quantity int) int {\n\tif quantity < 1 {\n\t\tquantity = 1\n\t}\n\treturn quantity\n}\n\nfunc later(step int) func() int {\n\treturn func() int { return step * 2 }\n}\n\nfunc main() {\n\tfmt.Println(cleaned(\"  ann \"), atLeastOne(0), later(3)())\n}\n" },
        new object[] { "Java", "import java.util.function.IntSupplier;\n\npublic class Near {\n    static String cleaned(String name) {\n        name = name.trim();\n        return name.toUpperCase();\n    }\n\n    static int atLeastOne(int quantity) {\n        if (quantity < 1) {\n            quantity = 1;\n        }\n        return quantity;\n    }\n\n    static int counted(int[] items, int total) {\n        for (int item : items) {\n            total = item;\n        }\n        return total;\n    }\n\n    public static void main(String[] args) {\n        System.out.println(cleaned(\"  ann \") + \" \" + atLeastOne(0) + \" \" + counted(new int[0], 5));\n    }\n}\n" },
        new object[] { "JavaScript", "function cleaned(name) {\n  name = name.trim();\n  return name.toUpperCase();\n}\n\nfunction atLeastOne(quantity) {\n  if (quantity < 1) {\n    quantity = 1;\n  }\n  return quantity;\n}\n\nfunction withDefault(options) {\n  options = options || {};\n  return options.size || 1;\n}\n\nfunction later(step) {\n  return () => step * 2;\n}\n\nconsole.log(cleaned(\"  ann \"), atLeastOne(0), withDefault(), later(3)());\n" },
        new object[] { "Python", "def cleaned(name):\n    name = name.strip()\n    return name.title()\n\n\ndef at_least_one(quantity):\n    if quantity < 1:\n        quantity = 1\n    return quantity\n\n\ndef later(step):\n    return lambda: step * 2\n\n\ndef counted(items, total):\n    for item in items:\n        total = item\n    return total\n\n\nprint(cleaned(\"  ann \"), at_least_one(0), later(3)(), counted([], 5))\n" },
    ];

    [Theory]
    [MemberData(nameof(Ignored))]
    public async Task AParameterGivenAValueBeforeAnythingReadsItIsFound(string language, string code, int line, string said)
    {
        if (await Analyse(language, code) is not { } findings) return;

        var ignored = Assert.Single(findings, finding => finding.CheckId == OverwrittenBeforeRead.ParameterCheckId);
        Assert.Equal(line, ignored.Span.Line);
        Assert.Equal(said, ignored.Message);
    }

    [Theory]
    [MemberData(nameof(NearMisses))]
    public async Task AParameterReadFirstOrGivenToTheCallerIsLeftAlone(string language, string code)
    {
        if (await Analyse(language, code) is not { } findings) return;

        Assert.DoesNotContain(findings, finding => finding.CheckId == OverwrittenBeforeRead.ParameterCheckId);
    }
}
