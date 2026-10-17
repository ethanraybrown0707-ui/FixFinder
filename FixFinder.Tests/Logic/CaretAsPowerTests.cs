using FixFinder.Core;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>^ written as a power: found where a name or a bracketed sum is raised to 2 or 3, with its change, and left alone elsewhere.</summary>
public class CaretAsPowerPatternTests : IDisposable
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
    [InlineData("area.c", "    int area = side ^ 2;", "logic-caret-as-power", "    int area = side * side;")]
    [InlineData("Area.java", "        int volume = side ^ 3;", "logic-caret-as-power", "        int volume = side * side * side;")]
    [InlineData("area.cpp", "    int square = (a + b) ^ 2;", "logic-caret-as-power", "    int square = (a + b) * (a + b);")]
    [InlineData("area.js", "const area = rect.width ^ 2;", "logic-caret-as-power", "const area = rect.width * rect.width;")]
    [InlineData("main.go", "\tarea := marks[i] ^ 2", "logic-go-caret-as-power", "\tarea := marks[i] * marks[i]")]
    [InlineData("main.rs", "    let area = side ^ 2;", "logic-rust-caret-as-power", "    let area = side * side;")]
    [InlineData("main.dart", "  var area = side ^ 2;", "logic-dart-caret-as-power", "  var area = side * side;")]
    [InlineData("area.py", "area = side ^ 2", "logic-python-caret-as-power", "area = side ** 2")]
    [InlineData("area.rb", "area = (a + b) ^ 2", "logic-ruby-caret-as-power", "area = (a + b) ** 2")]
    [InlineData("area.php", "$area = $side ^ 3;", "logic-php-caret-as-power", "$area = $side ** 3;")]
    [InlineData("area.pl", "my $area = $shape->{side} ^ 2;", "logic-perl-caret-as-power", "my $area = $shape->{side} ** 2;")]
    public void ANameRaisedWithACaretIsFoundAndRaisedProperly(string fileName, string line, string check, string fixedLine)
    {
        var raised = Assert.Single(Scan(fileName, line + "\n"), finding => finding.PatternId == check);

        Assert.Equal(fixedLine, raised.Fix?.NewLines.Single());
    }

    [Theory]
    // Exclusive or of two names, or with a mask, is what ^ is for.
    [InlineData("check.c", "    int flipped = flags ^ mask;")]
    [InlineData("check.c", "    flags ^= 2;")]
    // Two plain numbers are gcc's own warning, -Wxor-used-as-pow.
    [InlineData("check.c", "    int big = 2 ^ 8;")]
    [InlineData("check.py", "area = side ^ 4")]
    [InlineData("check.py", "area = side ^ 2.5")]
    [InlineData("check.py", "print(\"side ^ 2\")")]
    [InlineData("check.js", "// side ^ 2 is not a square")]
    // Lua's ^ is a power.
    [InlineData("check.lua", "local area = side ^ 2")]
    public void ExclusiveOrOrAPowerThatIsOneIsLeftAlone(string fileName, string line)
    {
        Assert.DoesNotContain(Scan(fileName, line + "\n"), finding => finding.PatternId.EndsWith("caret-as-power", StringComparison.Ordinal));
    }
}

/// <summary>
/// What ^ written as a power prints, run with each language's own toolchain the way FixFinder Learn runs a lesson, and what
/// the check's own change prints instead. A language this computer cannot run is passed over, and said to be.
/// </summary>
public class CaretAsPowerLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Each: the language, its file, a program printing side ^ 2 (or 3) for side = 5, and what that prints.</summary>
    public static TheoryData<string, string, string, string> Mistakes => new()
    {
        { "C", "main.c", "#include <stdio.h>\n\nint main(void) {\n    int side = 5;\n    printf(\"%d\\n\", side ^ 2);\n    return 0;\n}\n", "7" },
        { "C++", "main.cpp", "#include <iostream>\n\nint main() {\n    int a = 2;\n    int b = 3;\n    std::cout << ((a + b) ^ 2) << '\\n';\n    return 0;\n}\n", "7" },
        { "Java", "Main.java", "public class Main {\n    public static void main(String[] args) {\n        int side = 5;\n        System.out.println(side ^ 3);\n    }\n}\n", "6" },
        { "C#", "Program.cs", "int side = 5;\nConsole.WriteLine(side ^ 2);\n", "7" },
        { "JavaScript", "main.js", "const side = 5;\nconsole.log(side ^ 2);\n", "7" },
        { "Go", "main.go", "package main\n\nimport \"fmt\"\n\nfunc main() {\n\tside := 5\n\tfmt.Println(side ^ 2)\n}\n", "7" },
        { "Rust", "main.rs", "fn main() {\n    let side = 5;\n    println!(\"{}\", side ^ 2);\n}\n", "7" },
        { "Dart", "main.dart", "void main() {\n  var side = 5;\n  print(side ^ 2);\n}\n", "7" },
        { "Python", "main.py", "side = 5\nprint(side ^ 2)\n", "7" },
        { "Ruby", "main.rb", "side = 5\nputs side ^ 2\n", "7" },
        { "PHP", "main.php", "<?php\n$side = 5;\necho $side ^ 2;\n", "7" },
        { "Perl", "main.pl", "use strict;\nuse warnings;\n\nmy $side = 5;\nprint $side ^ 2, \"\\n\";\n", "7" },
    };

    [Theory]
    [MemberData(nameof(Mistakes))]
    public async Task TheMistakePrintsExclusiveOrAndTheChangeThePower(string language, string fileName, string mistake, string mistakePrints)
    {
        var path = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(path, mistake);
        var raised = Assert.Single(LogicPatterns.Scan(SourceFile.Read(path)!), finding => finding.PatternId.EndsWith("caret-as-power", StringComparison.Ordinal));

        var lines = mistake.TrimEnd('\n').Split('\n').ToList();
        lines.RemoveRange(raised.Fix!.StartLine - 1, raised.Fix.RemoveCount);
        lines.InsertRange(raised.Fix.StartLine - 1, raised.Fix.NewLines);
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
        Assert.True(right.Shows(Behaviour.Printing(mistakePrints == "6" ? "125" : "25")), right.Said(language));
    }
}
