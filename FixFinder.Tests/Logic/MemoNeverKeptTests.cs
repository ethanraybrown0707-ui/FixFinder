using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;

namespace FixFinder.Tests;

/// <summary>
/// fib(60) in each language with a memo that is asked for an answer and never given one, with the change that puts the answer
/// in where fib works it out; and the same programs keeping it, which are left alone. Each row is what the check gave for the
/// program when these were written.
/// </summary>
public class MemoNeverKeptTests : IDisposable
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

    /// <summary>The file, the program, the check, the line it is said of, the line the change starts on, how many it takes away, and the lines it writes.</summary>
    public static TheoryData<string, string, string, int, int, int, string> Memos => new()
    {
        { "fib.cpp", "#include <iostream>\n#include <map>\n\nstd::map<int, long long> memo;\n\nlong long fib(int n) {\n    if (memo.count(n)) {\n        return memo[n];\n    }\n    if (n <= 1) {\n        return n;\n    }\n    return fib(n - 1) + fib(n - 2);\n}\n\nint main() {\n    std::cout << fib(60) << \"\\n\";\n    return 0;\n}\n", "logic-c-memo-never-kept", 7, 13, 1, "    memo[n] = fib(n - 1) + fib(n - 2);|    return memo[n];" },
        { "Fib.cs", "var memo = new Dictionary<int, long>();\nConsole.WriteLine(Fib(60));\n\nlong Fib(int n)\n{\n    if (memo.ContainsKey(n))\n    {\n        return memo[n];\n    }\n    if (n <= 1)\n    {\n        return n;\n    }\n    return Fib(n - 1) + Fib(n - 2);\n}\n", "logic-csharp-memo-never-kept", 6, 14, 1, "    memo[n] = Fib(n - 1) + Fib(n - 2);|    return memo[n];" },
        { "fib.dart", "final memo = <int, int>{};\n\nint fib(int n) {\n  if (memo.containsKey(n)) {\n    return memo[n]!;\n  }\n  if (n <= 1) {\n    return n;\n  }\n  return fib(n - 1) + fib(n - 2);\n}\n\nvoid main() {\n  print(fib(60));\n}\n", "logic-dart-memo-never-kept", 4, 10, 1, "  memo[n] = fib(n - 1) + fib(n - 2);|  return memo[n]!;" },
        { "main.go", "package main\n\nimport \"fmt\"\n\nvar memo = map[int]int{}\n\nfunc fib(n int) int {\n\tif value, ok := memo[n]; ok {\n\t\treturn value\n\t}\n\tif n <= 1 {\n\t\treturn n\n\t}\n\treturn fib(n-1) + fib(n-2)\n}\n\nfunc main() {\n\tfmt.Println(fib(60))\n}\n", "logic-go-memo-never-kept", 8, 14, 1, "\tmemo[n] = fib(n-1) + fib(n-2)|\treturn memo[n]" },
        { "Fib.java", "import java.util.HashMap;\nimport java.util.Map;\n\npublic class Fib {\n    static Map<Integer, Long> memo = new HashMap<>();\n\n    static long fib(int n) {\n        if (memo.containsKey(n)) {\n            return memo.get(n);\n        }\n        if (n <= 1) {\n            return n;\n        }\n        return fib(n - 1) + fib(n - 2);\n    }\n\n    public static void main(String[] args) {\n        System.out.println(fib(60));\n    }\n}\n", "logic-java-memo-never-kept", 8, 14, 1, "        memo.put(n, fib(n - 1) + fib(n - 2));|        return memo.get(n);" },
        { "fib.js", "const memo = new Map();\n\nfunction fib(n) {\n    if (memo.has(n)) {\n        return memo.get(n);\n    }\n    if (n <= 1) {\n        return n;\n    }\n    return fib(n - 1) + fib(n - 2);\n}\n\nconsole.log(fib(60));\n", "logic-js-memo-never-kept", 4, 10, 1, "    memo.set(n, fib(n - 1) + fib(n - 2));|    return memo.get(n);" },
        { "fib.lua", "local memo = {}\n\nlocal function fib(n)\n  if memo[n] then\n    return memo[n]\n  end\n  if n <= 1 then\n    return n\n  end\n  return fib(n - 1) + fib(n - 2)\nend\n\nprint(fib(60))\n", "logic-lua-memo-never-kept", 4, 10, 1, "  memo[n] = fib(n - 1) + fib(n - 2)|  return memo[n]" },
        { "fib.pl", "use strict;\nuse warnings;\n\nmy %memo;\n\nsub fib {\n    my ($n) = @_;\n    return $memo{$n} if exists $memo{$n};\n    return $n if $n <= 1;\n    return fib($n - 1) + fib($n - 2);\n}\n\nprint fib(60), \"\\n\";\n", "logic-perl-memo-never-kept", 8, 10, 1, "    $memo{$n} = fib($n - 1) + fib($n - 2);|    return $memo{$n};" },
        { "fib.ps1", "$memo = @{}\n\nfunction Get-Fib($n) {\n    if ($memo.ContainsKey($n)) {\n        return $memo[$n]\n    }\n    if ($n -le 1) {\n        return $n\n    }\n    return (Get-Fib ($n - 1)) + (Get-Fib ($n - 2))\n}\n\nWrite-Output (Get-Fib 60)\n", "logic-powershell-memo-never-kept", 4, 10, 1, "    $memo[$n] = (Get-Fib ($n - 1)) + (Get-Fib ($n - 2))|    return $memo[$n]" },
        { "fib.py", "memo = {}\n\n\ndef fib(n):\n    if n in memo:\n        return memo[n]\n    if n <= 1:\n        return n\n    return fib(n - 1) + fib(n - 2)\n\n\nprint(fib(60))\n", "logic-python-memo-never-kept", 5, 9, 1, "    memo[n] = fib(n - 1) + fib(n - 2)|    return memo[n]" },
        { "fib.rb", "def fib(n, memo)\n  return memo[n] if memo.key?(n)\n  return n if n <= 1\n  fib(n - 1, memo) + fib(n - 2, memo)\nend\n\nputs fib(60, {})\n", "logic-ruby-memo-never-kept", 2, 4, 1, "  memo[n] = fib(n - 1, memo) + fib(n - 2, memo)" },
        { "Fib.scala", "import scala.collection.mutable\n\nobject Fib {\n  val memo = mutable.Map[Int, Long]()\n\n  def fib(n: Int): Long = {\n    if (memo.contains(n)) {\n      return memo(n)\n    }\n    if (n <= 1) {\n      return n\n    }\n    fib(n - 1) + fib(n - 2)\n  }\n\n  def main(args: Array[String]): Unit = {\n    println(fib(60))\n  }\n}\n", "logic-scala-memo-never-kept", 7, 13, 1, "    memo(n) = fib(n - 1) + fib(n - 2)|    memo(n)" },
        { "fib.ts", "const memo = new Map<number, number>();\n\nfunction fib(n: number): number {\n    if (memo.has(n)) {\n        return memo.get(n)!;\n    }\n    if (n <= 1) {\n        return n;\n    }\n    return fib(n - 1) + fib(n - 2);\n}\n\nconsole.log(fib(60));\n", "logic-js-memo-never-kept", 4, 10, 1, "    memo.set(n, fib(n - 1) + fib(n - 2));|    return memo.get(n)!;" },
    };

    [Theory]
    [MemberData(nameof(Memos))]
    public void AMemoNeverGivenAnAnswerIsFound(string fileName, string code, string check, int line, int changeAt, int removed, string added)
    {
        var found = Assert.Single(LogicPatterns.Scan(Program(fileName, code)), finding => finding.PatternId == check);

        Assert.Equal(line, found.Line);
        Assert.Equal(changeAt, found.Fix!.StartLine);
        Assert.Equal(removed, found.Fix.RemoveCount);
        Assert.Equal(added.Split('|'), found.Fix.NewLines);
    }

    [Theory]
    [InlineData("fib.cpp", "#include <iostream>\n#include <map>\n\nstd::map<int, long long> memo;\n\nlong long fib(int n) {\n    if (memo.count(n)) {\n        return memo[n];\n    }\n    if (n <= 1) {\n        return n;\n    }\n    memo[n] = fib(n - 1) + fib(n - 2);\n    return memo[n];\n}\n\nint main() {\n    std::cout << fib(60) << \"\\n\";\n    return 0;\n}\n")]
    [InlineData("Fib.cs", "var memo = new Dictionary<int, long>();\nConsole.WriteLine(Fib(60));\n\nlong Fib(int n)\n{\n    if (memo.ContainsKey(n))\n    {\n        return memo[n];\n    }\n    if (n <= 1)\n    {\n        return n;\n    }\n    memo[n] = Fib(n - 1) + Fib(n - 2);\n    return memo[n];\n}\n")]
    [InlineData("fib.dart", "final memo = <int, int>{};\n\nint fib(int n) {\n  if (memo.containsKey(n)) {\n    return memo[n]!;\n  }\n  if (n <= 1) {\n    return n;\n  }\n  memo[n] = fib(n - 1) + fib(n - 2);\n  return memo[n]!;\n}\n\nvoid main() {\n  print(fib(60));\n}\n")]
    [InlineData("main.go", "package main\n\nimport \"fmt\"\n\nvar memo = map[int]int{}\n\nfunc fib(n int) int {\n\tif value, ok := memo[n]; ok {\n\t\treturn value\n\t}\n\tif n <= 1 {\n\t\treturn n\n\t}\n\tmemo[n] = fib(n-1) + fib(n-2)\n\treturn memo[n]\n}\n\nfunc main() {\n\tfmt.Println(fib(60))\n}\n")]
    [InlineData("Fib.java", "import java.util.HashMap;\nimport java.util.Map;\n\npublic class Fib {\n    static Map<Integer, Long> memo = new HashMap<>();\n\n    static long fib(int n) {\n        if (memo.containsKey(n)) {\n            return memo.get(n);\n        }\n        if (n <= 1) {\n            return n;\n        }\n        memo.put(n, fib(n - 1) + fib(n - 2));\n        return memo.get(n);\n    }\n\n    public static void main(String[] args) {\n        System.out.println(fib(60));\n    }\n}\n")]
    [InlineData("fib.js", "const memo = new Map();\n\nfunction fib(n) {\n    if (memo.has(n)) {\n        return memo.get(n);\n    }\n    if (n <= 1) {\n        return n;\n    }\n    memo.set(n, fib(n - 1) + fib(n - 2));\n    return memo.get(n);\n}\n\nconsole.log(fib(60));\n")]
    [InlineData("fib.lua", "local memo = {}\n\nlocal function fib(n)\n  if memo[n] then\n    return memo[n]\n  end\n  if n <= 1 then\n    return n\n  end\n  memo[n] = fib(n - 1) + fib(n - 2)\n  return memo[n]\nend\n\nprint(fib(60))\n")]
    [InlineData("fib.pl", "use strict;\nuse warnings;\n\nmy %memo;\n\nsub fib {\n    my ($n) = @_;\n    return $memo{$n} if exists $memo{$n};\n    return $n if $n <= 1;\n    $memo{$n} = fib($n - 1) + fib($n - 2);\n    return $memo{$n};\n}\n\nprint fib(60), \"\\n\";\n")]
    [InlineData("fib.ps1", "$memo = @{}\n\nfunction Get-Fib($n) {\n    if ($memo.ContainsKey($n)) {\n        return $memo[$n]\n    }\n    if ($n -le 1) {\n        return $n\n    }\n    $memo[$n] = (Get-Fib ($n - 1)) + (Get-Fib ($n - 2))\n    return $memo[$n]\n}\n\nWrite-Output (Get-Fib 60)\n")]
    [InlineData("fib.py", "memo = {}\n\n\ndef fib(n):\n    if n in memo:\n        return memo[n]\n    if n <= 1:\n        return n\n    memo[n] = fib(n - 1) + fib(n - 2)\n    return memo[n]\n\n\nprint(fib(60))\n")]
    [InlineData("fib.rb", "def fib(n, memo)\n  return memo[n] if memo.key?(n)\n  return n if n <= 1\n  memo[n] = fib(n - 1, memo) + fib(n - 2, memo)\nend\n\nputs fib(60, {})\n")]
    [InlineData("Fib.scala", "import scala.collection.mutable\n\nobject Fib {\n  val memo = mutable.Map[Int, Long]()\n\n  def fib(n: Int): Long = {\n    if (memo.contains(n)) {\n      return memo(n)\n    }\n    if (n <= 1) {\n      return n\n    }\n    memo(n) = fib(n - 1) + fib(n - 2)\n    memo(n)\n  }\n\n  def main(args: Array[String]): Unit = {\n    println(fib(60))\n  }\n}\n")]
    [InlineData("fib.ts", "const memo = new Map<number, number>();\n\nfunction fib(n: number): number {\n    if (memo.has(n)) {\n        return memo.get(n)!;\n    }\n    if (n <= 1) {\n        return n;\n    }\n    memo.set(n, fib(n - 1) + fib(n - 2));\n    return memo.get(n)!;\n}\n\nconsole.log(fib(60));\n")]
    public void AMemoThatKeepsItsAnswersIsLeftAlone(string fileName, string code)
    {
        Assert.DoesNotContain(LogicPatterns.Scan(Program(fileName, code)), finding => finding.PatternId.EndsWith("memo-never-kept", StringComparison.Ordinal));
    }

    /// <summary>
    /// Lua's if visited[node] then and Perl's exists $seen{$node} ask a set of places visited the way a memo is asked, and
    /// were taken for memos: the asking is no answer read back, so neither is.
    /// </summary>
    [Theory]
    [InlineData("reach.lua", "local graph = { [0] = { 1 }, [1] = { 2 }, [2] = { 0, 3 }, [3] = {} }\nlocal visited = {}\n\nlocal function reach(node)\n  if visited[node] then\n    return 0\n  end\n  local count = 1\n  for _, next_node in ipairs(graph[node]) do\n    count = count + reach(next_node)\n  end\n  return count\nend\n\nprint(reach(0))\n")]
    [InlineData("reach.pl", "use strict;\nuse warnings;\n\nmy %graph = (0 => [1], 1 => [2], 2 => [0, 3], 3 => []);\nmy %seen;\n\nsub reach {\n    my ($node) = @_;\n    return 0 if exists $seen{$node};\n    my $count = 1;\n    $count += reach($_) for @{ $graph{$node} };\n    return $count;\n}\n\nprint reach(0), \"\\n\";\n")]
    public void ASetOfPlacesVisitedIsNotTakenForAMemo(string fileName, string code)
    {
        Assert.DoesNotContain(LogicPatterns.Scan(Program(fileName, code)), finding => finding.PatternId.EndsWith("memo-never-kept", StringComparison.Ordinal));
    }
}
