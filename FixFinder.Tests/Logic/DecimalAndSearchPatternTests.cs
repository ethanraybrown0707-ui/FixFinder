using FixFinder.Core;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// A decimal worked out and compared exactly, and a search's position compared with &gt; 0: found where they are, with their
/// changes, and left alone where they are not.
/// </summary>
public class DecimalAndSearchPatternTests : IDisposable
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
        return LogicPatterns.Scan(SourceFile.Read(path)!);
    }

    [Theory]
    [InlineData("check.py", "total = 0.1 + 0.2\nif total == 0.3:\n    print(\"equal\")\n", "logic-python-float-equality", "if abs(total - 0.3) < 1e-9:")]
    [InlineData("check.py", "total = 0\nfor step in range(3):\n    total += 0.1\nwhile total != 0.3:\n    break\n", "logic-python-float-equality", "while abs(total - 0.3) >= 1e-9:")]
    [InlineData("check.js", "const total = 0.1 + 0.2;\nif (total === 0.3) {\n  console.log(\"equal\");\n}\n", "logic-js-float-equality", "if (Math.abs(total - 0.3) < 1e-9) {")]
    public void ADecimalWorkedOutAndComparedExactlyIsFound(string fileName, string code, string check, string fixedLine)
    {
        var compared = Assert.Single(Scan(fileName, code), finding => finding.PatternId == check);

        Assert.Equal(fixedLine, compared.Fix?.NewLines.Single());
    }

    [Theory]
    [InlineData("check.py", "price = 2.5\nif price == 2.5:\n    print(\"equal\")\n")]
    [InlineData("check.py", "count = 1 + 2\nif count == 3:\n    print(\"three\")\n")]
    [InlineData("check.py", "average = 5 / 2\nif average == 2.5:\n    print(\"two and a half\")\n")]
    [InlineData("check.js", "const share = 1 / 10;\nif (share === 0.1) {\n  console.log(\"a tenth\");\n}\n")]
    [InlineData("check.js", "const price = 2.5;\nif (price === 2.5) {\n  console.log(\"equal\");\n}\n")]
    public void ADecimalGivenAsItIsOrAWholeNumberIsLeftAlone(string fileName, string code)
    {
        Assert.DoesNotContain(Scan(fileName, code), finding => finding.PatternId.EndsWith("float-equality", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Search.java", "if (text.indexOf(\"a\") > 0) {", "if (text.indexOf(\"a\") >= 0) {")]
    [InlineData("Search.cs", "if (names.IndexOf(\"Ada\") > 0)", "if (names.IndexOf(\"Ada\") >= 0)")]
    [InlineData("search.js", "if (marks.findIndex((mark) => mark > 50) > 0) {", "if (marks.findIndex((mark) => mark > 50) >= 0) {")]
    [InlineData("search.py", "if text.find(\"a\") > 0:", "if text.find(\"a\") >= 0:")]
    public void ASearchComparedAboveZeroIsFoundAndCountsAMatchAtTheStart(string fileName, string line, string fixedLine)
    {
        var found = Assert.Single(Scan(fileName, line + "\n"), finding => finding.PatternId.EndsWith("above-zero", StringComparison.Ordinal));

        Assert.Equal(fixedLine, found.Fix?.NewLines.Single());
    }

    [Theory]
    [InlineData("Search.java", "if (text.indexOf(\"a\") >= 0) {")]
    [InlineData("search.js", "if (text.indexOf(\"a\") !== -1) {")]
    [InlineData("search.py", "if text.find(\"a\") > 1:")]
    public void ASearchComparedWithMinusOneOrNotAboveZeroIsLeftAlone(string fileName, string line)
    {
        Assert.DoesNotContain(Scan(fileName, line + "\n"), finding => finding.PatternId.EndsWith("above-zero", StringComparison.Ordinal));
    }
}

/// <summary>
/// What each of these mistakes does, run with each language's own toolchain the way FixFinder Learn runs a lesson, and what
/// its change does instead. A language this computer cannot run is passed over, and said to be.
/// </summary>
public class DecimalAndSearchLiveTests(ITestOutputHelper output)
{
    public static TheoryData<string, string, string, string> Runs => new()
    {
        { "Python", "total = 0.1 + 0.2\nprint(total == 0.3)\n", "False", "total = 0.1 + 0.2\nprint(abs(total - 0.3) < 1e-9)\n" },
        { "JavaScript", "const total = 0.1 + 0.2;\nconsole.log(total === 0.3);\n", "false", "const total = 0.1 + 0.2;\nconsole.log(Math.abs(total - 0.3) < 1e-9);\n" },
        { "Python", "print(\"apple\".find(\"a\") > 0)\n", "False", "print(\"apple\".find(\"a\") >= 0)\n" },
        { "JavaScript", "console.log(\"apple\".indexOf(\"a\") > 0, [\"a\", \"b\"].findIndex((x) => x === \"a\") > 0);\n", "false false", "console.log(\"apple\".indexOf(\"a\") >= 0, [\"a\", \"b\"].findIndex((x) => x === \"a\") >= 0);\n" },
        {
            "Java",
            "import java.util.List;\n\npublic class Search {\n    public static void main(String[] args) {\n        System.out.println(\"apple\".indexOf(\"a\") > 0);\n        System.out.println(List.of(\"a\", \"b\").indexOf(\"a\") > 0);\n    }\n}\n",
            "false\nfalse",
            "import java.util.List;\n\npublic class Search {\n    public static void main(String[] args) {\n        System.out.println(\"apple\".indexOf(\"a\") >= 0);\n        System.out.println(List.of(\"a\", \"b\").indexOf(\"a\") >= 0);\n    }\n}\n"
        },
        {
            "C#",
            "using System.Collections.Generic;\n\nConsole.WriteLine(\"apple\".IndexOf(\"a\") > 0);\nConsole.WriteLine(new List<string> { \"a\", \"b\" }.IndexOf(\"a\") > 0);\n",
            "False\nFalse",
            "using System.Collections.Generic;\n\nConsole.WriteLine(\"apple\".IndexOf(\"a\") >= 0);\nConsole.WriteLine(new List<string> { \"a\", \"b\" }.IndexOf(\"a\") >= 0);\n"
        },
    };

    [Theory]
    [MemberData(nameof(Runs))]
    public async Task TheMistakePrintsTheWrongAnswerAndTheChangeTheRightOne(string language, string mistake, string mistakePrints, string changed)
    {
        var codeLanguage = CodeLanguage.All.Single(each => each.Name == language);

        var wrong = await SnippetRunner.RunAsync(codeLanguage, mistake);
        if (!wrong.Ran)
        {
            output.WriteLine($"Not run on this computer: {wrong.WhyNotRun}");
            return;
        }

        var right = await SnippetRunner.RunAsync(codeLanguage, changed);
        output.WriteLine($"the mistake: {wrong.Said(language)}\nthe change:  {right.Said(language)}");

        Assert.True(wrong.Shows(Behaviour.Printing(mistakePrints)), wrong.Said(language));
        Assert.True(right.Shows(Behaviour.Printing(mistakePrints.Replace("False", "True").Replace("false", "true"))), right.Said(language));
    }
}
