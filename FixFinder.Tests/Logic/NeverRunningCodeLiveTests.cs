using FixFinder.Core;
using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Code no run can reach, each run with the language's own toolchain the way FixFinder Learn runs a lesson: the mistake shows
/// that the code never ran - a score of 75 got C, a loop counted 0, the general handler took the error - and the check's change,
/// run the same way, reaches it. Code with no change - after return, a second case, a function defined twice - is shown never
/// running. A language this computer cannot run, or a program Windows refused to start, is passed over, and said to be.
/// </summary>
public class NeverRunningCodeLiveTests(ITestOutputHelper output)
{
    private const string Ladder = "75";

    /// <summary>Each: the language, the check, the mistake, what it prints, and what its change prints.</summary>
    public static TheoryData<string, string, string, string, string> Mistakes => new()
    {
        { "Python", "logic-python-branch-never-reached", $"score = {Ladder}\nif score >= 50:\n    print(\"C\")\nelif score >= 70:\n    print(\"B\")\nelse:\n    print(\"F\")\n", "C", "B" },
        {
            "Java", "logic-branch-never-reached",
            $"public class Main {{\n    public static void main(String[] args) {{\n        int score = {Ladder};\n        if (score >= 50) {{\n            System.out.println(\"C\");\n        }} else if (score >= 70) {{\n            System.out.println(\"B\");\n        }} else {{\n            System.out.println(\"F\");\n        }}\n    }}\n}}\n",
            "C", "B"
        },
        { "C", "logic-branch-never-reached", $"#include <stdio.h>\nint main(void) {{\n    int score = {Ladder};\n    if (score >= 50) {{\n        puts(\"C\");\n    }} else if (score >= 70) {{\n        puts(\"B\");\n    }} else {{\n        puts(\"F\");\n    }}\n    return 0;\n}}\n", "C", "B" },
        { "C++", "logic-branch-never-reached", $"#include <iostream>\nint main() {{\n    int score = {Ladder};\n    if (score >= 50) {{\n        std::cout << \"C\\n\";\n    }} else if (score >= 70) {{\n        std::cout << \"B\\n\";\n    }} else {{\n        std::cout << \"F\\n\";\n    }}\n}}\n", "C", "B" },
        { "C#", "logic-branch-never-reached", $"int score = {Ladder};\nif (score >= 50)\n{{\n    Console.WriteLine(\"C\");\n}}\nelse if (score >= 70)\n{{\n    Console.WriteLine(\"B\");\n}}\nelse\n{{\n    Console.WriteLine(\"F\");\n}}\n", "C", "B" },
        { "JavaScript", "logic-branch-never-reached", $"const score = {Ladder};\nif (score >= 50) {{\n    console.log(\"C\");\n}} else if (score >= 70) {{\n    console.log(\"B\");\n}} else {{\n    console.log(\"F\");\n}}\n", "C", "B" },
        { "Dart", "logic-branch-never-reached", $"void main() {{\n  var score = {Ladder};\n  if (score >= 50) {{\n    print('C');\n  }} else if (score >= 70) {{\n    print('B');\n  }} else {{\n    print('F');\n  }}\n}}\n", "C", "B" },
        { "PHP", "logic-branch-never-reached", $"<?php\n$score = {Ladder};\nif ($score >= 50) {{\n    echo \"C\", PHP_EOL;\n}} elseif ($score >= 70) {{\n    echo \"B\", PHP_EOL;\n}} else {{\n    echo \"F\", PHP_EOL;\n}}\n", "C", "B" },
        { "Go", "logic-go-branch-never-reached", $"package main\n\nimport \"fmt\"\n\nfunc main() {{\n\tscore := {Ladder}\n\tif score >= 50 {{\n\t\tfmt.Println(\"C\")\n\t}} else if score >= 70 {{\n\t\tfmt.Println(\"B\")\n\t}} else {{\n\t\tfmt.Println(\"F\")\n\t}}\n}}\n", "C", "B" },
        { "Rust", "logic-rust-branch-never-reached", $"fn main() {{\n    let score = {Ladder};\n    if score >= 50 {{\n        println!(\"C\");\n    }} else if score >= 70 {{\n        println!(\"B\");\n    }} else {{\n        println!(\"F\");\n    }}\n}}\n", "C", "B" },
        { "Scala", "logic-scala-branch-never-reached", $"@main def run(): Unit =\n  val score = {Ladder}\n  if score >= 50 then println(\"C\")\n  else if score >= 70 then println(\"B\")\n  else println(\"F\")\n", "C", "B" },
        { "Ruby", "logic-ruby-branch-never-reached", $"score = {Ladder}\nif score >= 50\n  puts \"C\"\nelsif score >= 70\n  puts \"B\"\nelse\n  puts \"F\"\nend\n", "C", "B" },
        { "Lua", "logic-lua-branch-never-reached", $"local score = {Ladder}\nif score >= 50 then\n  print(\"C\")\nelseif score >= 70 then\n  print(\"B\")\nelse\n  print(\"F\")\nend\n", "C", "B" },
        { "Perl", "logic-perl-branch-never-reached", $"use strict;\nuse warnings;\nmy $score = {Ladder};\nif ($score >= 50) {{\n    print \"C\\n\";\n}} elsif ($score >= 70) {{\n    print \"B\\n\";\n}} else {{\n    print \"F\\n\";\n}}\n", "C", "B" },
        {
            "Fortran", "logic-fortran-branch-never-reached",
            $"program grades\n  implicit none\n  integer :: score\n  score = {Ladder}\n  if (score >= 50) then\n    print *, 'C'\n  else if (score >= 70) then\n    print *, 'B'\n  else\n    print *, 'F'\n  end if\nend program grades\n",
            " C", " B"
        },
        { "PowerShell", "logic-powershell-branch-never-reached", $"$score = {Ladder}\nif ($score -ge 50) {{\n    Write-Output \"C\"\n}} elseif ($score -ge 70) {{\n    Write-Output \"B\"\n}} else {{\n    Write-Output \"F\"\n}}\n", "C", "B" },
        { "Elixir", "logic-elixir-branch-never-reached", $"score = {Ladder}\ncond do\n  score >= 50 -> IO.puts(\"C\")\n  score >= 70 -> IO.puts(\"B\")\n  true -> IO.puts(\"F\")\nend\n", "C", "B" },
        {
            "Erlang", "logic-erlang-branch-never-reached",
            $"-module(grades).\n-export([main/0]).\n\ngrade(Score) ->\n    if\n        Score >= 50 -> \"C\";\n        Score >= 70 -> \"B\";\n        true -> \"F\"\n    end.\n\nmain() ->\n    io:format(\"~s~n\", [grade({Ladder})]).\n",
            "C", "B"
        },
        { "F#", "logic-fsharp-branch-never-reached", $"let score = {Ladder}\nif score >= 50 then printfn \"C\"\nelif score >= 70 then printfn \"B\"\nelse printfn \"F\"\n", "C", "B" },

        // Loops that count the wrong way: 3 times round meant, 0 counted.
        { "Python", "logic-python-loop-never-runs", "count = 0\nfor i in range(3, 0):\n    count += 1\nprint(count)\n", "0", "3" },
        { "Ruby", "logic-ruby-loop-never-runs", "count = 0\n(3..1).each do |i|\n  count += 1\nend\nputs count\n", "0", "3" },
        { "Rust", "logic-rust-loop-never-runs", "fn main() {\n    let mut count = 0;\n    for _i in 3..0 {\n        count += 1;\n    }\n    println!(\"{}\", count);\n}\n", "0", "3" },
        { "Scala", "logic-scala-loop-never-runs", "@main def run(): Unit =\n  var count = 0\n  for i <- 3 to 1 do count += 1\n  println(count)\n", "0", "3" },
        { "Lua", "logic-lua-loop-never-runs", "local count = 0\nfor i = 3, 1 do\n  count = count + 1\nend\nprint(count)\n", "0", "3" },
        { "F#", "logic-fsharp-loop-never-runs", "let mutable count = 0\nfor i in 3 .. 1 do count <- count + 1\nprintfn \"%d\" count\n", "0", "3" },
        { "Perl", "logic-perl-loop-never-runs", "use strict;\nuse warnings;\nmy $count = 0;\nfor my $i (3..1) {\n    $count++;\n}\nprint \"$count\\n\";\n", "0", "3" },

        {
            "Fortran", "logic-fortran-loop-never-runs",
            "program countdown\n  implicit none\n  integer :: i, count\n  count = 0\n  do i = 3, 1\n    count = count + 1\n  end do\n  print *, count\nend program countdown\n",
            "           0", "           3"
        },

        // Handlers: the general one took the error meant for the specific one.
        {
            "Dart", "logic-dart-handler-never-reached",
            "void main() {\n  try {\n    int.parse(\"x\");\n  } on Exception {\n    print(\"something went wrong\");\n  } on FormatException {\n    print(\"not a number\");\n  }\n}\n",
            "something went wrong", "not a number"
        },
        {
            "C++", "logic-cpp-handler-never-reached",
            "#include <iostream>\n#include <stdexcept>\n\nint main() {\n    try {\n        throw std::runtime_error(\"x\");\n    } catch (std::exception& e) {\n        std::cout << \"something went wrong\" << std::endl;\n    } catch (std::runtime_error& e) {\n        std::cout << \"runtime\" << std::endl;\n    }\n}\n",
            "something went wrong", "runtime"
        },
        { "Python", "logic-python-handler-never-reached", "try:\n    int(\"x\")\nexcept Exception:\n    print(\"something went wrong\")\nexcept ValueError:\n    print(\"not a number\")\n", "something went wrong", "not a number" },
        { "Ruby", "logic-ruby-handler-never-reached", "begin\n  Integer(\"x\")\nrescue StandardError\n  puts \"something went wrong\"\nrescue ArgumentError\n  puts \"not a number\"\nend\n", "something went wrong", "not a number" },
        {
            "PHP", "logic-php-handler-never-reached",
            "<?php\ntry {\n    throw new InvalidArgumentException(\"x\");\n} catch (Exception $e) {\n    echo \"something went wrong\", PHP_EOL;\n} catch (InvalidArgumentException $e) {\n    echo \"not a number\", PHP_EOL;\n}\n",
            "something went wrong", "not a number"
        },

        // Python's misspelt __main__.
        { "Python", "logic-python-main-guard-misspelt", "print(\"loaded\")\n\n\ndef main():\n    print(\"started\")\n\n\nif __name__ == \"main\":\n    main()\n", "loaded", "loaded\nstarted" },
    };

    /// <summary>Code with no change: the language, the check, the program, and what it prints - which leaves out what never ran.</summary>
    public static TheoryData<string, string, string, string> NeverRunning => new()
    {
        { "C", "logic-code-after-return", "#include <stdio.h>\nint total(int count) {\n    return count;\n    puts(\"counted\");\n}\nint main(void) {\n    printf(\"%d\\n\", total(2));\n    return 0;\n}\n", "2" },
        { "C++", "logic-code-after-return", "#include <iostream>\nint total(int count) {\n    return count;\n    std::cout << \"counted\\n\";\n}\nint main() {\n    std::cout << total(2) << \"\\n\";\n}\n", "2" },
        { "JavaScript", "logic-code-after-return", "function total(marks) {\n    return marks.length;\n    console.log(\"counted\");\n}\nconsole.log(total([1, 2]));\n", "2" },
        { "PHP", "logic-php-code-after-return", "<?php\nfunction total($marks) {\n    return count($marks);\n    echo \"counted\\n\";\n}\necho total([1, 2]), PHP_EOL;\n", "2" },
        { "Perl", "logic-perl-code-after-return", "use strict;\nuse warnings;\nsub total {\n    my @marks = @_;\n    return scalar(@marks);\n    print \"counted\\n\";\n}\nprint total(1, 2), \"\\n\";\n", "2" },
        { "PowerShell", "logic-powershell-code-after-return", "function Get-Total($marks) {\n    return $marks.Count\n    Write-Output \"counted\"\n}\nGet-Total @(1, 2)\n", "2" },
        {
            "Fortran", "logic-fortran-code-after-return",
            "program dead\n  implicit none\n  print *, total(2)\ncontains\n  integer function total(count)\n    integer, intent(in) :: count\n    total = count\n    return\n    print *, 'counted'\n  end function total\nend program dead\n",
            "           2"
        },
        { "JavaScript", "logic-js-duplicate-case", "switch (2) {\n    case 1:\n        console.log(\"one\");\n        break;\n    case 2:\n        console.log(\"two\");\n        break;\n    case 2:\n        console.log(\"second two\");\n        break;\n}\n", "two" },
        { "PHP", "logic-php-duplicate-case", "<?php\nswitch (2) {\n    case 1: echo \"one\\n\"; break;\n    case 2: echo \"two\\n\"; break;\n    case 2: echo \"second two\\n\"; break;\n}\n", "two" },
        { "Python", "logic-python-defined-twice", "def area(side):\n    return side * side\n\ndef area(width, height=1):\n    return width * height\n\nprint(area(3))\n", "3" },
        { "JavaScript", "logic-js-defined-twice", "console.log(area(3));\nfunction area(side) { return side * side; }\nfunction area(width, height) { return width * height; }\n", "NaN" },
        { "Lua", "logic-lua-defined-twice", "function area(side) return side * side end\nfunction area(width, height) return width * (height or 1) end\nprint(area(3))\n", "3" },
        { "PowerShell", "logic-powershell-defined-twice", "function Get-Area($side) { $side * $side }\nfunction Get-Area($width, $height) { $width * $height }\nGet-Area 3\n", "0" },
        { "Ruby", "logic-ruby-code-after-return", "def total(marks)\n  return marks.length\n  puts \"counted\"\nend\nputs total([1, 2])\n", "2" },
        { "Ruby", "logic-ruby-defined-twice", "def area(side)\n  side * side\nend\ndef area(width, height = 1)\n  width * height\nend\nputs area(3)\n", "3" },
    };

    private static CodeLanguage LanguageNamed(string name) => CodeLanguage.All.Single(each => each.Name == name);

    /// <summary>Whether Windows' application control refused to start what was built - which says nothing about the code.</summary>
    private static bool RefusedByWindows(Observation observed) =>
        ApplicationControl.Refused(string.Join("\n", observed.Output) + "\n" + observed.WhyNotRun);

    private static LogicFinding FoundIn(string language, string check, string code, TempFolder temp)
    {
        var path = Path.Combine(temp.Path, Path.GetFileName(PastedCode.RelativePathFor(code, LanguageNamed(language))));
        File.WriteAllText(path, code);
        return Assert.Single(LogicPatterns.Scan(SourceFile.Read(path)!), finding => finding.PatternId == check);
    }

    [Theory]
    [MemberData(nameof(Mistakes))]
    public async Task TheMistakeNeverReachesTheCodeAndTheChangeReachesIt(string language, string check, string mistake, string mistakePrints, string changePrints)
    {
        using var temp = new TempFolder();
        var fix = FoundIn(language, check, mistake, temp).Fix!;
        var lines = mistake.TrimEnd('\n').Split('\n').ToList();
        lines.RemoveRange(fix.StartLine - 1, fix.RemoveCount);
        lines.InsertRange(fix.StartLine - 1, fix.NewLines);

        var wrong = await SnippetRunner.RunAsync(LanguageNamed(language), mistake);
        var right = wrong.Ran && !RefusedByWindows(wrong) ? await SnippetRunner.RunAsync(LanguageNamed(language), string.Join("\n", lines) + "\n") : null;
        if (right is null || RefusedByWindows(right))
        {
            output.WriteLine($"Not run on this computer: {wrong.WhyNotRun ?? "Windows refused to start it"}");
            return;
        }

        output.WriteLine($"the mistake: {wrong.Said(language)}\nthe change:  {right.Said(language)}");
        Assert.True(wrong.Shows(Behaviour.Printing(mistakePrints)), wrong.Said(language));
        Assert.True(right.Shows(Behaviour.Printing(changePrints)), right.Said(language));
    }

    [Theory]
    [MemberData(nameof(NeverRunning))]
    public async Task CodeFoundNeverRunningNeverRuns(string language, string check, string code, string prints)
    {
        using var temp = new TempFolder();
        FoundIn(language, check, code, temp);

        var ran = await SnippetRunner.RunAsync(LanguageNamed(language), code);
        if (!ran.Ran || RefusedByWindows(ran))
        {
            output.WriteLine($"Not run on this computer: {ran.WhyNotRun ?? "Windows refused to start it"}");
            return;
        }

        output.WriteLine($"it: {ran.Said(language)}");
        Assert.True(ran.Shows(Behaviour.Printing(prints)), ran.Said(language));
    }
}
