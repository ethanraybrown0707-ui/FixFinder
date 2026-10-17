using FixFinder.Core;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// A loop run while its counter != its end, whose steps never land on the end: found, with &lt; or &gt; as the change, and left
/// alone where the steps land on the end or the test is &lt; already.
/// </summary>
public class StepOverEndPatternTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private IReadOnlyList<LogicFinding> Scan(string fileName, string code, string patternId)
    {
        var path = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(path, code);
        return LogicPatterns.Scan(SourceFile.Read(path)!).Where(finding => finding.PatternId == patternId).ToList();
    }

    [Theory]
    [InlineData("main.py", "logic-python-loop-steps-over-end", "count = 0\nwhile count != 10:\n    count += 3\n    print(count)\n", "while count < 10:")]
    [InlineData("main.py", "logic-python-loop-steps-over-end", "count = 10\nwhile count != 0:\n    count -= 3\n", "while count > 0:")]
    [InlineData("Main.java", "logic-loop-steps-over-end", "class Main {\n        for (int count = 0; count != 10; count += 3) {\n", "        for (int count = 0; count < 10; count += 3) {")]
    [InlineData("main.js", "logic-loop-steps-over-end", "let total = 0;\nfor (let odd = 1; odd !== 10; odd += 2) {\n", "for (let odd = 1; odd < 10; odd += 2) {")]
    [InlineData("main.cs", "logic-loop-steps-over-end", "var total = 0;\nfor (int count = 20; count != 0; count -= 3)\n", "for (int count = 20; count > 0; count -= 3)")]
    [InlineData("main.ts", "logic-loop-steps-over-end", "let total: number = 0;\nfor (let count: number = 0; count != 10; count += 4) {\n  total += count;\n}\n", "for (let count: number = 0; count < 10; count += 4) {")]
    public void ALoopThatStepsOverItsEndIsFoundAndStopsOnceItReachesIt(string fileName, string patternId, string code, string changedTo)
    {
        var found = Assert.Single(Scan(fileName, code, patternId));
        Assert.Equal(2, found.Line);
        Assert.Equal(changedTo, found.Fix!.NewLines.Single());
    }

    [Fact]
    public void TheMessageSaysTheValuesTheCounterTakes()
    {
        var found = Assert.Single(Scan("main.py", "count = 0\nwhile count != 10:\n    count += 3\n", "logic-python-loop-steps-over-end"));
        Assert.Contains("3, 6, 9, 12", found.Message);
    }

    [Theory]
    // An int wraps round past its largest value: counting from 0 by 3 it reaches 10 after the passes Java, C and C# all printed.
    [InlineData("Main.java", "class Main {\n        for (int count = 0; count != 10; count += 3) {\n", "lands on 10 only after 2,863,311,534 passes")]
    // Counting by 2 from 1, an int is always odd, and never lands on 10.
    [InlineData("main.c", "int main(void) {\n    for (int count = 1; count != 10; count += 2) {\n", "never lands on 10, so the loop never ends")]
    // JavaScript's numbers never wrap round.
    [InlineData("main.js", "let total = 0;\nfor (let count = 0; count != 10; count += 3) {\n", "the loop never ends")]
    public void TheMessageSaysWhetherAndWhenTheLoopEnds(string fileName, string code, string said)
    {
        var found = Assert.Single(Scan(fileName, code, "logic-loop-steps-over-end"));
        Assert.Contains(said, found.Message);
    }

    [Fact]
    public void ACounterGoingAwayFromTheEndIsFoundWithNoChange()
    {
        var found = Assert.Single(Scan("main.js", "let total = 0;\nfor (let count = 10; count != 0; count++) {\n", "logic-loop-steps-over-end"));
        Assert.Contains("away from 0", found.Message);
        Assert.Null(found.Fix);
    }

    [Theory]
    // Steps that land on the end; < already; an end that is not a number written in the code.
    [InlineData("main.py", "logic-python-loop-steps-over-end", "count = 0\nwhile count != 9:\n    count += 3\ncount = 0\nwhile count < 10:\n    count += 3\n")]
    // The counter changed in two places in the loop, which this cannot follow.
    [InlineData("main.py", "logic-python-loop-steps-over-end", "count = 0\nwhile count != 10:\n    count += 3\n    if count > 5:\n        count = 10\n")]
    [InlineData("Main.java", "logic-loop-steps-over-end", "class Main {\n    void loop(int n) {\n        for (int i = 0; i != 9; i += 3) { }\n        for (int i = 0; i < 10; i += 3) { }\n        for (int i = 0; i != n; i++) { }\n    }\n}\n")]
    public void ALoopThatLandsOnItsEndOrStopsAnywayIsLeftAlone(string fileName, string patternId, string code) =>
        Assert.Empty(Scan(fileName, code, patternId));
}

/// <summary>
/// The mistake and its change run with each language's own toolchain, as FixFinder Learn runs a lesson, counting the loop's
/// passes. The change stops after 4. The mistake is still running when its time runs out - or, with an int in Java, C and C#,
/// which wrap round, it may finish first, after the 2,863,311,534 passes each printed here. A language this computer cannot run
/// is passed over, and said to be.
/// </summary>
public class StepOverEndLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    public static TheoryData<string, string, string, string, int> Mistakes => new()
    {
        { "Python", "main.py", "logic-python-loop-steps-over-end", "count = 0\npasses = 0\nwhile count != 10:\n    count += 3\n    passes += 1\nprint(passes)\n", 3 },
        { "JavaScript", "main.js", "logic-loop-steps-over-end", "let passes = 0;\nfor (let count = 0; count != 10; count += 3) {\n  passes++;\n}\nconsole.log(passes);\n", 2 },
        {
            "Java", "Main.java", "logic-loop-steps-over-end",
            "public class Main {\n    public static void main(String[] args) {\n        long passes = 0;\n        for (int count = 0; count != 10; count += 3) {\n            passes++;\n        }\n        System.out.println(passes);\n    }\n}\n", 4
        },
        { "C#", "main.cs", "logic-loop-steps-over-end", "long passes = 0;\nfor (int count = 0; count != 10; count += 3)\n{\n    passes++;\n}\nConsole.WriteLine(passes);\n", 2 },
        {
            "C", "main.c", "logic-loop-steps-over-end",
            "#include <stdio.h>\n\nint main(void) {\n    volatile long long passes = 0;\n    for (int count = 0; count != 10; count += 3) {\n        passes++;\n    }\n    printf(\"%lld\\n\", passes);\n    return 0;\n}\n", 5
        },
    };

    [Theory]
    [MemberData(nameof(Mistakes))]
    public async Task TheMistakeRunsFarPastItsEndAndTheChangeStopsAfterFourPasses(string languageName, string fileName, string patternId, string code, int line)
    {
        var path = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(path, code);

        var language = CodeLanguage.All.Single(each => each.Name == languageName);
        var found = Assert.Single(LogicPatterns.Scan(SourceFile.Read(path)!), finding => finding.PatternId == patternId);
        Assert.Equal(line, found.Line);

        var lines = code.TrimEnd('\n').Split('\n').ToList();
        lines[found.Fix!.StartLine - 1] = found.Fix.NewLines.Single();
        var changed = string.Join("\n", lines) + "\n";

        var wrong = await SnippetRunner.RunAsync(language, code, SnippetRunner.TimeLimitFor(Behaviour.Running()));
        if (!wrong.Ran)
        {
            output.WriteLine($"Not run on this computer: {wrong.WhyNotRun}");
            return;
        }

        var right = await SnippetRunner.RunAsync(language, changed);
        output.WriteLine($"the mistake: {wrong.Said(languageName)}\nthe change:  {right.Said(languageName)}");

        var wraps = languageName is "Java" or "C#" or "C";
        Assert.True(wrong.Shows(Behaviour.Running()) || (wraps && wrong.Shows(Behaviour.Printing("2863311534"))), wrong.Said(languageName));
        Assert.True(right.Shows(Behaviour.Printing("4")), right.Said(languageName));
    }
}
