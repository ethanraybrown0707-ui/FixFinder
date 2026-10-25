using FixFinder.Core;
using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// A language's own library used in a way that runs and does something else, each run with the language's own toolchain the
/// way FixFinder Learn runs a lesson, and the check's own change run the same way: the mistake does what it was seen to, and
/// the change what was meant. A language this computer cannot run - or a program Windows refused to start - is passed over,
/// and said to be.
/// </summary>
public class LibraryMistakeLiveTests(ITestOutputHelper output)
{
    /// <summary>Each: the language, the check, the mistake, what it prints, and what its change prints.</summary>
    public static TheoryData<string, string, string, string, string> Mistakes => new()
    {
        {
            "Java", "logic-java-thread-run",
            "public class Main {\n    public static void main(String[] args) throws InterruptedException {\n        Thread worker = new Thread(() -> System.out.println(Thread.currentThread().getName().equals(\"main\")));\n        worker.run();\n        worker.join();\n    }\n}\n",
            "true", "false"
        },
        {
            "Java", "logic-java-bigdecimal-from-double",
            "import java.math.BigDecimal;\n\npublic class Main {\n    public static void main(String[] args) {\n        System.out.println(new BigDecimal(0.1));\n    }\n}\n",
            "0.1000000000000000055511151231257827021181583404541015625", "0.1"
        },
        {
            "Java", "logic-java-aslist-of-primitive-array",
            "import java.util.Arrays;\n\npublic class Main {\n    public static void main(String[] args) {\n        int[] marks = {70, 80, 90};\n        System.out.println(Arrays.asList(marks).contains(80));\n    }\n}\n",
            "false", "true"
        },
        {
            "Java", "logic-java-abs-of-hash",
            "public class Main {\n    public static void main(String[] args) {\n        System.out.println(Math.abs(\"polygenelubricants\".hashCode()) % 10);\n    }\n}\n",
            "-8", "2"
        },
        {
            "C#", "logic-csharp-for-closure",
            "var actions = new List<Action>();\nfor (int i = 0; i < 3; i++)\n{\n    actions.Add(() => Console.WriteLine(i));\n}\nforeach (var action in actions) action();\n",
            "3\n3\n3", "0\n1\n2"
        },
        {
            "Python", "logic-python-thread-run",
            "import threading\nworker = threading.Thread(target=lambda: print(threading.current_thread() is threading.main_thread()))\nworker.run()\n", "True", "False"
        },
        { "Python", "logic-python-decimal-from-float", "from decimal import Decimal\nprint(Decimal(0.1))\n", "0.1000000000000000055511151231257827021181583404541015625", "0.1" },
        {
            "Python", "logic-python-get-default-changed",
            "groups = {}\nfor name, team in [(\"Ada\", \"red\"), (\"Bob\", \"red\")]:\n    groups.get(team, []).append(name)\nprint(groups)\n", "{}", "{'red': ['Ada', 'Bob']}"
        },
        { "Python", "logic-python-iterator-used-twice", "numbers = map(int, [\"3\", \"4\"])\nprint(sum(numbers), len(list(numbers)))\n", "7 0", "7 2" },
        { "Python", "logic-python-strip-word", "print(\"test.txt\".rstrip(\".txt\"))\n", "tes", "test" },
        { "Python", "logic-python-lambda-in-loop", "adders = [lambda x: x + step for step in range(3)]\nprint([add(10) for add in adders])\n", "[12, 12, 12]", "[10, 11, 12]" },
        { "JavaScript", "logic-js-array-holes-mapped", "const squares = new Array(3).map((_, i) => i * i);\nconsole.log(squares);\n", "[ <3 empty items> ]", "[ 0, 1, 4 ]" },
        { "JavaScript", "logic-js-replace-first-only", "console.log(\"a-b-c\".replace(\"-\", \"\"));\n", "ab-c", "abc" },
        { "JavaScript", "logic-js-promise-used-as-value", "async function count() {\n    return 3;\n}\nconst result = count();\nconsole.log(result);\n", "Promise { 3 }", "3" },
        { "JavaScript", "logic-js-promise-used-as-value", "const doubled = [1, 2].map(async (n) => n * 2);\nconsole.log(doubled);\n", "[ Promise { 2 }, Promise { 4 } ]", "[ 2, 4 ]" },
        { "JavaScript", "logic-js-get-year", "const when = new Date(2024, 1, 5);\nconsole.log(when.getYear());\n", "124", "2024" },
        {
            "JavaScript", "logic-js-get-day-for-date",
            "const when = new Date(2024, 1, 5);\nconsole.log(when.getDay() + \"/\" + (when.getMonth() + 1) + \"/\" + when.getFullYear());\n", "1/2/2024", "5/2/2024"
        },
        {
            "C", "logic-c-getchar-into-char",
            "#include <stdio.h>\n\nint main(void) {\n    FILE *file = fopen(\"bytes.bin\", \"wb\");\n    fputs(\"ab\", file);\n    fputc(255, file);\n    fputs(\"cd\", file);\n    fclose(file);\n    file = fopen(\"bytes.bin\", \"rb\");\n    char c;\n    int count = 0;\n    while ((c = fgetc(file)) != EOF) {\n        count++;\n    }\n    fclose(file);\n    printf(\"%d\\n\", count);\n    return 0;\n}\n",
            "2", "5"
        },
        {
            "C++", "logic-cpp-text-plus-number",
            "#include <iostream>\n#include <string>\n\nint main() {\n    int count = 3;\n    std::string shown = \"Total: \" + count;\n    std::cout << \"[\" << shown << \"]\" << std::endl;\n}\n",
            "[al: ]", "[Total: 3]"
        },
        {
            "C++", "logic-cpp-reserve-then-index",
            "#include <iostream>\n#include <vector>\n\nint main() {\n    std::vector<int> marks;\n    marks.reserve(3);\n    marks[0] = 70;\n    std::cout << marks.size() << std::endl;\n}\n",
            "0", "3"
        },
        {
            "Go", "logic-go-sleep-nanoseconds",
            "package main\n\nimport (\n\t\"fmt\"\n\t\"time\"\n)\n\nfunc main() {\n\tstart := time.Now()\n\ttime.Sleep(1)\n\tfmt.Println(time.Since(start) >= time.Second)\n}\n",
            "false", "true"
        },
        {
            "Go", "logic-go-copy-into-empty",
            "package main\n\nimport \"fmt\"\n\nfunc main() {\n\tmarks := []int{70, 80, 90}\n\tbackup := make([]int, 0)\n\tcopy(backup, marks)\n\tfmt.Println(backup, len(backup))\n}\n",
            "[] 0", "[70 80 90] 3"
        },
        { "Go", "logic-go-byte-printed", "package main\n\nimport \"fmt\"\n\nfunc main() {\n\tname := \"hello\"\n\tfmt.Println(name[0])\n}\n", "104", "h" },
    };

    /// <summary>The mistake and its change: each run, or null when this computer could not run them - which is said.</summary>
    private async Task<(Observation Mistake, Observation Change)?> RunBothAsync(string language, string check, string mistake)
    {
        var codeLanguage = CodeLanguage.All.Single(each => each.Name == language);
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, Path.GetFileName(PastedCode.RelativePathFor(mistake, codeLanguage)));
        File.WriteAllText(path, mistake);
        var found = Assert.Single(LogicPatterns.Scan(SourceFile.Read(path)!), finding => finding.PatternId == check);

        var lines = mistake.TrimEnd('\n').Split('\n').ToList();
        lines.RemoveRange(found.Fix!.StartLine - 1, found.Fix.RemoveCount);
        lines.InsertRange(found.Fix.StartLine - 1, found.Fix.NewLines);
        var changed = string.Join("\n", lines) + "\n";

        var wrong = await SnippetRunner.RunAsync(codeLanguage, mistake);
        var right = wrong.Ran && !RefusedByWindows(wrong) ? await SnippetRunner.RunAsync(codeLanguage, changed) : null;
        if (right is null || RefusedByWindows(right))
        {
            output.WriteLine($"Not run on this computer: {wrong.WhyNotRun ?? "Windows refused to start it"}");
            return null;
        }

        output.WriteLine($"the mistake: {wrong.Said(language)}\nthe change:  {right.Said(language)}");
        return (wrong, right);
    }

    /// <summary>Whether Windows' application control refused to start what was built - which says nothing about the code.</summary>
    private static bool RefusedByWindows(Observation observed) =>
        ApplicationControl.Refused(string.Join("\n", observed.Output) + "\n" + observed.WhyNotRun);

    [Theory]
    [MemberData(nameof(Mistakes))]
    public async Task TheMistakePrintsWhatItWasSeenToAndTheChangeWhatWasMeant(string language, string check, string mistake, string mistakePrints, string changePrints)
    {
        if (await RunBothAsync(language, check, mistake) is not var (wrong, right)) return;

        Assert.True(wrong.Shows(Behaviour.Printing(mistakePrints)), wrong.Said(language));
        Assert.True(right.Shows(Behaviour.Printing(changePrints)), right.Said(language));
    }

    [Fact]
    public async Task ATimerGivenACallRunsItAtOnceAndStopsAndTheChangeRunsItLater()
    {
        if (await RunBothAsync("JavaScript", "logic-js-timer-given-a-call",
                "function greet() {\n    console.log(\"hello\");\n}\nsetTimeout(greet(), 10);\nconsole.log(\"set\");\n") is not var (wrong, right)) return;

        Assert.NotNull(wrong.Error);
        Assert.Contains("ERR_INVALID_ARG_TYPE", string.Join("\n", wrong.Output));
        Assert.True(right.Shows(Behaviour.Printing("set\nhello")), right.Said("JavaScript"));
    }

    [Fact]
    public async Task AnAsyncMethodsTaskPrintsItsTypeAndTheChangePrintsItsValue()
    {
        if (await RunBothAsync("C#", "logic-csharp-task-printed",
                "var count = CountAsync();\nConsole.WriteLine(count);\n\nstatic async Task<int> CountAsync()\n{\n    await Task.Delay(1);\n    return 3;\n}\n") is not var (wrong, right)) return;

        Assert.StartsWith("System.", wrong.Printed);
        Assert.True(right.Shows(Behaviour.Printing("3")), right.Said("C#"));
    }
}
