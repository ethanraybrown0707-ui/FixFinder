using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;

namespace FixFinder.Tests;

/// <summary>
/// Euclid's greatest common divisor in each language going on with a % b and b, with the change that goes on with b and
/// a % b; the same searches going on so, and programs near the mistake but not it, which are left alone. Each row is what the check gave for the
/// program when these were written.
/// </summary>
public class EuclidPairNotMovedTests : IDisposable
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

    /// <summary>The file, the program, the check, the line it is said of, the line the change starts on, how many it takes away, and the lines it writes, one a line.</summary>
    public static TheoryData<string, string, string, int, int, int, string> Mistakes => new()
    {
        { "gcd.c", "#include <stdio.h>\n\nstatic int gcd(int a, int b) {\n    if (b == 0) return a;\n    return gcd(a % b, b);\n}\n\nint main(void) {\n    printf(\"%d %d %d\\n\", gcd(35, 21), gcd(17, 0), gcd(48, 18));\n    return 0;\n}\n", "logic-c-euclid-pair-not-moved", 5, 5, 1, "    return gcd(b, a % b);" },
        { "gcd.cpp", "#include <iostream>\n\nint gcd(int a, int b) {\n    if (b == 0) {\n        return a;\n    }\n    return gcd(a % b, b);\n}\n\nint main() {\n    std::cout << gcd(35, 21) << \" \" << gcd(17, 0) << \" \" << gcd(48, 18) << std::endl;\n    return 0;\n}\n", "logic-cpp-euclid-pair-not-moved", 7, 7, 1, "    return gcd(b, a % b);" },
        { "gcd.dart", "int gcd(int a, int b) {\n  if (b == 0) {\n    return a;\n  }\n  return gcd(a % b, b);\n}\n\nvoid main() {\n  print('${gcd(35, 21)} ${gcd(17, 0)} ${gcd(48, 18)}');\n}\n", "logic-dart-euclid-pair-not-moved", 5, 5, 1, "  return gcd(b, a % b);" },
        { "gcd.erl", "-module(gcd).\n-export([main/0]).\n\ngcd(A, 0) -> A;\ngcd(A, B) -> gcd(A rem B, B).\n\nmain() ->\n    io:format(\"~p ~p ~p~n\", [gcd(35, 21), gcd(17, 0), gcd(48, 18)]).\n", "logic-erlang-euclid-pair-not-moved", 5, 5, 1, "gcd(A, B) -> gcd(B, A rem B)." },
        { "gcd.exs", "defmodule Divisor do\n  def gcd(a, 0), do: a\n  def gcd(a, b), do: gcd(rem(a, b), b)\nend\n\nIO.puts(\"#{Divisor.gcd(35, 21)} #{Divisor.gcd(17, 0)} #{Divisor.gcd(48, 18)}\")\n", "logic-elixir-euclid-pair-not-moved", 3, 3, 1, "  def gcd(a, b), do: gcd(b, rem(a, b))" },
        { "gcd.f90", "module gcd_module\n  implicit none\ncontains\n  recursive integer function gcd(a, b) result(divisor)\n    integer, intent(in) :: a, b\n    if (b == 0) then\n      divisor = a\n    else\n      divisor = gcd(mod(a, b), b)\n    end if\n  end function gcd\nend module gcd_module\n\nprogram gcd_program\n  use gcd_module\n  implicit none\n  print '(I0, 1X, I0, 1X, I0)', gcd(35, 21), gcd(17, 0), gcd(48, 18)\nend program gcd_program\n", "logic-fortran-euclid-pair-not-moved", 9, 9, 1, "      divisor = gcd(b, mod(a, b))" },
        { "gcd.fsx", "let rec gcd a b =\n    if b = 0 then a else gcd (a % b) b\n\nprintfn \"%d %d %d\" (gcd 35 21) (gcd 17 0) (gcd 48 18)\n", "logic-fsharp-euclid-pair-not-moved", 2, 2, 1, "    if b = 0 then a else gcd b (a % b)" },
        { "gcd.go", "package main\n\nimport \"fmt\"\n\nfunc gcd(a, b int) int {\n\tfor b != 0 {\n\t\ta, b = a%b, b\n\t}\n\treturn a\n}\n\nfunc main() {\n\tfmt.Println(gcd(35, 21), gcd(17, 0), gcd(48, 18))\n}\n", "logic-go-euclid-pair-not-moved", 7, 7, 1, "\t\ta, b = b, a%b" },
        { "Gcd.java", "public class Gcd {\n    public static int gcd(int a, int b) {\n        if (b == 0) {\n            return a;\n        } else {\n            return gcd(a % b, b);\n        }\n    }\n\n    public static void main(String[] args) {\n        System.out.println(gcd(35, 21) + \" \" + gcd(17, 0) + \" \" + gcd(48, 18));\n    }\n}\n", "logic-java-euclid-pair-not-moved", 6, 6, 1, "            return gcd(b, a % b);" },
        { "gcd.js", "function gcd(a, b) {\n  return b === 0 ? a : gcd(a % b, b);\n}\n\nconsole.log(gcd(35, 21), gcd(17, 0), gcd(48, 18));\n", "logic-js-euclid-pair-not-moved", 2, 2, 1, "  return b === 0 ? a : gcd(b, a % b);" },
        { "gcd.lua", "local function gcd(a, b)\n  if b == 0 then\n    return a\n  end\n  return gcd(a % b, b)\nend\n\nprint(gcd(35, 21) .. \" \" .. gcd(17, 0) .. \" \" .. gcd(48, 18))\n", "logic-lua-euclid-pair-not-moved", 5, 5, 1, "  return gcd(b, a % b)" },
        { "gcd.ml", "let rec gcd a b =\n  if b = 0 then a else gcd (a mod b) b\n\nlet () = Printf.printf \"%d %d %d\\n\" (gcd 35 21) (gcd 17 0) (gcd 48 18)\n", "logic-ocaml-euclid-pair-not-moved", 2, 2, 1, "  if b = 0 then a else gcd b (a mod b)" },
        { "gcd.php", "<?php\nfunction gcd(int $a, int $b): int {\n    if ($b == 0) {\n        return $a;\n    }\n    return gcd($a % $b, $b);\n}\n\necho gcd(35, 21), \" \", gcd(17, 0), \" \", gcd(48, 18), \"\\n\";\n", "logic-php-euclid-pair-not-moved", 6, 6, 1, "    return gcd($b, $a % $b);" },
        { "gcd.pl", "use strict;\nuse warnings;\n\nsub gcd {\n    my ($a, $b) = @_;\n    while ($b != 0) {\n        ($a, $b) = ($a % $b, $b);\n    }\n    return $a;\n}\n\nprint join(\" \", gcd(35, 21), gcd(17, 0), gcd(48, 18)), \"\\n\";\n", "logic-perl-euclid-pair-not-moved", 7, 7, 1, "        ($a, $b) = ($b, $a % $b);" },
        { "gcd.ps1", "function Get-Gcd([int]$a, [int]$b) {\n    if ($b -eq 0) {\n        return $a\n    }\n    return Get-Gcd ($a % $b) $b\n}\n\n\"$(Get-Gcd 35 21) $(Get-Gcd 17 0) $(Get-Gcd 48 18)\"\n", "logic-powershell-euclid-pair-not-moved", 5, 5, 1, "    return Get-Gcd $b ($a % $b)" },
        { "gcd.py", "def gcd(a, b):\n    if b == 0:\n        return a\n    else:\n        return gcd(a % b, b)\n\n\nprint(gcd(35, 21), gcd(17, 0), gcd(48, 18))\n", "logic-python-euclid-pair-not-moved", 5, 5, 1, "        return gcd(b, a % b)" },
        { "gcd.rb", "def gcd(a, b)\n  return a if b.zero?\n\n  gcd(a % b, b)\nend\n\nputs \"#{gcd(35, 21)} #{gcd(17, 0)} #{gcd(48, 18)}\"\n", "logic-ruby-euclid-pair-not-moved", 4, 4, 1, "  gcd(b, a % b)" },
        { "gcd.rs", "fn gcd(a: u32, b: u32) -> u32 {\n    if b == 0 {\n        a\n    } else {\n        gcd(a % b, b)\n    }\n}\n\nfn main() {\n    println!(\"{} {} {}\", gcd(35, 21), gcd(17, 0), gcd(48, 18));\n}\n", "logic-rust-euclid-pair-not-moved", 5, 5, 1, "        gcd(b, a % b)" },
        { "Gcd.scala", "object Gcd {\n  def gcd(a: Int, b: Int): Int =\n    if (b == 0) a else gcd(a % b, b)\n\n  def main(args: Array[String]): Unit = println(s\"${gcd(35, 21)} ${gcd(17, 0)} ${gcd(48, 18)}\")\n}\n", "logic-scala-euclid-pair-not-moved", 3, 3, 1, "    if (b == 0) a else gcd(b, a % b)" },
        { "gcd.ts", "function gcd(a: number, b: number): number {\n  if (b === 0) return a;\n  return gcd(a % b, b);\n}\n\nconsole.log(gcd(35, 21), gcd(17, 0), gcd(48, 18));\n", "logic-js-euclid-pair-not-moved", 3, 3, 1, "  return gcd(b, a % b);" },
        { "gcd_loop.py", "def gcd(a, b):\n    while b != 0:\n        a, b = a % b, b\n    return a\n\n\nprint(gcd(35, 21), gcd(17, 0), gcd(48, 18))\n", "logic-python-euclid-pair-not-moved", 3, 3, 1, "        a, b = b, a % b" },
        { "Program.cs", "using System;\n\npublic static class Program\n{\n    static int Gcd(int first, int second)\n    {\n        while (second != 0)\n        {\n            (first, second) = (first % second, second);\n        }\n\n        return first;\n    }\n\n    public static void Main()\n    {\n        Console.WriteLine($\"{Gcd(35, 21)} {Gcd(17, 0)} {Gcd(48, 18)}\");\n    }\n}\n", "logic-csharp-euclid-pair-not-moved", 9, 9, 1, "            (first, second) = (second, first % second);" },
    };

    [Theory]
    [MemberData(nameof(Mistakes))]
    public void EuclidsPairInTheWrongOrderIsFound(string fileName, string code, string check, int line, int changeAt, int removed, string added)
    {
        var found = Assert.Single(LogicPatterns.All.Where(pattern => pattern.Id == check).SelectMany(pattern => pattern.Find(Program(fileName, code))));

        Assert.Equal(line, found.Line);
        Assert.Equal(changeAt, found.Fix!.StartLine);
        Assert.Equal(removed, found.Fix.RemoveCount);
        Assert.Equal(added.Split('\n'), found.Fix.NewLines);
    }

    /// <summary>
    /// The searches going on with b and a % b; Euclid's way stopping when the first number is 0, b % a going into a's
    /// place and a into b's; a number brought below a bound, which tests the number and keeps the bound, as meant; a loop
    /// moving the pair on; and Euclid's way by taking the smaller number from the larger.
    /// </summary>
    [Theory]
    [InlineData("gcd.c", "#include <stdio.h>\n\nstatic int gcd(int a, int b) {\n    if (b == 0) return a;\n    return gcd(b, a % b);\n}\n\nint main(void) {\n    printf(\"%d %d %d\\n\", gcd(35, 21), gcd(17, 0), gcd(48, 18));\n    return 0;\n}\n")]
    [InlineData("gcd.cpp", "#include <iostream>\n\nint gcd(int a, int b) {\n    if (b == 0) {\n        return a;\n    }\n    return gcd(b, a % b);\n}\n\nint main() {\n    std::cout << gcd(35, 21) << \" \" << gcd(17, 0) << \" \" << gcd(48, 18) << std::endl;\n    return 0;\n}\n")]
    [InlineData("gcd.dart", "int gcd(int a, int b) {\n  if (b == 0) {\n    return a;\n  }\n  return gcd(b, a % b);\n}\n\nvoid main() {\n  print('${gcd(35, 21)} ${gcd(17, 0)} ${gcd(48, 18)}');\n}\n")]
    [InlineData("gcd.erl", "-module(gcd).\n-export([main/0]).\n\ngcd(A, 0) -> A;\ngcd(A, B) -> gcd(B, A rem B).\n\nmain() ->\n    io:format(\"~p ~p ~p~n\", [gcd(35, 21), gcd(17, 0), gcd(48, 18)]).\n")]
    [InlineData("gcd.exs", "defmodule Divisor do\n  def gcd(a, 0), do: a\n  def gcd(a, b), do: gcd(b, rem(a, b))\nend\n\nIO.puts(\"#{Divisor.gcd(35, 21)} #{Divisor.gcd(17, 0)} #{Divisor.gcd(48, 18)}\")\n")]
    [InlineData("gcd.f90", "module gcd_module\n  implicit none\ncontains\n  recursive integer function gcd(a, b) result(divisor)\n    integer, intent(in) :: a, b\n    if (b == 0) then\n      divisor = a\n    else\n      divisor = gcd(b, mod(a, b))\n    end if\n  end function gcd\nend module gcd_module\n\nprogram gcd_program\n  use gcd_module\n  implicit none\n  print '(I0, 1X, I0, 1X, I0)', gcd(35, 21), gcd(17, 0), gcd(48, 18)\nend program gcd_program\n")]
    [InlineData("gcd.fsx", "let rec gcd a b =\n    if b = 0 then a else gcd b (a % b)\n\nprintfn \"%d %d %d\" (gcd 35 21) (gcd 17 0) (gcd 48 18)\n")]
    [InlineData("gcd.go", "package main\n\nimport \"fmt\"\n\nfunc gcd(a, b int) int {\n\tfor b != 0 {\n\t\ta, b = b, a%b\n\t}\n\treturn a\n}\n\nfunc main() {\n\tfmt.Println(gcd(35, 21), gcd(17, 0), gcd(48, 18))\n}\n")]
    [InlineData("Gcd.java", "public class Gcd {\n    public static int gcd(int a, int b) {\n        if (b == 0) {\n            return a;\n        } else {\n            return gcd(b, a % b);\n        }\n    }\n\n    public static void main(String[] args) {\n        System.out.println(gcd(35, 21) + \" \" + gcd(17, 0) + \" \" + gcd(48, 18));\n    }\n}\n")]
    [InlineData("gcd.js", "function gcd(a, b) {\n  return b === 0 ? a : gcd(b, a % b);\n}\n\nconsole.log(gcd(35, 21), gcd(17, 0), gcd(48, 18));\n")]
    [InlineData("gcd.lua", "local function gcd(a, b)\n  if b == 0 then\n    return a\n  end\n  return gcd(b, a % b)\nend\n\nprint(gcd(35, 21) .. \" \" .. gcd(17, 0) .. \" \" .. gcd(48, 18))\n")]
    [InlineData("gcd.ml", "let rec gcd a b =\n  if b = 0 then a else gcd b (a mod b)\n\nlet () = Printf.printf \"%d %d %d\\n\" (gcd 35 21) (gcd 17 0) (gcd 48 18)\n")]
    [InlineData("gcd.php", "<?php\nfunction gcd(int $a, int $b): int {\n    if ($b == 0) {\n        return $a;\n    }\n    return gcd($b, $a % $b);\n}\n\necho gcd(35, 21), \" \", gcd(17, 0), \" \", gcd(48, 18), \"\\n\";\n")]
    [InlineData("gcd.pl", "use strict;\nuse warnings;\n\nsub gcd {\n    my ($a, $b) = @_;\n    while ($b != 0) {\n        ($a, $b) = ($b, $a % $b);\n    }\n    return $a;\n}\n\nprint join(\" \", gcd(35, 21), gcd(17, 0), gcd(48, 18)), \"\\n\";\n")]
    [InlineData("gcd.ps1", "function Get-Gcd([int]$a, [int]$b) {\n    if ($b -eq 0) {\n        return $a\n    }\n    return Get-Gcd $b ($a % $b)\n}\n\n\"$(Get-Gcd 35 21) $(Get-Gcd 17 0) $(Get-Gcd 48 18)\"\n")]
    [InlineData("gcd.py", "def gcd(a, b):\n    if b == 0:\n        return a\n    else:\n        return gcd(b, a % b)\n\n\nprint(gcd(35, 21), gcd(17, 0), gcd(48, 18))\n")]
    [InlineData("gcd.rb", "def gcd(a, b)\n  return a if b.zero?\n\n  gcd(b, a % b)\nend\n\nputs \"#{gcd(35, 21)} #{gcd(17, 0)} #{gcd(48, 18)}\"\n")]
    [InlineData("gcd.rs", "fn gcd(a: u32, b: u32) -> u32 {\n    if b == 0 {\n        a\n    } else {\n        gcd(b, a % b)\n    }\n}\n\nfn main() {\n    println!(\"{} {} {}\", gcd(35, 21), gcd(17, 0), gcd(48, 18));\n}\n")]
    [InlineData("Gcd.scala", "object Gcd {\n  def gcd(a: Int, b: Int): Int =\n    if (b == 0) a else gcd(b, a % b)\n\n  def main(args: Array[String]): Unit = println(s\"${gcd(35, 21)} ${gcd(17, 0)} ${gcd(48, 18)}\")\n}\n")]
    [InlineData("gcd.ts", "function gcd(a: number, b: number): number {\n  if (b === 0) return a;\n  return gcd(b, a % b);\n}\n\nconsole.log(gcd(35, 21), gcd(17, 0), gcd(48, 18));\n")]
    [InlineData("gcd_loop.py", "def gcd(a, b):\n    while b != 0:\n        a, b = b, a % b\n    return a\n\n\nprint(gcd(35, 21), gcd(17, 0), gcd(48, 18))\n")]
    [InlineData("Program.cs", "using System;\n\npublic static class Program\n{\n    static int Gcd(int first, int second)\n    {\n        while (second != 0)\n        {\n            (first, second) = (second, first % second);\n        }\n\n        return first;\n    }\n\n    public static void Main()\n    {\n        Console.WriteLine($\"{Gcd(35, 21)} {Gcd(17, 0)} {Gcd(48, 18)}\");\n    }\n}\n")]
    [InlineData("loop_moves.js", "// Euclid's way in a loop, the pair moved on: prints 7 17 6.\nfunction gcd(a, b) {\n  while (b !== 0) {\n    [a, b] = [b, a % b];\n  }\n  return a;\n}\n\nconsole.log(gcd(35, 21), gcd(17, 0), gcd(48, 18));\n")]
    [InlineData("reduce.py", "# A number brought below a bound, stopping when it is: the bound is kept, as meant - prints 2 0 3.\ndef reduce(number, bound):\n    if bound == 0:\n        raise ValueError(\"no bound\")\n    if number < bound:\n        return number\n    return reduce(number % bound, bound)\n\n\nprint(reduce(17, 5), reduce(20, 4), reduce(3, 7))\n")]
    [InlineData("stops_at_first.py", "# Euclid's way stopping when the first number is 0: b % a goes into a's place, and a into b's - prints 7 17 6.\ndef gcd(a, b):\n    if a == 0:\n        return b\n    return gcd(b % a, a)\n\n\nprint(gcd(35, 21), gcd(17, 0), gcd(48, 18))\n")]
    [InlineData("Subtracted.java", "// Euclid's way by taking the smaller number from the larger: prints 7 17 6.\npublic class Subtracted {\n    static int gcd(int a, int b) {\n        if (b == 0) return a;\n        if (a == 0) return b;\n        return a > b ? gcd(a - b, b) : gcd(a, b - a);\n    }\n\n    public static void main(String[] args) {\n        System.out.println(gcd(35, 21) + \" \" + gcd(17, 0) + \" \" + gcd(48, 18));\n    }\n}\n")]
    public void EuclidsPairMovedOnIsLeftAlone(string fileName, string code)
    {
        // Each check of the file's own language, as FixFinder runs them.
        var file = Program(fileName, code);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        Assert.Empty(LogicPatterns.All.Where(pattern => pattern.Id.EndsWith("euclid-pair-not-moved", StringComparison.Ordinal) && pattern.Extensions.Contains(extension))
            .SelectMany(pattern => pattern.Find(file)));
    }
}
