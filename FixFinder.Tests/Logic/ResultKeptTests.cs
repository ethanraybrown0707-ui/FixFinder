using FixFinder.Core;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// What a method that changes a value in place gives back, kept as the changed value: found where it is, with its change, and
/// left alone where the value itself is used, or what is kept is never read.
/// </summary>
public class ResultKeptPatternTests : IDisposable
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

    private static readonly string[] Checks = ["logic-php-sort-result-kept", "logic-ruby-bang-result-kept", "logic-php-push-result-kept"];

    [Theory]
    [InlineData("main.php", "<?php\n$marks = [90, 70, 80];\n$sorted = sort($marks);\necho json_encode($sorted);\n", 3, "sort($marks);\n$sorted = $marks;")]
    // The array's own name given what sort gives back: sorting it is all the line needs.
    [InlineData("main.php", "<?php\n$names = [\"Bob\", \"Ada\"];\n$names = rsort($names);\nvar_dump($names);\n", 3, "rsort($names);")]
    [InlineData("main.php", "<?php\nfunction ordered($ages) {\n    $byAge = usort($ages, fn($a, $b) => $a <=> $b);\n    return $byAge;\n}\n", 3, "    usort($ages, fn($a, $b) => $a <=> $b);\n    $byAge = $ages;")]
    // Read only inside text.
    [InlineData("main.php", "<?php\n$marks = [2, 1];\n$mixed = shuffle($marks);\necho \"Now: $mixed\\n\";\n", 3, "shuffle($marks);\n$mixed = $marks;")]
    // A ! method on a name not read again, or on what gets gives back: the copy without the !.
    [InlineData("main.rb", "name = \"Ada\".dup\nfixed = name.strip!\nputs fixed\n", 2, "fixed = name.strip")]
    [InlineData("main.rb", "line = gets.chomp!\nputs \"Read #{line}\"\n", 1, "line = gets.chomp")]
    // A name read again: the ! method on a line of its own, then the name given the value it changed.
    [InlineData("main.rb", "marks = [70, 80]\npassed = marks.reject! { |mark| mark < 40 }\np passed\np marks\n", 2, "marks.reject! { |mark| mark < 40 }\npassed = marks")]
    [InlineData("main.rb", "text = \"a-b\".dup\nspaced = text.gsub!(\"-\", \" \")\nputs spaced\nputs text\n", 2, "text.gsub!(\"-\", \" \")\nspaced = text")]
    // The array given the count array_push and array_unshift give back: adding is all the line needs.
    [InlineData("main.php", "<?php\n$marks = [70, 80];\n$marks = array_push($marks, 90);\nprint_r($marks);\n", 3, "array_push($marks, 90);")]
    [InlineData("main.php", "<?php\n$marks = [80];\n    $marks = array_unshift($marks, 70);\n", 3, "    array_unshift($marks, 70);")]
    public void WhatAnInPlaceMethodGivesBackKeptIsFoundAndTheValueItselfKept(string fileName, string code, int line, string changedTo)
    {
        var found = Assert.Single(Scan(fileName, code), finding => Checks.Contains(finding.PatternId));

        Assert.Equal(line, found.Line);
        Assert.Equal(changedTo, string.Join("\n", found.Fix!.NewLines));
    }

    [Theory]
    // Sorted on a line of its own, then the array used.
    [InlineData("main.php", "<?php\n$marks = [90, 70, 80];\nsort($marks);\necho implode(\" \", $marks);\n")]
    // What sort gave back never read.
    [InlineData("main.php", "<?php\n$marks = [2, 1];\n$done = sort($marks);\n")]
    // A function of the program's own, whose name only starts with sort.
    [InlineData("main.php", "<?php\nfunction sorted($marks) { return $marks; }\n$list = sorted([2, 1]);\necho $list[0];\n")]
    // array_push's count given to a name of its own, which is what it gives back for.
    [InlineData("main.php", "<?php\n$marks = [70];\n$count = array_push($marks, 80);\necho $count;\n")]
    // The ! method on a line of its own, or the copy kept.
    [InlineData("main.rb", "name = \"Ada\".dup\nname.strip!\nputs name\n")]
    [InlineData("main.rb", "name = \" Ada \"\nfixed = name.strip\nputs fixed\n")]
    // sort! gives back the array every time.
    [InlineData("main.rb", "marks = [80, 70]\nsorted = marks.sort!\np sorted\n")]
    // What the ! method gave back never read.
    [InlineData("main.rb", "marks = [70, 70]\nunique = marks.uniq!\np marks\n")]
    // A method called on what it gave back: not a line the change can rewrite.
    [InlineData("main.rb", "name = \"Ada\".dup\nfixed = name.strip!.to_s\nputs fixed\n")]
    public void TheValueItselfUsedOrWhatIsKeptNeverReadIsLeftAlone(string fileName, string code) =>
        Assert.DoesNotContain(Scan(fileName, code), finding => Checks.Contains(finding.PatternId));
}

/// <summary>
/// Each kept result run with the language itself, as FixFinder Learn runs a lesson, and the check's own change run the same
/// way. A language this computer cannot run is passed over, and said to be.
/// </summary>
public class ResultKeptLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    public static TheoryData<string, string, string, string, int, string, string> Mistakes => new()
    {
        { "PHP", "main.php", "<?php\n$marks = [90, 70, 80];\n$sorted = sort($marks);\necho json_encode($sorted), \"\\n\";\n", "logic-php-sort-result-kept", 3, "true", "[70,80,90]" },
        { "PHP", "main.php", "<?php\n$names = [\"Ada\", \"Bob\"];\n$names = rsort($names);\necho json_encode($names), \"\\n\";\n", "logic-php-sort-result-kept", 3, "true", "[\"Bob\",\"Ada\"]" },
        { "Ruby", "main.rb", "name = \"Ada\".dup\nfixed = name.strip!\np fixed\n", "logic-ruby-bang-result-kept", 2, "nil", "\"Ada\"" },
        { "PHP", "main.php", "<?php\n$marks = [70, 80];\n$marks = array_push($marks, 90);\necho json_encode($marks), \"\\n\";\n", "logic-php-push-result-kept", 3, "3", "[70,80,90]" },
        { "Ruby", "main.rb", "marks = [70, 80]\npassed = marks.reject! { |mark| mark < 40 }\np passed\np marks\n", "logic-ruby-bang-result-kept", 2, "nil\n[70, 80]", "[70, 80]\n[70, 80]" },
        { "Ruby", "main.rb", "text = \"Ada\".dup\nchanged = text.gsub!(\"x\", \"y\")\np changed\n", "logic-ruby-bang-result-kept", 2, "nil", "\"Ada\"" },
    };

    [Theory]
    [MemberData(nameof(Mistakes))]
    public async Task TheMistakePrintsWhatTheMethodGaveBackAndTheChangeTheValue(string languageName, string fileName, string code, string check, int line, string mistakePrints, string changePrints)
    {
        var path = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(path, code);

        var found = Assert.Single(LogicPatterns.Scan(SourceFile.Read(path)!), finding => finding.PatternId == check);
        Assert.Equal(line, found.Line);

        var lines = code.TrimEnd('\n').Split('\n').ToList();
        lines.RemoveRange(found.Fix!.StartLine - 1, found.Fix.RemoveCount);
        lines.InsertRange(found.Fix.StartLine - 1, found.Fix.NewLines);
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
        Assert.True(right.Shows(Behaviour.Printing(changePrints)), right.Said(languageName));
    }
}
