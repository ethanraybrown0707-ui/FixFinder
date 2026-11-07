using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;

namespace FixFinder.Tests;

/// <summary>
/// Recursion whose stop is never reached - a factorial whose call gives n + 1 to a stop at 1, or n as it is, and a countdown by 2
/// that stops only at n == 0 - and a factorial that stops with 0, in each language, with the change the check offers; and the
/// factorial as it should be, which is left alone. Each row is what the check gave for the program when these were written.
/// </summary>
public class RecursionTests : IDisposable
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

    /// <summary>The file, the program, the check, the line it is said of and changed, and what the line becomes.</summary>
    public static TheoryData<string, string, string, int, int, string> Recursions => new()
    {
        { "AwayFactorial.cs", "Console.WriteLine(Factorial(5));\n\nstatic long Factorial(int n)\n{\n    if (n <= 1)\n    {\n        return 1;\n    }\n    return n * Factorial(n + 1);\n}\n", "logic-csharp-recursion-never-stops", 9, 9, "    return n * Factorial(n - 1);" },
        { "AwayFactorial.java", "public class AwayFactorial {\n    static long factorial(int n) {\n        if (n <= 1) {\n            return 1;\n        }\n        return n * factorial(n + 1);\n    }\n\n    public static void main(String[] args) {\n        System.out.println(factorial(5));\n    }\n}\n", "logic-java-recursion-never-stops", 6, 6, "        return n * factorial(n - 1);" },
        { "AwayFactorial.scala", "object AwayFactorial {\n  def factorial(n: Int): Long = {\n    if (n <= 1) {\n      return 1\n    }\n    return n * factorial(n + 1)\n  }\n\n  def main(args: Array[String]): Unit = {\n    println(factorial(5))\n  }\n}\n", "logic-scala-recursion-never-stops", 6, 6, "    return n * factorial(n - 1)" },
        { "away_factorial.c", "#include <stdio.h>\n\nlong factorial(int n) {\n    if (n <= 1) {\n        return 1;\n    }\n    return n * factorial(n + 1);\n}\n\nint main(void) {\n    printf(\"%ld\\n\", factorial(5));\n    return 0;\n}\n", "logic-c-recursion-never-stops", 7, 7, "    return n * factorial(n - 1);" },
        { "away_factorial.cpp", "#include <iostream>\n\nlong factorial(int n) {\n    if (n <= 1) {\n        return 1;\n    }\n    return n * factorial(n + 1);\n}\n\nint main() {\n    std::cout << factorial(5) << \"\\n\";\n    return 0;\n}\n", "logic-c-recursion-never-stops", 7, 7, "    return n * factorial(n - 1);" },
        { "away_factorial.dart", "int factorial(int n) {\n  if (n <= 1) {\n    return 1;\n  }\n  return n * factorial(n + 1);\n}\n\nvoid main() {\n  print(factorial(5));\n}\n", "logic-dart-recursion-never-stops", 5, 5, "  return n * factorial(n - 1);" },
        { "main.go", "package main\n\nimport \"fmt\"\n\nfunc factorial(n int) int {\n\tif n <= 1 {\n\t\treturn 1\n\t}\n\treturn n * factorial(n + 1)\n}\n\nfunc main() {\n\tfmt.Println(factorial(5))\n}\n", "logic-go-recursion-never-stops", 9, 9, "\treturn n * factorial(n - 1)" },
        { "away_factorial.js", "function factorial(n) {\n    if (n <= 1) {\n        return 1;\n    }\n    return n * factorial(n + 1);\n}\n\nconsole.log(factorial(5));\n", "logic-js-recursion-never-stops", 5, 5, "    return n * factorial(n - 1);" },
        { "away_factorial.lua", "local function factorial(n)\n  if n <= 1 then\n    return 1\n  end\n  return n * factorial(n + 1)\nend\n\nprint(factorial(5))\n", "logic-lua-recursion-never-stops", 5, 5, "  return n * factorial(n - 1)" },
        { "away_factorial.php", "<?php\n\nfunction factorial(int $n): int\n{\n    if ($n <= 1) {\n        return 1;\n    }\n    return $n * factorial($n + 1);\n}\n\necho factorial(5) . \"\\n\";\n", "logic-php-recursion-never-stops", 8, 8, "    return $n * factorial($n - 1);" },
        { "away_factorial.pl", "use strict;\nuse warnings;\n\nsub factorial {\n    my ($n) = @_;\n    if ($n <= 1) {\n        return 1;\n    }\n    return $n * factorial($n + 1);\n}\n\nprint factorial(5), \"\\n\";\n", "logic-perl-recursion-never-stops", 9, 9, "    return $n * factorial($n - 1);" },
        { "away_factorial.ps1", "function Get-Factorial($n) {\n    if ($n -le 1) {\n        return 1\n    }\n    return $n * (Get-Factorial ($n + 1))\n}\n\nWrite-Output (Get-Factorial 5)\n", "logic-powershell-recursion-never-stops", 5, 5, "    return $n * (Get-Factorial ($n - 1))" },
        { "away_factorial.py", "def factorial(n):\n    if n <= 1:\n        return 1\n    return n * factorial(n + 1)\n\n\nprint(factorial(5))\n", "logic-python-recursion-never-stops", 4, 4, "    return n * factorial(n - 1)" },
        { "away_factorial.rb", "def factorial(n)\n  if n <= 1\n    return 1\n  end\n  return n * factorial(n + 1)\nend\n\nputs factorial(5)\n", "logic-ruby-recursion-never-stops", 5, 5, "  return n * factorial(n - 1)" },
        { "away_factorial.rs", "fn factorial(n: u64) -> u64 {\n    if n <= 1 {\n        return 1;\n    }\n    return n * factorial(n + 1);\n}\n\nfn main() {\n    println!(\"{}\", factorial(5));\n}\n", "logic-rust-recursion-never-stops", 5, 5, "    return n * factorial(n - 1);" },
        { "away_factorial.ts", "function factorial(n: number): number {\n    if (n <= 1) {\n        return 1;\n    }\n    return n * factorial(n + 1);\n}\n\nconsole.log(factorial(5));\n", "logic-js-recursion-never-stops", 5, 5, "    return n * factorial(n - 1);" },
        { "JumpCountdown.java", "public class JumpCountdown {\n    static void countdown(int n) {\n        if (n == 0) {\n            return;\n        }\n        System.out.println(n);\n        countdown(n - 2);\n    }\n\n    public static void main(String[] args) {\n        countdown(5);\n    }\n}\n", "logic-java-recursion-never-stops", 3, 3, "        if (n <= 0) {" },
        { "jump_countdown.js", "function countdown(n) {\n    if (n === 0) {\n        return;\n    }\n    console.log(n);\n    countdown(n - 2);\n}\n\ncountdown(5);\n", "logic-js-recursion-never-stops", 2, 2, "    if (n <= 0) {" },
        { "jump_countdown.py", "def countdown(n):\n    if n == 0:\n        return\n    print(n)\n    countdown(n - 2)\n\n\ncountdown(5)\n", "logic-python-recursion-never-stops", 2, 2, "    if n <= 0:" },
        { "jump_countdown.rb", "def countdown(n)\n  return if n == 0\n  puts n\n  countdown(n - 2)\nend\n\ncountdown(5)\n", "logic-ruby-recursion-never-stops", 2, 2, "  return if n <= 0" },
        { "SameFactorial.cs", "Console.WriteLine(Factorial(5));\n\nstatic long Factorial(int n)\n{\n    if (n <= 1)\n    {\n        return 1;\n    }\n    return n * Factorial(n);\n}\n", "logic-csharp-recursion-never-stops", 9, 9, "    return n * Factorial(n - 1);" },
        { "main.go", "package main\n\nimport \"fmt\"\n\nfunc factorial(n int) int {\n\tif n <= 1 {\n\t\treturn 1\n\t}\n\treturn n * factorial(n)\n}\n\nfunc main() {\n\tfmt.Println(factorial(5))\n}\n", "logic-go-recursion-never-stops", 9, 9, "\treturn n * factorial(n - 1)" },
        { "same_factorial.js", "function factorial(n) {\n    if (n <= 1) {\n        return 1;\n    }\n    return n * factorial(n);\n}\n\nconsole.log(factorial(5));\n", "logic-js-recursion-never-stops", 5, 5, "    return n * factorial(n - 1);" },
        { "same_factorial.py", "def factorial(n):\n    if n <= 1:\n        return 1\n    return n * factorial(n)\n\n\nprint(factorial(5))\n", "logic-python-recursion-never-stops", 4, 4, "    return n * factorial(n - 1)" },
        { "ZeroFactorial.cs", "Console.WriteLine(Factorial(5));\n\nstatic long Factorial(int n)\n{\n    if (n == 0)\n    {\n        return 0;\n    }\n    return n * Factorial(n - 1);\n}\n", "logic-csharp-recursion-never-stops", 7, 7, "        return 1;" },
        { "ZeroFactorial.java", "public class ZeroFactorial {\n    static long factorial(int n) {\n        if (n == 0) {\n            return 0;\n        }\n        return n * factorial(n - 1);\n    }\n\n    public static void main(String[] args) {\n        System.out.println(factorial(5));\n    }\n}\n", "logic-java-recursion-never-stops", 4, 4, "            return 1;" },
        { "ZeroFactorial.scala", "object ZeroFactorial {\n  def factorial(n: Int): Long = {\n    if (n == 0) {\n      return 0\n    }\n    return n * factorial(n - 1)\n  }\n\n  def main(args: Array[String]): Unit = {\n    println(factorial(5))\n  }\n}\n", "logic-scala-recursion-never-stops", 4, 4, "      return 1" },
        { "zero_factorial.c", "#include <stdio.h>\n\nlong factorial(int n) {\n    if (n == 0) {\n        return 0;\n    }\n    return n * factorial(n - 1);\n}\n\nint main(void) {\n    printf(\"%ld\\n\", factorial(5));\n    return 0;\n}\n", "logic-c-recursion-never-stops", 5, 5, "        return 1;" },
        { "zero_factorial.cpp", "#include <iostream>\n\nlong factorial(int n) {\n    if (n == 0) {\n        return 0;\n    }\n    return n * factorial(n - 1);\n}\n\nint main() {\n    std::cout << factorial(5) << \"\\n\";\n    return 0;\n}\n", "logic-c-recursion-never-stops", 5, 5, "        return 1;" },
        { "zero_factorial.dart", "int factorial(int n) {\n  if (n == 0) {\n    return 0;\n  }\n  return n * factorial(n - 1);\n}\n\nvoid main() {\n  print(factorial(5));\n}\n", "logic-dart-recursion-never-stops", 3, 3, "    return 1;" },
        { "main.go", "package main\n\nimport \"fmt\"\n\nfunc factorial(n int) int {\n\tif n == 0 {\n\t\treturn 0\n\t}\n\treturn n * factorial(n - 1)\n}\n\nfunc main() {\n\tfmt.Println(factorial(5)) // 5 times 4 times 3 times 2 times 1\n}\n", "logic-go-recursion-never-stops", 7, 7, "\t\treturn 1" },
        { "zero_factorial.js", "function factorial(n) {\n    if (n == 0) {\n        return 0;\n    }\n    return n * factorial(n - 1);\n}\n\nconsole.log(factorial(5));\n", "logic-js-recursion-never-stops", 3, 3, "        return 1;" },
        { "zero_factorial.lua", "local function factorial(n)\n  if n == 0 then\n    return 0\n  end\n  return n * factorial(n - 1)\nend\n\nprint(factorial(5))\n", "logic-lua-recursion-never-stops", 3, 3, "    return 1" },
        { "zero_factorial.php", "<?php\n\nfunction factorial(int $n): int\n{\n    if ($n == 0) {\n        return 0;\n    }\n    return $n * factorial($n - 1);\n}\n\necho factorial(5) . \"\\n\";\n", "logic-php-recursion-never-stops", 6, 6, "        return 1;" },
        { "zero_factorial.pl", "use strict;\nuse warnings;\n\nsub factorial {\n    my ($n) = @_;\n    if ($n == 0) {\n        return 0;\n    }\n    return $n * factorial($n - 1);\n}\n\nprint factorial(5), \"\\n\";\n", "logic-perl-recursion-never-stops", 7, 7, "        return 1;" },
        { "zero_factorial.ps1", "function Get-Factorial($n) {\n    if ($n -eq 0) {\n        return 0\n    }\n    return $n * (Get-Factorial ($n - 1))\n}\n\nWrite-Output (Get-Factorial 5)\n", "logic-powershell-recursion-never-stops", 3, 3, "        return 1" },
        { "zero_factorial.py", "def factorial(n):\n    if n == 0:\n        return 0\n    return n * factorial(n - 1)\n\n\nprint(factorial(5))\n", "logic-python-recursion-never-stops", 3, 3, "        return 1" },
        { "zero_factorial.rb", "def factorial(n)\n  if n == 0\n    return 0\n  end\n  return n * factorial(n - 1)\nend\n\nputs factorial(5)\n", "logic-ruby-recursion-never-stops", 3, 3, "    return 1" },
        { "zero_factorial.rs", "fn factorial(n: u64) -> u64 {\n    if n == 0 {\n        return 0;\n    }\n    return n * factorial(n - 1);\n}\n\nfn main() {\n    println!(\"{}\", factorial(5));\n}\n", "logic-rust-recursion-never-stops", 3, 3, "        return 1;" },
        { "zero_factorial.ts", "function factorial(n: number): number {\n    if (n == 0) {\n        return 0;\n    }\n    return n * factorial(n - 1);\n}\n\nconsole.log(factorial(5));\n", "logic-js-recursion-never-stops", 3, 3, "        return 1;" },
    };

    [Theory]
    [MemberData(nameof(Recursions))]
    public void RecursionThatNeverStopsOrStopsWithZeroIsFound(string fileName, string code, string check, int line, int changeAt, string changedTo)
    {
        var found = Assert.Single(LogicPatterns.Scan(Program(fileName, code)), finding => finding.PatternId == check);

        Assert.Equal(line, found.Line);
        Assert.Equal(changeAt, found.Fix!.StartLine);
        Assert.Equal(1, found.Fix.RemoveCount);
        Assert.Equal([changedTo], found.Fix.NewLines);
    }

    [Theory]
    [InlineData("factorial.c", "#include <stdio.h>\n\nlong factorial(int n) {\n    if (n <= 1) {\n        return 1;\n    }\n    return n * factorial(n - 1);\n}\n\nint main(void) {\n    printf(\"%ld\\n\", factorial(5));\n    return 0;\n}\n")]
    [InlineData("factorial.cpp", "#include <iostream>\n\nlong factorial(int n) {\n    if (n <= 1) {\n        return 1;\n    }\n    return n * factorial(n - 1);\n}\n\nint main() {\n    std::cout << factorial(5) << \"\\n\";\n    return 0;\n}\n")]
    [InlineData("Factorial.cs", "Console.WriteLine(Factorial(5));\n\nstatic long Factorial(int n)\n{\n    if (n <= 1)\n    {\n        return 1;\n    }\n    return n * Factorial(n - 1);\n}\n")]
    [InlineData("factorial.dart", "int factorial(int n) {\n  if (n <= 1) {\n    return 1;\n  }\n  return n * factorial(n - 1);\n}\n\nvoid main() {\n  print(factorial(5));\n}\n")]
    [InlineData("main.go", "package main\n\nimport \"fmt\"\n\nfunc factorial(n int) int {\n\tif n <= 1 {\n\t\treturn 1\n\t}\n\treturn n * factorial(n - 1)\n}\n\nfunc main() {\n\tfmt.Println(factorial(5))\n}\n")]
    [InlineData("Factorial.java", "public class Factorial {\n    static long factorial(int n) {\n        if (n <= 1) {\n            return 1;\n        }\n        return n * factorial(n - 1);\n    }\n\n    public static void main(String[] args) {\n        System.out.println(factorial(5));\n    }\n}\n")]
    [InlineData("factorial.js", "function factorial(n) {\n    if (n <= 1) {\n        return 1;\n    }\n    return n * factorial(n - 1);\n}\n\nconsole.log(factorial(5));\n")]
    [InlineData("factorial.lua", "local function factorial(n)\n  if n <= 1 then\n    return 1\n  end\n  return n * factorial(n - 1)\nend\n\nprint(factorial(5))\n")]
    [InlineData("factorial.php", "<?php\n\nfunction factorial(int $n): int\n{\n    if ($n <= 1) {\n        return 1;\n    }\n    return $n * factorial($n - 1);\n}\n\necho factorial(5) . \"\\n\";\n")]
    [InlineData("factorial.pl", "use strict;\nuse warnings;\n\nsub factorial {\n    my ($n) = @_;\n    if ($n <= 1) {\n        return 1;\n    }\n    return $n * factorial($n - 1);\n}\n\nprint factorial(5), \"\\n\";\n")]
    [InlineData("factorial.ps1", "function Get-Factorial($n) {\n    if ($n -le 1) {\n        return 1\n    }\n    return $n * (Get-Factorial ($n - 1))\n}\n\nWrite-Output (Get-Factorial 5)\n")]
    [InlineData("factorial.py", "def factorial(n):\n    if n <= 1:\n        return 1\n    return n * factorial(n - 1)\n\n\nprint(factorial(5))\n")]
    [InlineData("factorial.rb", "def factorial(n)\n  if n <= 1\n    return 1\n  end\n  return n * factorial(n - 1)\nend\n\nputs factorial(5)\n")]
    [InlineData("factorial.rs", "fn factorial(n: u64) -> u64 {\n    if n <= 1 {\n        return 1;\n    }\n    return n * factorial(n - 1);\n}\n\nfn main() {\n    println!(\"{}\", factorial(5));\n}\n")]
    [InlineData("Factorial.scala", "object Factorial {\n  def factorial(n: Int): Long = {\n    if (n <= 1) {\n      return 1\n    }\n    return n * factorial(n - 1)\n  }\n\n  def main(args: Array[String]): Unit = {\n    println(factorial(5))\n  }\n}\n")]
    [InlineData("factorial.ts", "function factorial(n: number): number {\n    if (n <= 1) {\n        return 1;\n    }\n    return n * factorial(n - 1);\n}\n\nconsole.log(factorial(5));\n")]
    public void AFactorialThatStopsIsLeftAlone(string fileName, string code)
    {
        Assert.DoesNotContain(LogicPatterns.Scan(Program(fileName, code)), finding => finding.PatternId.EndsWith("recursion-never-stops", StringComparison.Ordinal));
    }
}
