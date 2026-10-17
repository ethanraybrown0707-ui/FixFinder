using FixFinder.Core;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// typeof compared with a word it never gives, and Python's type() compared with a type's name as text: found, with the change
/// that asks what was meant, and left alone where the comparison can be true.
/// </summary>
public class TypeNamePatternTests : IDisposable
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
    [InlineData("main.js", "const marks = [70, 80];\nif (typeof marks === \"array\") {\n  console.log(\"an array\");\n}\n", "if (Array.isArray(marks)) {")]
    [InlineData("main.js", "const count = 3;\nconsole.log(typeof count == 'integer');\n", "console.log(Number.isInteger(count));")]
    [InlineData("main.js", "let value = null;\nif (typeof value !== \"null\") {}\n", "if (value !== null) {}")]
    [InlineData("main.js", "const name = \"Ada\";\nif (typeof name === \"String\") {}\n", "if (typeof name === \"string\") {}")]
    [InlineData("main.js", "const total = 1.5;\nif (typeof total === \"float\") {}\n", "if (typeof total === \"number\") {}")]
    [InlineData("main.js", "const marks = [70];\nif (typeof marks != \"array\") {}\n", "if (!Array.isArray(marks)) {}")]
    [InlineData("main.ts", "const marks: number[] = [70, 80];\nif (typeof marks === \"array\") {}\n", "if (Array.isArray(marks)) {}")]
    public void TypeofComparedWithAWordItNeverGivesIsFoundAndAsksWhatWasMeant(string fileName, string code, string changedTo)
    {
        var found = Assert.Single(Scan(fileName, code, "logic-js-typeof-never-gives"));
        Assert.Equal(2, found.Line);
        Assert.Equal(changedTo, found.Fix!.NewLines.Single());
    }

    [Fact]
    public void TypeofComparedWithAWordWhoseMeaningIsNotPlainIsFoundWithNoChange()
    {
        var found = Assert.Single(Scan("main.js", "const letter = \"a\";\nif (typeof letter === \"char\") {}\n", "logic-js-typeof-never-gives"));
        Assert.Equal(2, found.Line);
        Assert.Null(found.Fix);
    }

    [Theory]
    // typeof's own words.
    [InlineData("const count = 3;\nif (typeof count === \"number\") {}\nif (typeof missing !== 'undefined') {}\nif (typeof (count) === \"object\") {}\n")]
    // Inside text, and in a comment.
    [InlineData("console.log('typeof marks === \"array\"');\n// if (typeof marks === \"array\") {}\n")]
    public void TypeofComparedWithItsOwnWordsIsLeftAlone(string code) =>
        Assert.Empty(Scan("main.js", code, "logic-js-typeof-never-gives"));

    [Theory]
    [InlineData("age = 21\nif type(age) == \"int\":\n    print(\"whole\")\n", "if isinstance(age, int):")]
    [InlineData("name = \"Ada\"\nif type(name) != 'string':\n    print(\"not text\")\n", "if not isinstance(name, str):")]
    [InlineData("marks = [70]\nprint(type(marks) == \"list\", len(marks))\n", "print(isinstance(marks, list), len(marks))")]
    public void PythonTypeComparedWithItsNameAsTextIsFoundAndBecomesIsinstance(string code, string changedTo)
    {
        var found = Assert.Single(Scan("main.py", code, "logic-python-type-compared-with-text"));
        Assert.Equal(2, found.Line);
        Assert.Equal(changedTo, found.Fix!.NewLines.Single());
    }

    [Theory]
    // The type itself, which the check on type() == int reads; and a class's own name, which is no built-in type.
    [InlineData("age = 21\nif type(age) == int:\n    print(\"whole\")\nif type(age) == \"Student\":\n    print(\"a student\")\n")]
    // A comment.
    [InlineData("age = 21\n# if type(age) == \"int\":\n")]
    public void PythonTypeComparedWithNoBuiltInTypesNameIsLeftAlone(string code) =>
        Assert.Empty(Scan("main.py", code, "logic-python-type-compared-with-text"));
}

/// <summary>
/// The mistake and its change run with each language's own toolchain, as FixFinder Learn runs a lesson: the mistake takes the
/// branch for a value of another kind, and the change the right one. A language this computer cannot run is passed over, and said to be.
/// </summary>
public class TypeNameLiveTests(ITestOutputHelper output) : IDisposable
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
            "JavaScript", "main.js", "logic-js-typeof-never-gives",
            "const marks = [70, 80];\nif (typeof marks === \"array\") {\n  console.log(\"an array\");\n} else {\n  console.log(\"not an array\");\n}\n",
            2, "not an array", "an array"
        },
        {
            "TypeScript", "main.ts", "logic-js-typeof-never-gives",
            "const marks: number[] = [70, 80];\nif (typeof marks === \"array\") {\n  console.log(\"an array\");\n} else {\n  console.log(\"not an array\");\n}\n",
            2, "not an array", "an array"
        },
        {
            "Python", "main.py", "logic-python-type-compared-with-text",
            "age = 21\nif type(age) == \"int\":\n    print(\"a whole number\")\nelse:\n    print(\"not a whole number\")\n",
            2, "not a whole number", "a whole number"
        },
    };

    [Theory]
    [MemberData(nameof(Mistakes))]
    public async Task TheMistakeTakesTheOtherBranchAndTheChangeTheRightOne(string languageName, string fileName, string patternId, string code, int line, string mistakePrints, string changePrints)
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
