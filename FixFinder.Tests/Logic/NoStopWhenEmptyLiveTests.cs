using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Counts of the ways to make change with no stop for when no coins are left, in Python, JavaScript and TypeScript, checked
/// with what they should print: each stops with an error, and the change the check offers is proved by a copy with it,
/// which prints 4. Each row is what FixFinder gave for the program on this computer when these were written. A
/// language this computer cannot run is passed over, and a copy Windows would not let start is not asked to be proved.
/// </summary>
/// <remarks>Each check runs the program and copies of it, so the rows run alone, after the other tests.</remarks>
[Collection(BuildsRunAlone.Name)]
public class NoStopWhenEmptyLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string Ways = "4\n";

    /// <summary>The file, the program, the check, the line the change starts on, how many it takes away, and the lines it writes, one a line.</summary>
    public static TheoryData<string, string, string, int, int, string> Programs => new()
    {
        { "change.py", "def possible_change(coins, total):\n    if total == 0:\n        return 1\n    if total < 0:\n        return 0\n\n    first, *rest = coins\n    return possible_change(coins, total - first) + possible_change(rest, total)\n\n\nprint(possible_change([1, 5, 10, 25], 11))\n", "logic-python-no-stop-when-empty", 4, 1, "    if total < 0 or not coins:" },
        { "ways.py", "def ways(coins, total):\n    if total == 0:\n        return 1\n    if total < 0:\n        return 0\n    return ways(coins, total - coins[0]) + ways(coins[1:], total)\n\n\nprint(ways([1, 5, 10, 25], 11))\n", "logic-python-no-stop-when-empty", 4, 1, "    if total < 0 or not coins:" },
        { "change.js", "function possibleChange(coins, total) {\n  if (total === 0) return 1;\n  if (total < 0) return 0;\n  const [first, ...rest] = coins;\n  return possibleChange(coins, total - first) + possibleChange(rest, total);\n}\n\nconsole.log(possibleChange([1, 5, 10, 25], 11));\n", "logic-js-no-stop-when-empty", 3, 1, "  if (total < 0 || coins.length === 0) return 0;" },
        { "change.ts", "function possibleChange(coins: number[], total: number): number {\n  if (total === 0) {\n    return 1;\n  }\n  if (total < 0) {\n    return 0;\n  }\n  return possibleChange(coins, total - coins[0]) + possibleChange(coins.slice(1), total);\n}\n\nconsole.log(possibleChange([1, 5, 10, 25], 11));\n", "logic-js-no-stop-when-empty", 5, 1, "  if (total < 0 || coins.length === 0) {" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheChangeMakesItCountTheWays(string fileName, string code, string check, int changeAt, int removed, string added)
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
            Expected = ExpectedBehaviour.From([new ExpectedRun(null, Ways)], ""),
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
