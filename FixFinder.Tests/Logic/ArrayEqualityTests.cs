using FixFinder.Core;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>Two arrays compared with equals or ==: found where both names are arrays, with their items compared instead, and left alone otherwise.</summary>
public class ArrayEqualityPatternTests : IDisposable
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
        return LogicPatterns.Scan(SourceFile.Read(path)!).Where(finding => finding.PatternId.EndsWith("array-equals", StringComparison.Ordinal)).ToList();
    }

    [Theory]
    [InlineData("Main.java", "class Main {\n    static boolean same(int[] first, int[] second) { return first.equals(second); }\n}\n", 2,
        "    static boolean same(int[] first, int[] second) { return java.util.Arrays.equals(first, second); }")]
    // java.util.Arrays brought in by name: written short.
    [InlineData("Main.java", "import java.util.Arrays;\nclass Main {\n    static boolean differ(String[] first, String[] second) { return first != second; }\n}\n", 3,
        "    static boolean differ(String[] first, String[] second) { return !Arrays.equals(first, second); }")]
    [InlineData("Program.cs", "int[] first = { 70, 80 };\nint[] second = { 70, 80 };\nConsole.WriteLine(first == second);\n", 3,
        "Console.WriteLine(System.Linq.Enumerable.SequenceEqual(first, second));")]
    [InlineData("Program.cs", "using System.Linq;\nint[] first = { 70, 80 };\nvar second = new int[2];\nConsole.WriteLine(first.Equals(second));\n", 4,
        "Console.WriteLine(first.SequenceEqual(second));")]
    public void TwoArraysComparedAsArraysAreFoundAndTheirItemsCompared(string fileName, string code, int line, string changedTo)
    {
        var found = Assert.Single(Scan(fileName, code));

        Assert.Equal(line, found.Line);
        Assert.Equal(changedTo, found.Fix!.NewLines.Single());
    }

    [Theory]
    // Text, numbers and lists, whose equals compares what they hold.
    [InlineData("Main.java", "class Main {\n    static boolean same(String first, String second) { return first.equals(second); }\n}\n")]
    [InlineData("Program.cs", "int first = 70;\nint second = 70;\nConsole.WriteLine(first == second);\n")]
    // One array checked against null, and the items compared already.
    [InlineData("Main.java", "class Main {\n    static boolean empty(int[] marks, int[] none) { return marks == null; }\n}\n")]
    [InlineData("Main.java", "import java.util.Arrays;\nclass Main {\n    static boolean same(int[] first, int[] second) { return Arrays.equals(first, second); }\n}\n")]
    public void ThingsThatAreNotTwoArraysAreLeftAlone(string fileName, string code) => Assert.Empty(Scan(fileName, code));
}

/// <summary>Two arrays compared with == and equals, run with javac and the .NET SDK, and the check's own change run the same way.</summary>
public class ArrayEqualityLiveTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("Java", "Main.java", "public class Main {\n    public static void main(String[] args) {\n        int[] first = {70, 80};\n        int[] second = {70, 80};\n        System.out.println(first.equals(second));\n    }\n}\n", "false", "true")]
    [InlineData("C#", "Program.cs", "int[] first = { 70, 80 };\nint[] second = { 70, 80 };\nConsole.WriteLine(first == second);\n", "False", "True")]
    public async Task TheMistakeSaysTheArraysDifferAndTheChangeTheyMatch(string language, string fileName, string mistake, string mistakePrints, string changePrints)
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, fileName);
        File.WriteAllText(path, mistake);
        var found = Assert.Single(LogicPatterns.Scan(SourceFile.Read(path)!), finding => finding.PatternId.EndsWith("array-equals", StringComparison.Ordinal));

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
        Assert.True(wrong.Shows(Behaviour.Printing(mistakePrints)), wrong.Said(language));
        Assert.True(right.Shows(Behaviour.Printing(changePrints)), right.Said(language));
    }
}
