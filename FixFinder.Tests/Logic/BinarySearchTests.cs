using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;

namespace FixFinder.Tests;

/// <summary>
/// Binary searches that can stop moving or read past their list - lo = mid, hi = mid under lo &lt;= hi, a search started again
/// from mid, hi started at the length under lo &lt;= hi - and a middle that can overflow, in each language, with the change for
/// each; and searches written correctly in each of the ways they can be, which are left alone. Each row is what the check gave
/// for the program when these were written.
/// </summary>
public class BinarySearchTests : IDisposable
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

    /// <summary>The file, the program, and each finding: "line:check:what the line becomes", split by |.</summary>
    public static TheoryData<string, string, string> Searches => new()
    {
        { "closed.py", "def find(arr, x):\n    lo = 0\n    hi = len(arr)\n    while lo <= hi:\n        mid = (lo + hi) // 2\n        if arr[mid] == x:\n            return mid\n        if arr[mid] < x:\n            lo = mid + 1\n        else:\n            hi = mid - 1\n    return -1\n", "3:logic-python-binary-search:    hi = len(arr) - 1" },
        { "high.py", "def find(arr, x):\n    lo = 0\n    hi = len(arr) - 1\n    while lo <= hi:\n        mid = (lo + hi) // 2\n        if arr[mid] == x:\n            return mid\n        if arr[mid] < x:\n            lo = mid + 1\n        else:\n            hi = mid\n    return -1\n", "11:logic-python-binary-search:            hi = mid - 1" },
        { "low.c", "int find(const int arr[], int count, int x) {\n    int lo = 0;\n    int hi = count - 1;\n    while (lo < hi) {\n        int mid = lo + (hi - lo) / 2;\n        if (arr[mid] < x) {\n            lo = mid;\n        } else {\n            hi = mid;\n        }\n    }\n    return lo;\n}\n", "7:logic-binary-search:            lo = mid + 1;" },
        { "low.cs", "static int Find(int[] arr, int x)\n{\n    int lo = 0;\n    int hi = arr.Length - 1;\n    while (lo < hi)\n    {\n        int mid = lo + (hi - lo) / 2;\n        if (arr[mid] < x)\n        {\n            lo = mid;\n        }\n        else\n        {\n            hi = mid;\n        }\n    }\n    return lo;\n}\n", "10:logic-binary-search:            lo = mid + 1;" },
        { "low.dart", "int find(List<int> arr, int x) {\n  var lo = 0;\n  var hi = arr.length - 1;\n  while (lo < hi) {\n    final mid = (lo + hi) ~/ 2;\n    if (arr[mid] < x) {\n      lo = mid;\n    } else {\n      hi = mid;\n    }\n  }\n  return lo;\n}\n", "7:logic-dart-binary-search:      lo = mid + 1;" },
        { "low.fsx", "let find (arr: int[]) x =\n    let mutable lo = 0\n    let mutable hi = arr.Length - 1\n    while lo < hi do\n        let mid = (lo + hi) / 2\n        if arr.[mid] < x then\n            lo <- mid\n        else\n            hi <- mid\n    lo\n", "7:logic-fsharp-binary-search:            lo <- mid + 1" },
        { "low.go", "package main\n\nfunc find(arr []int, x int) int {\n\tlo := 0\n\thi := len(arr) - 1\n\tfor lo < hi {\n\t\tmid := (lo + hi) / 2\n\t\tif arr[mid] < x {\n\t\t\tlo = mid\n\t\t} else {\n\t\t\thi = mid\n\t\t}\n\t}\n\treturn lo\n}\n", "9:logic-go-binary-search:\t\t\tlo = mid + 1" },
        { "Low.java", "class Low {\n    static int find(int[] arr, int x) {\n        int lo = 0;\n        int hi = arr.length - 1;\n        while (lo < hi) {\n            int mid = lo + (hi - lo) / 2;\n            if (arr[mid] < x) {\n                lo = mid;\n            } else {\n                hi = mid;\n            }\n        }\n        return lo;\n    }\n}\n", "8:logic-java-binary-search:                lo = mid + 1;" },
        { "low.js", "function find(arr, x) {\n    let lo = 0;\n    let hi = arr.length - 1;\n    while (lo < hi) {\n        const mid = Math.floor((lo + hi) / 2);\n        if (arr[mid] < x) {\n            lo = mid;\n        } else {\n            hi = mid;\n        }\n    }\n    return lo;\n}\n", "7:logic-binary-search:            lo = mid + 1;" },
        { "low.lua", "local function find(arr, x)\n  local lo = 1\n  local hi = #arr\n  while lo < hi do\n    local mid = (lo + hi) // 2\n    if arr[mid] < x then\n      lo = mid\n    else\n      hi = mid\n    end\n  end\n  return lo\nend\n", "7:logic-lua-binary-search:      lo = mid + 1" },
        { "low.php", "<?php\nfunction find($arr, $x) {\n    $lo = 0;\n    $hi = count($arr) - 1;\n    while ($lo < $hi) {\n        $mid = intdiv($lo + $hi, 2);\n        if ($arr[$mid] < $x) {\n            $lo = $mid;\n        } else {\n            $hi = $mid;\n        }\n    }\n    return $lo;\n}\n", "8:logic-php-binary-search:            $lo = $mid + 1;" },
        { "low.pl", "sub find {\n    my ($arr, $x) = @_;\n    my $lo = 0;\n    my $hi = $#$arr;\n    while ($lo < $hi) {\n        my $mid = int(($lo + $hi) / 2);\n        if ($arr->[$mid] < $x) {\n            $lo = $mid;\n        } else {\n            $hi = $mid;\n        }\n    }\n    return $lo;\n}\n", "8:logic-perl-binary-search:            $lo = $mid + 1;" },
        { "low.ps1", "function Find($arr, $x) {\n    $lo = 0\n    $hi = $arr.Count - 1\n    while ($lo -lt $hi) {\n        $mid = [math]::Floor(($lo + $hi) / 2)\n        if ($arr[$mid] -lt $x) {\n            $lo = $mid\n        } else {\n            $hi = $mid\n        }\n    }\n    return $lo\n}\n", "7:logic-powershell-binary-search:            $lo = $mid + 1" },
        { "low.py", "def find(arr, x):\n    lo = 0\n    hi = len(arr) - 1\n    while lo < hi:\n        mid = (lo + hi) // 2\n        if arr[mid] < x:\n            lo = mid\n        else:\n            hi = mid\n    return lo\n", "7:logic-python-binary-search:            lo = mid + 1" },
        { "low.rb", "def find(arr, x)\n  lo = 0\n  hi = arr.length - 1\n  while lo < hi\n    mid = (lo + hi) / 2\n    if arr[mid] < x\n      lo = mid\n    else\n      hi = mid\n    end\n  end\n  lo\nend\n", "7:logic-ruby-binary-search:      lo = mid + 1" },
        { "low.rs", "fn find(arr: &[i32], x: i32) -> usize {\n    let mut lo = 0;\n    let mut hi = arr.len() - 1;\n    while lo < hi {\n        let mid = (lo + hi) / 2;\n        if arr[mid] < x {\n            lo = mid;\n        } else {\n            hi = mid;\n        }\n    }\n    lo\n}\n", "7:logic-rust-binary-search:            lo = mid + 1;" },
        { "low.scala", "object Low {\n  def find(arr: Array[Int], x: Int): Int = {\n    var lo = 0\n    var hi = arr.length - 1\n    while (lo < hi) {\n      val mid = (lo + hi) / 2\n      if (arr(mid) < x) {\n        lo = mid\n      } else {\n        hi = mid\n      }\n    }\n    lo\n  }\n}\n", "8:logic-scala-binary-search:        lo = mid + 1" },
        { "Overflow.java", "class Overflow {\n    static int find(int[] arr, int x) {\n        int lo = 0;\n        int hi = arr.length - 1;\n        while (lo <= hi) {\n            int mid = (lo + hi) / 2;\n            if (arr[mid] == x) {\n                return mid;\n            } else if (arr[mid] < x) {\n                lo = mid + 1;\n            } else {\n                hi = mid - 1;\n            }\n        }\n        return -1;\n    }\n}\n", "6:logic-java-binary-search:            int mid = lo + (hi - lo) / 2;" },
        { "Past.java", "public class Past {\n    static int findFirst(int[] arr, int x) {\n        int lo = 0;\n        int hi = arr.length;\n\n        while (lo <= hi) {\n            int mid = lo + (hi - lo) / 2;\n\n            if (x == arr[mid] && (mid == 0 || x != arr[mid - 1])) {\n                return mid;\n            } else if (x <= arr[mid]) {\n                hi = mid;\n            } else {\n                lo = mid + 1;\n            }\n        }\n\n        return -1;\n    }\n\n    public static void main(String[] args) {\n        System.out.println(findFirst(new int[] {1, 2, 3}, 4));\n    }\n}\n", "6:logic-java-binary-search:        while (lo < hi) {" },
        { "past.py", "def find_first_in_sorted(arr, x):\n    lo = 0\n    hi = len(arr)\n\n    while lo <= hi:\n        mid = (lo + hi) // 2\n\n        if x == arr[mid] and (mid == 0 or x != arr[mid - 1]):\n            return mid\n\n        elif x <= arr[mid]:\n            hi = mid\n\n        else:\n            lo = mid + 1\n\n    return -1\n\n\nprint(find_first_in_sorted([1, 2, 3], 4))\n", "5:logic-python-binary-search:    while lo < hi:" },
        { "recursing.dart", "int binsearch(List<int> arr, int x, int start, int end) {\n  if (start == end) {\n    return -1;\n  }\n  final mid = start + (end - start) ~/ 2;\n  if (x < arr[mid]) {\n    return binsearch(arr, x, start, mid);\n  } else if (x > arr[mid]) {\n    return binsearch(arr, x, mid, end);\n  }\n  return mid;\n}\n\nvoid main() {\n  print(binsearch([1, 2, 3], 4, 0, 3));\n}\n", "9:logic-dart-binary-search:    return binsearch(arr, x, mid + 1, end);" },
        { "recursing.erl", "-module(sorted).\n-export([search/4]).\n\nsearch(_, _, Lo, Lo) -> -1;\nsearch(Arr, X, Lo, Hi) ->\n    Mid = (Lo + Hi) div 2,\n    At = lists:nth(Mid + 1, Arr),\n    if\n        X < At -> search(Arr, X, Lo, Mid);\n        X > At -> search(Arr, X, Mid, Hi);\n        true -> Mid\n    end.\n", "10:logic-erlang-binary-search:        X > At -> search(Arr, X, Mid + 1, Hi);" },
        { "recursing.exs", "defmodule Sorted do\n  def search(arr, x, lo, hi) do\n    if lo == hi do\n      -1\n    else\n      mid = div(lo + hi, 2)\n      at = Enum.at(arr, mid)\n      cond do\n        x < at -> search(arr, x, lo, mid)\n        x > at -> search(arr, x, mid, hi)\n        true -> mid\n      end\n    end\n  end\nend\n", "10:logic-elixir-binary-search:        x > at -> search(arr, x, mid + 1, hi)" },
        { "Recursing.java", "public class Recursing {\n    static int binsearch(int[] arr, int x, int start, int end) {\n        if (start == end) {\n            return -1;\n        }\n        int mid = start + (end - start) / 2;\n        if (x < arr[mid]) {\n            return binsearch(arr, x, start, mid);\n        } else if (x > arr[mid]) {\n            return binsearch(arr, x, mid, end);\n        } else {\n            return mid;\n        }\n    }\n\n    public static void main(String[] args) {\n        System.out.println(binsearch(new int[] {1, 2, 3}, 4, 0, 3));\n    }\n}\n", "10:logic-java-binary-search:            return binsearch(arr, x, mid + 1, end);" },
        { "recursing.js", "function binsearch(arr, x, start, end) {\n    if (start === end) {\n        return -1;\n    }\n    const mid = start + Math.floor((end - start) / 2);\n    if (x < arr[mid]) {\n        return binsearch(arr, x, start, mid);\n    } else if (x > arr[mid]) {\n        return binsearch(arr, x, mid, end);\n    }\n    return mid;\n}\n\nconsole.log(binsearch([1, 2, 3], 4, 0, 3));\n", "9:logic-binary-search:        return binsearch(arr, x, mid + 1, end);" },
        { "recursing.ps1", "function Search($arr, $x, $start, $end) {\n    if ($start -eq $end) {\n        return -1\n    }\n    $mid = $start + [math]::Floor(($end - $start) / 2)\n    if ($x -lt $arr[$mid]) {\n        return Search $arr $x $start $mid\n    } elseif ($x -gt $arr[$mid]) {\n        return Search $arr $x $mid $end\n    }\n    return $mid\n}\n\nWrite-Output (Search @(1, 2, 3) 4 0 3)\n", "9:logic-powershell-binary-search:        return Search $arr $x ($mid + 1) $end" },
        { "recursing.py", "def find_in_sorted(arr, x):\n    def binsearch(start, end):\n        if start == end:\n            return -1\n        mid = start + (end - start) // 2\n        if x < arr[mid]:\n            return binsearch(start, mid)\n        elif x > arr[mid]:\n            return binsearch(mid, end)\n        else:\n            return mid\n\n    return binsearch(0, len(arr))\n\n\nprint(find_in_sorted([1, 2, 3], 4))\n", "9:logic-python-binary-search:            return binsearch(mid + 1, end)" },
        { "recursing.rb", "def binsearch(arr, x, start, finish)\n  return -1 if start == finish\n  mid = start + (finish - start) / 2\n  if x < arr[mid]\n    binsearch(arr, x, start, mid)\n  elsif x > arr[mid]\n    binsearch(arr, x, mid, finish)\n  else\n    mid\n  end\nend\n\nputs binsearch([1, 2, 3], 4, 0, 3)\n", "7:logic-ruby-binary-search:    binsearch(arr, x, mid + 1, finish)" },
    };

    [Theory]
    [MemberData(nameof(Searches))]
    public void EachMistakeIsFoundWithItsChange(string fileName, string code, string expected)
    {
        var found = LogicPatterns.Scan(Program(fileName, code))
            .Where(finding => finding.PatternId.EndsWith("binary-search", StringComparison.Ordinal))
            .OrderBy(finding => finding.Line)
            .Select(finding => $"{finding.Line}:{finding.PatternId}:{string.Join("|", finding.Fix!.NewLines)}");

        Assert.Equal(expected.Split('|'), found);
    }

    [Theory]
    [InlineData("apart_right.py", "def find(arr, x):\n    lo = 0\n    hi = len(arr)\n    while hi - lo > 1:\n        mid = (lo + hi) // 2\n        if arr[mid] <= x:\n            lo = mid\n        else:\n            hi = mid\n    return lo\n")]
    [InlineData("closed_right.py", "def find(arr, x):\n    lo = 0\n    hi = len(arr) - 1\n    while lo <= hi:\n        mid = (lo + hi) // 2\n        if arr[mid] == x:\n            return mid\n        if arr[mid] < x:\n            lo = mid + 1\n        else:\n            hi = mid - 1\n    return -1\n")]
    [InlineData("half_open_right.py", "def find_first(arr, x):\n    lo = 0\n    hi = len(arr)\n    while lo < hi:\n        mid = (lo + hi) // 2\n        if arr[mid] < x:\n            lo = mid + 1\n        else:\n            hi = mid\n    return lo\n")]
    [InlineData("recursing_right.py", "def find_in_sorted(arr, x):\n    def binsearch(start, end):\n        if start == end:\n            return -1\n        mid = start + (end - start) // 2\n        if x < arr[mid]:\n            return binsearch(start, mid)\n        elif x > arr[mid]:\n            return binsearch(mid + 1, end)\n        else:\n            return mid\n\n    return binsearch(0, len(arr))\n")]
    [InlineData("Right.java", "class Right {\n    static int find(int[] arr, int x) {\n        int lo = 0;\n        int hi = arr.length - 1;\n        while (lo <= hi) {\n            int mid = lo + (hi - lo) / 2;\n            if (arr[mid] == x) {\n                return mid;\n            } else if (arr[mid] < x) {\n                lo = mid + 1;\n            } else {\n                hi = mid - 1;\n            }\n        }\n        return -1;\n    }\n}\n")]
    [InlineData("rounded_up_right.py", "def last_at_most(arr, x):\n    lo = 0\n    hi = len(arr) - 1\n    while lo < hi:\n        mid = (lo + hi + 1) // 2\n        if arr[mid] <= x:\n            lo = mid\n        else:\n            hi = mid - 1\n    return lo\n")]
    public void ASearchWrittenCorrectlyIsLeftAlone(string fileName, string code)
    {
        Assert.DoesNotContain(LogicPatterns.Scan(Program(fileName, code)), finding => finding.PatternId.EndsWith("binary-search", StringComparison.Ordinal));
    }

    /// <summary>
    /// Sorts over lo up to hi that call themselves on both halves every time - in Python, JavaScript, Rust and Java - and a
    /// search that stops once one place is left, if hi - lo &lt;= 1: none is a search started again from the middle, for ever.
    /// Each ran here and printed its answer; the check once said each never ended.
    /// </summary>
    [Theory]
    [InlineData("halves.py", "def merge(left, right):\n    merged = []\n    i = j = 0\n    while i < len(left) and j < len(right):\n        if left[i] <= right[j]:\n            merged.append(left[i])\n            i += 1\n        else:\n            merged.append(right[j])\n            j += 1\n    return merged + left[i:] + right[j:]\n\n\ndef sort_part(items, lo, hi):\n    if hi - lo > 1:\n        mid = lo + (hi - lo) // 2\n        sort_part(items, lo, mid)\n        sort_part(items, mid, hi)\n        items[lo:hi] = merge(items[lo:mid], items[mid:hi])\n\n\nnumbers = [5, 2, 4, 1, 3]\nsort_part(numbers, 0, len(numbers))\nprint(*numbers)\n")]
    [InlineData("ranges.js", "function merge(left, right) {\n  const merged = [];\n  let i = 0;\n  let j = 0;\n  while (i < left.length && j < right.length) {\n    if (left[i] <= right[j]) {\n      merged.push(left[i]);\n      i += 1;\n    } else {\n      merged.push(right[j]);\n      j += 1;\n    }\n  }\n  return merged.concat(left.slice(i), right.slice(j));\n}\n\nfunction sortPart(items, lo, hi) {\n  if (hi - lo <= 1) return;\n  const mid = Math.floor((lo + hi) / 2);\n  sortPart(items, lo, mid);\n  sortPart(items, mid, hi);\n  const merged = merge(items.slice(lo, mid), items.slice(mid, hi));\n  items.splice(lo, merged.length, ...merged);\n}\n\nconst numbers = [5, 2, 4, 1, 3];\nsortPart(numbers, 0, numbers.length);\nconsole.log(numbers.join(\" \"));\n")]
    [InlineData("ranges.rs", "fn merge(items: &mut Vec<i32>, lo: usize, mid: usize, hi: usize) {\n    let left = items[lo..mid].to_vec();\n    let right = items[mid..hi].to_vec();\n    let (mut i, mut j, mut k) = (0, 0, lo);\n    while i < left.len() && j < right.len() {\n        if left[i] <= right[j] {\n            items[k] = left[i];\n            i += 1;\n        } else {\n            items[k] = right[j];\n            j += 1;\n        }\n        k += 1;\n    }\n    for &number in left[i..].iter().chain(right[j..].iter()) {\n        items[k] = number;\n        k += 1;\n    }\n}\n\nfn sort_part(items: &mut Vec<i32>, lo: usize, hi: usize) {\n    if hi - lo <= 1 { return; }\n    let mid = (lo + hi) / 2;\n    sort_part(items, lo, mid);\n    sort_part(items, mid, hi);\n    merge(items, lo, mid, hi);\n}\n\nfn main() {\n    let mut numbers = vec![5, 2, 4, 1, 3];\n    let count = numbers.len();\n    sort_part(&mut numbers, 0, count);\n    let words: Vec<String> = numbers.iter().map(|number| number.to_string()).collect();\n    println!(\"{}\", words.join(\" \"));\n}\n")]
    [InlineData("last_at_most.py", "def last_at_most(items, target, lo, hi):\n    if hi - lo <= 1:\n        return lo if items[lo] <= target else -1\n    mid = (lo + hi) // 2\n    if items[mid] <= target:\n        return last_at_most(items, target, mid, hi)\n    return last_at_most(items, target, lo, mid)\n\n\nnumbers = [1, 3, 5, 7, 9]\nprint(last_at_most(numbers, 6, 0, len(numbers)))\n")]
    [InlineData("HalfOpen.java", "public class HalfOpen {\n    static void mergeSort(int[] items, int lo, int hi) {\n        if (hi - lo < 2) {\n            return;\n        }\n        int mid = (lo + hi) / 2;\n        mergeSort(items, lo, mid);\n        mergeSort(items, mid, hi);\n        java.util.Arrays.sort(items, lo, hi);\n    }\n\n    public static void main(String[] args) {\n        int[] numbers = {5, 2, 4, 1, 3};\n        mergeSort(numbers, 0, numbers.length);\n        StringBuilder line = new StringBuilder();\n        for (int number : numbers) {\n            if (line.length() > 0) line.append(' ');\n            line.append(number);\n        }\n        System.out.println(line);\n    }\n}\n")]
    public void ASortOfBothHalvesOrASearchThatStopsAtOnePlaceIsNotSaidToSearchAgainFromTheMiddle(string fileName, string code)
    {
        Assert.DoesNotContain(LogicPatterns.Scan(Program(fileName, code)),
            finding => finding.PatternId.EndsWith("binary-search", StringComparison.Ordinal) && finding.Fix?.Title.StartsWith("Search again from", StringComparison.Ordinal) == true);
    }
}
