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

    private IReadOnlyList<LogicFinding> ScanForCasts(string fileName, string code)
    {
        var path = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(path, code);
        return LogicPatterns.Scan(SourceFile.Read(path)!).Where(finding => finding.PatternId == "logic-cast-divided-as-whole-number").ToList();
    }

    [Theory]
    [InlineData("main.c", "int main(void) {\n    double price = 12.345;\n    double rounded = (int)(price * 100 + 0.5) / 100;\n", "    double rounded = (int)(price * 100 + 0.5) / 100.0;")]
    [InlineData("main.cpp", "int main() {\n    double price = 12.345;\n    double rounded = (long)(price * 100 + 0.5) / 100;\n", "    double rounded = (long)(price * 100 + 0.5) / 100.0;")]
    [InlineData("Main.java", "class Main { static void show(double price) {\n    double rounded;\n    rounded = (int) (price * 100 + 0.5) / 100;\n", "    rounded = (int) (price * 100 + 0.5) / 100.0;")]
    [InlineData("main.cs", "double price = 12.345;\nfloat share = 0;\nshare = (int)(price * 10) / 10;\n", "share = (int)(price * 10) / 10.0;")]
    public void RoundingByHandGivenToADecimalIsFoundAndDividesAsDecimals(string fileName, string code, string changedTo)
    {
        var found = Assert.Single(ScanForCasts(fileName, code));
        Assert.Equal(3, found.Line);
        Assert.Equal(changedTo, found.Fix!.NewLines.Single());
    }

    [Theory]
    // Dividing as decimals already, and a whole number kept as one.
    [InlineData("main.c", "int main(void) {\n    double price = 12.345;\n    double rounded = (int)(price * 100 + 0.5) / 100.0;\n    int pounds = (int)(price * 100) / 100;\n    printf(\"%d\", (int)(price * 100) / 100);\n")]
    public void RoundingByHandThatKeepsItsPlacesOrIsMeantWholeIsLeftAlone(string fileName, string code) =>
        Assert.Empty(ScanForCasts(fileName, code));
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

    public static TheoryData<string, string, string, int, string> RoundedByHand => new()
    {
        { "C", "main.c", "#include <stdio.h>\n\nint main(void) {\n    double price = 12.345;\n    double rounded = (int)(price * 100 + 0.5) / 100;\n    printf(\"%.2f\\n\", rounded);\n    return 0;\n}\n", 5, "12.00" },
        { "C++", "main.cpp", "#include <iostream>\n\nint main() {\n    double price = 12.345;\n    double rounded = (int)(price * 100 + 0.5) / 100;\n    std::cout << rounded << \"\\n\";\n}\n", 5, "12" },
        { "Java", "Main.java", "public class Main {\n    public static void main(String[] args) {\n        double price = 12.345;\n        double rounded = (int) (price * 100 + 0.5) / 100;\n        System.out.println(rounded);\n    }\n}\n", 4, "12.0" },
        { "C#", "main.cs", "double price = 12.345;\ndouble rounded = (int)(price * 100 + 0.5) / 100;\nConsole.WriteLine(rounded);\n", 2, "12" },
    };

    [Theory]
    [MemberData(nameof(RoundedByHand))]
    public async Task RoundingByHandLosesThePlacesAndTheChangeKeepsThem(string languageName, string fileName, string code, int line, string mistakePrints)
    {
        var path = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(path, code);

        var found = Assert.Single(LogicPatterns.Scan(SourceFile.Read(path)!), finding => finding.PatternId == "logic-cast-divided-as-whole-number");
        Assert.Equal(line, found.Line);

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

        Assert.True(wrong.Shows(Behaviour.Printing(mistakePrints)), wrong.Said(languageName));
        Assert.True(right.Shows(Behaviour.Printing("12.35")), right.Said(languageName));
    }
}
