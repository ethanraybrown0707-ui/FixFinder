using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// The recursion mistakes run in each language with what they should print - 120 for factorial(5), and 5, 3 and 1 for the
/// countdown from 5: the change the check offers is proved by a copy with it that prints that. Each row is what FixFinder gave
/// for the program on this computer when these were written. A language this computer cannot run is passed over, and a copy
/// Windows would not let start is not asked to be proved.
/// </summary>
/// <remarks>Each row builds its program, and a copy with the change, in seven of the languages - so the rows run alone, after the other tests.</remarks>
[Collection(BuildsRunAlone.Name)]
public class RecursionLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>The file, the program, what it should print, the check, the line the change is on, and what the line becomes.</summary>
    public static TheoryData<string, string, string, string, int, string> Programs => new()
    {
        { "away_factorial.py", "def factorial(n):\n    if n <= 1:\n        return 1\n    return n * factorial(n + 1)\n\n\nprint(factorial(5))\n", "120\n", "logic-python-recursion-never-stops", 4, "    return n * factorial(n - 1)" },
        { "zero_factorial.py", "def factorial(n):\n    if n == 0:\n        return 0\n    return n * factorial(n - 1)\n\n\nprint(factorial(5))\n", "120\n", "logic-python-recursion-never-stops", 3, "        return 1" },
        { "same_factorial.py", "def factorial(n):\n    if n <= 1:\n        return 1\n    return n * factorial(n)\n\n\nprint(factorial(5))\n", "120\n", "logic-python-recursion-never-stops", 4, "    return n * factorial(n - 1)" },
        { "jump_countdown.py", "def countdown(n):\n    if n == 0:\n        return\n    print(n)\n    countdown(n - 2)\n\n\ncountdown(5)\n", "5\n3\n1\n", "logic-python-recursion-never-stops", 2, "    if n <= 0:" },
        { "away_factorial.js", "function factorial(n) {\n    if (n <= 1) {\n        return 1;\n    }\n    return n * factorial(n + 1);\n}\n\nconsole.log(factorial(5));\n", "120\n", "logic-js-recursion-never-stops", 5, "    return n * factorial(n - 1);" },
        { "zero_factorial.js", "function factorial(n) {\n    if (n == 0) {\n        return 0;\n    }\n    return n * factorial(n - 1);\n}\n\nconsole.log(factorial(5));\n", "120\n", "logic-js-recursion-never-stops", 3, "        return 1;" },
        { "same_factorial.js", "function factorial(n) {\n    if (n <= 1) {\n        return 1;\n    }\n    return n * factorial(n);\n}\n\nconsole.log(factorial(5));\n", "120\n", "logic-js-recursion-never-stops", 5, "    return n * factorial(n - 1);" },
        { "jump_countdown.js", "function countdown(n) {\n    if (n === 0) {\n        return;\n    }\n    console.log(n);\n    countdown(n - 2);\n}\n\ncountdown(5);\n", "5\n3\n1\n", "logic-js-recursion-never-stops", 2, "    if (n <= 0) {" },
        { "away_factorial.ts", "function factorial(n: number): number {\n    if (n <= 1) {\n        return 1;\n    }\n    return n * factorial(n + 1);\n}\n\nconsole.log(factorial(5));\n", "120\n", "logic-js-recursion-never-stops", 5, "    return n * factorial(n - 1);" },
        { "zero_factorial.ts", "function factorial(n: number): number {\n    if (n == 0) {\n        return 0;\n    }\n    return n * factorial(n - 1);\n}\n\nconsole.log(factorial(5));\n", "120\n", "logic-js-recursion-never-stops", 3, "        return 1;" },
        { "away_factorial.rb", "def factorial(n)\n  if n <= 1\n    return 1\n  end\n  return n * factorial(n + 1)\nend\n\nputs factorial(5)\n", "120\n", "logic-ruby-recursion-never-stops", 5, "  return n * factorial(n - 1)" },
        { "zero_factorial.rb", "def factorial(n)\n  if n == 0\n    return 0\n  end\n  return n * factorial(n - 1)\nend\n\nputs factorial(5)\n", "120\n", "logic-ruby-recursion-never-stops", 3, "    return 1" },
        { "jump_countdown.rb", "def countdown(n)\n  return if n == 0\n  puts n\n  countdown(n - 2)\nend\n\ncountdown(5)\n", "5\n3\n1\n", "logic-ruby-recursion-never-stops", 2, "  return if n <= 0" },
        { "away_factorial.lua", "local function factorial(n)\n  if n <= 1 then\n    return 1\n  end\n  return n * factorial(n + 1)\nend\n\nprint(factorial(5))\n", "120\n", "logic-lua-recursion-never-stops", 5, "  return n * factorial(n - 1)" },
        { "zero_factorial.lua", "local function factorial(n)\n  if n == 0 then\n    return 0\n  end\n  return n * factorial(n - 1)\nend\n\nprint(factorial(5))\n", "120\n", "logic-lua-recursion-never-stops", 3, "    return 1" },
        { "away_factorial.php", "<?php\n\nfunction factorial(int $n): int\n{\n    if ($n <= 1) {\n        return 1;\n    }\n    return $n * factorial($n + 1);\n}\n\necho factorial(5) . \"\\n\";\n", "120\n", "logic-php-recursion-never-stops", 8, "    return $n * factorial($n - 1);" },
        { "zero_factorial.php", "<?php\n\nfunction factorial(int $n): int\n{\n    if ($n == 0) {\n        return 0;\n    }\n    return $n * factorial($n - 1);\n}\n\necho factorial(5) . \"\\n\";\n", "120\n", "logic-php-recursion-never-stops", 6, "        return 1;" },
        { "zero_factorial.pl", "use strict;\nuse warnings;\n\nsub factorial {\n    my ($n) = @_;\n    if ($n == 0) {\n        return 0;\n    }\n    return $n * factorial($n - 1);\n}\n\nprint factorial(5), \"\\n\";\n", "120\n", "logic-perl-recursion-never-stops", 7, "        return 1;" },
        { "zero_factorial.ps1", "function Get-Factorial($n) {\n    if ($n -eq 0) {\n        return 0\n    }\n    return $n * (Get-Factorial ($n - 1))\n}\n\nWrite-Output (Get-Factorial 5)\n", "120\n", "logic-powershell-recursion-never-stops", 3, "        return 1" },
        { "main.go", "package main\n\nimport \"fmt\"\n\nfunc factorial(n int) int {\n\tif n <= 1 {\n\t\treturn 1\n\t}\n\treturn n * factorial(n + 1)\n}\n\nfunc main() {\n\tfmt.Println(factorial(5))\n}\n", "120\n", "logic-go-recursion-never-stops", 9, "\treturn n * factorial(n - 1)" },
        { "main.go", "package main\n\nimport \"fmt\"\n\nfunc factorial(n int) int {\n\tif n == 0 {\n\t\treturn 0\n\t}\n\treturn n * factorial(n - 1)\n}\n\nfunc main() {\n\tfmt.Println(factorial(5)) // 5 times 4 times 3 times 2 times 1\n}\n", "120\n", "logic-go-recursion-never-stops", 7, "\t\treturn 1" },
        { "main.go", "package main\n\nimport \"fmt\"\n\nfunc factorial(n int) int {\n\tif n <= 1 {\n\t\treturn 1\n\t}\n\treturn n * factorial(n)\n}\n\nfunc main() {\n\tfmt.Println(factorial(5))\n}\n", "120\n", "logic-go-recursion-never-stops", 9, "\treturn n * factorial(n - 1)" },
        { "away_factorial.rs", "fn factorial(n: u64) -> u64 {\n    if n <= 1 {\n        return 1;\n    }\n    return n * factorial(n + 1);\n}\n\nfn main() {\n    println!(\"{}\", factorial(5));\n}\n", "120\n", "logic-rust-recursion-never-stops", 5, "    return n * factorial(n - 1);" },
        { "zero_factorial.rs", "fn factorial(n: u64) -> u64 {\n    if n == 0 {\n        return 0;\n    }\n    return n * factorial(n - 1);\n}\n\nfn main() {\n    println!(\"{}\", factorial(5));\n}\n", "120\n", "logic-rust-recursion-never-stops", 3, "        return 1;" },
        { "AwayFactorial.java", "public class AwayFactorial {\n    static long factorial(int n) {\n        if (n <= 1) {\n            return 1;\n        }\n        return n * factorial(n + 1);\n    }\n\n    public static void main(String[] args) {\n        System.out.println(factorial(5));\n    }\n}\n", "120\n", "logic-java-recursion-never-stops", 6, "        return n * factorial(n - 1);" },
        { "ZeroFactorial.java", "public class ZeroFactorial {\n    static long factorial(int n) {\n        if (n == 0) {\n            return 0;\n        }\n        return n * factorial(n - 1);\n    }\n\n    public static void main(String[] args) {\n        System.out.println(factorial(5));\n    }\n}\n", "120\n", "logic-java-recursion-never-stops", 4, "            return 1;" },
        { "JumpCountdown.java", "public class JumpCountdown {\n    static void countdown(int n) {\n        if (n == 0) {\n            return;\n        }\n        System.out.println(n);\n        countdown(n - 2);\n    }\n\n    public static void main(String[] args) {\n        countdown(5);\n    }\n}\n", "5\n3\n1\n", "logic-java-recursion-never-stops", 3, "        if (n <= 0) {" },
        { "AwayFactorial.cs", "Console.WriteLine(Factorial(5));\n\nstatic long Factorial(int n)\n{\n    if (n <= 1)\n    {\n        return 1;\n    }\n    return n * Factorial(n + 1);\n}\n", "120\n", "logic-csharp-recursion-never-stops", 9, "    return n * Factorial(n - 1);" },
        { "ZeroFactorial.cs", "Console.WriteLine(Factorial(5));\n\nstatic long Factorial(int n)\n{\n    if (n == 0)\n    {\n        return 0;\n    }\n    return n * Factorial(n - 1);\n}\n", "120\n", "logic-csharp-recursion-never-stops", 7, "        return 1;" },
        { "SameFactorial.cs", "Console.WriteLine(Factorial(5));\n\nstatic long Factorial(int n)\n{\n    if (n <= 1)\n    {\n        return 1;\n    }\n    return n * Factorial(n);\n}\n", "120\n", "logic-csharp-recursion-never-stops", 9, "    return n * Factorial(n - 1);" },
        { "away_factorial.c", "#include <stdio.h>\n\nlong factorial(int n) {\n    if (n <= 1) {\n        return 1;\n    }\n    return n * factorial(n + 1);\n}\n\nint main(void) {\n    printf(\"%ld\\n\", factorial(5));\n    return 0;\n}\n", "120\n", "logic-c-recursion-never-stops", 7, "    return n * factorial(n - 1);" },
        { "zero_factorial.c", "#include <stdio.h>\n\nlong factorial(int n) {\n    if (n == 0) {\n        return 0;\n    }\n    return n * factorial(n - 1);\n}\n\nint main(void) {\n    printf(\"%ld\\n\", factorial(5));\n    return 0;\n}\n", "120\n", "logic-c-recursion-never-stops", 5, "        return 1;" },
        { "away_factorial.cpp", "#include <iostream>\n\nlong factorial(int n) {\n    if (n <= 1) {\n        return 1;\n    }\n    return n * factorial(n + 1);\n}\n\nint main() {\n    std::cout << factorial(5) << \"\\n\";\n    return 0;\n}\n", "120\n", "logic-c-recursion-never-stops", 7, "    return n * factorial(n - 1);" },
        { "zero_factorial.cpp", "#include <iostream>\n\nlong factorial(int n) {\n    if (n == 0) {\n        return 0;\n    }\n    return n * factorial(n - 1);\n}\n\nint main() {\n    std::cout << factorial(5) << \"\\n\";\n    return 0;\n}\n", "120\n", "logic-c-recursion-never-stops", 5, "        return 1;" },
        { "away_factorial.dart", "int factorial(int n) {\n  if (n <= 1) {\n    return 1;\n  }\n  return n * factorial(n + 1);\n}\n\nvoid main() {\n  print(factorial(5));\n}\n", "120\n", "logic-dart-recursion-never-stops", 5, "  return n * factorial(n - 1);" },
        { "zero_factorial.dart", "int factorial(int n) {\n  if (n == 0) {\n    return 0;\n  }\n  return n * factorial(n - 1);\n}\n\nvoid main() {\n  print(factorial(5));\n}\n", "120\n", "logic-dart-recursion-never-stops", 3, "    return 1;" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheChangeMakesItStopAndPrintWhatItShould(string fileName, string code, string expected, string check, int changeAt, string changedTo)
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

        if (report.Findings.Any(finding => ApplicationControl.Refused(finding.Verified.Summary)))
        {
            output.WriteLine("Windows did not let the copy start here, so the change could not be proved.");
            return;
        }

        var found = report.Findings.SingleOrDefault(finding => finding.RuleId == check);
        Assert.True(found is not null, $"no {check} among {string.Join(", ", report.Findings.Select(finding => finding.RuleId))}");
        Assert.Equal(changeAt, found!.Fix!.StartLine);
        Assert.Equal([changedTo], found.Fix.NewLines);
        Assert.True(found.Verified.IsVerified, found.Verified.Summary);

        Assert.Equal(code, File.ReadAllText(program));
    }
}
