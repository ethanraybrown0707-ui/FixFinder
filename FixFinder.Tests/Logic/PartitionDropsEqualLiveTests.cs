using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Quicksorts whose parts leave out what equals the pivot, in Python, JavaScript and TypeScript, checked with what they should
/// print: each prints its list with the repeated values gone, and the change the check offers is proved by a copy with it,
/// which prints 1 1 2 3 3. Each row is what FixFinder gave for the program on this computer when these were written. A
/// language this computer cannot run is passed over, and a copy Windows would not let start is not asked to be proved.
/// </summary>
/// <remarks>Each check runs the program and copies of it, so the rows run alone, after the other tests.</remarks>
[Collection(BuildsRunAlone.Name)]
public class PartitionDropsEqualLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string Sorted = "1 1 2 3 3\n";

    /// <summary>The file, the program, the check, the line the change starts on, how many it takes away, and the lines it writes, one a line.</summary>
    public static TheoryData<string, string, string, int, int, string> Programs => new()
    {
        { "quicksort.py", "def quicksort(items):\n    if not items:\n        return []\n    pivot = items[0]\n    lesser = quicksort([x for x in items[1:] if x < pivot])\n    greater = quicksort([x for x in items[1:] if x > pivot])\n    return lesser + [pivot] + greater\n\n\nprint(*quicksort([3, 1, 2, 3, 1]))\n", "logic-python-partition-drops-equal", 6, 1, "    greater = quicksort([x for x in items[1:] if x >= pivot])" },
        { "partition.py", "def quicksort(items):\n    if len(items) <= 1:\n        return items\n    pivot, *rest = items\n    smaller = []\n    larger = []\n    for item in rest:\n        if item < pivot:\n            smaller.append(item)\n        elif item > pivot:\n            larger.append(item)\n    return quicksort(smaller) + [pivot] + quicksort(larger)\n\n\nprint(*quicksort([3, 1, 2, 3, 1]))\n", "logic-python-partition-drops-equal", 10, 1, "        elif item >= pivot:" },
        { "quicksort.js", "function quickSort(items) {\n  if (items.length <= 1) return items;\n  const [pivot, ...rest] = items;\n  const lower = rest.filter((x) => x < pivot);\n  const higher = rest.filter((x) => x > pivot);\n  return [...quickSort(lower), pivot, ...quickSort(higher)];\n}\n\nconsole.log(quickSort([3, 1, 2, 3, 1]).join(\" \"));\n", "logic-js-partition-drops-equal", 5, 1, "  const higher = rest.filter((x) => x >= pivot);" },
        { "quicksort.ts", "function quickSort(items: number[]): number[] {\n  if (items.length <= 1) return items;\n  const pivot: number = items[0];\n  const rest: number[] = items.slice(1);\n  const lower: number[] = rest.filter((x: number) => x < pivot);\n  const higher: number[] = rest.filter((x: number) => x > pivot);\n  return [...quickSort(lower), pivot, ...quickSort(higher)];\n}\n\nconsole.log(quickSort([3, 1, 2, 3, 1]).join(\" \"));\n", "logic-js-partition-drops-equal", 6, 1, "  const higher: number[] = rest.filter((x: number) => x >= pivot);" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheChangeMakesItPrintEveryValue(string fileName, string code, string check, int changeAt, int removed, string added)
    {
        var program = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(program, code);

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
