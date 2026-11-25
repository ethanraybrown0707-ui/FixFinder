using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Largest-sub-list-sum passes never starting the run again, in Python, JavaScript and TypeScript, checked with what they
/// should print: each prints 4 2, and the change the check offers is proved by a copy with it, which prints 5 6. Each row is what FixFinder gave for the program on this computer when these were written. A
/// language this computer cannot run is passed over, and a copy Windows would not let start is not asked to be proved.
/// </summary>
/// <remarks>Each check runs the program and copies of it, so the rows run alone, after the other tests.</remarks>
[Collection(BuildsRunAlone.Name)]
public class SumNeverStartedAgainLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string Answer = "5 6\n";

    /// <summary>The file, the program, the check, the line the change starts on, how many it takes away, and the lines it writes, one a line.</summary>
    public static TheoryData<string, string, string, int, int, string> Programs => new()
    {
        { "kadane.py", "def max_sublist_sum(arr):\n    max_ending_here = 0\n    max_so_far = 0\n\n    for x in arr:\n        max_ending_here = max_ending_here + x\n        max_so_far = max(max_so_far, max_ending_here)\n\n    return max_so_far\n\n\nprint(max_sublist_sum([4, -5, 2, 1, -1, 3]), max_sublist_sum([-2, 1, -3, 4, -1, 2, 1, -5, 4]))\n", "logic-python-sum-never-started-again", 6, 1, "        max_ending_here = max(0, max_ending_here + x)" },
        { "kadane.js", "function maxSubarray(items) {\n  let endingHere = 0;\n  let best = 0;\n  for (let i = 0; i < items.length; i++) {\n    endingHere = endingHere + items[i];\n    best = Math.max(best, endingHere);\n  }\n  return best;\n}\n\nconsole.log(maxSubarray([4, -5, 2, 1, -1, 3]), maxSubarray([-2, 1, -3, 4, -1, 2, 1, -5, 4]));\n", "logic-js-sum-never-started-again", 5, 1, "    endingHere = Math.max(0, endingHere + items[i]);" },
        { "kadane.ts", "function maxSubarray(items: number[]): number {\n  let endingHere: number = 0;\n  let best: number = 0;\n  for (const item of items) {\n    endingHere += item;\n    if (endingHere > best) best = endingHere;\n  }\n  return best;\n}\n\nconsole.log(maxSubarray([4, -5, 2, 1, -1, 3]), maxSubarray([-2, 1, -3, 4, -1, 2, 1, -5, 4]));\n", "logic-js-sum-never-started-again", 5, 1, "    endingHere = Math.max(0, endingHere + item);" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheChangeMakesItFindTheLargestRun(string fileName, string code, string check, int changeAt, int removed, string added)
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
            Expected = ExpectedBehaviour.From([new ExpectedRun(null, Answer)], ""),
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
