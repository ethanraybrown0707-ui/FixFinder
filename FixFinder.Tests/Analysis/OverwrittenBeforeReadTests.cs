using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;

namespace FixFinder.Tests;

/// <summary>
/// A value worked out and then lost: area gives result width * height, then width * width, before anything reads it - found
/// in Python, Java, C#, JavaScript, Go, C and C++. Left alone in each: a starting value given first, a value read before the
/// next, one given another only sometimes, one a handler for an error reads, and one a lambda uses.
/// </summary>
public class OverwrittenBeforeReadTests : IDisposable
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
                var className = code.Contains("class Left", StringComparison.Ordinal) ? "Left.java" : "Area.java";
                return AbstractChecks.Run(await JavaFrontend.ReadAsync([Write(className, code)], tools.Javac, tools.Java), new SourceText());

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
                return AbstractChecks.Run(await CSharpFrontend.ReadAsync([Write("Sample.cs", code)]), new SourceText());
        }
    }

    private string Write(string name, string code)
    {
        var path = Path.Combine(_temp.Path, name);
        File.WriteAllText(path, code);
        return path;
    }

    /// <summary>The language, the program, the line whose value is lost, and what the check said of it here.</summary>
    public static IEnumerable<object[]> Lost =>
    [
        new object[] { "Python", "def area(width, height):\n    result = width * height\n    result = width * width\n    return result\n\n\nprint(area(3, 4))\n", 2, "`result` is given `width * height` on line 2, then given another value on line 3 before anything reads it, so the value from line 2 is never used" },
        new object[] { "Java", "public class Area {\n    static int area(int width, int height) {\n        int result = width * height;\n        result = width * width;\n        return result;\n    }\n\n    public static void main(String[] args) {\n        System.out.println(area(3, 4));\n    }\n}\n", 3, "`result` is given `width * height` on line 3, then given another value on line 4 before anything reads it, so the value from line 3 is never used" },
        new object[] { "C#", "using System;\n\npublic static class Area\n{\n    static int Measure(int width, int height)\n    {\n        var result = width * height;\n        result = width * width;\n        return result;\n    }\n\n    public static void Main()\n    {\n        Console.WriteLine(Measure(3, 4));\n    }\n}\n", 7, "`result` is given `width * height` on line 7, then given another value on line 8 before anything reads it, so the value from line 7 is never used" },
        new object[] { "JavaScript", "function area(width, height) {\n  let result = width * height;\n  result = width * width;\n  return result;\n}\n\nconsole.log(area(3, 4));\n", 2, "`result` is given `width * height` on line 2, then given another value on line 3 before anything reads it, so the value from line 2 is never used" },
        new object[] { "Go", "package main\n\nimport \"fmt\"\n\nfunc area(width int, height int) int {\n\tresult := width * height\n\tresult = width * width\n\treturn result\n}\n\nfunc main() {\n\tfmt.Println(area(3, 4))\n}\n", 6, "`result` is given `width * height` on line 6, then given another value on line 7 before anything reads it, so the value from line 6 is never used" },
        new object[] { "C", "#include <stdio.h>\n\nint area(int width, int height) {\n    int result = width * height;\n    result = width * width;\n    return result;\n}\n\nint main(void) {\n    printf(\"%d\\n\", area(3, 4));\n    return 0;\n}\n", 4, "`result` is given `width * height` on line 4, then given another value on line 5 before anything reads it, so the value from line 4 is never used" },
        new object[] { "C++", "#include <cstdio>\n\nint area(int width, int height) {\n    int result = width * height;\n    result = width * width;\n    return result;\n}\n\nint main(void) {\n    printf(\"%d\\n\", area(3, 4));\n    return 0;\n}\n", 4, "`result` is given `width * height` on line 4, then given another value on line 5 before anything reads it, so the value from line 4 is never used" },
    ];

    /// <summary>The language, and a program of near misses the check must leave alone.</summary>
    public static IEnumerable<object[]> NearMisses =>
    [
        new object[] { "Python", "def total_of(values):\n    total = 0\n    total = sum(values)\n    return total\n\n\ndef area(width, height):\n    result = width * height\n    print(result)\n    result = width * width\n    return result\n\n\ndef pick(values, wanted):\n    choice = values[0]\n    if wanted in values:\n        choice = wanted\n    return choice\n\n\ndef parse(text):\n    value = len(text)\n    try:\n        value = int(text)\n    except ValueError:\n        print(\"not a number, its length is\", value)\n    return value\n\n\ndef scaled(values, factor):\n    size = len(values)\n    times = lambda each: each * factor * size\n    size = 0\n    return [times(each) for each in values]\n\n\nfor _ in range(2):\n    print(total_of([1, 2]), area(3, 4), pick([1, 2], 2), parse(\"x\"), scaled([1], 2))\n" },
        new object[] { "Java", "import java.util.List;\n\npublic class Left {\n    static int totalOf(List<Integer> values) {\n        int total = 0;\n        total = values.stream().mapToInt(Integer::intValue).sum();\n        return total;\n    }\n\n    static int area(int width, int height) {\n        int result = width * height;\n        System.out.println(result);\n        result = width * width;\n        return result;\n    }\n\n    static int pick(List<Integer> values, int wanted) {\n        int choice = values.get(0);\n        if (values.contains(wanted)) {\n            choice = wanted;\n        }\n        return choice;\n    }\n\n    static int parse(String text) {\n        int value = text.length();\n        try {\n            value = Integer.parseInt(text);\n        } catch (NumberFormatException error) {\n            System.out.println(\"not a number, its length is \" + value);\n        }\n        return value;\n    }\n\n    public static void main(String[] args) {\n        System.out.println(totalOf(List.of(1, 2)) + area(3, 4) + pick(List.of(1, 2), 2) + parse(\"x\"));\n    }\n}\n" },
        new object[] { "C#", "using System;\nusing System.Collections.Generic;\nusing System.Linq;\n\npublic static class Left\n{\n    static int TotalOf(List<int> values)\n    {\n        var total = 0;\n        total = values.Sum();\n        return total;\n    }\n\n    static int Area(int width, int height)\n    {\n        var result = width * height;\n        Console.WriteLine(result);\n        result = width * width;\n        return result;\n    }\n\n    static int Parse(string text)\n    {\n        var value = text.Length;\n        try\n        {\n            value = int.Parse(text);\n        }\n        catch (FormatException)\n        {\n            Console.WriteLine(\"not a number, its length is \" + value);\n        }\n        return value;\n    }\n\n    static List<int> Scaled(List<int> values, int factor)\n    {\n        var size = values.Count;\n        Func<int, int> times = each => each * factor * size;\n        size = 0;\n        return values.Select(times).ToList();\n    }\n\n    public static void Main()\n    {\n        Console.WriteLine(TotalOf(new List<int> { 1, 2 }) + Area(3, 4) + Parse(\"x\") + Scaled(new List<int> { 1 }, 2).Count);\n    }\n}\n" },
        new object[] { "JavaScript", "function totalOf(values) {\n  let total = 0;\n  total = values.reduce((sum, value) => sum + value, 0);\n  return total;\n}\n\nfunction area(width, height) {\n  let result = width * height;\n  console.log(result);\n  result = width * width;\n  return result;\n}\n\nfunction pick(values, wanted) {\n  let choice = values[0];\n  if (values.includes(wanted)) {\n    choice = wanted;\n  }\n  return choice;\n}\n\nfunction parse(text) {\n  let value = text.length;\n  try {\n    value = JSON.parse(text);\n  } catch (error) {\n    console.log(\"not JSON, its length is\", value);\n  }\n  return value;\n}\n\nfunction scaled(values, factor) {\n  let size = values.length;\n  const times = (each) => each * factor * size;\n  size = 0;\n  return values.map(times);\n}\n\nconsole.log(totalOf([1, 2]), area(3, 4), pick([1, 2], 2), parse(\"x\"), scaled([1], 2));\n" },
        new object[] { "C", "#include <stdio.h>\n\nint total_of(const int *values, int count) {\n    int total = 0;\n    for (int index = 0; index < count; index++) total += values[index];\n    return total;\n}\n\nint area(int width, int height) {\n    int result = width * height;\n    printf(\"%d\\n\", result);\n    result = width * width;\n    return result;\n}\n\nint pick(const int *values, int count, int wanted) {\n    int choice = values[0];\n    for (int index = 0; index < count; index++) {\n        if (values[index] == wanted) choice = wanted;\n    }\n    return choice;\n}\n\nint main(void) {\n    int values[] = {1, 2};\n    printf(\"%d %d %d\\n\", total_of(values, 2), area(3, 4), pick(values, 2, 2));\n    return 0;\n}\n" },
    ];

    [Theory]
    [MemberData(nameof(Lost))]
    public async Task AValueGivenAnotherBeforeAnythingReadsItIsFound(string language, string code, int line, string said)
    {
        if (await Analyse(language, code) is not { } findings) return;

        var lost = Assert.Single(findings, finding => finding.CheckId == OverwrittenBeforeRead.CheckId);
        Assert.Equal(line, lost.Span.Line);
        Assert.Equal(said, lost.Message);
    }

    [Theory]
    [MemberData(nameof(NearMisses))]
    public async Task AValueThatIsReadOrIsOnlyAStartIsLeftAlone(string language, string code)
    {
        if (await Analyse(language, code) is not { } findings) return;

        Assert.DoesNotContain(findings, finding => finding.CheckId == OverwrittenBeforeRead.CheckId);
    }
}
