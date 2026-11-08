using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Merge sorts that stop only when nothing is left, in Python, JavaScript and TypeScript, checked with what they should
/// print: each calls itself with one item, or one place, again until it runs out of room for its calls, and the change the
/// check offers is proved by a copy with it, which prints 1 2 3 4 5. Each row is what FixFinder gave for the program on this
/// computer when these were written. A language this computer cannot run is passed over, and a copy Windows would not let
/// start is not asked to be proved.
/// </summary>
/// <remarks>Each check runs the program and copies of it, so the rows run alone, after the other tests.</remarks>
[Collection(BuildsRunAlone.Name)]
public class HalvingNeverStopsLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string Sorted = "1 2 3 4 5\n";

    /// <summary>The file, the program, the check, the line the change starts on, how many it takes away, and the lines it writes, one a line.</summary>
    public static TheoryData<string, string, string, int, int, string> Programs => new()
    {
        { "mergesort.py", "def merge(left, right):\n    merged = []\n    i = j = 0\n    while i < len(left) and j < len(right):\n        if left[i] <= right[j]:\n            merged.append(left[i])\n            i += 1\n        else:\n            merged.append(right[j])\n            j += 1\n    return merged + left[i:] + right[j:]\n\n\ndef merge_sort(items):\n    if not items:\n        return items\n    middle = len(items) // 2\n    return merge(merge_sort(items[:middle]), merge_sort(items[middle:]))\n\n\nprint(*merge_sort([5, 2, 4, 1, 3]))\n", "logic-python-halving-never-stops", 15, 1, "    if len(items) <= 1:" },
        { "ranges.py", "def merge(left, right):\n    merged = []\n    i = j = 0\n    while i < len(left) and j < len(right):\n        if left[i] <= right[j]:\n            merged.append(left[i])\n            i += 1\n        else:\n            merged.append(right[j])\n            j += 1\n    return merged + left[i:] + right[j:]\n\n\ndef sort_range(items, lo, hi):\n    if lo > hi:\n        return\n    mid = (lo + hi) // 2\n    sort_range(items, lo, mid)\n    sort_range(items, mid + 1, hi)\n    items[lo:hi + 1] = merge(items[lo:mid + 1], items[mid + 1:hi + 1])\n\n\nnumbers = [5, 2, 4, 1, 3]\nsort_range(numbers, 0, len(numbers) - 1)\nprint(*numbers)\n", "logic-python-halving-never-stops", 15, 1, "    if lo >= hi:" },
        { "mergesort.js", "function merge(left, right) {\n  const merged = [];\n  let i = 0;\n  let j = 0;\n  while (i < left.length && j < right.length) {\n    if (left[i] <= right[j]) {\n      merged.push(left[i]);\n      i += 1;\n    } else {\n      merged.push(right[j]);\n      j += 1;\n    }\n  }\n  return merged.concat(left.slice(i), right.slice(j));\n}\n\nfunction mergeSort(items) {\n  if (items.length === 0) {\n    return items;\n  }\n  const mid = Math.floor(items.length / 2);\n  return merge(mergeSort(items.slice(0, mid)), mergeSort(items.slice(mid)));\n}\n\nconsole.log(mergeSort([5, 2, 4, 1, 3]).join(\" \"));\n", "logic-js-halving-never-stops", 18, 1, "  if (items.length <= 1) {" },
        { "mergesort.ts", "function merge(left: number[], right: number[]): number[] {\n  const merged: number[] = [];\n  let i: number = 0;\n  let j: number = 0;\n  while (i < left.length && j < right.length) {\n    if (left[i] <= right[j]) {\n      merged.push(left[i]);\n      i += 1;\n    } else {\n      merged.push(right[j]);\n      j += 1;\n    }\n  }\n  return merged.concat(left.slice(i), right.slice(j));\n}\n\nfunction mergeSort(items: number[]): number[] {\n  if (items.length < 1) return items;\n  const middle: number = Math.floor(items.length / 2);\n  const left: number[] = mergeSort(items.slice(0, middle));\n  const right: number[] = mergeSort(items.slice(middle));\n  return merge(left, right);\n}\n\nconsole.log(mergeSort([5, 2, 4, 1, 3]).join(\" \"));\n", "logic-js-halving-never-stops", 18, 1, "  if (items.length <= 1) return items;" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheChangeMakesItFinishWithTheAnswer(string fileName, string code, string check, int changeAt, int removed, string added)
    {
        var program = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(program, code);

        // Twenty seconds a run: a copy whose change does not mend it can run until its time runs out, and the three minutes the
        // tests that build give would keep a row running minutes on end; the copy with the change finishes at once.
        var launch = TargetFactory.FromFile(program, TimeSpan.FromSeconds(20));
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

        var kept = report.Findings.SingleOrDefault(finding => finding.Fix?.RuleId == check);
        Assert.True(kept is not null, $"no {check} among {string.Join(", ", report.Findings.Select(finding => finding.RuleId))}");
        Assert.Equal(changeAt, kept!.Fix!.StartLine);
        Assert.Equal(removed, kept.Fix.RemoveCount);
        Assert.Equal(added.Split('\n'), kept.Fix.NewLines);
        Assert.True(kept.Verified.IsVerified, kept.Verified.Summary);

        Assert.Equal(code, File.ReadAllText(program));
    }
}
