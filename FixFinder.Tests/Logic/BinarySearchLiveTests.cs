using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Binary searches that fail quickly for an item that is not there - a search started again from mid runs out of stack, a
/// read past the end stops - run with what they should print, -1: the change the check offers is the one that makes the
/// program print it, confirmed by running a copy with it. A language this computer cannot run is passed over.
/// </summary>
public class BinarySearchLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>The file, the program, the line the change is on, and what the line becomes.</summary>
    public static TheoryData<string, string, int, string> Programs => new()
    {
        { "recursing.py", "def find_in_sorted(arr, x):\n    def binsearch(start, end):\n        if start == end:\n            return -1\n        mid = start + (end - start) // 2\n        if x < arr[mid]:\n            return binsearch(start, mid)\n        elif x > arr[mid]:\n            return binsearch(mid, end)\n        else:\n            return mid\n\n    return binsearch(0, len(arr))\n\n\nprint(find_in_sorted([1, 2, 3], 4))\n", 9, "            return binsearch(mid + 1, end)" },
        { "Recursing.java", "public class Recursing {\n    static int binsearch(int[] arr, int x, int start, int end) {\n        if (start == end) {\n            return -1;\n        }\n        int mid = start + (end - start) / 2;\n        if (x < arr[mid]) {\n            return binsearch(arr, x, start, mid);\n        } else if (x > arr[mid]) {\n            return binsearch(arr, x, mid, end);\n        } else {\n            return mid;\n        }\n    }\n\n    public static void main(String[] args) {\n        System.out.println(binsearch(new int[] {1, 2, 3}, 4, 0, 3));\n    }\n}\n", 10, "            return binsearch(arr, x, mid + 1, end);" },
        { "recursing.js", "function binsearch(arr, x, start, end) {\n    if (start === end) {\n        return -1;\n    }\n    const mid = start + Math.floor((end - start) / 2);\n    if (x < arr[mid]) {\n        return binsearch(arr, x, start, mid);\n    } else if (x > arr[mid]) {\n        return binsearch(arr, x, mid, end);\n    }\n    return mid;\n}\n\nconsole.log(binsearch([1, 2, 3], 4, 0, 3));\n", 9, "        return binsearch(arr, x, mid + 1, end);" },
        { "recursing.rb", "def binsearch(arr, x, start, finish)\n  return -1 if start == finish\n  mid = start + (finish - start) / 2\n  if x < arr[mid]\n    binsearch(arr, x, start, mid)\n  elsif x > arr[mid]\n    binsearch(arr, x, mid, finish)\n  else\n    mid\n  end\nend\n\nputs binsearch([1, 2, 3], 4, 0, 3)\n", 7, "    binsearch(arr, x, mid + 1, finish)" },
        { "recursing.dart", "int binsearch(List<int> arr, int x, int start, int end) {\n  if (start == end) {\n    return -1;\n  }\n  final mid = start + (end - start) ~/ 2;\n  if (x < arr[mid]) {\n    return binsearch(arr, x, start, mid);\n  } else if (x > arr[mid]) {\n    return binsearch(arr, x, mid, end);\n  }\n  return mid;\n}\n\nvoid main() {\n  print(binsearch([1, 2, 3], 4, 0, 3));\n}\n", 9, "    return binsearch(arr, x, mid + 1, end);" },
        { "recursing.ps1", "function Search($arr, $x, $start, $end) {\n    if ($start -eq $end) {\n        return -1\n    }\n    $mid = $start + [math]::Floor(($end - $start) / 2)\n    if ($x -lt $arr[$mid]) {\n        return Search $arr $x $start $mid\n    } elseif ($x -gt $arr[$mid]) {\n        return Search $arr $x $mid $end\n    }\n    return $mid\n}\n\nWrite-Output (Search @(1, 2, 3) 4 0 3)\n", 9, "        return Search $arr $x ($mid + 1) $end" },
        { "past.py", "def find_first_in_sorted(arr, x):\n    lo = 0\n    hi = len(arr)\n\n    while lo <= hi:\n        mid = (lo + hi) // 2\n\n        if x == arr[mid] and (mid == 0 or x != arr[mid - 1]):\n            return mid\n\n        elif x <= arr[mid]:\n            hi = mid\n\n        else:\n            lo = mid + 1\n\n    return -1\n\n\nprint(find_first_in_sorted([1, 2, 3], 4))\n", 5, "    while lo < hi:" },
        { "Past.java", "public class Past {\n    static int findFirst(int[] arr, int x) {\n        int lo = 0;\n        int hi = arr.length;\n\n        while (lo <= hi) {\n            int mid = lo + (hi - lo) / 2;\n\n            if (x == arr[mid] && (mid == 0 || x != arr[mid - 1])) {\n                return mid;\n            } else if (x <= arr[mid]) {\n                hi = mid;\n            } else {\n                lo = mid + 1;\n            }\n        }\n\n        return -1;\n    }\n\n    public static void main(String[] args) {\n        System.out.println(findFirst(new int[] {1, 2, 3}, 4));\n    }\n}\n", 6, "        while (lo < hi) {" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheChangeMakesItPrintWhatItShould(string fileName, string code, int line, string changedTo)
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
            Expected = ExpectedBehaviour.From([new ExpectedRun(null, "-1\n")], ""),
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
        Assert.True(changed!.Verified.IsVerified, changed.Verified.Summary);

        Assert.Equal(code, File.ReadAllText(program));
    }
}
