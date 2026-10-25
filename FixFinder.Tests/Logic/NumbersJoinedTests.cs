using FixFinder.Core;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>Text and then two numbers joined with +: found where both are numbers, and left alone where the + means what it says.</summary>
public class NumbersJoinedPatternTests : IDisposable
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
        return LogicPatterns.Scan(SourceFile.Read(path)!).Where(finding => finding.PatternId.EndsWith("numbers-joined-as-text", StringComparison.Ordinal)).ToList();
    }

    [Theory]
    // Two pieces of text joined: + joins them, as meant.
    [InlineData("Main.java", "class Main { public static void main(String[] a) {\n    String first = \"Ada\";\n    String last = \"Lovelace\";\n    System.out.println(\"Name: \" + first + last);\n} }\n")]
    // The numbers already in brackets.
    [InlineData("main.js", "const first = 70;\nconst second = 80;\nconsole.log(\"Total: \" + (first + second));\n")]
    // Text between the numbers: written side by side on purpose.
    [InlineData("main.js", "const first = 70;\nconst second = 80;\nconsole.log(\"Marks: \" + first + \", \" + second);\n")]
    // A number of unknown kind - a method's result - is not taken for one.
    [InlineData("main.cs", "int first = 70;\nConsole.WriteLine(\"Total: \" + first + Next());\nstatic int Next() => 1;\n")]
    // The second multiplied: * binds more tightly, and is another sum.
    [InlineData("main.js", "const first = 70;\nconst rate = 2;\nconsole.log(\"Total: \" + first + rate * 2);\n")]
    // Text in Scala and PowerShell, and a PowerShell number's property, whose value is not known here.
    [InlineData("main.scala", "@main def run(): Unit =\n  val first = \"Ada\"\n  val last = \"Lovelace\"\n  println(\"Name: \" + first + last)\n")]
    [InlineData("main.ps1", "$first = \"Ada\"\n$last = \"Lovelace\"\nWrite-Output (\"Name: \" + $first + $last)\n")]
    [InlineData("main.ps1", "$first = 70\n$marks = @(1, 2)\nWrite-Output (\"Total: \" + $first + $marks.Count)\n")]
    public void JoiningThatMeansWhatItSaysIsLeftAlone(string fileName, string code) => Assert.Empty(Scan(fileName, code));

    [Theory]
    [InlineData("Main.java", "public class Main {\n    public static void main(String[] args) {\n        int first = 70;\n        int second = 80;\n        System.out.println(\"Total: \" + first + second);\n    }\n}\n", 5, "        System.out.println(\"Total: \" + (first + second));")]
    [InlineData("main.cs", "int count = 3;\nConsole.WriteLine(\"Next: \" + count + 1);\n", 2, "Console.WriteLine(\"Next: \" + (count + 1));")]
    [InlineData("main.js", "let count = 3;\nconsole.log(\"Next: \" + count + 1);\n", 2, "console.log(\"Next: \" + (count + 1));")]
    [InlineData("main.scala", "@main def run(): Unit =\n  val first = 70\n  val second: Int = 80\n  println(\"Total: \" + first + second)\n", 4, "  println(\"Total: \" + (first + second))")]
    [InlineData("main.ps1", "$first = 70\n$second = 80\nWrite-Output (\"Total: \" + $first + $second)\n", 3, "Write-Output (\"Total: \" + ($first + $second))")]
    [InlineData("main.ps1", "[int]$count = 3\nWrite-Output (\"Next: \" + $count + 1)\n", 2, "Write-Output (\"Next: \" + ($count + 1))")]
    public void TextThenTwoNumbersIsFoundAndTheNumbersAreAddedFirst(string fileName, string code, int line, string changedTo)
    {
        var found = Assert.Single(Scan(fileName, code));
        Assert.Equal(line, found.Line);
        Assert.Equal(changedTo, found.Fix!.NewLines.Single());
    }
}

/// <summary>
/// The mistake and its change run with each language's own toolchain, as FixFinder Learn runs a lesson: the mistake prints the
/// numbers side by side, and the change their sum. A language this computer cannot run is passed over, and said to be.
/// </summary>
public class NumbersJoinedLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    public static TheoryData<string, string, string, int, string, string> Mistakes => new()
    {
        { "Java", "Main.java", "public class Main {\n    public static void main(String[] args) {\n        int first = 70;\n        int second = 80;\n        System.out.println(\"Total: \" + first + second);\n    }\n}\n", 5, "Total: 7080", "Total: 150" },
        { "C#", "main.cs", "int count = 3;\nConsole.WriteLine(\"Next: \" + count + 1);\n", 2, "Next: 31", "Next: 4" },
        { "JavaScript", "main.js", "const first = 70;\nconst second = 80;\nconsole.log(\"Total: \" + first + second);\n", 3, "Total: 7080", "Total: 150" },
        { "TypeScript", "main.ts", "const first: number = 70;\nconst second: number = 80;\nconsole.log(\"Total: \" + first + second);\n", 3, "Total: 7080", "Total: 150" },
        { "Scala", "main.scala", "@main def run(): Unit =\n  val first = 70\n  val second = 80\n  println(\"Total: \" + first + second)\n", 4, "Total: 7080", "Total: 150" },
        { "PowerShell", "main.ps1", "$first = 70\n$second = 80\nWrite-Output (\"Total: \" + $first + $second)\n", 3, "Total: 7080", "Total: 150" },
    };

    [Theory]
    [MemberData(nameof(Mistakes))]
    public async Task TheMistakePrintsTheNumbersSideBySideAndTheChangeTheirSum(string languageName, string fileName, string code, int line, string mistakePrints, string changePrints)
    {
        var path = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(path, code);

        var language = CodeLanguage.All.Single(each => each.Name == languageName);
        var found = Assert.Single(LogicPatterns.Scan(SourceFile.Read(path)!), finding => finding.PatternId.EndsWith("numbers-joined-as-text", StringComparison.Ordinal));
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
