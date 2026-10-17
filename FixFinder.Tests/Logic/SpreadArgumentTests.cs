using FixFinder.Core;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>Math.max or Math.min given an array: found, with the array spread into its items, and left alone where it is given numbers.</summary>
public class SpreadArgumentPatternTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private IReadOnlyList<LogicFinding> Scan(string fileName, string code)
    {
        var path = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(path, code);
        return LogicPatterns.Scan(SourceFile.Read(path)!).Where(finding => finding.PatternId == "logic-js-math-max-of-array").ToList();
    }

    [Theory]
    [InlineData("main.js", "const marks = [70, 85, 90];\nconsole.log(\"Highest: \" + Math.max(marks));\n", "console.log(\"Highest: \" + Math.max(...marks));")]
    [InlineData("main.js", "const words = \"7 3 9\".split(\" \").map(Number);\nlet lowest = Math.min(words);\n", "let lowest = Math.min(...words);")]
    [InlineData("main.ts", "const marks: number[] = [70, 85];\nconst top: number = Math.max(marks);\n", "const top: number = Math.max(...marks);")]
    public void MathMaxGivenAnArrayIsFoundAndGivenItsItems(string fileName, string code, string changedTo)
    {
        var found = Assert.Single(Scan(fileName, code));
        Assert.Equal(2, found.Line);
        Assert.Equal(changedTo, found.Fix!.NewLines.Single());
    }

    [Theory]
    // Spread already; numbers; and a name that is no array the code made.
    [InlineData("main.js", "const marks = [70, 85];\nconsole.log(Math.max(...marks), Math.max(70, 85));\nfunction top(score) { return Math.max(score); }\n")]
    public void MathMaxGivenNumbersIsLeftAlone(string fileName, string code) => Assert.Empty(Scan(fileName, code));
}

/// <summary>
/// The mistake and its change run with Node.js, as FixFinder Learn runs a lesson: the mistake prints NaN, and the change the
/// highest mark. Passed over, and said to be, where this computer cannot run them.
/// </summary>
public class SpreadArgumentLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    public static TheoryData<string, string, string> Mistakes => new()
    {
        { "JavaScript", "main.js", "const marks = [70, 85, 90];\nconsole.log(\"Highest: \" + Math.max(marks));\n" },
        { "TypeScript", "main.ts", "const marks: number[] = [70, 85, 90];\nconsole.log(\"Highest: \" + Math.max(marks));\n" },
    };

    [Theory]
    [MemberData(nameof(Mistakes))]
    public async Task TheMistakePrintsNaNAndTheChangeTheHighestMark(string languageName, string fileName, string code)
    {
        var path = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(path, code);

        var found = Assert.Single(LogicPatterns.Scan(SourceFile.Read(path)!), finding => finding.PatternId == "logic-js-math-max-of-array");
        Assert.Equal(2, found.Line);

        var lines = code.TrimEnd('\n').Split('\n').ToList();
        lines[found.Fix!.StartLine - 1] = found.Fix.NewLines.Single();
        var changed = string.Join("\n", lines) + "\n";

        var language = CodeLanguage.All.Single(each => each.Name == languageName);
        var wrong = await SnippetRunner.RunAsync(language, code);
        if (!wrong.Ran)
        {
            output.WriteLine($"Not run on this computer: {wrong.WhyNotRun}");
            return;
        }

        var right = await SnippetRunner.RunAsync(language, changed);
        output.WriteLine($"the mistake: {wrong.Said(languageName)}\nthe change:  {right.Said(languageName)}");

        Assert.True(wrong.Shows(Behaviour.Printing("Highest: NaN")), wrong.Said(languageName));
        Assert.True(right.Shows(Behaviour.Printing("Highest: 90")), right.Said(languageName));
    }
}
