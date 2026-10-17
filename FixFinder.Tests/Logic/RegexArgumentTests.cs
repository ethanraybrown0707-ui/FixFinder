using FixFinder.Core;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// ".", "|" or "$" given alone to split, replaceAll or replaceFirst, which read a regular expression: found in Java and Scala,
/// escaped or replaced with replace, and left alone where the text means itself already.
/// </summary>
public class RegexArgumentPatternTests : IDisposable
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
    [InlineData("Main.java", "logic-java-regex-character-as-text", "class Main {\n        String[] parts = line.split(\".\");\n", "        String[] parts = line.split(\"\\\\.\");")]
    [InlineData("Main.java", "logic-java-regex-character-as-text", "class Main {\n        String[] fields = row.split(\"|\");\n", "        String[] fields = row.split(\"\\\\|\");")]
    [InlineData("Main.java", "logic-java-regex-character-as-text", "class Main {\n        String[] prices = text.split(\"$\");\n", "        String[] prices = text.split(\"\\\\$\");")]
    [InlineData("Main.java", "logic-java-regex-character-as-text", "class Main {\n        String euro = price.replaceAll(\".\", \",\");\n", "        String euro = price.replace(\".\", \",\");")]
    [InlineData("Main.java", "logic-java-regex-character-as-text", "class Main {\n        String first = price.replaceFirst(\".\", \",\");\n", "        String first = price.replaceFirst(\"\\\\.\", \",\");")]
    [InlineData("main.scala", "logic-scala-regex-character-as-text", "@main def run(): Unit =\n  val parts = \"3.14\".split(\".\")\n", "  val parts = \"3.14\".split(\"\\\\.\")")]
    public void ARegexCharacterMeantAsItselfIsFoundAndMadeToMeanItself(string fileName, string patternId, string code, string changedTo)
    {
        var found = Assert.Single(Scan(fileName, code, patternId));
        Assert.Equal(2, found.Line);
        Assert.Equal(changedTo, found.Fix!.NewLines.Single());
    }

    [Theory]
    // Escaped already, a comma or a space, which mean themselves, and replace, which takes its text as it is.
    [InlineData("Main.java", "logic-java-regex-character-as-text", "class Main {\n    void read(String line) {\n        line.split(\"\\\\.\");\n        line.split(\",\");\n        line.split(\" \");\n        line.replace(\".\", \",\");\n    }\n}\n")]
    public void TextThatMeansItselfIsLeftAlone(string fileName, string patternId, string code) =>
        Assert.Empty(Scan(fileName, code, patternId));
}

/// <summary>
/// The mistake and its change built and run with each language's own compiler, as FixFinder Learn runs a lesson: the mistake's
/// split gives no parts, and the change's two. A language this computer cannot build is passed over, and said to be.
/// </summary>
public class RegexArgumentLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    public static TheoryData<string, string, string, string, int> Mistakes => new()
    {
        {
            "Java", "Main.java", "logic-java-regex-character-as-text",
            "public class Main {\n    public static void main(String[] args) {\n        String[] parts = \"3.14\".split(\".\");\n        System.out.println(parts.length);\n    }\n}\n", 3
        },
        { "Scala", "main.scala", "logic-scala-regex-character-as-text", "@main def run(): Unit =\n  val parts = \"3.14\".split(\".\")\n  println(parts.length)\n", 2 },
    };

    [Theory]
    [MemberData(nameof(Mistakes))]
    public async Task TheMistakeSplitsIntoNothingAndTheChangeIntoTwo(string languageName, string fileName, string patternId, string code, int line)
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

        Assert.True(wrong.Shows(Behaviour.Printing("0")), wrong.Said(languageName));
        Assert.True(right.Shows(Behaviour.Printing("2")), right.Said(languageName));
    }
}
