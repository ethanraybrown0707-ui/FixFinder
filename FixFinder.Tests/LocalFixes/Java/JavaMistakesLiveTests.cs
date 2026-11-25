using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Beginner Java programs in App.java with one syntax or logic mistake each, checked the way the window checks a file - built
/// with javac and run with java - and the change FixFinder offers: its rule, the line it is about, the lines it replaces and
/// writes, whether a build or run of the changed copy confirmed it, and the program itself left as it was.
/// </summary>
public class JavaMistakesLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>A class App whose main holds these lines.</summary>
    private static string App(string mainBody) =>
        "public class App {\n    public static void main(String[] args) {\n" + mainBody + "    }\n}\n";

    /// <summary>What was wrong, the program, the rule that fixes it, the line it is found on, where the change starts, how many lines it replaces, the lines it writes (split by |), and whether a build or run confirmed it.</summary>
    public static TheoryData<string, string, string, int, int, int, string, bool> Mistakes => new()
    {
        // Syntax and types: javac refuses the file, and the change is confirmed by javac building the copy.
        { "missing semicolon", App("        int total = 5\n        System.out.println(total);\n"), "java-missing-semicolon", 3, 3, 1, "        int total = 5;", true },
        { "missing closing brace", "public class App {\n    public static void main(String[] args) {\n        System.out.println(\"hi\");\n    \n}\n", "java-missing-closing-brace", 5, 6, 0, "}", true },
        { "missing closing bracket", App("        System.out.println(\"total: \" + (2 + 3);\n"), "java-missing-closing-parenthesis", 3, 3, 1, "        System.out.println(\"total: \" + (2 + 3));", true },
        { "string for String", App("        string name = \"Ada\";\n        System.out.println(name);\n"), "java-nearest-name", 3, 3, 1, "        String name = \"Ada\";", true },
        { "misspelt variable", App("        int total = 5;\n        System.out.println(totl);\n"), "java-nearest-name", 4, 4, 1, "        System.out.println(total);", true },
        { "text given to an int", App("        int age = \"20\";\n        System.out.println(age);\n"), "java-string-conversion", 3, 3, 1, "        int age = Integer.parseInt(\"20\");", true },
        { "variable used before it has a value", App("        int total;\n        for (int i = 0; i < 3; i++) {\n            total += i;\n        }\n        System.out.println(total);\n"), "java-uninitialised", 5, 3, 1, "        int total = 0;", true },
        { "method called from static main", "public class App {\n    int doubled(int x) {\n        return x * 2;\n    }\n\n    public static void main(String[] args) {\n        System.out.println(doubled(4));\n    }\n}\n", "java-non-static-member", 7, 2, 1, "    static int doubled(int x) {", true },
        { "public class named for another file", "public class Main {\n    public static void main(String[] args) {\n        System.out.println(\"hi\");\n    }\n}\n", "java-public-class-name", 1, 1, 1, "public class App {", true },

        // Crashes: the run stops with an exception, and the change is confirmed by a run of the copy that does not.
        { "loop one past the end", App("        int[] marks = {60, 70, 80};\n        int total = 0;\n        for (int i = 0; i <= marks.length; i++) {\n            total += marks[i];\n        }\n        System.out.println(total);\n"), "java-off-by-one-loop", 6, 5, 1, "        for (int i = 0; i < marks.length; i++) {", true },
        { "average of no marks", App("        int[] marks = {};\n        int total = 0;\n        for (int mark : marks) {\n            total += mark;\n        }\n        System.out.println(total / marks.length);\n"), "java-division-guard", 8, 8, 1, "        System.out.println((marks.length == 0 ? 0 : total / marks.length));", true },

        // Logic: the program builds and runs, and the change is offered from the code - or from javac's own warning.
        { "semicolon after an if", App("        int mark = 30;\n        if (mark >= 40);\n        {\n            System.out.println(\"pass\");\n        }\n"), "logic-empty-if-body", 4, 4, 1, "        if (mark >= 40)", true },
        { "case with no break", App("        int day = 1;\n        switch (day) {\n            case 1:\n                System.out.println(\"Monday\");\n            case 2:\n                System.out.println(\"Tuesday\");\n                break;\n        }\n"), "java-fallthrough-break", 7, 7, 0, "                break;", true },
        { "Strings compared with ==", "import java.util.Scanner;\n\npublic class App {\n    public static void main(String[] args) {\n        Scanner in = new Scanner(\"yes\");\n        String answer = in.nextLine();\n        if (answer == \"yes\") {\n            System.out.println(\"ok\");\n        }\n    }\n}\n", "logic-java-string-equals", 7, 7, 1, "        if (answer.equals(\"yes\")) {", false },
        { "average worked out in whole numbers", App("        int total = 7;\n        int count = 2;\n        double average = total / count;\n        System.out.println(average);\n"), "logic-integer-division", 5, 5, 1, "        double average = (double) total / count;", false },
        { "semicolon after a for", App("        int total = 0;\n        for (int i = 0; i < 3; i++);\n        {\n            total += 1;\n        }\n        System.out.println(total);\n"), "logic-empty-loop-body", 4, 4, 1, "        for (int i = 0; i < 3; i++)", false },
        { "decimals compared exactly", App("        double total = 0.1 + 0.2;\n        if (total == 0.3) {\n            System.out.println(\"equal\");\n        }\n"), "logic-float-equality", 4, 4, 1, "        if (Math.abs(total - 0.3) < 1e-9) {", false },
        { "count = count++", App("        int count = 0;\n        for (int i = 0; i < 5; i++) {\n            count = count++;\n        }\n        System.out.println(count);\n"), "logic-lost-increment", 5, 5, 1, "            count++;", false },
        { "largest started at 0", App("        int[] temperatures = {-5, -2, -8};\n        int highest = 0;\n        for (int t : temperatures) {\n            if (t > highest) {\n                highest = t;\n            }\n        }\n        System.out.println(highest);\n"), "logic-java-best-starts-at-zero", 4, 4, 1, "        int highest = temperatures[0];", false },
    };

    [Theory]
    [MemberData(nameof(Mistakes))]
    public async Task TheMistakeIsFoundWithItsChange(string mistake, string source, string rule, int line, int fixStart, int fixRemoved, string fixLines, bool confirmedByARun)
    {
        if (!LocalFixLiveTests.Available("java")) return;

        var program = Path.Combine(_temp.Path, "App.java");
        File.WriteAllText(program, source);

        var launch = TargetFactory.FromFile(program, LiveAllowance.For(program, TimeSpan.FromMinutes(3)));
        Assert.True(launch.Ok, launch.Problem);

        using var http = new FixFinderHttpClient();
        var report = await new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Java }.CheckAsync(launch);

        output.WriteLine($"syntax: {report.SyntaxSummary}");
        foreach (var each in report.Findings)
            output.WriteLine($"[{each.RuleId}] {each.Line}: {each.Title} :: {each.Fix?.StartLine} {string.Join("|", each.Fix?.NewLines ?? [])} :: {each.Verified.Summary}");

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
}
