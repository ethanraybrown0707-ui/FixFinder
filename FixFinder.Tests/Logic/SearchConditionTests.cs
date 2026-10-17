using FixFinder.Core;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// A search's position used as true or false: found in JavaScript, TypeScript and Python, with a change that asks whether it is
/// there, and left alone where the position is compared or kept.
/// </summary>
public class SearchConditionPatternTests : IDisposable
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
    [InlineData("main.js", "logic-js-search-as-condition", "const marks = [70, 80];\nif (marks.indexOf(70)) {\n", "if (marks.includes(70)) {")]
    [InlineData("main.js", "logic-js-search-as-condition", "let text = \"abc\";\nwhile (!text.indexOf(\"x\")) {\n", "while (!text.includes(\"x\")) {")]
    [InlineData("main.js", "logic-js-search-as-condition", "const name = \"Ada\";\nconsole.log(name.indexOf(\"A\") ? \"has A\" : \"no A\");\n", "console.log(name.includes(\"A\") ? \"has A\" : \"no A\");")]
    [InlineData("main.ts", "logic-js-search-as-condition", "const marks: number[] = [70];\nif (marks.lastIndexOf(70)) {}\n", "if (marks.includes(70)) {}")]
    [InlineData("main.py", "logic-python-find-as-condition", "name = \"Ada\"\nif name.find(\"A\"):\n    pass\n", "if \"A\" in name:")]
    [InlineData("main.py", "logic-python-find-as-condition", "name = \"Ada\"\nwhile not name.find(\"z\"):\n    break\n", "while \"z\" not in name:")]
    public void ASearchsPositionAsAConditionIsFoundAndAsksWhetherItIsThere(string fileName, string patternId, string code, string changedTo)
    {
        var found = Assert.Single(Scan(fileName, code, patternId));
        Assert.Equal(2, found.Line);
        Assert.Equal(changedTo, found.Fix!.NewLines.Single());
    }

    [Theory]
    // The position compared with -1 or 0, or kept.
    [InlineData("main.js", "logic-js-search-as-condition", "const marks = [70];\nif (marks.indexOf(70) !== -1) {}\nif (marks.indexOf(70) >= 0) {}\nconst at = marks.indexOf(70);\n")]
    [InlineData("main.py", "logic-python-find-as-condition", "name = \"Ada\"\nif name.find(\"A\") != -1:\n    pass\nat = name.find(\"A\")\n")]
    // find from a position on, which in cannot ask.
    [InlineData("main.py", "logic-python-find-as-condition", "name = \"Ada\"\nif name.find(\"a\", 1):\n    pass\n")]
    public void APositionComparedOrKeptIsLeftAlone(string fileName, string patternId, string code) =>
        Assert.Empty(Scan(fileName, code, patternId));
}

/// <summary>
/// The mistake and its change run with each language's own toolchain, as FixFinder Learn runs a lesson: the mistake takes the
/// match at the start for none, and the change finds it. A language this computer cannot run is passed over, and said to be.
/// </summary>
public class SearchConditionLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    public static TheoryData<string, string, string, string, int, string, string> Mistakes => new()
    {
        {
            "JavaScript", "main.js", "logic-js-search-as-condition",
            "const marks = [70, 80];\nif (marks.indexOf(70)) {\n  console.log(\"found 70\");\n} else {\n  console.log(\"no 70\");\n}\n", 2, "no 70", "found 70"
        },
        {
            "TypeScript", "main.ts", "logic-js-search-as-condition",
            "const marks: number[] = [70, 80];\nif (marks.indexOf(70)) {\n  console.log(\"found 70\");\n} else {\n  console.log(\"no 70\");\n}\n", 2, "no 70", "found 70"
        },
        {
            "Python", "main.py", "logic-python-find-as-condition",
            "name = \"Ada\"\nif name.find(\"A\"):\n    print(\"has A\")\nelse:\n    print(\"no A\")\n", 2, "no A", "has A"
        },
    };

    [Theory]
    [MemberData(nameof(Mistakes))]
    public async Task TheMistakeTakesAMatchAtTheStartForNoneAndTheChangeFindsIt(string languageName, string fileName, string patternId, string code, int line, string mistakePrints, string changePrints)
    {
        var path = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(path, code);

        var language = CodeLanguage.All.Single(each => each.Name == languageName);
        var found = Assert.Single(LogicPatterns.Scan(SourceFile.Read(path)!), finding => finding.PatternId == patternId);
        Assert.Equal(line, found.Line);

        var lines = code.TrimEnd('\n').Split('\n').ToList();
        lines[found.Fix!.StartLine - 1] = found.Fix.NewLines.Single();
        var changed = string.Join("\n", lines) + "\n";

        var wrong = await SnippetRunner.RunAsync(language, code);
        if (!wrong.Ran)
        {
            output.WriteLine($"Not run on this computer: {wrong.WhyNotRun}");
            return;
        }

        var right = await SnippetRunner.RunAsync(language, changed);
        output.WriteLine($"the mistake: {wrong.Said(languageName)}\nthe change:  {right.Said(languageName)}");

        Assert.True(wrong.Shows(Behaviour.Printing(mistakePrints)), wrong.Said(languageName));
        Assert.True(right.Shows(Behaviour.Printing(changePrints)), right.Said(languageName));
    }
}
