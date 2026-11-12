using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Knapsacks leaving out the item that fits exactly, in Python, JavaScript and TypeScript, checked with what they should
/// print: each prints 50, and the change the check offers is proved by a copy with it, which prints 90. Each row is what FixFinder gave for the program on this computer when these were written. A
/// language this computer cannot run is passed over, and a copy Windows would not let start is not asked to be proved.
/// </summary>
/// <remarks>Each check runs the program and copies of it, so the rows run alone, after the other tests.</remarks>
[Collection(BuildsRunAlone.Name)]
public class ExactFitLeftOutLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string BestValue = "90\n";

    /// <summary>The file, the program, the check, the line the change starts on, how many it takes away, and the lines it writes, one a line.</summary>
    public static TheoryData<string, string, string, int, int, string> Programs => new()
    {
        { "knapsack.py", "def knapsack(capacity, items):\n    table = [[0] * (capacity + 1) for _ in range(len(items) + 1)]\n    for i in range(1, len(items) + 1):\n        weight, value = items[i - 1]\n        for j in range(1, capacity + 1):\n            if weight < j:\n                table[i][j] = max(table[i - 1][j], value + table[i - 1][j - weight])\n            else:\n                table[i][j] = table[i - 1][j]\n    return table[len(items)][capacity]\n\n\nprint(knapsack(7, [(5, 10), (4, 40), (6, 30), (3, 50)]))\n", "logic-python-exact-fit-left-out", 6, 1, "            if weight <= j:" },
        { "best.py", "def knapsack(capacity, items):\n    best = [0] * (capacity + 1)\n    for weight, value in items:\n        for j in range(capacity, 0, -1):\n            if weight < j:\n                best[j] = max(best[j], value + best[j - weight])\n    return best[capacity]\n\n\nprint(knapsack(7, [(5, 10), (4, 40), (6, 30), (3, 50)]))\n", "logic-python-exact-fit-left-out", 5, 1, "            if weight <= j:" },
        { "knapsack.js", "function knapsack(capacity, items) {\n  const table = Array.from({ length: items.length + 1 }, () => new Array(capacity + 1).fill(0));\n  for (let i = 1; i <= items.length; i++) {\n    for (let j = 1; j <= capacity; j++) {\n      if (items[i - 1][0] < j) {\n        table[i][j] = Math.max(table[i - 1][j], items[i - 1][1] + table[i - 1][j - items[i - 1][0]]);\n      } else {\n        table[i][j] = table[i - 1][j];\n      }\n    }\n  }\n  return table[items.length][capacity];\n}\n\nconsole.log(knapsack(7, [[5, 10], [4, 40], [6, 30], [3, 50]]));\n", "logic-js-exact-fit-left-out", 5, 1, "      if (items[i - 1][0] <= j) {" },
        { "knapsack.ts", "function knapsack(capacity: number, items: [number, number][]): number {\n  const best: number[] = new Array<number>(capacity + 1).fill(0);\n  for (const [weight, value] of items) {\n    for (let j = capacity; j > 0; j--) {\n      if (weight < j) {\n        best[j] = Math.max(best[j], value + best[j - weight]);\n      }\n    }\n  }\n  return best[capacity];\n}\n\nconsole.log(knapsack(7, [[5, 10], [4, 40], [6, 30], [3, 50]]));\n", "logic-js-exact-fit-left-out", 5, 1, "      if (weight <= j) {" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheChangeMakesItFindTheBestValue(string fileName, string code, string check, int changeAt, int removed, string added)
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
            Expected = ExpectedBehaviour.From([new ExpectedRun(null, BestValue)], ""),
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
