using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// The programs that use another value in place of the one worked out, run in each language with the output they should
/// print: the change the check offers is the one that makes the program print it, confirmed by running a copy with it. A language this computer cannot run is passed over,
/// and a copy Windows would not let start is not asked to be confirmed.
/// </summary>
public class WrongValueLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>The file, the program, what it should print, the line it is on, and what the change makes of it.</summary>
    public static TheoryData<string, string, string, int, string> Programs => new()
    {
        { "average.c", "#include <stdio.h>\n\nint average(const int marks[], int count) {\n    int total = 0;\n    for (int i = 0; i < count; i++) {\n        total += marks[i];\n    }\n    int average = total / count;\n    return total;\n}\n\nint main(void) {\n    int marks[] = {50, 70, 90};\n    printf(\"%d\\n\", average(marks, 3));\n    return 0;\n}\n", "70\n", 9, "    return average;" },
        { "average.exs", "defmodule Marks do\n  def average(marks) do\n    total = Enum.sum(marks)\n    average = div(total, length(marks))\n    total\n  end\nend\n\nIO.puts(Marks.average([50, 70, 90]))\n", "70\n", 5, "    average" },
        { "average.fsx", "let average (marks: int list) =\n    let total = List.sum marks\n    let average = total / List.length marks\n    total\n\nprintfn \"%d\" (average [50; 70; 90])\n", "70\n", 4, "    average" },
        { "Average.java", "public class Average {\n    static int average(int[] marks) {\n        int total = 0;\n        for (int mark : marks) {\n            total += mark;\n        }\n        int average = total / marks.length;\n        return total;\n    }\n\n    public static void main(String[] args) {\n        System.out.println(average(new int[] {50, 70, 90}));\n    }\n}\n", "70\n", 8, "        return average;" },
        { "average.js", "function average(marks) {\n    let total = 0;\n    for (const mark of marks) {\n        total += mark;\n    }\n    const average = total / marks.length;\n    return total;\n}\n\nconsole.log(average([50, 70, 90]));\n", "70\n", 7, "    return average;" },
        { "average.lua", "local function average(marks)\n  local total = 0\n  for _, mark in ipairs(marks) do\n    total = total + mark\n  end\n  local average = total // #marks\n  return total\nend\n\nprint(average({50, 70, 90}))\n", "70\n", 7, "  return average" },
        { "average.php", "<?php\nfunction average($marks) {\n    $total = array_sum($marks);\n    $average = intdiv($total, count($marks));\n    return $total;\n}\n\necho average([50, 70, 90]), \"\\n\";\n", "70\n", 5, "    return $average;" },
        { "average.pl", "use strict;\nuse warnings;\n\nsub average {\n    my @marks = @_;\n    my $total = 0;\n    $total += $_ for @marks;\n    my $average = $total / scalar(@marks);\n    return $total;\n}\n\nprint average(50, 70, 90), \"\\n\";\n", "70\n", 9, "    return $average;" },
        { "average.ps1", "function Get-Average($marks) {\n    $total = ($marks | Measure-Object -Sum).Sum\n    $average = $total / $marks.Count\n    return $total\n}\n\nWrite-Output (Get-Average @(50, 70, 90))\n", "70\n", 4, "    return $average" },
        { "average.py", "def average(marks):\n    total = sum(marks)\n    average = total / len(marks)\n    return total\n\n\nprint(average([50, 70, 90]))\n", "70.0\n", 4, "    return average" },
        { "average.rb", "def average(marks)\n  total = marks.sum\n  average = total / marks.length\n  total\nend\n\nputs average([50, 70, 90])\n", "70\n", 4, "  average" },
        { "main.dart", "int average(List<int> marks) {\n  var total = 0;\n  for (final mark in marks) {\n    total += mark;\n  }\n  var average = total ~/ marks.length;\n  return total;\n}\n\nvoid main() {\n  print(average([50, 70, 90]));\n}\n", "70\n", 7, "  return average;" },
        { "main.rs", "fn average(marks: &[i32]) -> i32 {\n    let total: i32 = marks.iter().sum();\n    let average = total / marks.len() as i32;\n    total\n}\n\nfn main() {\n    println!(\"{}\", average(&[50, 70, 90]));\n}\n", "70\n", 4, "    average" },
        { "Main.scala", "object Main extends App {\n  def average(marks: List[Int]): Int = {\n    val total = marks.sum\n    val average = total / marks.length\n    total\n  }\n\n  println(average(List(50, 70, 90)))\n}\n", "70\n", 5, "    average" },
        { "Printed.java", "public class Printed {\n    public static void main(String[] args) {\n        int[] marks = {50, 70, 90};\n        int total = 0;\n        for (int mark : marks) {\n            total += mark;\n        }\n        int average = total / marks.length;\n        System.out.println(\"Average: \" + total);\n    }\n}\n", "Average: 70\n", 9, "        System.out.println(\"Average: \" + average);" },
        { "printed.js", "const marks = [50, 70, 90];\nconst total = marks.reduce((sum, mark) => sum + mark, 0);\nconst average = total / marks.length;\nconsole.log(\"Average: \" + total);\n", "Average: 70\n", 4, "console.log(\"Average: \" + average);" },
        { "printed.py", "marks = [50, 70, 90]\ntotal = sum(marks)\naverage = total // len(marks)\nprint(\"Average:\", total)\n", "Average: 70\n", 4, "print(\"Average:\", average)" },
        { "Program.cs", "int[] marks = { 50, 70, 90 };\nvar total = 0;\nforeach (var mark in marks)\n{\n    total += mark;\n}\nvar average = total / marks.Length;\nConsole.WriteLine(\"Average: \" + total);\n", "Average: 70\n", 8, "Console.WriteLine(\"Average: \" + average);" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheChangeMakesItPrintWhatItShould(string fileName, string code, string expected, int line, string changedTo)
    {
        var program = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(program, code);

        var launch = TargetFactory.FromFile(program, LiveAllowance.For(program, TimeSpan.FromMinutes(3)));
        if (!launch.Ok)
        {
            output.WriteLine($"Not run on this computer: {launch.Problem}");
            return;
        }

        using var http = new FixFinderHttpClient();
        var report = await new ProgramChecker(http, new FixSourceRegistry())
        {
            Language = CodeLanguage.Of(program) ?? CodeLanguage.Any,
            Expected = ExpectedBehaviour.From([new ExpectedRun(null, expected)], ""),
        }.CheckAsync(launch);
        foreach (var each in report.Findings)
            output.WriteLine($"[{each.RuleId}] {each.Line}: {each.Title} :: {string.Join("|", each.Fix?.NewLines ?? [])} :: {each.Verified.Summary}");

        if (report.Notes.Any(ApplicationControl.Refused) || report.Findings.Any(finding => ApplicationControl.Refused(finding.Verified.Summary)))
        {
            output.WriteLine("Windows did not let the program start here, so the change could not be confirmed.");
            return;
        }

        var changed = report.Findings.FirstOrDefault(finding => finding.Fix is { } fix && fix.StartLine == line && fix.NewLines.SequenceEqual([changedTo]));
        Assert.True(changed is not null, $"no change to line {line} among {string.Join(", ", report.Findings.Select(finding => finding.RuleId))}");
        if (changed!.RuleId == "wrong-output") Assert.True(changed.Verified.IsVerified, changed.Verified.Summary);

        Assert.Equal(code, File.ReadAllText(program));
    }
}
