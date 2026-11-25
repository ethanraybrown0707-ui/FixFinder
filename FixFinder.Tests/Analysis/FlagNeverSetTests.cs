using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;

namespace FixFinder.Tests;

/// <summary>
/// A flag a loop never sets: report sets found to false before a loop that looks for the wanted mark and never sets it, and
/// tests found after the loop - found in Python, Java, C#, JavaScript, Go, C and C++. Left alone in each: each found program
/// put right, a flag the loop sets, a switch like debug set off away from any loop, a count a loop adds to, and a flag set
/// through a nested function, a closure, a callback, a pointer or a C++ reference.
/// </summary>
public class FlagNeverSetTests : IDisposable
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

    /// <summary>The language, the program, the line testing the flag, and what the check said of it here.</summary>
    public static IEnumerable<object[]> NeverSet =>
    [
        new object[] { "C", "#include <stdio.h>\n\nconst char *report(const int *marks, int count, int wanted) {\n    int found = 0;\n    for (int index = 0; index < count; index++) {\n        if (marks[index] == wanted) {\n            printf(\"found it\\n\");\n        }\n    }\n    if (found) {\n        return \"present\";\n    }\n    return \"missing\";\n}\n\nint main(void) {\n    int marks[] = {1, 2, 3};\n    printf(\"%s\\n\", report(marks, 3, 2));\n    return 0;\n}\n", 10, "`found` is set to 0 on line 4 and nothing changes it - the loop on line 5 never sets it - so the test of `found` on line 10 is never true" },
        new object[] { "C++", "#include <iostream>\n#include <string>\n#include <vector>\n\nstd::string report(const std::vector<int>& marks, int wanted) {\n    bool found = false;\n    for (int mark : marks) {\n        if (mark == wanted) {\n            std::cout << \"found it\\n\";\n        }\n    }\n    if (found) {\n        return \"present\";\n    }\n    return \"missing\";\n}\n\nint main() {\n    std::cout << report({1, 2, 3}, 2) << \"\\n\";\n}\n", 12, "`found` is set to false on line 6 and nothing changes it - the loop on line 7 never sets it - so the test of `found` on line 12 is never true" },
        new object[] { "C#", "using System;\n\nConsole.WriteLine(Report(new[] { 1, 2, 3 }, 2));\n\nstatic string Report(int[] marks, int wanted)\n{\n    bool found = false;\n    foreach (var mark in marks)\n    {\n        if (mark == wanted)\n        {\n            Console.WriteLine(\"found it\");\n        }\n    }\n    if (found)\n    {\n        return \"present\";\n    }\n    return \"missing\";\n}\n", 15, "`found` is set to false on line 7 and nothing changes it - the loop on line 8 never sets it - so the test of `found` on line 15 is never true" },
        new object[] { "Go", "package main\n\nimport \"fmt\"\n\nfunc report(marks []int, wanted int) string {\n\tfound := false\n\tfor _, mark := range marks {\n\t\tif mark == wanted {\n\t\t\tfmt.Println(\"found it\")\n\t\t}\n\t}\n\tif found {\n\t\treturn \"present\"\n\t}\n\treturn \"missing\"\n}\n\nfunc main() {\n\tfmt.Println(report([]int{1, 2, 3}, 2))\n}\n", 12, "`found` is set to false on line 6 and nothing changes it - the loop on line 7 never sets it - so the test of `found` on line 12 is never true" },
        new object[] { "Java", "public class Report {\n    static String report(int[] marks, int wanted) {\n        boolean found = false;\n        for (int mark : marks) {\n            if (mark == wanted) {\n                System.out.println(\"found it\");\n            }\n        }\n        if (found) {\n            return \"present\";\n        }\n        return \"missing\";\n    }\n\n    public static void main(String[] args) {\n        System.out.println(report(new int[] {1, 2, 3}, 2));\n    }\n}\n", 9, "`found` is set to false on line 3 and nothing changes it - the loop on line 4 never sets it - so the test of `found` on line 9 is never true" },
        new object[] { "JavaScript", "function report(marks, wanted) {\n  let found = false;\n  for (const mark of marks) {\n    if (mark === wanted) {\n      console.log(\"found it\");\n    }\n  }\n  if (found) {\n    return \"present\";\n  }\n  return \"missing\";\n}\n\nconsole.log(report([1, 2, 3], 2));\n", 8, "`found` is set to false on line 2 and nothing changes it - the loop on line 3 never sets it - so the test of `found` on line 8 is never true" },
        new object[] { "Python", "def report(marks, wanted):\n    found = False\n    for mark in marks:\n        if mark == wanted:\n            print(\"found it\")\n    if found:\n        return \"present\"\n    return \"missing\"\n\n\nprint(report([1, 2, 3], 2))\n", 6, "`found` is set to false on line 2 and nothing changes it - the loop on line 3 never sets it - so the test of `found` on line 6 is never true" },
    ];

    /// <summary>The language, and a program the check must leave alone: each found program put right, and the near misses.</summary>
    public static IEnumerable<object[]> NearMisses =>
    [
        new object[] { "C", "#include <stdio.h>\n\nconst char *report(const int *marks, int count, int wanted) {\n    int found = 0;\n    for (int index = 0; index < count; index++) {\n        if (marks[index] == wanted) {\n            found = 1;\n        }\n    }\n    if (found) {\n        return \"present\";\n    }\n    return \"missing\";\n}\n\nint main(void) {\n    int marks[] = {1, 2, 3};\n    printf(\"%s\\n\", report(marks, 3, 2));\n    return 0;\n}\n" },
        new object[] { "C++", "#include <iostream>\n#include <string>\n#include <vector>\n\nstd::string report(const std::vector<int>& marks, int wanted) {\n    bool found = false;\n    for (int mark : marks) {\n        if (mark == wanted) {\n            found = true;\n        }\n    }\n    if (found) {\n        return \"present\";\n    }\n    return \"missing\";\n}\n\nint main() {\n    std::cout << report({1, 2, 3}, 2) << \"\\n\";\n}\n" },
        new object[] { "C#", "using System;\n\nConsole.WriteLine(Report(new[] { 1, 2, 3 }, 2));\n\nstatic string Report(int[] marks, int wanted)\n{\n    bool found = false;\n    foreach (var mark in marks)\n    {\n        if (mark == wanted)\n        {\n            found = true;\n        }\n    }\n    if (found)\n    {\n        return \"present\";\n    }\n    return \"missing\";\n}\n" },
        new object[] { "Go", "package main\n\nimport \"fmt\"\n\nfunc report(marks []int, wanted int) string {\n\tfound := false\n\tfor _, mark := range marks {\n\t\tif mark == wanted {\n\t\t\tfound = true\n\t\t}\n\t}\n\tif found {\n\t\treturn \"present\"\n\t}\n\treturn \"missing\"\n}\n\nfunc main() {\n\tfmt.Println(report([]int{1, 2, 3}, 2))\n}\n" },
        new object[] { "Java", "public class Report {\n    static String report(int[] marks, int wanted) {\n        boolean found = false;\n        for (int mark : marks) {\n            if (mark == wanted) {\n                found = true;\n            }\n        }\n        if (found) {\n            return \"present\";\n        }\n        return \"missing\";\n    }\n\n    public static void main(String[] args) {\n        System.out.println(report(new int[] {1, 2, 3}, 2));\n    }\n}\n" },
        new object[] { "JavaScript", "function report(marks, wanted) {\n  let found = false;\n  for (const mark of marks) {\n    if (mark === wanted) {\n      found = true;\n    }\n  }\n  if (found) {\n    return \"present\";\n  }\n  return \"missing\";\n}\n\nconsole.log(report([1, 2, 3], 2));\n" },
        new object[] { "Python", "def report(marks, wanted):\n    found = False\n    for mark in marks:\n        if mark == wanted:\n            found = True\n    if found:\n        return \"present\"\n    return \"missing\"\n\n\nprint(report([1, 2, 3], 2))\n" },
        new object[] { "C", "#include <stdio.h>\n\nstatic void mark_found(int *flag) {\n    *flag = 1;\n}\n\nconst char *has_negative(const int *marks, int count) {\n    int negative = 0;\n    for (int index = 0; index < count; index++) {\n        if (marks[index] < 0) {\n            negative = 1;\n        }\n    }\n    if (negative) {\n        return \"a negative mark\";\n    }\n    return \"all at least 0\";\n}\n\nconst char *through_pointer(const int *marks, int count) {\n    int seen = 0;\n    for (int index = 0; index < count; index++) {\n        if (marks[index] > 0) {\n            mark_found(&seen);\n        }\n    }\n    if (seen) {\n        return \"seen\";\n    }\n    return \"not seen\";\n}\n\nint main(void) {\n    int marks[] = {1, -2};\n    printf(\"%s %s\\n\", has_negative(marks, 2), through_pointer(marks, 2));\n    return 0;\n}\n" },
        new object[] { "C++", "#include <iostream>\n#include <string>\n#include <vector>\n\nstatic void markFound(bool& flag) {\n    flag = true;\n}\n\nstd::string throughReference(const std::vector<int>& marks) {\n    bool seen = false;\n    for (int mark : marks) {\n        if (mark > 0) {\n            markFound(seen);\n        }\n    }\n    if (seen) {\n        return \"seen\";\n    }\n    return \"not seen\";\n}\n\nstd::string hasNegative(const std::vector<int>& marks) {\n    bool negative = false;\n    for (int mark : marks) {\n        if (mark < 0) {\n            negative = true;\n        }\n    }\n    if (negative) {\n        return \"a negative mark\";\n    }\n    return \"all at least 0\";\n}\n\nint main() {\n    std::cout << throughReference({1}) << \" \" << hasNegative({1, -2}) << \"\\n\";\n}\n" },
        new object[] { "C#", "using System;\nusing System.Collections.Generic;\n\nConsole.WriteLine($\"{HasNegative(new[] { 1, -2 })} {Lookup(new Dictionary<string, int> { [\"a\"] = 1 }, new[] { \"a\" })}\");\n\nstatic string HasNegative(int[] marks)\n{\n    bool negative = false;\n    foreach (var mark in marks)\n    {\n        if (mark < 0)\n        {\n            negative = true;\n        }\n    }\n    if (negative)\n    {\n        return \"a negative mark\";\n    }\n    return \"all at least 0\";\n}\n\nstatic string Lookup(Dictionary<string, int> counts, string[] names)\n{\n    bool known = false;\n    foreach (var name in names)\n    {\n        if (name.Length > 0)\n        {\n            known = counts.TryGetValue(name, out _) || known;\n        }\n    }\n    if (known)\n    {\n        return \"known\";\n    }\n    return \"unknown\";\n}\n" },
        new object[] { "Go", "package main\n\nimport \"fmt\"\n\nfunc hasNegative(marks []int) string {\n\tnegative := false\n\tfor _, mark := range marks {\n\t\tif mark < 0 {\n\t\t\tnegative = true\n\t\t}\n\t}\n\tif negative {\n\t\treturn \"a negative mark\"\n\t}\n\treturn \"all at least 0\"\n}\n\nfunc setThrough(marks []int) string {\n\tseen := false\n\tmark := func() { seen = true }\n\tfor _, value := range marks {\n\t\tif value > 0 {\n\t\t\tmark()\n\t\t}\n\t}\n\tif seen {\n\t\treturn \"seen\"\n\t}\n\treturn \"not seen\"\n}\n\nfunc main() {\n\tfmt.Println(hasNegative([]int{1, -2}), setThrough([]int{3}))\n}\n" },
        new object[] { "Java", "public class Near {\n    static int show(int[] marks) {\n        boolean debug = false;\n        if (debug) {\n            System.out.println(marks.length);\n        }\n        return marks.length;\n    }\n\n    static String hasNegative(int[] marks) {\n        boolean negative = false;\n        for (int mark : marks) {\n            if (mark < 0) {\n                negative = true;\n            }\n        }\n        if (negative) {\n            return \"a negative mark\";\n        }\n        return \"all at least 0\";\n    }\n\n    static int countAbove(int[] marks, int floor) {\n        int above = 0;\n        for (int mark : marks) {\n            if (mark > floor) {\n                above++;\n            }\n        }\n        if (above == 0) {\n            return -1;\n        }\n        return above;\n    }\n\n    public static void main(String[] args) {\n        System.out.println(show(new int[] {1}) + \" \" + hasNegative(new int[] {1, -2}) + \" \" + countAbove(new int[] {1, 5}, 2));\n    }\n}\n" },
        new object[] { "JavaScript", "function hasNegative(marks) {\n  let negative = false;\n  for (const mark of marks) {\n    if (mark < 0) {\n      negative = true;\n    }\n  }\n  if (negative) {\n    return \"a negative mark\";\n  }\n  return \"all at least 0\";\n}\n\nfunction seenByCallback(marks) {\n  let seen = false;\n  for (const mark of marks) {\n    if (mark > 0) {\n      [mark].forEach(() => { seen = true; });\n    }\n  }\n  if (seen) {\n    return \"seen\";\n  }\n  return \"not seen\";\n}\n\nconsole.log(hasNegative([1, -2]), seenByCallback([3]));\n" },
        new object[] { "Python", "def show(marks):\n    debug = False\n    if debug:\n        print(\"marks:\", marks)\n    return len(marks)\n\n\ndef has_negative(marks):\n    negative = False\n    for mark in marks:\n        if mark < 0:\n            negative = True\n    if negative:\n        return \"a negative mark\"\n    return \"all at least 0\"\n\n\ndef seen_by_helper(marks):\n    seen = False\n\n    def look():\n        nonlocal seen\n        seen = True\n\n    for mark in marks:\n        if mark > 0:\n            look()\n    if seen:\n        return \"seen\"\n    return \"not seen\"\n\n\ndef checked_first(marks):\n    ready = False\n    if ready:\n        return \"ready\"\n    for mark in marks:\n        if mark > 0:\n            print(mark)\n    return \"done\"\n\n\nprint(show([1]), has_negative([1, -2]), seen_by_helper([3]), checked_first([4]))\n" },
    ];

    [Theory]
    [MemberData(nameof(NeverSet))]
    public async Task AFlagNothingSetsIsFoundWhereItIsTested(string language, string code, int line, string said)
    {
        if (await Analyse(language, code) is not { } findings) return;

        var neverSet = Assert.Single(findings, finding => finding.CheckId == FlagNeverSet.CheckId);
        Assert.Equal(line, neverSet.Span.Line);
        Assert.Equal(said, neverSet.Message);
    }

    [Theory]
    [MemberData(nameof(NearMisses))]
    public async Task AFlagSomethingMaySetIsLeftAlone(string language, string code)
    {
        if (await Analyse(language, code) is not { } findings) return;

        Assert.DoesNotContain(findings, finding => finding.CheckId == FlagNeverSet.CheckId);
    }
}
