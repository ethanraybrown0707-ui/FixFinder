using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Edit distances whose match is charged as an edit, in Python, JavaScript and TypeScript, checked with what they should
/// print: each prints 7, and the change the check offers is proved by a copy with it, which prints 3. Each row is what FixFinder gave for the program on this computer when these were written. A
/// language this computer cannot run is passed over, and a copy Windows would not let start is not asked to be proved.
/// </summary>
/// <remarks>Each check runs the program and copies of it, so the rows run alone, after the other tests.</remarks>
[Collection(BuildsRunAlone.Name)]
public class MatchChargedLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string Distance = "3\n";

    /// <summary>The file, the program, the check, the line the change starts on, how many it takes away, and the lines it writes, one a line.</summary>
    public static TheoryData<string, string, string, int, int, string> Programs => new()
    {
        { "distance.py", "def edit_distance(first, second):\n    table = [[0] * (len(second) + 1) for _ in range(len(first) + 1)]\n    for i in range(len(first) + 1):\n        table[i][0] = i\n    for j in range(len(second) + 1):\n        table[0][j] = j\n    for i in range(1, len(first) + 1):\n        for j in range(1, len(second) + 1):\n            if first[i - 1] == second[j - 1]:\n                table[i][j] = table[i - 1][j - 1] + 1\n            else:\n                table[i][j] = 1 + min(table[i - 1][j], table[i][j - 1], table[i - 1][j - 1])\n    return table[len(first)][len(second)]\n\n\nprint(edit_distance(\"kitten\", \"sitting\"))\n", "logic-python-match-charged", 10, 1, "                table[i][j] = table[i - 1][j - 1]" },
        { "levenshtein.py", "def levenshtein(source, target):\n    if source == '' or target == '':\n        return len(source) or len(target)\n\n    elif source[0] == target[0]:\n        return 1 + levenshtein(source[1:], target[1:])\n\n    else:\n        return 1 + min(\n            levenshtein(source, target[1:]),\n            levenshtein(source[1:], target[1:]),\n            levenshtein(source[1:], target)\n        )\n\n\nprint(levenshtein(\"kitten\", \"sitting\"))\n", "logic-python-match-charged", 6, 1, "        return levenshtein(source[1:], target[1:])" },
        { "levenshtein.js", "function levenshtein(source, target) {\n  if (source.length === 0 || target.length === 0) return source.length || target.length;\n  if (source[0] === target[0]) return 1 + levenshtein(source.slice(1), target.slice(1));\n  return 1 + Math.min(levenshtein(source, target.slice(1)), levenshtein(source.slice(1), target.slice(1)), levenshtein(source.slice(1), target));\n}\n\nconsole.log(levenshtein(\"kitten\", \"sitting\"));\n", "logic-js-match-charged", 3, 1, "  if (source[0] === target[0]) return levenshtein(source.slice(1), target.slice(1));" },
        { "distance.ts", "function editDistance(first: string, second: string): number {\n  const table: number[][] = Array.from({ length: first.length + 1 }, (_, i) => Array.from({ length: second.length + 1 }, (_, j) => (i === 0 ? j : j === 0 ? i : 0)));\n  for (let i = 1; i <= first.length; i++) {\n    for (let j = 1; j <= second.length; j++) {\n      if (first[i - 1] === second[j - 1]) {\n        table[i][j] = table[i - 1][j - 1] + 1;\n      } else {\n        table[i][j] = Math.min(table[i - 1][j] + 1, table[i][j - 1] + 1, table[i - 1][j - 1] + 1);\n      }\n    }\n  }\n  return table[first.length][second.length];\n}\n\nconsole.log(editDistance(\"kitten\", \"sitting\"));\n", "logic-js-match-charged", 6, 1, "        table[i][j] = table[i - 1][j - 1];" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheChangeMakesItFindTheEditDistance(string fileName, string code, string check, int changeAt, int removed, string added)
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
            Expected = ExpectedBehaviour.From([new ExpectedRun(null, Distance)], ""),
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
