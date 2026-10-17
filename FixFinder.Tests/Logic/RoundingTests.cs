using FixFinder.Core;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Java's Math.round(...) / 100 where the result is meant to keep a fraction: found, with the divisor written as a double, and
/// left alone where the result is a whole number on purpose or divides as decimals already.
/// </summary>
public class RoundingPatternTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private IReadOnlyList<LogicFinding> Scan(string body)
    {
        var path = Path.Combine(_temp.Path, "Main.java");
        File.WriteAllText(path, "public class Main {\n    public static void main(String[] args) {\n        double price = 12.345;\n" + body + "    }\n}\n");
        return LogicPatterns.Scan(SourceFile.Read(path)!).Where(finding => finding.PatternId == "logic-java-round-divided-as-whole-number").ToList();
    }

    [Theory]
    [InlineData("        double rounded = Math.round(price * 100) / 100;\n", 4, "        double rounded = Math.round(price * 100) / 100.0;")]
    [InlineData("        System.out.println(Math.round(price * 10) / 10);\n", 4, "        System.out.println(Math.round(price * 10) / 10.0);")]
    [InlineData("        double rounded;\n        rounded = Math.round(price * 100) / 100;\n", 5, "        rounded = Math.round(price * 100) / 100.0;")]
    public void RoundingThatIsMeantToKeepItsPlacesIsFoundAndDividesAsDecimals(string body, int line, string changedTo)
    {
        var found = Assert.Single(Scan(body));
        Assert.Equal(line, found.Line);
        Assert.Equal(changedTo, found.Fix!.NewLines.Single());
    }

    [Theory]
    // Dividing as decimals already.
    [InlineData("        double rounded = Math.round(price * 100) / 100.0;\n")]
    // A whole number, on purpose.
    [InlineData("        long pounds = Math.round(price * 100) / 100;\n        int halves = Math.round(3.4f) / 2;\n")]
    public void RoundingWhoseResultIsMeantToBeWholeIsLeftAlone(string body) => Assert.Empty(Scan(body));
}

/// <summary>
/// The mistake and its change built and run with javac, as FixFinder Learn runs a lesson: the mistake prints 12.0, and the
/// change 12.35. Passed over, and said to be, where this computer cannot build Java.
/// </summary>
public class RoundingLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task TheMistakeLosesThePlacesAndTheChangeKeepsThem()
    {
        const string code = "public class Main {\n    public static void main(String[] args) {\n        double price = 12.345;\n        double rounded = Math.round(price * 100) / 100;\n        System.out.println(rounded);\n    }\n}\n";
        var path = Path.Combine(_temp.Path, "Main.java");
        File.WriteAllText(path, code);

        var found = Assert.Single(LogicPatterns.Scan(SourceFile.Read(path)!), finding => finding.PatternId == "logic-java-round-divided-as-whole-number");
        Assert.Equal(4, found.Line);

        var lines = code.TrimEnd('\n').Split('\n').ToList();
        lines[found.Fix!.StartLine - 1] = found.Fix.NewLines.Single();
        var changed = string.Join("\n", lines) + "\n";

        var java = CodeLanguage.All.Single(each => each.Name == "Java");
        var wrong = await SnippetRunner.RunAsync(java, code);
        if (!wrong.Ran)
        {
            output.WriteLine($"Not run on this computer: {wrong.WhyNotRun}");
            return;
        }

        var right = await SnippetRunner.RunAsync(java, changed);
        output.WriteLine($"the mistake: {wrong.Said("Java")}\nthe change:  {right.Said("Java")}");

        Assert.True(wrong.Shows(Behaviour.Printing("12.0")), wrong.Said("Java"));
        Assert.True(right.Shows(Behaviour.Printing("12.35")), right.Said("Java"));
    }
}
