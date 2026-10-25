using FixFinder.Core;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Mistakes eight languages run without a word, each run with the language's own toolchain the way FixFinder Learn runs a
/// lesson, and the check's own change run the same way: the mistake prints what it was seen to, and the change what was
/// meant. A language this computer cannot run is passed over, and said to be.
/// </summary>
public class SilentMistakeLiveTests(ITestOutputHelper output)
{
    /// <summary>Each: the language, the check, the mistake, what it prints, and what its change prints.</summary>
    public static TheoryData<string, string, string, string, string> Mistakes => new()
    {
        {
            "C#", "logic-csharp-chars-added", "char grade = 'A';\nConsole.WriteLine(grade + 1);\n", "66", "B"
        },
        {
            "C#", "logic-csharp-chars-added", "Console.WriteLine('a' + 'b');\n", "195", "ab"
        },
        {
            "Java", "logic-java-boolean-assigned-in-condition",
            "public class Main {\n    public static void main(String[] args) {\n        boolean done = false;\n        if (done = true) {\n            System.out.println(\"done\");\n        }\n        System.out.println(done);\n    }\n}\n",
            "done\ntrue", "false"
        },
        {
            "Python", "logic-python-or-before-in", "word = \"sky\"\nif \"a\" or \"e\" in word:\n    print(\"has a vowel\")\nelse:\n    print(\"no vowel\")\n", "has a vowel", "no vowel"
        },
        {
            "Ruby", "logic-ruby-each-with-index-order", "marks = [70, 80]\nmarks.each_with_index do |index, mark|\n  puts \"#{index}: #{mark}\"\nend\n", "70: 0\n80: 1", "0: 70\n1: 80"
        },
        {
            "PHP", "logic-php-search-as-condition", "<?php\n$word = \"apple\";\nif (strpos($word, \"a\")) {\n    echo \"has an a\\n\";\n} else {\n    echo \"no a\\n\";\n}\n", "no a", "has an a"
        },
        {
            "Perl", "logic-perl-loop-to-last-index", "use strict;\nuse warnings;\nmy @marks = (70, 80, 90);\nfor (my $i = 0; $i < $#marks; $i++) {\n    print $marks[$i], \"\\n\";\n}\n", "70\n80", "70\n80\n90"
        },
        {
            "Fortran", "logic-fortran-single-precision-constant",
            "program marks\n  implicit none\n  double precision :: rate\n  rate = 0.1\n  print *, rate\nend program marks\n", "  0.10000000149011612", "  0.10000000000000001"
        },
        {
            "PowerShell", "logic-powershell-arguments-with-commas",
            "function Get-Area($width, $height) {\n    return $width * $height\n}\n$area = Get-Area 3, 4\nWrite-Output \"[$area]\"\n", "[]", "[12]"
        },
    };

    [Theory]
    [MemberData(nameof(Mistakes))]
    public async Task TheMistakePrintsWhatItWasSeenToAndTheChangeWhatWasMeant(string language, string check, string mistake, string mistakePrints, string changePrints)
    {
        var codeLanguage = CodeLanguage.All.Single(each => each.Name == language);
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, (language == "Java" ? "Main" : "main") + codeLanguage.Extensions[0]);
        File.WriteAllText(path, mistake);
        var found = Assert.Single(LogicPatterns.Scan(SourceFile.Read(path)!), finding => finding.PatternId == check);

        var lines = mistake.TrimEnd('\n').Split('\n').ToList();
        lines.RemoveRange(found.Fix!.StartLine - 1, found.Fix.RemoveCount);
        lines.InsertRange(found.Fix.StartLine - 1, found.Fix.NewLines);
        var changed = string.Join("\n", lines) + "\n";

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
