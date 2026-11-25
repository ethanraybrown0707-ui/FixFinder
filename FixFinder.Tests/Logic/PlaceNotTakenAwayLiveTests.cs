using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Quickselects asking the part above for place k as it is, in Python, JavaScript and TypeScript, checked with what they
/// should print: each goes wrong, and the change the check offers is proved by a copy with it, which prints 1 2 4 4 7 8 9. Each row is what FixFinder gave for the program on this computer when these were written. A
/// language this computer cannot run is passed over, and a copy Windows would not let start is not asked to be proved.
/// </summary>
/// <remarks>Each check runs the program and copies of it, so the rows run alone, after the other tests.</remarks>
[Collection(BuildsRunAlone.Name)]
public class PlaceNotTakenAwayLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string Answer = "1 2 4 4 7 8 9\n";

    /// <summary>The file, the program, the check, the line the change starts on, how many it takes away, and the lines it writes, one a line.</summary>
    public static TheoryData<string, string, string, int, int, string> Programs => new()
    {
        { "kth.py", "def kth(arr, k):\n    pivot = arr[0]\n    below = [x for x in arr if x < pivot]\n    above = [x for x in arr if x > pivot]\n\n    num_less = len(below)\n    num_lessoreq = len(arr) - len(above)\n\n    if k < num_less:\n        return kth(below, k)\n    elif k >= num_lessoreq:\n        return kth(above, k)\n    else:\n        return pivot\n\n\nitems = [4, 9, 2, 7, 4, 1, 8]\nprint(*(kth(items, k) for k in range(len(items))))\n", "logic-python-place-not-taken-away", 12, 1, "        return kth(above, k - num_lessoreq)" },
        { "kth.js", "function kth(items, k) {\n  const pivot = items[0];\n  const below = items.filter((x) => x < pivot);\n  const above = items.filter((x) => x > pivot);\n  const lessOrEqual = items.length - above.length;\n  if (k < below.length) return kth(below, k);\n  if (k >= lessOrEqual) return kth(above, k);\n  return pivot;\n}\n\nconst items = [4, 9, 2, 7, 4, 1, 8];\nconsole.log(items.map((_, k) => kth(items, k)).join(\" \"));\n", "logic-js-place-not-taken-away", 7, 1, "  if (k >= lessOrEqual) return kth(above, k - lessOrEqual);" },
        { "kth.ts", "function kth(items: number[], k: number): number {\n  const pivot = items[0];\n  const below = items.filter((x) => x < pivot);\n  const above = items.filter((x) => x > pivot);\n  const lessOrEqual = items.length - above.length;\n  if (k < below.length) {\n    return kth(below, k);\n  }\n  if (k >= lessOrEqual) {\n    return kth(above, k);\n  }\n  return pivot;\n}\n\nconst items: number[] = [4, 9, 2, 7, 4, 1, 8];\nconsole.log(items.map((_, k) => kth(items, k)).join(\" \"));\n", "logic-js-place-not-taken-away", 10, 1, "    return kth(above, k - lessOrEqual);" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheChangeMakesItFindEachPlace(string fileName, string code, string check, int changeAt, int removed, string added)
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
