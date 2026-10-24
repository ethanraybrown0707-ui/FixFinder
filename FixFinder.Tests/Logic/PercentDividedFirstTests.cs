using FixFinder.Core;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// A percentage of two whole numbers worked out by dividing first: found where both are whole numbers, with the multiplication
/// moved first, and left alone where either may hold a fraction or the order is already right.
/// </summary>
public class PercentDividedFirstPatternTests : IDisposable
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
        return LogicPatterns.Scan(SourceFile.Read(path)!).Where(finding => finding.PatternId.EndsWith("percent-divided-first", StringComparison.Ordinal)).ToList();
    }

    [Theory]
    [InlineData("main.c", "int main(void) {\n    int score = 45, total = 60;\n    int percent = score / total * 100;\n    return percent;\n}\n", 3, "    int percent = score * 100 / total;")]
    [InlineData("main.cpp", "int main() {\n    int score = 45;\n    int total = 60;\n    std::cout << (score / total) * 100 << '\\n';\n}\n", 4, "    std::cout << score * 100 / total << '\\n';")]
    [InlineData("Main.java", "class Main {\n    static double share(int score, int[] marks) {\n        return score / marks.length * 100.0;\n    }\n}\n", 3, "        return score * 100.0 / marks.length;")]
    [InlineData("Program.cs", "int score = 45;\nint total = 60;\nConsole.WriteLine(score / total * 100);\n", 3, "Console.WriteLine(score * 100 / total);")]
    [InlineData("main.go", "package main\n\nfunc main() {\n\tscore := 45\n\tmarks := []int{1, 2}\n\tprintln(score / len(marks) * 100)\n}\n", 6, "\tprintln(score * 100 / len(marks))")]
    [InlineData("main.rs", "fn main() {\n    let score = 45;\n    let total: i32 = 60;\n    let percent = score / total * 100;\n}\n", 4, "    let percent = score * 100 / total;")]
    [InlineData("main.scala", "@main def run(): Unit =\n  val score = 45\n  val total: Int = 60\n  println(score / total * 100)\n", 4, "  println(score * 100 / total)")]
    [InlineData("main.rb", "score = gets.to_i\ntotal = 60\nputs score / total * 100\n", 3, "puts score * 100 / total")]
    [InlineData("main.fsx", "let score = 45\nlet total = 60\nprintfn \"%d\" (score / total * 100)\n", 3, "printfn \"%d\" (score * 100 / total)")]
    [InlineData("main.f90", "program marks\n  implicit none\n  integer :: score, total\n  score = 45\n  total = 60\n  print *, score / total * 100\nend program marks\n", 6, "  print *, score * 100 / total")]
    public void APercentageOfWholeNumbersThatDividesFirstIsFoundAndMultipliesFirst(string fileName, string code, int line, string changedTo)
    {
        var found = Assert.Single(Scan(fileName, code));

        Assert.Equal(line, found.Line);
        Assert.Equal(changedTo, found.Fix!.NewLines.Single());
    }

    [Theory]
    // Multiplied first already.
    [InlineData("main.c", "int main(void) {\n    int score = 45, total = 60;\n    return score * 100 / total;\n}\n")]
    // A number that may hold a fraction: its division keeps it.
    [InlineData("Main.java", "class Main {\n    static double share(double score, int total) {\n        return score / total * 100;\n    }\n}\n")]
    [InlineData("main.rb", "score = 45.0\ntotal = 60\nputs score / total * 100\n")]
    [InlineData("main.rs", "fn main() {\n    let score = 45.0;\n    let total = 60.0;\n    println!(\"{}\", score / total * 100.0);\n}\n")]
    [InlineData("main.f90", "program marks\n  implicit none\n  real :: score, total\n  score = 45\n  total = 60\n  print *, score / total * 100\nend program marks\n")]
    // A name the file never declares, whose kind is not known here.
    [InlineData("main.go", "package main\n\nfunc share(score, total float64) float64 {\n\treturn score / total * 100\n}\n")]
    // Something multiplied or divided just before it, which makes it another sum.
    [InlineData("Program.cs", "int score = 45;\nint total = 60;\nint weight = 2;\nConsole.WriteLine(weight * score / total * 100);\n")]
    // Inside text, and inside a comment.
    [InlineData("main.c", "int main(void) {\n    int score = 45, total = 60;\n    // score / total * 100 is a percentage\n    return 0;\n}\n")]
    public void APercentageThatKeepsItsFractionIsLeftAlone(string fileName, string code) => Assert.Empty(Scan(fileName, code));
}

/// <summary>
/// Each percentage that divides first, run with the language's own toolchain the way FixFinder Learn runs a lesson, and the
/// check's own change run the same way. A language this computer cannot run is passed over, and said to be.
/// </summary>
public class PercentDividedFirstLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Each: the language, its file, a program printing 45 out of 60 as a percentage, what that prints, and what its change prints.</summary>
    public static TheoryData<string, string, string, string, string> Mistakes => new()
    {
        { "C", "main.c", "#include <stdio.h>\n\nint main(void) {\n    int score = 45;\n    int total = 60;\n    printf(\"%d\\n\", score / total * 100);\n    return 0;\n}\n", "0", "75" },
        { "C++", "main.cpp", "#include <iostream>\n\nint main() {\n    int score = 45;\n    int total = 60;\n    std::cout << (score / total) * 100 << '\\n';\n    return 0;\n}\n", "0", "75" },
        { "Java", "Main.java", "public class Main {\n    public static void main(String[] args) {\n        int score = 45;\n        int total = 60;\n        System.out.println(score / total * 100.0);\n    }\n}\n", "0.0", "75.0" },
        { "C#", "Program.cs", "int score = 45;\nint total = 60;\nConsole.WriteLine(score / total * 100);\n", "0", "75" },
        { "Go", "main.go", "package main\n\nimport \"fmt\"\n\nfunc main() {\n\tscore := 45\n\ttotal := 60\n\tfmt.Println(score / total * 100)\n}\n", "0", "75" },
        { "Rust", "main.rs", "fn main() {\n    let score = 45;\n    let total = 60;\n    println!(\"{}\", score / total * 100);\n}\n", "0", "75" },
        { "Scala", "main.scala", "@main def run(): Unit =\n  val score = 45\n  val total = 60\n  println(score / total * 100)\n", "0", "75" },
        { "Ruby", "main.rb", "score = 45\ntotal = 60\nputs score / total * 100\n", "0", "75" },
        { "F#", "main.fsx", "let score = 45\nlet total = 60\nprintfn \"%d\" (score / total * 100)\n", "0", "75" },
        { "Fortran", "main.f90", "program marks\n  implicit none\n  integer :: score, total\n  score = 45\n  total = 60\n  print *, score / total * 100\nend program marks\n", "           0", "          75" },
    };

    [Theory]
    [MemberData(nameof(Mistakes))]
    public async Task TheMistakePrintsNoughtAndTheChangeTheRightPercentage(string language, string fileName, string mistake, string mistakePrints, string changePrints)
    {
        var path = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(path, mistake);
        var found = Assert.Single(LogicPatterns.Scan(SourceFile.Read(path)!), finding => finding.PatternId.EndsWith("percent-divided-first", StringComparison.Ordinal));

        var lines = mistake.TrimEnd('\n').Split('\n').ToList();
        lines.RemoveRange(found.Fix!.StartLine - 1, found.Fix.RemoveCount);
        lines.InsertRange(found.Fix.StartLine - 1, found.Fix.NewLines);
        var changed = string.Join("\n", lines) + "\n";

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
        Assert.True(right.Shows(Behaviour.Printing(changePrints)), right.Said(language));
    }
}
