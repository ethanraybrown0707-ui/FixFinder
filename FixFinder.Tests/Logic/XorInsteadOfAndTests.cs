using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;

namespace FixFinder.Tests;

/// <summary>
/// A count of set bits in each language that takes the lowest one away with xor, n ^= n - 1, with the change that uses
/// and; the same counts using and, and programs near the mistake but not it, which are left alone. Each row is what the check gave for the
/// program when these were written.
/// </summary>
public class XorInsteadOfAndTests : IDisposable
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
        { "bitcount.c", "#include <stdio.h>\n\nstatic int bitcount(unsigned int n) {\n    int count = 0;\n    while (n) {\n        n ^= n - 1;\n        count++;\n    }\n    return count;\n}\n\nint main(void) {\n    printf(\"%d %d %d\\n\", bitcount(127), bitcount(128), bitcount(3005));\n    return 0;\n}\n", "logic-c-xor-instead-of-and", 6, 6, 1, "        n &= n - 1;" },
        { "bitcount.cpp", "#include <iostream>\n\nint bitcount(unsigned int n) {\n    int count = 0;\n    for (; n != 0; count++) {\n        n ^= n - 1;\n    }\n    return count;\n}\n\nint main() {\n    std::cout << bitcount(127) << \" \" << bitcount(128) << \" \" << bitcount(3005) << std::endl;\n    return 0;\n}\n", "logic-cpp-xor-instead-of-and", 6, 6, 1, "        n &= n - 1;" },
        { "bitcount.dart", "int bitcount(int n) {\n  var count = 0;\n  while (n != 0) {\n    n ^= n - 1;\n    count++;\n  }\n  return count;\n}\n\nvoid main() {\n  print('${bitcount(127)} ${bitcount(128)} ${bitcount(3005)}');\n}\n", "logic-dart-xor-instead-of-and", 4, 4, 1, "    n &= n - 1;" },
        { "bitcount.erl", "-module(bitcount).\n-export([main/0]).\n\ncount(0, Count) -> Count;\ncount(N, Count) -> count(N bxor (N - 1), Count + 1).\n\nmain() ->\n    io:format(\"~p ~p ~p~n\", [count(127, 0), count(128, 0), count(3005, 0)]).\n", "logic-erlang-xor-instead-of-and", 5, 5, 1, "count(N, Count) -> count(N band (N - 1), Count + 1)." },
        { "bitcount.exs", "defmodule Bits do\n  import Bitwise\n\n  def count(n), do: count(n, 0)\n\n  defp count(0, count), do: count\n  defp count(n, count), do: count(bxor(n, n - 1), count + 1)\nend\n\nIO.puts(\"#{Bits.count(127)} #{Bits.count(128)} #{Bits.count(3005)}\")\n", "logic-elixir-xor-instead-of-and", 7, 7, 1, "  defp count(n, count), do: count(band(n, n - 1), count + 1)" },
        { "bitcount.f90", "module bits_module\n  implicit none\ncontains\n  integer function bitcount(start)\n    integer, intent(in) :: start\n    integer :: n\n    n = start\n    bitcount = 0\n    do while (n /= 0)\n      n = ieor(n, n - 1)\n      bitcount = bitcount + 1\n    end do\n  end function bitcount\nend module bits_module\n\nprogram bitcount_program\n  use bits_module\n  implicit none\n  print '(I0, 1X, I0, 1X, I0)', bitcount(127), bitcount(128), bitcount(3005)\nend program bitcount_program\n", "logic-fortran-xor-instead-of-and", 10, 10, 1, "      n = iand(n, n - 1)" },
        { "bitcount.fsx", "let rec countBits n count =\n    if n = 0 then count else countBits (n ^^^ (n - 1)) (count + 1)\n\nprintfn \"%d %d %d\" (countBits 127 0) (countBits 128 0) (countBits 3005 0)\n", "logic-fsharp-xor-instead-of-and", 2, 2, 1, "    if n = 0 then count else countBits (n &&& (n - 1)) (count + 1)" },
        { "bitcount.go", "package main\n\nimport \"fmt\"\n\nfunc bitcount(n uint) int {\n\tcount := 0\n\tfor n != 0 {\n\t\tn ^= n - 1\n\t\tcount++\n\t}\n\treturn count\n}\n\nfunc main() {\n\tfmt.Println(bitcount(127), bitcount(128), bitcount(3005))\n}\n", "logic-go-xor-instead-of-and", 8, 8, 1, "\t\tn &= n - 1" },
        { "Bitcount.java", "public class Bitcount {\n    public static int bitcount(int n) {\n        int count = 0;\n        while (n != 0) {\n            n = (n ^ (n - 1));\n            count++;\n        }\n        return count;\n    }\n\n    public static void main(String[] args) {\n        System.out.println(bitcount(127) + \" \" + bitcount(128) + \" \" + bitcount(3005));\n    }\n}\n", "logic-java-xor-instead-of-and", 5, 5, 1, "            n = (n & (n - 1));" },
        { "bitcount.js", "function bitcount(n) {\n  let count = 0;\n  while (n !== 0) {\n    n = n ^ (n - 1);\n    count++;\n  }\n  return count;\n}\n\nconsole.log(bitcount(127), bitcount(128), bitcount(3005));\n", "logic-js-xor-instead-of-and", 4, 4, 1, "    n = n & (n - 1);" },
        { "bitcount.lua", "local function bitcount(n)\n  local count = 0\n  while n ~= 0 do\n    n = n ~ (n - 1)\n    count = count + 1\n  end\n  return count\nend\n\nprint(bitcount(127) .. \" \" .. bitcount(128) .. \" \" .. bitcount(3005))\n", "logic-lua-xor-instead-of-and", 4, 4, 1, "    n = n & (n - 1)" },
        { "bitcount.ml", "let rec count_bits n count =\n  if n = 0 then count else count_bits (n lxor (n - 1)) (count + 1)\n\nlet () = Printf.printf \"%d %d %d\\n\" (count_bits 127 0) (count_bits 128 0) (count_bits 3005 0)\n", "logic-ocaml-xor-instead-of-and", 2, 2, 1, "  if n = 0 then count else count_bits (n land (n - 1)) (count + 1)" },
        { "bitcount.php", "<?php\nfunction bitcount(int $n): int {\n    $count = 0;\n    while ($n != 0) {\n        $n ^= $n - 1;\n        $count++;\n    }\n    return $count;\n}\n\necho bitcount(127), \" \", bitcount(128), \" \", bitcount(3005), \"\\n\";\n", "logic-php-xor-instead-of-and", 5, 5, 1, "        $n &= $n - 1;" },
        { "bitcount.pl", "use strict;\nuse warnings;\n\nsub bitcount {\n    my ($n) = @_;\n    my $count = 0;\n    while ($n) {\n        $n ^= $n - 1;\n        $count++;\n    }\n    return $count;\n}\n\nprint join(\" \", bitcount(127), bitcount(128), bitcount(3005)), \"\\n\";\n", "logic-perl-xor-instead-of-and", 8, 8, 1, "        $n &= $n - 1;" },
        { "bitcount.ps1", "function Get-BitCount([int]$n) {\n    $count = 0\n    while ($n -ne 0) {\n        $n = $n -bxor ($n - 1)\n        $count++\n    }\n    return $count\n}\n\n\"$(Get-BitCount 127) $(Get-BitCount 128) $(Get-BitCount 3005)\"\n", "logic-powershell-xor-instead-of-and", 4, 4, 1, "        $n = $n -band ($n - 1)" },
        { "bitcount.py", "def bitcount(n):\n    count = 0\n    while n:\n        n ^= n - 1\n        count += 1\n    return count\n\n\nprint(bitcount(127), bitcount(128), bitcount(3005))\n", "logic-python-xor-instead-of-and", 4, 4, 1, "        n &= n - 1" },
        { "bitcount.rb", "def bitcount(n)\n  count = 0\n  until n.zero?\n    n ^= n - 1\n    count += 1\n  end\n  count\nend\n\nputs \"#{bitcount(127)} #{bitcount(128)} #{bitcount(3005)}\"\n", "logic-ruby-xor-instead-of-and", 4, 4, 1, "    n &= n - 1" },
        { "bitcount.rs", "fn bitcount(mut n: u32) -> u64 {\n    let mut count = 0;\n    while n != 0 {\n        n ^= n - 1;\n        count += 1;\n    }\n    count\n}\n\nfn main() {\n    println!(\"{} {} {}\", bitcount(127), bitcount(128), bitcount(3005));\n}\n", "logic-rust-xor-instead-of-and", 4, 4, 1, "        n &= n - 1;" },
        { "Bitcount.scala", "object Bitcount {\n  def bitcount(start: Int): Int = {\n    var n = start\n    var count = 0\n    while (n != 0) {\n      n ^= n - 1\n      count += 1\n    }\n    count\n  }\n\n  def main(args: Array[String]): Unit = println(s\"${bitcount(127)} ${bitcount(128)} ${bitcount(3005)}\")\n}\n", "logic-scala-xor-instead-of-and", 6, 6, 1, "      n &= n - 1" },
        { "bitcount.ts", "function bitcount(n: number): number {\n  let count: number = 0;\n  while (n) {\n    n ^= n - 1;\n    count += 1;\n  }\n  return count;\n}\n\nconsole.log(bitcount(127), bitcount(128), bitcount(3005));\n", "logic-js-xor-instead-of-and", 4, 4, 1, "    n &= n - 1;" },
        { "Program.cs", "using System;\n\npublic static class Program\n{\n    static int CountBits(int number)\n    {\n        var count = 0;\n        while (number > 0)\n        {\n            number ^= number - 1;\n            count++;\n        }\n\n        return count;\n    }\n\n    public static void Main()\n    {\n        Console.WriteLine($\"{CountBits(127)} {CountBits(128)} {CountBits(3005)}\");\n    }\n}\n", "logic-csharp-xor-instead-of-and", 10, 10, 1, "            number &= number - 1;" },
    };

    [Theory]
    [MemberData(nameof(Mistakes))]
    public void TheLowestBitTakenAwayWithXorIsFound(string fileName, string code, string check, int line, int changeAt, int removed, string added)
    {
        var found = Assert.Single(LogicPatterns.All.Where(pattern => pattern.Id == check).SelectMany(pattern => pattern.Find(Program(fileName, code))));

        Assert.Equal(line, found.Line);
        Assert.Equal(changeAt, found.Fix!.StartLine);
        Assert.Equal(removed, found.Fix.RemoveCount);
        Assert.Equal(added.Split('\n'), found.Fix.NewLines);
    }

    /// <summary>
    /// The counts using and; n ^ (n - 1) kept as a mask of its own, n left as it is; the lowest set bit cleared by xor with
    /// that bit itself, n ^= n &amp; -n; and n ^ (n - 1) taken three times, the loop counting the times.
    /// </summary>
    [Theory]
    [InlineData("bitcount.c", "#include <stdio.h>\n\nstatic int bitcount(unsigned int n) {\n    int count = 0;\n    while (n) {\n        n &= n - 1;\n        count++;\n    }\n    return count;\n}\n\nint main(void) {\n    printf(\"%d %d %d\\n\", bitcount(127), bitcount(128), bitcount(3005));\n    return 0;\n}\n")]
    [InlineData("bitcount.cpp", "#include <iostream>\n\nint bitcount(unsigned int n) {\n    int count = 0;\n    for (; n != 0; count++) {\n        n &= n - 1;\n    }\n    return count;\n}\n\nint main() {\n    std::cout << bitcount(127) << \" \" << bitcount(128) << \" \" << bitcount(3005) << std::endl;\n    return 0;\n}\n")]
    [InlineData("bitcount.dart", "int bitcount(int n) {\n  var count = 0;\n  while (n != 0) {\n    n &= n - 1;\n    count++;\n  }\n  return count;\n}\n\nvoid main() {\n  print('${bitcount(127)} ${bitcount(128)} ${bitcount(3005)}');\n}\n")]
    [InlineData("bitcount.erl", "-module(bitcount).\n-export([main/0]).\n\ncount(0, Count) -> Count;\ncount(N, Count) -> count(N band (N - 1), Count + 1).\n\nmain() ->\n    io:format(\"~p ~p ~p~n\", [count(127, 0), count(128, 0), count(3005, 0)]).\n")]
    [InlineData("bitcount.exs", "defmodule Bits do\n  import Bitwise\n\n  def count(n), do: count(n, 0)\n\n  defp count(0, count), do: count\n  defp count(n, count), do: count(band(n, n - 1), count + 1)\nend\n\nIO.puts(\"#{Bits.count(127)} #{Bits.count(128)} #{Bits.count(3005)}\")\n")]
    [InlineData("bitcount.f90", "module bits_module\n  implicit none\ncontains\n  integer function bitcount(start)\n    integer, intent(in) :: start\n    integer :: n\n    n = start\n    bitcount = 0\n    do while (n /= 0)\n      n = iand(n, n - 1)\n      bitcount = bitcount + 1\n    end do\n  end function bitcount\nend module bits_module\n\nprogram bitcount_program\n  use bits_module\n  implicit none\n  print '(I0, 1X, I0, 1X, I0)', bitcount(127), bitcount(128), bitcount(3005)\nend program bitcount_program\n")]
    [InlineData("bitcount.fsx", "let rec countBits n count =\n    if n = 0 then count else countBits (n &&& (n - 1)) (count + 1)\n\nprintfn \"%d %d %d\" (countBits 127 0) (countBits 128 0) (countBits 3005 0)\n")]
    [InlineData("bitcount.go", "package main\n\nimport \"fmt\"\n\nfunc bitcount(n uint) int {\n\tcount := 0\n\tfor n != 0 {\n\t\tn &= n - 1\n\t\tcount++\n\t}\n\treturn count\n}\n\nfunc main() {\n\tfmt.Println(bitcount(127), bitcount(128), bitcount(3005))\n}\n")]
    [InlineData("Bitcount.java", "public class Bitcount {\n    public static int bitcount(int n) {\n        int count = 0;\n        while (n != 0) {\n            n = (n & (n - 1));\n            count++;\n        }\n        return count;\n    }\n\n    public static void main(String[] args) {\n        System.out.println(bitcount(127) + \" \" + bitcount(128) + \" \" + bitcount(3005));\n    }\n}\n")]
    [InlineData("bitcount.js", "function bitcount(n) {\n  let count = 0;\n  while (n !== 0) {\n    n = n & (n - 1);\n    count++;\n  }\n  return count;\n}\n\nconsole.log(bitcount(127), bitcount(128), bitcount(3005));\n")]
    [InlineData("bitcount.lua", "local function bitcount(n)\n  local count = 0\n  while n ~= 0 do\n    n = n & (n - 1)\n    count = count + 1\n  end\n  return count\nend\n\nprint(bitcount(127) .. \" \" .. bitcount(128) .. \" \" .. bitcount(3005))\n")]
    [InlineData("bitcount.ml", "let rec count_bits n count =\n  if n = 0 then count else count_bits (n land (n - 1)) (count + 1)\n\nlet () = Printf.printf \"%d %d %d\\n\" (count_bits 127 0) (count_bits 128 0) (count_bits 3005 0)\n")]
    [InlineData("bitcount.php", "<?php\nfunction bitcount(int $n): int {\n    $count = 0;\n    while ($n != 0) {\n        $n &= $n - 1;\n        $count++;\n    }\n    return $count;\n}\n\necho bitcount(127), \" \", bitcount(128), \" \", bitcount(3005), \"\\n\";\n")]
    [InlineData("bitcount.pl", "use strict;\nuse warnings;\n\nsub bitcount {\n    my ($n) = @_;\n    my $count = 0;\n    while ($n) {\n        $n &= $n - 1;\n        $count++;\n    }\n    return $count;\n}\n\nprint join(\" \", bitcount(127), bitcount(128), bitcount(3005)), \"\\n\";\n")]
    [InlineData("bitcount.ps1", "function Get-BitCount([int]$n) {\n    $count = 0\n    while ($n -ne 0) {\n        $n = $n -band ($n - 1)\n        $count++\n    }\n    return $count\n}\n\n\"$(Get-BitCount 127) $(Get-BitCount 128) $(Get-BitCount 3005)\"\n")]
    [InlineData("bitcount.py", "def bitcount(n):\n    count = 0\n    while n:\n        n &= n - 1\n        count += 1\n    return count\n\n\nprint(bitcount(127), bitcount(128), bitcount(3005))\n")]
    [InlineData("bitcount.rb", "def bitcount(n)\n  count = 0\n  until n.zero?\n    n &= n - 1\n    count += 1\n  end\n  count\nend\n\nputs \"#{bitcount(127)} #{bitcount(128)} #{bitcount(3005)}\"\n")]
    [InlineData("bitcount.rs", "fn bitcount(mut n: u32) -> u64 {\n    let mut count = 0;\n    while n != 0 {\n        n &= n - 1;\n        count += 1;\n    }\n    count\n}\n\nfn main() {\n    println!(\"{} {} {}\", bitcount(127), bitcount(128), bitcount(3005));\n}\n")]
    [InlineData("Bitcount.scala", "object Bitcount {\n  def bitcount(start: Int): Int = {\n    var n = start\n    var count = 0\n    while (n != 0) {\n      n &= n - 1\n      count += 1\n    }\n    count\n  }\n\n  def main(args: Array[String]): Unit = println(s\"${bitcount(127)} ${bitcount(128)} ${bitcount(3005)}\")\n}\n")]
    [InlineData("bitcount.ts", "function bitcount(n: number): number {\n  let count: number = 0;\n  while (n) {\n    n &= n - 1;\n    count += 1;\n  }\n  return count;\n}\n\nconsole.log(bitcount(127), bitcount(128), bitcount(3005));\n")]
    [InlineData("Program.cs", "using System;\n\npublic static class Program\n{\n    static int CountBits(int number)\n    {\n        var count = 0;\n        while (number > 0)\n        {\n            number &= number - 1;\n            count++;\n        }\n\n        return count;\n    }\n\n    public static void Main()\n    {\n        Console.WriteLine($\"{CountBits(127)} {CountBits(128)} {CountBits(3005)}\");\n    }\n}\n")]
    [InlineData("cleared_by_lowest.py", "# The lowest set bit cleared by xor with that bit itself, n & -n: prints 7 1 9.\ndef bitcount(n):\n    count = 0\n    while n:\n        n ^= n & -n\n        count += 1\n    return count\n\n\nprint(bitcount(127), bitcount(128), bitcount(3005))\n")]
    [InlineData("lowest_mask.py", "# The lowest set bit and every bit below it, as a mask of its own - n is left as it is: prints 15 8.\nn = 8\nmask = n ^ (n - 1)\nprint(mask, n)\n")]
    [InlineData("steps.js", "// n ^ (n - 1) taken three times, the loop counting the times, not waiting for n to reach 0: prints 1.\nlet n = 6;\nfor (let step = 0; step < 3; step++) {\n  n = n ^ (n - 1);\n}\nconsole.log(n);\n")]
    public void TheLowestBitTakenAwayWithAndIsLeftAlone(string fileName, string code)
    {
        // Each check of the file's own language, as FixFinder runs them.
        var file = Program(fileName, code);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        Assert.Empty(LogicPatterns.All.Where(pattern => pattern.Id.EndsWith("xor-instead-of-and", StringComparison.Ordinal) && pattern.Extensions.Contains(extension))
            .SelectMany(pattern => pattern.Find(file)));
    }
}
