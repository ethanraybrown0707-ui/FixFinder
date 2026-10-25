using FixFinder.Core;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// A printf code - %d, %s - given to a print that fills nothing in: found where it is, with the language's own way of filling it
/// in, and left alone where the text is filled in already or the codes and values do not pair up.
/// </summary>
public class FormatCodePatternTests : IDisposable
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
        return LogicPatterns.Scan(SourceFile.Read(path)!).Where(finding => finding.PatternId.EndsWith("format-code-printed", StringComparison.Ordinal)).ToList();
    }

    [Theory]
    [InlineData("main.py", "total = 150\nprint(\"Total: %d\", total)\n", 2, "print(\"Total: %d\" % total)")]
    [InlineData("main.py", "name = \"Ada\"\nmark = 70\nprint(\"%s scored %d\", name, mark + 5)\n", 3, "print(\"%s scored %d\" % (name, mark + 5))")]
    // A value that is more than a name is bracketed, as % comes before /; print's own keyword arguments stay after it.
    [InlineData("main.py", "total = 150\nprint(\"Average: %.2f\", total / 7, end=\"!\\n\")\n", 2, "print(\"Average: %.2f\" % (total / 7), end=\"!\\n\")")]
    [InlineData("main.rb", "total = 150\nputs \"Total: %d\", total\n", 2, "puts format(\"Total: %d\", total)")]
    [InlineData("main.rb", "puts(\"%s scored %d\", name, mark)\n", 1, "puts(format(\"%s scored %d\", name, mark))")]
    [InlineData("main.php", "<?php\n$total = 150;\necho \"Total: %d\", $total, \"\\n\";\n", 3, "echo sprintf(\"Total: %d\", $total), \"\\n\";")]
    [InlineData("main.lua", "local total = 150\nprint(\"Total: %d\", total)\n", 2, "print(string.format(\"Total: %d\", total))")]
    [InlineData("main.lua", "io.write(\"%s scored %d\", name, mark, \"\\n\")\n", 1, "io.write(string.format(\"%s scored %d\", name, mark), \"\\n\")")]
    [InlineData("main.pl", "my $total = 150;\nprint \"Total: %d\", $total, \"\\n\";\n", 2, "print sprintf(\"Total: %d\", $total), \"\\n\";")]
    [InlineData("main.pl", "print(\"%s scored %d\\n\", $name, $mark);\n", 1, "print(sprintf(\"%s scored %d\\n\", $name, $mark));")]
    [InlineData("main.ps1", "$total = 150\nWrite-Host \"Total: %d\" $total\n", 2, "Write-Host (\"Total: {0}\" -f $total)")]
    [InlineData("main.ps1", "Write-Output \"%s scored %5.1f\" $name $mark\n", 1, "Write-Output (\"{0} scored {1,5:F1}\" -f $name, $mark)")]
    [InlineData("Program.cs", "int total = 150;\nConsole.WriteLine(\"Total: %d\", total);\n", 2, "Console.WriteLine(\"Total: {0}\", total);")]
    [InlineData("Program.cs", "Console.Write(\"%-6s|%.2f\", name, price);\n", 1, "Console.Write(\"{0,-6}|{1:F2}\", name, price);")]
    public void APrintfCodeGivenToAPrintIsFoundAndFilledInTheLanguagesOwnWay(string fileName, string code, int line, string changedTo)
    {
        var found = Assert.Single(Scan(fileName, code));

        Assert.Equal(line, found.Line);
        Assert.Equal(changedTo, found.Fix!.NewLines.Single());
    }

    [Theory]
    // %x has no form .NET's {0} writes the same way, and a brace in the text would be read as a place to fill in.
    [InlineData("Program.cs", "Console.WriteLine(\"Hex: %x\", value);\n")]
    [InlineData("Program.cs", "Console.WriteLine(\"{x} %d\", value);\n")]
    [InlineData("main.ps1", "Write-Host \"Code: %x\" $value\n")]
    public void WhereNoPlaceForTheValueFitsItIsFoundWithNoChange(string fileName, string code)
    {
        var found = Assert.Single(Scan(fileName, code));

        Assert.Null(found.Fix);
    }

    [Fact]
    public void TheMessageSaysHowTheLineCameOut()
    {
        var found = Assert.Single(Scan("main.rb", "total = 150\nputs \"Total: %d\", total\n"));

        Assert.Equal("puts does not fill in %d - it prints the text as it is, so the line comes out as `Total: %d` with the value of total on a line of " +
                     "its own after it", found.Message);
    }

    [Theory]
    // Filled in already.
    [InlineData("main.py", "total = 150\nprint(\"Total: %d\" % total)\n")]
    [InlineData("main.rb", "total = 150\nputs \"Total: %d\" % total\n")]
    [InlineData("main.php", "<?php\n$total = 150;\nprintf(\"Total: %d\\n\", $total);\n")]
    [InlineData("main.lua", "local total = 150\nprint(string.format(\"Total: %d\", total))\n")]
    [InlineData("main.pl", "my $total = 150;\nprintf(\"Total: %d\\n\", $total);\n")]
    [InlineData("main.ps1", "$total = 150\nWrite-Host (\"Total: {0}\" -f $total)\n")]
    [InlineData("Program.cs", "int total = 150;\nConsole.WriteLine(\"Total: {0}\", total);\n")]
    // No code: a % sign followed by a space, or by nothing a code ends with.
    [InlineData("main.py", "total = 150\nprint(\"Rate: 100% done\", total)\n")]
    [InlineData("main.py", "total = 150\nprint(\"Total:\", total)\n")]
    // Codes and values that do not pair up.
    [InlineData("main.py", "print(\"%d and %d\", first)\n")]
    [InlineData("Program.cs", "Console.WriteLine(\"Total: %d\");\n")]
    // Not a print of the text: a filehandle first, or another parameter of Write-Host's after the values.
    [InlineData("main.pl", "print STDERR \"Total: %d\", $total;\n")]
    [InlineData("main.ps1", "Write-Host \"Total: %d\" $total -ForegroundColor Red\n")]
    // Inside text, and inside a comment.
    [InlineData("main.py", "note = 'print(\"Total: %d\", total)'\n")]
    [InlineData("main.lua", "-- print(\"Total: %d\", total)\n")]
    public void TextFilledInAlreadyOrCodesThatDoNotPairUpAreLeftAlone(string fileName, string code) =>
        Assert.Empty(Scan(fileName, code));
}

/// <summary>
/// Each printf code given to a print, run with the language itself the way FixFinder Learn runs a lesson, and the check's own
/// change run the same way. A language this computer cannot run is passed over, and said to be.
/// </summary>
public class FormatCodeLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Each: the language, its file, the mistake, what it prints, and what its change prints.</summary>
    public static TheoryData<string, string, string, string, string> Mistakes => new()
    {
        { "Python", "main.py", "total = 150\nprint(\"Total: %d\", total)\n", "Total: %d 150", "Total: 150" },
        { "Python", "main.py", "name = \"Ada\"\nmark = 70\nprint(\"%s scored %.1f\", name, mark / 3)\n", "%s scored %.1f Ada 23.333333333333332", "Ada scored 23.3" },
        { "Ruby", "main.rb", "total = 150\nputs \"Total: %d\", total\n", "Total: %d\n150", "Total: 150" },
        { "PHP", "main.php", "<?php\n$total = 150;\necho \"Total: %d\", $total, \"\\n\";\n", "Total: %d150", "Total: 150" },
        { "Lua", "main.lua", "local total = 150\nprint(\"Total: %d\", total)\n", "Total: %d\t150", "Total: 150" },
        { "Perl", "main.pl", "use strict;\nuse warnings;\n\nmy $total = 150;\nprint \"Total: %d\", $total, \"\\n\";\n", "Total: %d150", "Total: 150" },
        { "PowerShell", "main.ps1", "$total = 150\nWrite-Output \"Total: %d\" $total\n", "Total: %d\n150", "Total: 150" },
        { "C#", "Program.cs", "int total = 150;\nConsole.WriteLine(\"Total: %d\", total);\n", "Total: %d", "Total: 150" },
    };

    [Theory]
    [MemberData(nameof(Mistakes))]
    public async Task TheMistakePrintsTheCodeAsItIsAndTheChangeFillsItIn(string language, string fileName, string mistake, string mistakePrints, string changePrints)
    {
        var path = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(path, mistake);
        var found = Assert.Single(LogicPatterns.Scan(SourceFile.Read(path)!), finding => finding.PatternId.EndsWith("format-code-printed", StringComparison.Ordinal));

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
