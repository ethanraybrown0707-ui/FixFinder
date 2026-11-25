using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;

namespace FixFinder.Tests;

/// <summary>
/// A value worked out and never used while one it was worked out from is given back or printed in its place - average =
/// total / len(marks) then return total, in a function called average - in each language, with the change that uses the
/// value worked out; and programs that use what they work out, which are left alone. Each row is what the check gave for
/// the program when these were written.
/// </summary>
public class WrongValueTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private SourceFile Program(string fileName, string code)
    {
        var path = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(path, code);
        return SourceFile.Read(path)!;
    }

    /// <summary>The file, the program, the check, the line it is on, and what the change makes of it.</summary>
    public static TheoryData<string, string, string, int, string> Mistakes => new()
    {
        { "average.c", "#include <stdio.h>\n\nint average(const int marks[], int count) {\n    int total = 0;\n    for (int i = 0; i < count; i++) {\n        total += marks[i];\n    }\n    int average = total / count;\n    return total;\n}\n\nint main(void) {\n    int marks[] = {50, 70, 90};\n    printf(\"%d\\n\", average(marks, 3));\n    return 0;\n}\n", "logic-wrong-value-used", 9, "    return average;" },
        { "average.exs", "defmodule Marks do\n  def average(marks) do\n    total = Enum.sum(marks)\n    average = div(total, length(marks))\n    total\n  end\nend\n\nIO.puts(Marks.average([50, 70, 90]))\n", "logic-elixir-wrong-value-used", 5, "    average" },
        { "average.f90", "program marks_average\n  implicit none\n  integer :: marks(3) = [50, 70, 90]\n  integer :: total, average\n  total = sum(marks)\n  average = total / size(marks)\n  print *, 'Average:', total\nend program marks_average\n", "logic-fortran-wrong-value-used", 7, "  print *, 'Average:', average" },
        { "average.fsx", "let average (marks: int list) =\n    let total = List.sum marks\n    let average = total / List.length marks\n    total\n\nprintfn \"%d\" (average [50; 70; 90])\n", "logic-fsharp-wrong-value-used", 4, "    average" },
        { "Average.java", "public class Average {\n    static int average(int[] marks) {\n        int total = 0;\n        for (int mark : marks) {\n            total += mark;\n        }\n        int average = total / marks.length;\n        return total;\n    }\n\n    public static void main(String[] args) {\n        System.out.println(average(new int[] {50, 70, 90}));\n    }\n}\n", "logic-java-wrong-value-used", 8, "        return average;" },
        { "average.js", "function average(marks) {\n    let total = 0;\n    for (const mark of marks) {\n        total += mark;\n    }\n    const average = total / marks.length;\n    return total;\n}\n\nconsole.log(average([50, 70, 90]));\n", "logic-wrong-value-used", 7, "    return average;" },
        { "average.lua", "local function average(marks)\n  local total = 0\n  for _, mark in ipairs(marks) do\n    total = total + mark\n  end\n  local average = total // #marks\n  return total\nend\n\nprint(average({50, 70, 90}))\n", "logic-lua-wrong-value-used", 7, "  return average" },
        { "average.php", "<?php\nfunction average($marks) {\n    $total = array_sum($marks);\n    $average = intdiv($total, count($marks));\n    return $total;\n}\n\necho average([50, 70, 90]), \"\\n\";\n", "logic-php-wrong-value-used", 5, "    return $average;" },
        { "average.pl", "use strict;\nuse warnings;\n\nsub average {\n    my @marks = @_;\n    my $total = 0;\n    $total += $_ for @marks;\n    my $average = $total / scalar(@marks);\n    return $total;\n}\n\nprint average(50, 70, 90), \"\\n\";\n", "logic-perl-wrong-value-used", 9, "    return $average;" },
        { "average.ps1", "function Get-Average($marks) {\n    $total = ($marks | Measure-Object -Sum).Sum\n    $average = $total / $marks.Count\n    return $total\n}\n\nWrite-Output (Get-Average @(50, 70, 90))\n", "logic-powershell-wrong-value-used", 4, "    return $average" },
        { "average.py", "def average(marks):\n    total = sum(marks)\n    average = total / len(marks)\n    return total\n\n\nprint(average([50, 70, 90]))\n", "logic-python-wrong-value-used", 4, "    return average" },
        { "average.rb", "def average(marks)\n  total = marks.sum\n  average = total / marks.length\n  total\nend\n\nputs average([50, 70, 90])\n", "logic-ruby-wrong-value-used", 4, "  average" },
        { "main.dart", "int average(List<int> marks) {\n  var total = 0;\n  for (final mark in marks) {\n    total += mark;\n  }\n  var average = total ~/ marks.length;\n  return total;\n}\n\nvoid main() {\n  print(average([50, 70, 90]));\n}\n", "logic-dart-wrong-value-used", 7, "  return average;" },
        { "main.rs", "fn average(marks: &[i32]) -> i32 {\n    let total: i32 = marks.iter().sum();\n    let average = total / marks.len() as i32;\n    total\n}\n\nfn main() {\n    println!(\"{}\", average(&[50, 70, 90]));\n}\n", "logic-rust-wrong-value-used", 4, "    average" },
        { "Main.scala", "object Main extends App {\n  def average(marks: List[Int]): Int = {\n    val total = marks.sum\n    val average = total / marks.length\n    total\n  }\n\n  println(average(List(50, 70, 90)))\n}\n", "logic-scala-wrong-value-used", 5, "    average" },
        { "Printed.java", "public class Printed {\n    public static void main(String[] args) {\n        int[] marks = {50, 70, 90};\n        int total = 0;\n        for (int mark : marks) {\n            total += mark;\n        }\n        int average = total / marks.length;\n        System.out.println(\"Average: \" + total);\n    }\n}\n", "logic-java-wrong-value-used", 9, "        System.out.println(\"Average: \" + average);" },
        { "printed.js", "const marks = [50, 70, 90];\nconst total = marks.reduce((sum, mark) => sum + mark, 0);\nconst average = total / marks.length;\nconsole.log(\"Average: \" + total);\n", "logic-wrong-value-used", 4, "console.log(\"Average: \" + average);" },
        { "printed.py", "marks = [50, 70, 90]\ntotal = sum(marks)\naverage = total // len(marks)\nprint(\"Average:\", total)\n", "logic-python-wrong-value-used", 4, "print(\"Average:\", average)" },
        { "Program.cs", "int[] marks = { 50, 70, 90 };\nvar total = 0;\nforeach (var mark in marks)\n{\n    total += mark;\n}\nvar average = total / marks.Length;\nConsole.WriteLine(\"Average: \" + total);\n", "logic-wrong-value-used", 8, "Console.WriteLine(\"Average: \" + average);" },
    };

    [Theory]
    [MemberData(nameof(Mistakes))]
    public void TheValueWorkedOutIsTheOneUsed(string fileName, string code, string check, int line, string changedTo)
    {
        var found = LogicPatterns.Scan(Program(fileName, code)).Where(finding => finding.PatternId.EndsWith("wrong-value-used", StringComparison.Ordinal)).ToList();

        var wrong = Assert.Single(found);
        Assert.Equal(check, wrong.PatternId);
        Assert.Equal(line, wrong.Line);
        Assert.Equal([changedTo], wrong.Fix!.NewLines);
        Assert.Equal(line, wrong.Fix.StartLine);
    }

    [Theory]
    [InlineData("Right.java", "public class Right {\n    static int sum(int[] marks) {\n        int total = 0;\n        for (int mark : marks) {\n            total += mark;\n        }\n        int count = marks.length;\n        return total;\n    }\n\n    public static void main(String[] args) {\n        int total = sum(new int[] {50, 70, 90});\n        int average = total / 3;\n        System.out.println(\"Total: \" + total + \", average: \" + average);\n    }\n}\n")]
    [InlineData("right.js", "function largest(marks) {\n    let best = marks[0];\n    for (const mark of marks) {\n        if (mark > best) best = mark;\n    }\n    const count = marks.length;\n    return best;\n}\n\nconsole.log(largest([50, 70, 90]));\n")]
    [InlineData("right.py", "def total(marks):\n    count = len(marks)\n    total = sum(marks)\n    return total\n\n\ndef average(marks):\n    total = sum(marks)\n    average = total / len(marks)\n    return average\n\n\nmarks = [50, 70, 90]\nmean = sum(marks) / len(marks)\nprint(\"Average:\", mean)\nprint(\"Total:\", total(marks))\n")]
    public void AValueUsedAsItsNameSaysIsLeftAlone(string fileName, string code)
    {
        Assert.DoesNotContain(LogicPatterns.Scan(Program(fileName, code)), finding => finding.PatternId.EndsWith("wrong-value-used", StringComparison.Ordinal));
    }
}
