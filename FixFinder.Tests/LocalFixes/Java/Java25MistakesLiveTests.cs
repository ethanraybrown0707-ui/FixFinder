using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Java 25 programs with one syntax or logic mistake each - compact source files with void main() and IO, records, switch
/// expressions, patterns, var and text blocks - checked the way the window checks a file: built with javac, run, and the
/// change FixFinder offers built and run again. Each row is what JDK 25 (Eclipse's JustJ 25.0.4) gave when these were written:
/// the rule, the line, the lines the change replaces and writes, whether a build or run of the changed copy confirmed it,
/// and the program itself left as it was.
/// </summary>
/// <remarks>
/// A compact source file - methods with no class around them - needs Java 25, and FixFinder builds it with a JDK of 25 or
/// later wherever one is installed; it is skipped where none is. A program written as a class is built with the default JDK,
/// so it runs on CI's JDK 21, 25 and 27.
/// </remarks>
public class Java25MistakesLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>A compact source file whose main holds these lines.</summary>
    private static string Compact(string mainBody) => "void main() {\n" + mainBody + "}\n";

    /// <summary>Whether a program can be built here: a compact source file - one with no class - needs a JDK of Java 25.</summary>
    private static bool CanBuild(string source) =>
        LocalFixLiveTests.Available("java") && (source.Contains("class ", StringComparison.Ordinal) || Jdks.AtLeast(25) is not null);

    private async Task<(CheckReport Report, string Program)> CheckAsync(string fileName, string source, string? typed = null)
    {
        var program = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(program, source);

        var launch = TargetFactory.FromFile(program, LiveAllowance.For(program, TimeSpan.FromMinutes(3)));
        Assert.True(launch.Ok, launch.Problem);
        if (typed is not null) launch = launch with { Spec = launch.Spec!.WithInput(typed) };
        output.WriteLine($"how: {launch.Explanation}");

        using var http = new FixFinderHttpClient();
        var report = await new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Java }.CheckAsync(launch);

        output.WriteLine($"syntax: {report.SyntaxSummary}");
        foreach (var each in report.Findings)
            output.WriteLine($"[{each.RuleId}] {each.Line}: {each.Title} :: {each.Fix?.StartLine} {string.Join("|", each.Fix?.NewLines ?? [])} :: {each.Verified.Summary}");
        return (report, program);
    }

    /// <summary>What was wrong, the file, the program, the rule that fixes it, the line it is found on, where the change starts, how many lines it replaces, the lines it writes (split by |), and whether a build or run confirmed it.</summary>
    public static TheoryData<string, string, string, string, int, int, int, string, bool> Mistakes => new()
    {
        // Syntax and types in a compact source file.
        { "missing semicolon", "Main.java", Compact("    int total = 5\n    IO.println(total);\n"), "java-missing-semicolon", 2, 2, 1, "    int total = 5;", true },
        { "misspelt variable", "Main.java", Compact("    int total = 5;\n    IO.println(totl);\n"), "java-nearest-name", 3, 3, 1, "    IO.println(total);", true },
        { "string for String", "Main.java", Compact("    string name = \"Ada\";\n    IO.println(name);\n"), "java-nearest-name", 2, 2, 1, "    String name = \"Ada\";", true },
        { "IO.printn for IO.println", "Main.java", Compact("    IO.printn(\"hi\");\n"), "java-nearest-name", 2, 2, 1, "    IO.println(\"hi\");", true },
        { "io for IO", "Main.java", Compact("    io.println(\"hi\");\n"), "java-lowercase-class", 2, 2, 1, "    IO.println(\"hi\");", true },
        { "missing closing bracket", "Main.java", Compact("    IO.println(\"total: \" + (2 + 3);\n"), "java-missing-closing-parenthesis", 2, 2, 1, "    IO.println(\"total: \" + (2 + 3));", true },
        { "missing closing brace", "Main.java", "void main() {\n    IO.println(\"hi\");\n", "java-missing-closing-brace", 2, 3, 0, "}", true },
        { "variable used before it has a value", "Main.java", Compact("    int total;\n    for (int i = 0; i < 3; i++) {\n        total += i;\n    }\n    IO.println(total);\n"), "java-uninitialised", 4, 2, 1, "    int total = 0;", true },
        { "value worked out but not returned", "Main.java", "int twice(int x) {\n    int doubled = x * 2;\n}\n\nvoid main() {\n    IO.println(twice(4));\n}\n", "java-missing-return", 3, 3, 0, "    return doubled;", true },
        { "sum not returned after its loop", "Main.java", "int total(int[] marks) {\n    int sum = 0;\n    for (int mark : marks) {\n        sum += mark;\n    }\n}\n\nvoid main() {\n    IO.println(total(new int[] {60, 70}));\n}\n", "java-missing-return", 6, 6, 0, "    return sum;", true },
        { "args in a main that takes nothing", "Main.java", Compact("    IO.println(args.length);\n"), "java-main-args", 2, 1, 1, "void main(String[] args) {", true },
        { "elif", "Main.java", Compact("    int mark = 55;\n    if (mark >= 70) {\n        IO.println(\"First\");\n    } elif (mark >= 50) {\n        IO.println(\"Pass\");\n    }\n"), "java-elif", 5, 5, 1, "    } else if (mark >= 50) {", true },
        { "Python's print", "Main.java", Compact("    print(\"hi\");\n"), "java-foreign-word", 2, 2, 1, "    System.out.println(\"hi\");", true },
        { "var with no value", "Main.java", Compact("    var total;\n    total = 5;\n    IO.println(total);\n"), "java-var-without-value", 2, 2, 2, "    var total = 5;", true },
        { "text block on one line", "Main.java", Compact("    String poem = \"\"\"Roses are red\"\"\";\n    IO.println(poem);\n"), "java-one-line-text-block", 2, 2, 1, "    String poem = \"Roses are red\";", true },
        { "switch over text with no default", "Main.java", Compact("    String day = \"Sat\";\n    int hours = switch (day) {\n        case \"Mon\" -> 8;\n        case \"Sat\" -> 0;\n    };\n    IO.println(hours);\n"), "java-switch-default", 3, 6, 0, "        default -> throw new IllegalArgumentException(\"Unexpected value: \" + day);", true },

        // Crashes in a compact source file: the run stops with an exception, and a run of the changed copy does not.
        { "List loop past the end", "Main.java", Compact("    List<Integer> marks = List.of(60, 70, 80);\n    int total = 0;\n    for (int i = 0; i <= marks.size(); i++) {\n        total += marks.get(i);\n    }\n    IO.println(total);\n"), "java-off-by-one-loop", 5, 4, 1, "    for (int i = 0; i < marks.size(); i++) {", true },
        { "array loop past the end", "Main.java", Compact("    int[] marks = {60, 70, 80};\n    int total = 0;\n    for (int i = 0; i <= marks.length; i++) {\n        total += marks[i];\n    }\n    IO.println(total);\n"), "java-off-by-one-loop", 5, 4, 1, "    for (int i = 0; i < marks.length; i++) {", true },
        { "position from the other end of a String", "Main.java", Compact("    String word = \"level\";\n    boolean same = true;\n    for (int i = 0; i < word.length(); i++) {\n        if (word.charAt(i) != word.charAt(word.length() - i)) {\n            same = false;\n        }\n    }\n    IO.println(same);\n"), "java-mirror-index", 5, 5, 1, "        if (word.charAt(i) != word.charAt(word.length() - 1 - i)) {", true },
        { "position from the other end of an array", "Main.java", Compact("    int[] marks = {60, 70, 80};\n    for (int i = 0; i < marks.length; i++) {\n        IO.println(marks[marks.length - i]);\n    }\n"), "java-mirror-index", 4, 4, 1, "        IO.println(marks[marks.length - 1 - i]);", true },
        { "average of no marks", "Main.java", Compact("    int[] marks = {};\n    int total = 0;\n    for (int mark : marks) {\n        total += mark;\n    }\n    IO.println(total / marks.length);\n"), "java-division-guard", 7, 7, 1, "    IO.println((marks.length == 0 ? 0 : total / marks.length));", true },
        { "removed from the list being looped over", "Main.java", Compact("    List<Integer> marks = new ArrayList<>(List.of(40, 45, 70, 80));\n    for (Integer mark : marks) {\n        if (mark < 50) {\n            marks.remove(mark);\n        }\n    }\n    IO.println(marks);\n"), "java-remove-in-for-each", 3, 3, 5, "    marks.removeIf(mark -> mark < 50);", true },

        // Logic in a compact source file: it builds and runs, and the change is offered from the code or javac's warning.
        { "Strings compared with ==", "Main.java", Compact("    String answer = new String(\"yes\");\n    if (answer == \"yes\") {\n        IO.println(\"ok\");\n    }\n"), "logic-java-string-equals", 3, 3, 1, "    if (answer.equals(\"yes\")) {", false },
        { "average worked out in whole numbers", "Main.java", Compact("    int total = 7;\n    int count = 2;\n    double average = total / count;\n    IO.println(average);\n"), "logic-integer-division", 4, 4, 1, "    double average = (double) total / count;", false },
        { "largest started at 0", "Main.java", Compact("    int[] temperatures = {-5, -2, -8};\n    int highest = 0;\n    for (int t : temperatures) {\n        if (t > highest) {\n            highest = t;\n        }\n    }\n    IO.println(highest);\n"), "logic-java-best-starts-at-zero", 3, 3, 1, "    int highest = temperatures[0];", false },
        { "semicolon after an if", "Main.java", Compact("    int mark = 30;\n    if (mark >= 40);\n    {\n        IO.println(\"pass\");\n    }\n"), "logic-empty-if-body", 3, 3, 1, "    if (mark >= 40)", true },
        { "semicolon after a for", "Main.java", Compact("    int total = 0;\n    for (int i = 0; i < 3; i++);\n    {\n        total += 1;\n    }\n    IO.println(total);\n"), "logic-empty-loop-body", 3, 3, 1, "    for (int i = 0; i < 3; i++)", false },
        { "count = count++", "Main.java", Compact("    int count = 0;\n    for (int i = 0; i < 5; i++) {\n        count = count++;\n    }\n    IO.println(count);\n"), "logic-lost-increment", 4, 4, 1, "        count++;", false },
        { "decimals compared exactly", "Main.java", Compact("    double total = 0.1 + 0.2;\n    if (total == 0.3) {\n        IO.println(\"equal\");\n    }\n"), "logic-float-equality", 3, 3, 1, "    if (Math.abs(total - 0.3) < 1e-9) {", false },
        { "case with no break", "Main.java", Compact("    int day = 1;\n    switch (day) {\n        case 1:\n            IO.println(\"Monday\");\n        case 2:\n            IO.println(\"Tuesday\");\n            break;\n    }\n"), "java-fallthrough-break", 6, 6, 0, "            break;", true },
        { "Integers compared with ==", "Main.java", Compact("    Integer a = 1000;\n    Integer b = 1000;\n    if (a == b) {\n        IO.println(\"same\");\n    }\n"), "logic-java-wrapper-equality", 4, 4, 1, "    if (a.equals(b)) {", false },
        { "toUpperCase's result dropped", "Main.java", Compact("    String name = \"ada\";\n    name.toUpperCase();\n    IO.println(name);\n"), "logic-result-discarded", 3, 3, 1, "    name = name.toUpperCase();", false },
        { "records compared with !=", "Main.java", "record Point(int x, int y) {}\n\nvoid main() {\n    var a = new Point(1, 2);\n    var b = new Point(1, 2);\n    if (a != b) {\n        IO.println(\"different\");\n    }\n}\n", "logic-java-record-equality", 6, 6, 1, "    if (!a.equals(b)) {", false },

        // Records, switch expressions and patterns in a class.
        { "record component read as a field", "App.java", "record Point(int x, int y) {}\n\npublic class App {\n    public static void main(String[] args) {\n        Point p = new Point(3, 4);\n        System.out.println(p.x);\n    }\n}\n", "java-record-accessor", 6, 6, 1, "        System.out.println(p.x());", true },
        { "switch over a number with no default", "App.java", "public class App {\n    public static void main(String[] args) {\n        int month = 2;\n        int days = switch (month) {\n            case 1 -> 31;\n            case 2 -> 28;\n        };\n        System.out.println(days);\n    }\n}\n", "java-switch-default", 4, 7, 0, "            default -> throw new IllegalArgumentException(\"Unexpected value: \" + month);", true },
        { "switch over an enum missing a constant", "App.java", "public class App {\n    enum Size { SMALL, MEDIUM, LARGE }\n\n    public static void main(String[] args) {\n        Size size = Size.SMALL;\n        int price = switch (size) {\n            case SMALL -> 2;\n            case MEDIUM -> 3;\n        };\n        System.out.println(price);\n    }\n}\n", "java-switch-default", 6, 9, 0, "            default -> throw new IllegalArgumentException(\"Unexpected value: \" + size);", true },
        { "case : among case ->", "App.java", "public class App {\n    public static void main(String[] args) {\n        int day = 1;\n        switch (day) {\n            case 1 -> System.out.println(\"Monday\");\n            case 2: System.out.println(\"Tuesday\");\n        }\n    }\n}\n", "java-case-kinds", 6, 6, 1, "            case 2 -> System.out.println(\"Tuesday\");", true },
        { "case : yield among case ->", "App.java", "public class App {\n    public static void main(String[] args) {\n        int month = 2;\n        int days = switch (month) {\n            case 1 -> 31;\n            case 2: yield 28;\n            default -> 30;\n        };\n        System.out.println(days);\n    }\n}\n", "java-case-kinds", 6, 6, 1, "            case 2 -> 28;", true },
        { "case block with no yield", "App.java", "public class App {\n    public static void main(String[] args) {\n        int month = 2;\n        int days = switch (month) {\n            case 2 -> {\n                int leap = 0;\n                28 + leap;\n            }\n            default -> 31;\n        };\n        System.out.println(days);\n    }\n}\n", "java-missing-yield", 7, 7, 1, "                yield 28 + leap;", true },
        { "case Object above case String", "App.java", "public class App {\n    static String describe(Object value) {\n        return switch (value) {\n            case Object o -> \"something\";\n            case String s -> \"text \" + s;\n        };\n    }\n\n    public static void main(String[] args) {\n        System.out.println(describe(\"hi\"));\n    }\n}\n", "java-dominated-case", 5, 4, 2, "            case String s -> \"text \" + s;|            case Object o -> \"something\";", true },
        { "case Shape above case Circle", "App.java", "public class App {\n    sealed interface Shape permits Circle, Square {}\n    record Circle(double r) implements Shape {}\n    record Square(double side) implements Shape {}\n\n    static String name(Shape shape) {\n        return switch (shape) {\n            case Shape s -> \"shape\";\n            case Circle c -> \"circle\";\n        };\n    }\n\n    public static void main(String[] args) {\n        System.out.println(name(new Circle(1)));\n    }\n}\n", "java-dominated-case", 9, 8, 2, "            case Circle c -> \"circle\";|            case Shape s -> \"shape\";", true },
        { "case String above its guarded case String", "App.java", "public class App {\n    static String describe(Object value) {\n        return switch (value) {\n            case String s -> \"text\";\n            case String s when s.isEmpty() -> \"empty text\";\n            default -> \"other\";\n        };\n    }\n\n    public static void main(String[] args) {\n        System.out.println(describe(\"\"));\n    }\n}\n", "java-dominated-case", 5, 4, 2, "            case String s when s.isEmpty() -> \"empty text\";|            case String s -> \"text\";", true },
        { "records compared with ==", "App.java", "public class App {\n    record Point(int x, int y) {}\n\n    public static void main(String[] args) {\n        Point a = new Point(1, 2);\n        Point b = new Point(1, 2);\n        if (a == b) {\n            System.out.println(\"same point\");\n        }\n    }\n}\n", "logic-java-record-equality", 7, 7, 1, "        if (a.equals(b)) {", false },
    };

    [Theory]
    [MemberData(nameof(Mistakes))]
    public async Task TheMistakeIsFoundWithItsChange(
        string mistake, string fileName, string source, string rule, int line, int fixStart, int fixRemoved, string fixLines, bool confirmedByARun)
    {
        if (!CanBuild(source)) return;

        var (report, program) = await CheckAsync(fileName, source);

        var found = report.Findings.FirstOrDefault(finding => finding.RuleId == rule);
        Assert.True(found is not null, $"{mistake}: no {rule} among {string.Join(", ", report.Findings.Select(finding => finding.RuleId))}");
        Assert.Equal(line, found!.Line);

        Assert.NotNull(found.Fix);
        Assert.Equal(fixStart, found.Fix!.StartLine);
        Assert.Equal(fixRemoved, found.Fix.RemoveCount);
        Assert.Equal(fixLines.Split('|'), found.Fix.NewLines);
        if (confirmedByARun) Assert.True(found.Verified.IsVerified, $"{mistake}: {found.Verified.Summary}");

        Assert.Equal(source, File.ReadAllText(program));
    }

    /// <summary>
    /// IO.readln's text given to a number: converted with the number's parse method, and confirmed by a run with a number
    /// typed - with nothing typed, IO.readln has no line to give, and the change is offered without that confirmation.
    /// </summary>
    [Theory]
    [InlineData("an int", "    int age = IO.readln(\"Age: \");\n    IO.println(age + 1);\n", "20\n", "    int age = Integer.parseInt(IO.readln(\"Age: \"));")]
    [InlineData("a double", "    double height = IO.readln(\"Height: \");\n    IO.println(height * 2);\n", "1.8\n", "    double height = Double.parseDouble(IO.readln(\"Height: \"));")]
    public async Task WhatIOReadlnGivesIsReadAsTheNumberItIsGivenTo(string numberType, string mainBody, string typed, string converted)
    {
        var source = Compact(mainBody);
        if (!CanBuild(source)) return;

        var (report, program) = await CheckAsync("Main.java", source, typed);

        var found = report.Findings.FirstOrDefault(finding => finding.RuleId == "java-string-conversion");
        Assert.True(found is not null, $"{numberType}: no java-string-conversion among {string.Join(", ", report.Findings.Select(finding => finding.RuleId))}");
        Assert.Equal(2, found!.Line);
        Assert.Equal([converted], found.Fix?.NewLines);
        Assert.True(found.Verified.IsVerified, $"{numberType}: {found.Verified.Summary}");
        Assert.Equal(source, File.ReadAllText(program));
    }

    /// <summary>What was wrong, the file, the program, and the rule whose change it must not be given.</summary>
    public static TheoryData<string, string, string, string> LookAlikes => new()
    {
        { "two variables it could return", "Main.java", "int bigger(int a, int b) {\n    int first = a;\n    int second = b;\n    if (a > b) {\n        IO.println(first);\n    }\n}\n\nvoid main() {\n    IO.println(bigger(3, 4));\n}\n", "java-missing-return" },
        { "args read outside main", "App.java", "public class App {\n    static void show() {\n        System.out.println(args.length);\n    }\n\n    public static void main(String[] args) {\n        show();\n    }\n}\n", "java-main-args" },
        { "a sealed type with a case missing", "App.java", "public class App {\n    sealed interface Shape permits Circle, Square {}\n    record Circle(double r) implements Shape {}\n    record Square(double side) implements Shape {}\n\n    static double area(Shape shape) {\n        return switch (shape) {\n            case Circle c -> Math.PI * c.r() * c.r();\n        };\n    }\n\n    public static void main(String[] args) {\n        System.out.println(area(new Circle(1)));\n    }\n}\n", "java-switch-default" },
        { "a record compared with null", "Main.java", "record Point(int x, int y) {}\n\nvoid main() {\n    Point a = new Point(1, 2);\n    if (a == null) {\n        IO.println(\"none\");\n    }\n}\n", "logic-java-record-equality" },
    };

    /// <summary>
    /// Mistakes that look like ones a rule fixes and are not: two variables the method could return, args read in a method
    /// that is not main, a sealed type whose missing case is a type of its own, and a record compared with null.
    /// </summary>
    [Theory]
    [MemberData(nameof(LookAlikes))]
    public async Task ALookAlikeIsNotGivenTheChange(string lookAlike, string fileName, string source, string rule)
    {
        if (!CanBuild(source)) return;

        var (report, program) = await CheckAsync(fileName, source);

        Assert.True(report.Findings.Count > 0, $"{lookAlike}: nothing found at all");
        Assert.DoesNotContain(report.Findings, finding => finding.RuleId == rule);
        Assert.Equal(source, File.ReadAllText(program));
    }
}
