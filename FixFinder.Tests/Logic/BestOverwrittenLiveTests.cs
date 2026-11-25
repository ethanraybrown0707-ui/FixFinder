using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Longest increasing subsequences writing over the longest found so far, in Python, JavaScript and TypeScript, checked with
/// what they should print: each prints 2, and the change the check offers is proved by a copy with it, which prints 3. Each row is what FixFinder gave for the program on this computer when these were written. A
/// language this computer cannot run is passed over, and a copy Windows would not let start is not asked to be proved.
/// </summary>
/// <remarks>Each check runs the program and copies of it, so the rows run alone, after the other tests.</remarks>
[Collection(BuildsRunAlone.Name)]
public class BestOverwrittenLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string Longest = "3\n";

    /// <summary>The file, the program, the check, the line the change starts on, how many it takes away, and the lines it writes, one a line.</summary>
    public static TheoryData<string, string, string, int, int, string> Programs => new()
    {
        { "lis.py", "def lis(arr):\n    ends = {}\n    longest = 0\n\n    for i, val in enumerate(arr):\n\n        prefix_lengths = [j for j in range(1, longest + 1) if arr[ends[j]] < val]\n\n        length = max(prefix_lengths) if prefix_lengths else 0\n\n        if length == longest or val < arr[ends[length + 1]]:\n            ends[length + 1] = i\n            longest = length + 1\n\n    return longest\n\n\nprint(lis([4, 1, 5, 3, 7, 6, 2]))\n", "logic-python-best-overwritten", 13, 1, "            longest = max(longest, length + 1)" },
        { "ends.py", "def longest_increasing(items):\n    ends = [0] * (len(items) + 1)\n    longest = 0\n    for i in range(len(items)):\n        length = 0\n        for j in range(1, longest + 1):\n            if items[ends[j]] < items[i]:\n                length = j\n        if length == longest or items[i] < items[ends[length + 1]]:\n            ends[length + 1] = i\n            longest = length + 1\n    return longest\n\n\nprint(longest_increasing([4, 1, 5, 3, 7, 6, 2]))\n", "logic-python-best-overwritten", 11, 1, "            longest = max(longest, length + 1)" },
        { "lis.js", "function lis(items) {\n  const ends = new Array(items.length + 1).fill(0);\n  let longest = 0;\n  for (let i = 0; i < items.length; i++) {\n    let length = 0;\n    for (let j = 1; j <= longest; j++) {\n      if (items[ends[j]] < items[i]) length = j;\n    }\n    if (length === longest || items[i] < items[ends[length + 1]]) {\n      ends[length + 1] = i;\n      longest = length + 1;\n    }\n  }\n  return longest;\n}\n\nconsole.log(lis([4, 1, 5, 3, 7, 6, 2]));\n", "logic-js-best-overwritten", 11, 1, "      longest = Math.max(longest, length + 1);" },
        { "lis.ts", "function lis(items: number[]): number {\n  const ends: number[] = new Array<number>(items.length + 1).fill(0);\n  let longest = 0;\n  for (let i = 0; i < items.length; i++) {\n    let length = 0;\n    for (let j = 1; j <= longest; j++) {\n      if (items[ends[j]] < items[i]) {\n        length = j;\n      }\n    }\n    if (length === longest || items[i] < items[ends[length + 1]]) {\n      ends[length + 1] = i;\n      longest = length + 1;\n    }\n  }\n  return longest;\n}\n\nconsole.log(lis([4, 1, 5, 3, 7, 6, 2]));\n", "logic-js-best-overwritten", 13, 1, "      longest = Math.max(longest, length + 1);" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheChangeMakesItKeepTheLongest(string fileName, string code, string check, int changeAt, int removed, string added)
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
            Expected = ExpectedBehaviour.From([new ExpectedRun(null, Longest)], ""),
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
