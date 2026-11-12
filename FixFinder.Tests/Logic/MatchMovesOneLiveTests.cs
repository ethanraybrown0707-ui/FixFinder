using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Longest common subsequences whose match moves on in one sequence only, in Python, JavaScript and TypeScript, checked with
/// what they should print: each prints a wrong length, and the change the check offers is proved by a copy with it, which
/// prints 3. Each row is what FixFinder gave for the program on this computer when these were written. A
/// language this computer cannot run is passed over, and a copy Windows would not let start is not asked to be proved.
/// </summary>
/// <remarks>Each check runs the program and copies of it, so the rows run alone, after the other tests.</remarks>
[Collection(BuildsRunAlone.Name)]
public class MatchMovesOneLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string Length = "3\n";

    /// <summary>The file, the program, the check, the line the change starts on, how many it takes away, and the lines it writes, one a line.</summary>
    public static TheoryData<string, string, string, int, int, string> Programs => new()
    {
        { "lcs.py", "def lcs_length(first, second):\n    table = [[0] * (len(second) + 1) for _ in range(len(first) + 1)]\n    for i in range(1, len(first) + 1):\n        for j in range(1, len(second) + 1):\n            if first[i - 1] == second[j - 1]:\n                table[i][j] = table[i - 1][j] + 1\n            else:\n                table[i][j] = max(table[i - 1][j], table[i][j - 1])\n    return table[len(first)][len(second)]\n\n\nprint(lcs_length(\"AGGTAB\", \"GTTCCAA\"))\n", "logic-python-match-moves-one", 6, 1, "                table[i][j] = table[i - 1][j - 1] + 1" },
        { "common.py", "def longest_common(first, second):\n    if not first or not second:\n        return \"\"\n    if first[0] == second[0]:\n        return first[0] + longest_common(first[1:], second)\n    left = longest_common(first, second[1:])\n    right = longest_common(first[1:], second)\n    return left if len(left) >= len(right) else right\n\n\nprint(len(longest_common(\"AGGTAB\", \"GTTCCAA\")))\n", "logic-python-match-moves-one", 5, 1, "        return first[0] + longest_common(first[1:], second[1:])" },
        { "common.js", "function longestCommon(first, second) {\n  if (first.length === 0 || second.length === 0) return \"\";\n  if (first[0] === second[0]) return first[0] + longestCommon(first, second.slice(1));\n  const left = longestCommon(first, second.slice(1));\n  const right = longestCommon(first.slice(1), second);\n  return left.length >= right.length ? left : right;\n}\n\nconsole.log(longestCommon(\"AGGTAB\", \"GTTCCAA\").length);\n", "logic-js-match-moves-one", 3, 1, "  if (first[0] === second[0]) return first[0] + longestCommon(first.slice(1), second.slice(1));" },
        { "lcs.ts", "function lcsLength(first: string, second: string): number {\n  const table: number[][] = Array.from({ length: first.length + 1 }, () => new Array<number>(second.length + 1).fill(0));\n  for (let i = 1; i <= first.length; i++) {\n    for (let j = 1; j <= second.length; j++) {\n      if (first[i - 1] === second[j - 1]) {\n        table[i][j] = table[i][j - 1] + 1;\n      } else {\n        table[i][j] = Math.max(table[i - 1][j], table[i][j - 1]);\n      }\n    }\n  }\n  return table[first.length][second.length];\n}\n\nconsole.log(lcsLength(\"AGGTAB\", \"GTTCCAA\"));\n", "logic-js-match-moves-one", 6, 1, "        table[i][j] = table[i - 1][j - 1] + 1;" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheChangeMakesItFindTheLongestCommonLength(string fileName, string code, string check, int changeAt, int removed, string added)
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
            Expected = ExpectedBehaviour.From([new ExpectedRun(null, Length)], ""),
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
