using FixFinder.Core;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Whole numbers in the code whose product or sum is too big for an int: found in Java and Scala, with the arithmetic done in
/// longs, and left alone where it fits or is done in longs already.
/// </summary>
public class ConstantOverflowPatternTests : IDisposable
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
    [InlineData("Main.java", "logic-java-constant-overflow", "class Main {\n        long millisPerYear = 365 * 24 * 60 * 60 * 1000;\n", "        long millisPerYear = 365L * 24 * 60 * 60 * 1000;")]
    [InlineData("Main.java", "logic-java-constant-overflow", "class Main {\n        int big = 50000 * 50000;\n", "        long big = 50000L * 50000;")]
    [InlineData("Main.java", "logic-java-constant-overflow", "class Main {\n        long cents = 1_000_000 * 3_000;\n", "        long cents = 1_000_000L * 3_000;")]
    [InlineData("main.scala", "logic-scala-constant-overflow", "@main def run(): Unit =\n  val millisPerYear: Long = 365 * 24 * 60 * 60 * 1000\n", "  val millisPerYear: Long = 365L * 24 * 60 * 60 * 1000")]
    [InlineData("main.scala", "logic-scala-constant-overflow", "@main def run(): Unit =\n  val big: Int = 50000 * 50000\n", "  val big: Long = 50000L * 50000")]
    public void WholeNumbersTooBigForAnIntAreFoundAndWorkedOutInLongs(string fileName, string patternId, string code, string changedTo)
    {
        var found = Assert.Single(Scan(fileName, code, patternId));
        Assert.Equal(2, found.Line);
        Assert.Equal(changedTo, found.Fix!.NewLines.Single());
    }

    [Fact]
    public void TheMessageGivesTheExactValueAndTheOneItWrapsRoundTo()
    {
        var found = Assert.Single(Scan("Main.java", "class Main {\n        long millisPerYear = 365 * 24 * 60 * 60 * 1000;\n", "logic-java-constant-overflow"));
        Assert.Contains("31,536,000,000", found.Message);
        Assert.Contains("1,471,228,928", found.Message);
    }

    [Theory]
    // Fits in an int; worked out in longs already; and decimals.
    [InlineData("Main.java", "logic-java-constant-overflow", "class Main {\n        int secondsPerDay = 60 * 60 * 24;\n        long millis = 365L * 24 * 60 * 60 * 1000;\n        double half = 0.5 * 3;\n}\n")]
    [InlineData("main.scala", "logic-scala-constant-overflow", "@main def run(): Unit =\n  val secondsPerDay = 60 * 60 * 24\n  val millis = 365L * 24 * 60 * 60 * 1000\n")]
    public void WholeNumbersThatFitOrAreLongsAreLeftAlone(string fileName, string patternId, string code) =>
        Assert.Empty(Scan(fileName, code, patternId));
}

/// <summary>
/// The mistake and its change built and run with each language's own compiler, as FixFinder Learn runs a lesson: the mistake
/// prints the wrapped number, and the change the right one. A language this computer cannot build is passed over, and said to be.
/// </summary>
public class ConstantOverflowLiveTests(ITestOutputHelper output) : IDisposable
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
            "Java", "Main.java", "logic-java-constant-overflow",
            "public class Main {\n    public static void main(String[] args) {\n        long millisPerYear = 365 * 24 * 60 * 60 * 1000;\n        System.out.println(millisPerYear);\n    }\n}\n", 3
        },
        {
            "Scala", "main.scala", "logic-scala-constant-overflow",
            "@main def run(): Unit =\n  val millisPerYear: Long = 365 * 24 * 60 * 60 * 1000\n  println(millisPerYear)\n", 2
        },
    };

    [Theory]
    [MemberData(nameof(Mistakes))]
    public async Task TheMistakePrintsTheWrappedNumberAndTheChangeTheRightOne(string languageName, string fileName, string patternId, string code, int line)
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

        Assert.True(wrong.Shows(Behaviour.Printing("1471228928")), wrong.Said(languageName));
        Assert.True(right.Shows(Behaviour.Printing("31536000000")), right.Said(languageName));
    }
}
