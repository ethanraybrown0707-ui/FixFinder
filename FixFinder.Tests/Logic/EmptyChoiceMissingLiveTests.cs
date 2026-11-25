using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Choosings whose base case gives back no choices, in Python, JavaScript and TypeScript, checked with what they should
/// print: each prints no choices, and the change the check offers is proved by a copy with it, which prints the 10 choices
/// of 3 from 1 to 5. Each row is what FixFinder gave for the program on this computer when these were written. A
/// language this computer cannot run is passed over, and a copy Windows would not let start is not asked to be proved.
/// </summary>
/// <remarks>Each check runs the program and copies of it, so the rows run alone, after the other tests.</remarks>
[Collection(BuildsRunAlone.Name)]
public class EmptyChoiceMissingLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string Answer = "1 2 3|1 2 4|1 2 5|1 3 4|1 3 5|1 4 5|2 3 4|2 3 5|2 4 5|3 4 5\n";

    /// <summary>The file, the program, the check, the line the change starts on, how many it takes away, and the lines it writes, one a line.</summary>
    public static TheoryData<string, string, string, int, int, string> Programs => new()
    {
        { "subsequences.py", "def subsequences(a, b, k):\n    if k == 0:\n        return []\n\n    ret = []\n    for i in range(a, b + 1 - k):\n        ret.extend(\n            [i] + rest for rest in subsequences(i + 1, b, k - 1)\n        )\n\n    return ret\n\n\nprint(\"|\".join(\" \".join(str(number) for number in choice) for choice in subsequences(1, 6, 3)))\n", "logic-python-empty-choice-missing", 3, 1, "        return [[]]" },
        { "choices.js", "function choices(from, to, count) {\n  if (count === 0) return [];\n  const found = [];\n  for (let first = from; first <= to - count; first++) {\n    for (const rest of choices(first + 1, to, count - 1)) {\n      found.push([first, ...rest]);\n    }\n  }\n  return found;\n}\n\nconsole.log(choices(1, 6, 3).map((choice) => choice.join(\" \")).join(\"|\"));\n", "logic-js-empty-choice-missing", 2, 1, "  if (count === 0) return [[]];" },
        { "choices.ts", "function choices(from: number, to: number, count: number): number[][] {\n  if (count === 0) {\n    return [];\n  }\n  const found: number[][] = [];\n  for (let first = from; first <= to - count; first++) {\n    found.push(...choices(first + 1, to, count - 1).map((rest: number[]) => [first, ...rest]));\n  }\n  return found;\n}\n\nconsole.log(choices(1, 6, 3).map((choice: number[]) => choice.join(\" \")).join(\"|\"));\n", "logic-js-empty-choice-missing", 3, 1, "    return [[]];" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheChangeMakesItFindTheChoices(string fileName, string code, string check, int changeAt, int removed, string added)
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
