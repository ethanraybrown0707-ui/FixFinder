using FixFinder.Core;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>What compareTo gives back compared with 1 or -1: found with the sign asked for instead, and left alone where the sign is asked.</summary>
public class CompareToPatternTests : IDisposable
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
        return LogicPatterns.Scan(SourceFile.Read(path)!).Where(finding => finding.PatternId.EndsWith("compare-to-one", StringComparison.Ordinal)).ToList();
    }

    [Theory]
    [InlineData("Main.java", "class Main {\n    boolean before(String a, String b) { return a.compareTo(b) == -1; }\n}\n", "    boolean before(String a, String b) { return a.compareTo(b) < 0; }")]
    [InlineData("Main.java", "class Main {\n    boolean after(String a, String b) { return a.compareToIgnoreCase(b) == 1; }\n}\n", "    boolean after(String a, String b) { return a.compareToIgnoreCase(b) > 0; }")]
    [InlineData("Main.java", "class Main {\n    boolean notAfter(Integer a, Integer b) { return a.compareTo(b) != 1; }\n}\n", "    boolean notAfter(Integer a, Integer b) { return a.compareTo(b) <= 0; }")]
    [InlineData("main.scala", "@main def run(): Unit =\n  println(\"apple\".compareTo(\"cherry\") == -1)\n", "  println(\"apple\".compareTo(\"cherry\") < 0)")]
    public void CompareToComparedWithOneIsFoundAndAsksForTheSign(string fileName, string code, string changedTo)
    {
        var found = Assert.Single(Scan(fileName, code));

        Assert.Equal(2, found.Line);
        Assert.Equal(changedTo, found.Fix!.NewLines.Single());
    }

    [Theory]
    [InlineData("Main.java", "class Main {\n    boolean before(String a, String b) { return a.compareTo(b) < 0; }\n}\n")]
    [InlineData("Main.java", "class Main {\n    boolean same(String a, String b) { return a.compareTo(b) == 0; }\n}\n")]
    // Compared with 10, not 1.
    [InlineData("Main.java", "class Main {\n    boolean far(String a, String b) { return a.compareTo(b) == 10; }\n}\n")]
    public void ASignOrZeroAskedIsLeftAlone(string fileName, string code) => Assert.Empty(Scan(fileName, code));
}

/// <summary>compareTo compared with -1, run with javac and Scala CLI, and the check's own change run the same way.</summary>
public class CompareToLiveTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("Java", "Main.java", "public class Main {\n    public static void main(String[] args) {\n        System.out.println(\"apple\".compareTo(\"cherry\") == -1);\n    }\n}\n")]
    [InlineData("Scala", "main.scala", "@main def run(): Unit =\n  println(\"apple\".compareTo(\"cherry\") == -1)\n")]
    public async Task TheMistakePrintsFalseAndTheChangeTrue(string language, string fileName, string mistake)
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, fileName);
        File.WriteAllText(path, mistake);
        var found = Assert.Single(LogicPatterns.Scan(SourceFile.Read(path)!), finding => finding.PatternId.EndsWith("compare-to-one", StringComparison.Ordinal));

        var lines = mistake.TrimEnd('\n').Split('\n').ToList();
        lines[found.Fix!.StartLine - 1] = found.Fix.NewLines.Single();
        var changed = string.Join("\n", lines) + "\n";

        var codeLanguage = CodeLanguage.All.Single(each => each.Name == language);
        var wrong = await SnippetRunner.RunAsync(codeLanguage, mistake);
        if (!wrong.Ran)
        {
            output.WriteLine($"Not run on this computer: {wrong.WhyNotRun}");
            return;
        }

        var right = await SnippetRunner.RunAsync(codeLanguage, changed);
        Assert.True(wrong.Shows(Behaviour.Printing("false")), wrong.Said(language));
        Assert.True(right.Shows(Behaviour.Printing("true")), right.Said(language));
    }
}
