using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// The bubble sorts whose swap loses a value, run in each language with what a sort of 55, 40, 70 and 10 should print: the
/// change the check offers is proved by a copy with it, which prints 10, 40, 55 and 70. Each row is what FixFinder gave for
/// the program on this computer when these were written. A language this computer cannot run is passed over, and a copy
/// Windows would not let start is not asked to be proved.
/// </summary>
/// <remarks>Each row builds its program, and a copy with the change, in seven of the languages - so the rows run alone, after the other tests.</remarks>
[Collection(BuildsRunAlone.Name)]
public class SwapLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string Sorted = "10\n40\n55\n70\n";

    /// <summary>The file, the program, the check, the line the change starts on, how many it takes away, and the lines it writes.</summary>
    public static TheoryData<string, string, string, int, int, string> Programs => new()
    {
        { "sort.py", "marks = [55, 40, 70, 10]\nfor i in range(len(marks)):\n    for j in range(len(marks) - 1 - i):\n        if marks[j] > marks[j + 1]:\n            marks[j] = marks[j + 1]\n            marks[j + 1] = marks[j]\nfor mark in marks:\n    print(mark)\n", "logic-python-swap-loses-value", 5, 2, "            marks[j], marks[j + 1] = marks[j + 1], marks[j]" },
        { "kept.py", "marks = [55, 40, 70, 10]\nfor i in range(len(marks)):\n    for j in range(len(marks) - 1 - i):\n        if marks[j] > marks[j + 1]:\n            temp = marks[j]\n            marks[j] = marks[j + 1]\n            marks[j + 1] = marks[j]\nfor mark in marks:\n    print(mark)\n", "logic-python-swap-loses-value", 7, 1, "            marks[j + 1] = temp" },
        { "sort.rb", "marks = [55, 40, 70, 10]\n(0...marks.length).each do |i|\n  (0...(marks.length - 1 - i)).each do |j|\n    if marks[j] > marks[j + 1]\n      marks[j] = marks[j + 1]\n      marks[j + 1] = marks[j]\n    end\n  end\nend\nmarks.each { |mark| puts mark }\n", "logic-ruby-swap-loses-value", 5, 2, "      marks[j], marks[j + 1] = marks[j + 1], marks[j]" },
        { "sort.lua", "local marks = { 55, 40, 70, 10 }\nfor i = 1, #marks do\n  for j = 1, #marks - i do\n    if marks[j] > marks[j + 1] then\n      marks[j] = marks[j + 1]\n      marks[j + 1] = marks[j]\n    end\n  end\nend\nfor _, mark in ipairs(marks) do\n  print(mark)\nend\n", "logic-lua-swap-loses-value", 5, 2, "      marks[j], marks[j + 1] = marks[j + 1], marks[j]" },
        { "sort.go", "package main\n\nimport \"fmt\"\n\nfunc main() {\n\tmarks := []int{55, 40, 70, 10}\n\tfor i := 0; i < len(marks); i++ {\n\t\tfor j := 0; j < len(marks)-1-i; j++ {\n\t\t\tif marks[j] > marks[j+1] {\n\t\t\t\tmarks[j] = marks[j+1]\n\t\t\t\tmarks[j+1] = marks[j]\n\t\t\t}\n\t\t}\n\t}\n\tfor _, mark := range marks {\n\t\tfmt.Println(mark)\n\t}\n}\n", "logic-go-swap-loses-value", 10, 2, "\t\t\t\tmarks[j], marks[j+1] = marks[j+1], marks[j]" },
        { "sort.ps1", "$marks = @(55, 40, 70, 10)\nfor ($i = 0; $i -lt $marks.Count; $i++) {\n    for ($j = 0; $j -lt $marks.Count - 1 - $i; $j++) {\n        if ($marks[$j] -gt $marks[$j + 1]) {\n            $marks[$j] = $marks[$j + 1]\n            $marks[$j + 1] = $marks[$j]\n        }\n    }\n}\nforeach ($mark in $marks) {\n    Write-Output $mark\n}\n", "logic-powershell-swap-loses-value", 5, 2, "            $marks[$j], $marks[$j + 1] = $marks[$j + 1], $marks[$j]" },
        { "sort.php", "<?php\n$marks = [55, 40, 70, 10];\nfor ($i = 0; $i < count($marks); $i++) {\n    for ($j = 0; $j < count($marks) - 1 - $i; $j++) {\n        if ($marks[$j] > $marks[$j + 1]) {\n            $marks[$j] = $marks[$j + 1];\n            $marks[$j + 1] = $marks[$j];\n        }\n    }\n}\nforeach ($marks as $mark) {\n    echo $mark . \"\\n\";\n}\n", "logic-php-swap-loses-value", 6, 2, "            [$marks[$j], $marks[$j + 1]] = [$marks[$j + 1], $marks[$j]];" },
        { "sort.pl", "use strict;\nuse warnings;\n\nmy @marks = (55, 40, 70, 10);\nfor my $i (0 .. $#marks) {\n    for my $j (0 .. $#marks - 1 - $i) {\n        if ($marks[$j] > $marks[$j + 1]) {\n            $marks[$j] = $marks[$j + 1];\n            $marks[$j + 1] = $marks[$j];\n        }\n    }\n}\nprint \"$_\\n\" for @marks;\n", "logic-perl-swap-loses-value", 8, 2, "            ($marks[$j], $marks[$j + 1]) = ($marks[$j + 1], $marks[$j]);" },
        { "sort.js", "const marks = [55, 40, 70, 10];\nfor (let i = 0; i < marks.length; i++) {\n    for (let j = 0; j < marks.length - 1 - i; j++) {\n        if (marks[j] > marks[j + 1]) {\n            marks[j] = marks[j + 1];\n            marks[j + 1] = marks[j];\n        }\n    }\n}\nfor (const mark of marks) {\n    console.log(mark);\n}\n", "logic-js-swap-loses-value", 5, 2, "            [marks[j], marks[j + 1]] = [marks[j + 1], marks[j]];" },
        { "kept.js", "const marks = [55, 40, 70, 10];\nfor (let i = 0; i < marks.length; i++) {\n    for (let j = 0; j < marks.length - 1 - i; j++) {\n        if (marks[j] > marks[j + 1]) {\n            const temp = marks[j];\n            marks[j] = marks[j + 1];\n            marks[j + 1] = marks[j];\n        }\n    }\n}\nfor (const mark of marks) {\n    console.log(mark);\n}\n", "logic-js-swap-loses-value", 7, 1, "            marks[j + 1] = temp;" },
        { "sort.ts", "const marks: number[] = [55, 40, 70, 10];\nfor (let i = 0; i < marks.length; i++) {\n    for (let j = 0; j < marks.length - 1 - i; j++) {\n        if (marks[j] > marks[j + 1]) {\n            marks[j] = marks[j + 1];\n            marks[j + 1] = marks[j];\n        }\n    }\n}\nfor (const mark of marks) {\n    console.log(mark);\n}\n", "logic-js-swap-loses-value", 5, 2, "            [marks[j], marks[j + 1]] = [marks[j + 1], marks[j]];" },
        { "Sort.cs", "int[] marks = { 55, 40, 70, 10 };\nfor (int i = 0; i < marks.Length; i++)\n{\n    for (int j = 0; j < marks.Length - 1 - i; j++)\n    {\n        if (marks[j] > marks[j + 1])\n        {\n            marks[j] = marks[j + 1];\n            marks[j + 1] = marks[j];\n        }\n    }\n}\nforeach (int mark in marks)\n{\n    Console.WriteLine(mark);\n}\n", "logic-csharp-swap-loses-value", 8, 2, "            (marks[j], marks[j + 1]) = (marks[j + 1], marks[j]);" },
        { "Sort.java", "public class Sort {\n    public static void main(String[] args) {\n        int[] marks = {55, 40, 70, 10};\n        for (int i = 0; i < marks.length; i++) {\n            for (int j = 0; j < marks.length - 1 - i; j++) {\n                if (marks[j] > marks[j + 1]) {\n                    marks[j] = marks[j + 1];\n                    marks[j + 1] = marks[j];\n                }\n            }\n        }\n        for (int mark : marks) {\n            System.out.println(mark);\n        }\n    }\n}\n", "logic-java-swap-loses-value", 7, 2, "                    var held = marks[j];|                    marks[j] = marks[j + 1];|                    marks[j + 1] = held;" },
        { "Kept.java", "public class Kept {\n    public static void main(String[] args) {\n        int[] marks = {55, 40, 70, 10};\n        for (int i = 0; i < marks.length; i++) {\n            for (int j = 0; j < marks.length - 1 - i; j++) {\n                if (marks[j] > marks[j + 1]) {\n                    int temp = marks[j];\n                    marks[j] = marks[j + 1];\n                    marks[j + 1] = marks[j];\n                }\n            }\n        }\n        for (int mark : marks) {\n            System.out.println(mark);\n        }\n    }\n}\n", "logic-java-swap-loses-value", 9, 1, "                    marks[j + 1] = temp;" },
        { "sort.c", "#include <stdio.h>\n\nint main(void) {\n    int marks[] = {55, 40, 70, 10};\n    int count = 4;\n    for (int i = 0; i < count; i++) {\n        for (int j = 0; j < count - 1 - i; j++) {\n            if (marks[j] > marks[j + 1]) {\n                marks[j] = marks[j + 1];\n                marks[j + 1] = marks[j];\n            }\n        }\n    }\n    for (int i = 0; i < count; i++) {\n        printf(\"%d\\n\", marks[i]);\n    }\n    return 0;\n}\n", "logic-c-swap-loses-value", 9, 2, "                int held = marks[j];|                marks[j] = marks[j + 1];|                marks[j + 1] = held;" },
        { "sort.cpp", "#include <iostream>\n#include <vector>\n\nint main() {\n    std::vector<int> marks = {55, 40, 70, 10};\n    for (size_t i = 0; i < marks.size(); i++) {\n        for (size_t j = 0; j + 1 < marks.size() - i; j++) {\n            if (marks[j] > marks[j + 1]) {\n                marks[j] = marks[j + 1];\n                marks[j + 1] = marks[j];\n            }\n        }\n    }\n    for (int mark : marks) {\n        std::cout << mark << \"\\n\";\n    }\n    return 0;\n}\n", "logic-cpp-swap-loses-value", 9, 2, "                auto held = marks[j];|                marks[j] = marks[j + 1];|                marks[j + 1] = held;" },
        { "sort.dart", "void main() {\n  final marks = [55, 40, 70, 10];\n  for (var i = 0; i < marks.length; i++) {\n    for (var j = 0; j < marks.length - 1 - i; j++) {\n      if (marks[j] > marks[j + 1]) {\n        marks[j] = marks[j + 1];\n        marks[j + 1] = marks[j];\n      }\n    }\n  }\n  for (final mark in marks) {\n    print(mark);\n  }\n}\n", "logic-dart-swap-loses-value", 6, 2, "        final held = marks[j];|        marks[j] = marks[j + 1];|        marks[j + 1] = held;" },
        { "sort.rs", "fn main() {\n    let mut marks = vec![55, 40, 70, 10];\n    for i in 0..marks.len() {\n        for j in 0..marks.len() - 1 - i {\n            if marks[j] > marks[j + 1] {\n                marks[j] = marks[j + 1];\n                marks[j + 1] = marks[j];\n            }\n        }\n    }\n    for mark in &marks {\n        println!(\"{}\", mark);\n    }\n}\n", "logic-rust-swap-loses-value", 6, 2, "                marks.swap(j, j + 1);" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheChangeMakesTheSortPrintTheMarksInOrder(string fileName, string code, string check, int changeAt, int removed, string added)
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
            Expected = ExpectedBehaviour.From([new ExpectedRun(null, Sorted)], ""),
        }.CheckAsync(launch);
        foreach (var each in report.Findings)
            output.WriteLine($"[{each.RuleId}] {each.Line}: {each.Title} :: {string.Join("|", each.Fix?.NewLines ?? [])} :: {each.Verified.Summary}");

        if (report.Notes.Any(ApplicationControl.Refused) || report.Findings.Any(finding => ApplicationControl.Refused(finding.Verified.Summary)))
        {
            output.WriteLine("Windows did not let the program start here, so the change could not be proved.");
            return;
        }

        var swapped = report.Findings.SingleOrDefault(finding => finding.RuleId == check);
        Assert.True(swapped is not null, $"no {check} among {string.Join(", ", report.Findings.Select(finding => finding.RuleId))}");
        Assert.Equal(changeAt, swapped!.Fix!.StartLine);
        Assert.Equal(removed, swapped.Fix.RemoveCount);
        Assert.Equal(added.Split('|'), swapped.Fix.NewLines);
        Assert.True(swapped.Verified.IsVerified, swapped.Verified.Summary);

        Assert.Equal(code, File.ReadAllText(program));
    }
}
