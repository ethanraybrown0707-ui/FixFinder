using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;

namespace FixFinder.Tests;

/// <summary>
/// A count of the ways to make change in each language, taking its list of coins apart with no stop for when none are left,
/// with the change that stops there; the same counts stopping there, and programs near the mistake but not it, which are
/// left alone. Each row is what the check gave for the
/// program when these were written.
/// </summary>
public class NoStopWhenEmptyTests : IDisposable
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
        { "change.c", "#include <stdio.h>\n\nint possible_change(const int *coins, int count, int total) {\n    if (total == 0) return 1;\n    if (total < 0) return 0;\n    return possible_change(coins, count, total - coins[0]) + possible_change(coins + 1, count - 1, total);\n}\n\nint main(void) {\n    int coins[] = {1, 5, 10, 25};\n    printf(\"%d\\n\", possible_change(coins, 4, 11));\n    return 0;\n}\n", "logic-c-no-stop-when-empty", 6, 5, 1, "    if (total < 0 || count == 0) return 0;" },
        { "change.cpp", "#include <iostream>\n#include <vector>\n\nint possibleChange(const std::vector<int>& coins, int total) {\n    if (total == 0) {\n        return 1;\n    }\n    if (total < 0) {\n        return 0;\n    }\n    std::vector<int> rest(coins.begin() + 1, coins.end());\n    return possibleChange(coins, total - coins[0]) + possibleChange(rest, total);\n}\n\nint main() {\n    std::cout << possibleChange({1, 5, 10, 25}, 11) << std::endl;\n    return 0;\n}\n", "logic-cpp-no-stop-when-empty", 12, 8, 1, "    if (total < 0 || coins.empty()) {" },
        { "change.dart", "int possibleChange(List<int> coins, int total) {\n  if (total == 0) {\n    return 1;\n  }\n  if (total < 0) {\n    return 0;\n  }\n  return possibleChange(coins, total - coins[0]) + possibleChange(coins.sublist(1), total);\n}\n\nvoid main() {\n  print(possibleChange([1, 5, 10, 25], 11));\n}\n", "logic-dart-no-stop-when-empty", 8, 5, 1, "  if (total < 0 || coins.isEmpty) {" },
        { "change.f90", "module coins_module\n  implicit none\ncontains\n  recursive function possible_change(coins, total) result(ways)\n    integer, intent(in) :: coins(:), total\n    integer :: ways\n    if (total == 0) then\n      ways = 1\n    else if (total < 0) then\n      ways = 0\n    else\n      ways = possible_change(coins, total - coins(1)) + possible_change(coins(2:), total)\n    end if\n  end function possible_change\nend module coins_module\n\nprogram change\n  use coins_module\n  implicit none\n  print '(I0)', possible_change([1, 5, 10, 25], 11)\nend program change\n", "logic-fortran-no-stop-when-empty", 12, 9, 1, "    else if (total < 0 .or. size(coins) == 0) then" },
        { "change.go", "package main\n\nimport \"fmt\"\n\nfunc possibleChange(coins []int, total int) int {\n\tif total == 0 {\n\t\treturn 1\n\t}\n\tif total < 0 {\n\t\treturn 0\n\t}\n\treturn possibleChange(coins, total-coins[0]) + possibleChange(coins[1:], total)\n}\n\nfunc main() {\n\tfmt.Println(possibleChange([]int{1, 5, 10, 25}, 11))\n}\n", "logic-go-no-stop-when-empty", 12, 9, 1, "\tif total < 0 || len(coins) == 0 {" },
        { "Change.java", "import java.util.Arrays;\n\npublic class Change {\n    static int possibleChange(int[] coins, int total) {\n        if (total == 0) {\n            return 1;\n        }\n        if (total < 0) {\n            return 0;\n        }\n        int first = coins[0];\n        int[] rest = Arrays.copyOfRange(coins, 1, coins.length);\n        return possibleChange(coins, total - first) + possibleChange(rest, total);\n    }\n\n    public static void main(String[] args) {\n        System.out.println(possibleChange(new int[] {1, 5, 10, 25}, 11));\n    }\n}\n", "logic-java-no-stop-when-empty", 11, 8, 1, "        if (total < 0 || coins.length == 0) {" },
        { "change.js", "function possibleChange(coins, total) {\n  if (total === 0) return 1;\n  if (total < 0) return 0;\n  const [first, ...rest] = coins;\n  return possibleChange(coins, total - first) + possibleChange(rest, total);\n}\n\nconsole.log(possibleChange([1, 5, 10, 25], 11));\n", "logic-js-no-stop-when-empty", 4, 3, 1, "  if (total < 0 || coins.length === 0) return 0;" },
        { "change.lua", "local function possible_change(coins, total)\n  if total == 0 then\n    return 1\n  end\n  if total < 0 then\n    return 0\n  end\n  local rest = { table.unpack(coins, 2) }\n  return possible_change(coins, total - coins[1]) + possible_change(rest, total)\nend\n\nprint(possible_change({ 1, 5, 10, 25 }, 11))\n", "logic-lua-no-stop-when-empty", 9, 5, 1, "  if total < 0 or #coins == 0 then" },
        { "change.php", "<?php\nfunction possibleChange(array $coins, int $total): int\n{\n    if ($total === 0) {\n        return 1;\n    }\n    if ($total < 0) {\n        return 0;\n    }\n    return possibleChange($coins, $total - $coins[0]) + possibleChange(array_slice($coins, 1), $total);\n}\n\necho possibleChange([1, 5, 10, 25], 11), \"\\n\";\n", "logic-php-no-stop-when-empty", 10, 7, 1, "    if ($total < 0 || count($coins) === 0) {" },
        { "change.pl", "use strict;\nuse warnings;\n\nsub possible_change {\n    my ($coins, $total) = @_;\n    return 1 if $total == 0;\n    if ($total < 0) {\n        return 0;\n    }\n    my ($first, @rest) = @$coins;\n    return possible_change($coins, $total - $first) + possible_change(\\@rest, $total);\n}\n\nprint possible_change([1, 5, 10, 25], 11), \"\\n\";\n", "logic-perl-no-stop-when-empty", 10, 7, 1, "    if ($total < 0 || !@$coins) {" },
        { "change.ps1", "function Get-PossibleChange($coins, [int]$total) {\n    if ($total -eq 0) {\n        return 1\n    }\n    if ($total -lt 0) {\n        return 0\n    }\n    $rest = @($coins | Select-Object -Skip 1)\n    return (Get-PossibleChange $coins ($total - $coins[0])) + (Get-PossibleChange $rest $total)\n}\n\nGet-PossibleChange @(1, 5, 10, 25) 11\n", "logic-powershell-no-stop-when-empty", 9, 5, 1, "    if ($total -lt 0 -or $coins.Count -eq 0) {" },
        { "change.py", "def possible_change(coins, total):\n    if total == 0:\n        return 1\n    if total < 0:\n        return 0\n\n    first, *rest = coins\n    return possible_change(coins, total - first) + possible_change(rest, total)\n\n\nprint(possible_change([1, 5, 10, 25], 11))\n", "logic-python-no-stop-when-empty", 7, 4, 1, "    if total < 0 or not coins:" },
        { "change.rb", "def possible_change(coins, total)\n  return 1 if total == 0\n  if total < 0\n    return 0\n  end\n\n  first, *rest = coins\n  possible_change(coins, total - first) + possible_change(rest, total)\nend\n\nputs possible_change([1, 5, 10, 25], 11)\n", "logic-ruby-no-stop-when-empty", 7, 3, 1, "  if total < 0 || coins.empty?" },
        { "change.rs", "fn possible_change(coins: &[i32], total: i32) -> i32 {\n    if total == 0 {\n        return 1;\n    }\n    if total < 0 {\n        return 0;\n    }\n    possible_change(coins, total - coins[0]) + possible_change(&coins[1..], total)\n}\n\nfn main() {\n    println!(\"{}\", possible_change(&[1, 5, 10, 25], 11));\n}\n", "logic-rust-no-stop-when-empty", 8, 5, 1, "    if total < 0 || coins.is_empty() {" },
        { "Change.scala", "object Change {\n  def possibleChange(coins: List[Int], total: Int): Int = {\n    if (total == 0) {\n      1\n    } else if (total < 0) {\n      0\n    } else {\n      possibleChange(coins, total - coins.head) + possibleChange(coins.tail, total)\n    }\n  }\n\n  def main(args: Array[String]): Unit = {\n    println(possibleChange(List(1, 5, 10, 25), 11))\n  }\n}\n", "logic-scala-no-stop-when-empty", 8, 5, 1, "    } else if (total < 0 || coins.isEmpty) {" },
        { "change.ts", "function possibleChange(coins: number[], total: number): number {\n  if (total === 0) {\n    return 1;\n  }\n  if (total < 0) {\n    return 0;\n  }\n  return possibleChange(coins, total - coins[0]) + possibleChange(coins.slice(1), total);\n}\n\nconsole.log(possibleChange([1, 5, 10, 25], 11));\n", "logic-js-no-stop-when-empty", 8, 5, 1, "  if (total < 0 || coins.length === 0) {" },
        { "Program.cs", "using System;\nusing System.Collections.Generic;\nusing System.Linq;\n\npublic static class Program\n{\n    static int PossibleChange(List<int> coins, int total)\n    {\n        if (total == 0)\n        {\n            return 1;\n        }\n\n        if (total < 0)\n        {\n            return 0;\n        }\n\n        var first = coins[0];\n        return PossibleChange(coins, total - first) + PossibleChange(coins.Skip(1).ToList(), total);\n    }\n\n    public static void Main()\n    {\n        Console.WriteLine(PossibleChange(new List<int> { 1, 5, 10, 25 }, 11));\n    }\n}\n", "logic-csharp-no-stop-when-empty", 19, 14, 1, "        if (total < 0 || coins.Count == 0)" },
        { "ways.py", "def ways(coins, total):\n    if total == 0:\n        return 1\n    if total < 0:\n        return 0\n    return ways(coins, total - coins[0]) + ways(coins[1:], total)\n\n\nprint(ways([1, 5, 10, 25], 11))\n", "logic-python-no-stop-when-empty", 6, 4, 1, "    if total < 0 or not coins:" },
    };

    [Theory]
    [MemberData(nameof(Mistakes))]
    public void AListTakenApartWithNoStopWhenEmptyIsFound(string fileName, string code, string check, int line, int changeAt, int removed, string added)
    {
        var found = Assert.Single(LogicPatterns.All.Where(pattern => pattern.Id == check).SelectMany(pattern => pattern.Find(Program(fileName, code))));

        Assert.Equal(line, found.Line);
        Assert.Equal(changeAt, found.Fix!.StartLine);
        Assert.Equal(removed, found.Fix.RemoveCount);
        Assert.Equal(added.Split('\n'), found.Fix.NewLines);
    }

    /// <summary>
    /// The counts stopping when no coins are left; and functions taking a list apart that stop - at a count handed over
    /// beside it, at an empty list asked in a ?, at one item left, at an empty list asked first, at C's count reaching 0 - and
    /// a merge asking whether either list is empty.
    /// </summary>
    [Theory]
    [InlineData("change.c", "#include <stdio.h>\n\nint possible_change(const int *coins, int count, int total) {\n    if (total == 0) return 1;\n    if (total < 0 || count == 0) return 0;\n    return possible_change(coins, count, total - coins[0]) + possible_change(coins + 1, count - 1, total);\n}\n\nint main(void) {\n    int coins[] = {1, 5, 10, 25};\n    printf(\"%d\\n\", possible_change(coins, 4, 11));\n    return 0;\n}\n")]
    [InlineData("change.cpp", "#include <iostream>\n#include <vector>\n\nint possibleChange(const std::vector<int>& coins, int total) {\n    if (total == 0) {\n        return 1;\n    }\n    if (total < 0 || coins.empty()) {\n        return 0;\n    }\n    std::vector<int> rest(coins.begin() + 1, coins.end());\n    return possibleChange(coins, total - coins[0]) + possibleChange(rest, total);\n}\n\nint main() {\n    std::cout << possibleChange({1, 5, 10, 25}, 11) << std::endl;\n    return 0;\n}\n")]
    [InlineData("change.dart", "int possibleChange(List<int> coins, int total) {\n  if (total == 0) {\n    return 1;\n  }\n  if (total < 0 || coins.isEmpty) {\n    return 0;\n  }\n  return possibleChange(coins, total - coins[0]) + possibleChange(coins.sublist(1), total);\n}\n\nvoid main() {\n  print(possibleChange([1, 5, 10, 25], 11));\n}\n")]
    [InlineData("change.f90", "module coins_module\n  implicit none\ncontains\n  recursive function possible_change(coins, total) result(ways)\n    integer, intent(in) :: coins(:), total\n    integer :: ways\n    if (total == 0) then\n      ways = 1\n    else if (total < 0 .or. size(coins) == 0) then\n      ways = 0\n    else\n      ways = possible_change(coins, total - coins(1)) + possible_change(coins(2:), total)\n    end if\n  end function possible_change\nend module coins_module\n\nprogram change\n  use coins_module\n  implicit none\n  print '(I0)', possible_change([1, 5, 10, 25], 11)\nend program change\n")]
    [InlineData("change.go", "package main\n\nimport \"fmt\"\n\nfunc possibleChange(coins []int, total int) int {\n\tif total == 0 {\n\t\treturn 1\n\t}\n\tif total < 0 || len(coins) == 0 {\n\t\treturn 0\n\t}\n\treturn possibleChange(coins, total-coins[0]) + possibleChange(coins[1:], total)\n}\n\nfunc main() {\n\tfmt.Println(possibleChange([]int{1, 5, 10, 25}, 11))\n}\n")]
    [InlineData("Change.java", "import java.util.Arrays;\n\npublic class Change {\n    static int possibleChange(int[] coins, int total) {\n        if (total == 0) {\n            return 1;\n        }\n        if (total < 0 || coins.length == 0) {\n            return 0;\n        }\n        int first = coins[0];\n        int[] rest = Arrays.copyOfRange(coins, 1, coins.length);\n        return possibleChange(coins, total - first) + possibleChange(rest, total);\n    }\n\n    public static void main(String[] args) {\n        System.out.println(possibleChange(new int[] {1, 5, 10, 25}, 11));\n    }\n}\n")]
    [InlineData("change.js", "function possibleChange(coins, total) {\n  if (total === 0) return 1;\n  if (total < 0 || coins.length === 0) return 0;\n  const [first, ...rest] = coins;\n  return possibleChange(coins, total - first) + possibleChange(rest, total);\n}\n\nconsole.log(possibleChange([1, 5, 10, 25], 11));\n")]
    [InlineData("change.lua", "local function possible_change(coins, total)\n  if total == 0 then\n    return 1\n  end\n  if total < 0 or #coins == 0 then\n    return 0\n  end\n  local rest = { table.unpack(coins, 2) }\n  return possible_change(coins, total - coins[1]) + possible_change(rest, total)\nend\n\nprint(possible_change({ 1, 5, 10, 25 }, 11))\n")]
    [InlineData("change.php", "<?php\nfunction possibleChange(array $coins, int $total): int\n{\n    if ($total === 0) {\n        return 1;\n    }\n    if ($total < 0 || count($coins) === 0) {\n        return 0;\n    }\n    return possibleChange($coins, $total - $coins[0]) + possibleChange(array_slice($coins, 1), $total);\n}\n\necho possibleChange([1, 5, 10, 25], 11), \"\\n\";\n")]
    [InlineData("change.pl", "use strict;\nuse warnings;\n\nsub possible_change {\n    my ($coins, $total) = @_;\n    return 1 if $total == 0;\n    if ($total < 0 || !@$coins) {\n        return 0;\n    }\n    my ($first, @rest) = @$coins;\n    return possible_change($coins, $total - $first) + possible_change(\\@rest, $total);\n}\n\nprint possible_change([1, 5, 10, 25], 11), \"\\n\";\n")]
    [InlineData("change.ps1", "function Get-PossibleChange($coins, [int]$total) {\n    if ($total -eq 0) {\n        return 1\n    }\n    if ($total -lt 0 -or $coins.Count -eq 0) {\n        return 0\n    }\n    $rest = @($coins | Select-Object -Skip 1)\n    return (Get-PossibleChange $coins ($total - $coins[0])) + (Get-PossibleChange $rest $total)\n}\n\nGet-PossibleChange @(1, 5, 10, 25) 11\n")]
    [InlineData("change.py", "def possible_change(coins, total):\n    if total == 0:\n        return 1\n    if total < 0 or not coins:\n        return 0\n\n    first, *rest = coins\n    return possible_change(coins, total - first) + possible_change(rest, total)\n\n\nprint(possible_change([1, 5, 10, 25], 11))\n")]
    [InlineData("change.rb", "def possible_change(coins, total)\n  return 1 if total == 0\n  if total < 0 || coins.empty?\n    return 0\n  end\n\n  first, *rest = coins\n  possible_change(coins, total - first) + possible_change(rest, total)\nend\n\nputs possible_change([1, 5, 10, 25], 11)\n")]
    [InlineData("change.rs", "fn possible_change(coins: &[i32], total: i32) -> i32 {\n    if total == 0 {\n        return 1;\n    }\n    if total < 0 || coins.is_empty() {\n        return 0;\n    }\n    possible_change(coins, total - coins[0]) + possible_change(&coins[1..], total)\n}\n\nfn main() {\n    println!(\"{}\", possible_change(&[1, 5, 10, 25], 11));\n}\n")]
    [InlineData("Change.scala", "object Change {\n  def possibleChange(coins: List[Int], total: Int): Int = {\n    if (total == 0) {\n      1\n    } else if (total < 0 || coins.isEmpty) {\n      0\n    } else {\n      possibleChange(coins, total - coins.head) + possibleChange(coins.tail, total)\n    }\n  }\n\n  def main(args: Array[String]): Unit = {\n    println(possibleChange(List(1, 5, 10, 25), 11))\n  }\n}\n")]
    [InlineData("change.ts", "function possibleChange(coins: number[], total: number): number {\n  if (total === 0) {\n    return 1;\n  }\n  if (total < 0 || coins.length === 0) {\n    return 0;\n  }\n  return possibleChange(coins, total - coins[0]) + possibleChange(coins.slice(1), total);\n}\n\nconsole.log(possibleChange([1, 5, 10, 25], 11));\n")]
    [InlineData("Program.cs", "using System;\nusing System.Collections.Generic;\nusing System.Linq;\n\npublic static class Program\n{\n    static int PossibleChange(List<int> coins, int total)\n    {\n        if (total == 0)\n        {\n            return 1;\n        }\n\n        if (total < 0 || coins.Count == 0)\n        {\n            return 0;\n        }\n\n        var first = coins[0];\n        return PossibleChange(coins, total - first) + PossibleChange(coins.Skip(1).ToList(), total);\n    }\n\n    public static void Main()\n    {\n        Console.WriteLine(PossibleChange(new List<int> { 1, 5, 10, 25 }, 11));\n    }\n}\n")]
    [InlineData("ways.py", "def ways(coins, total):\n    if total == 0:\n        return 1\n    if total < 0 or not coins:\n        return 0\n    return ways(coins, total - coins[0]) + ways(coins[1:], total)\n\n\nprint(ways([1, 5, 10, 25], 11))\n")]
    [InlineData("first_items.py", "def sum_first(items, n):\n    if n == 0:\n        return 0\n    return items[0] + sum_first(items[1:], n - 1)\n\n\nprint(sum_first([1, 2, 3, 4, 5], 3))\n")]
    [InlineData("largest.rb", "def largest(items)\n  return items[0] if items.length == 1\n  rest = largest(items[1..])\n  items[0] > rest ? items[0] : rest\nend\n\nputs largest([3, 9, 2, 7])\n")]
    [InlineData("merge.py", "def merge(left, right):\n    if not left:\n        return right\n    if not right:\n        return left\n    if left[0] <= right[0]:\n        return [left[0]] + merge(left[1:], right)\n    return [right[0]] + merge(left, right[1:])\n\n\nprint(*merge([1, 3, 5], [2, 4, 6]))\n")]
    [InlineData("total.c", "#include <stdio.h>\n\nint total(const int *items, int count) {\n    if (count == 0) return 0;\n    return items[0] + total(items + 1, count - 1);\n}\n\nint main(void) {\n    int items[] = {1, 2, 3, 4, 5};\n    printf(\"%d\\n\", total(items, 5));\n    return 0;\n}\n")]
    [InlineData("Total.java", "import java.util.List;\n\npublic class Total {\n    static int total(List<Integer> items) {\n        if (items.isEmpty()) {\n            return 0;\n        }\n        return items.get(0) + total(items.subList(1, items.size()));\n    }\n\n    public static void main(String[] args) {\n        System.out.println(total(List.of(1, 2, 3, 4, 5)));\n    }\n}\n")]
    [InlineData("total.js", "function total(items) {\n  return items.length > 0 ? items[0] + total(items.slice(1)) : 0;\n}\n\nconsole.log(total([1, 2, 3, 4, 5]));\n")]
    public void AListTakenApartThatStopsIsLeftAlone(string fileName, string code)
    {
        // Each check of the file's own language, as FixFinder runs them.
        var file = Program(fileName, code);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        Assert.Empty(LogicPatterns.All.Where(pattern => pattern.Id.EndsWith("no-stop-when-empty", StringComparison.Ordinal) && pattern.Extensions.Contains(extension))
            .SelectMany(pattern => pattern.Find(file)));
    }
}
