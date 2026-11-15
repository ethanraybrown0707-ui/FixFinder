using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Powersets giving back only the subsets with the first item put in front, in Python, JavaScript and TypeScript, checked
/// with what they should print: each prints [1 2 3] alone, and the change the check offers is proved by a copy with it,
/// which prints all 8 subsets of 1 2 3. Each row is what FixFinder gave for the program on this computer when these were written. A
/// language this computer cannot run is passed over, and a copy Windows would not let start is not asked to be proved.
/// </summary>
/// <remarks>Each check runs the program and copies of it, so the rows run alone, after the other tests.</remarks>
[Collection(BuildsRunAlone.Name)]
public class SubsetsWithoutFirstDroppedLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string Answer = "[] [3] [2] [2 3] [1] [1 3] [1 2] [1 2 3]\n";

    /// <summary>The file, the program, the check, the line the change starts on, how many it takes away, and the lines it writes, one a line.</summary>
    public static TheoryData<string, string, string, int, int, string> Programs => new()
    {
        { "powerset.py", "def powerset(items):\n    if not items:\n        return [[]]\n    first, *rest = items\n    rest_subsets = powerset(rest)\n    return [[first] + subset for subset in rest_subsets]\n\n\nprint(\" \".join(\"[\" + \" \".join(str(item) for item in subset) + \"]\" for subset in powerset([1, 2, 3])))\n", "logic-python-subsets-without-first-dropped", 6, 1, "    return rest_subsets + [[first] + subset for subset in rest_subsets]" },
        { "powerset.js", "function powerset(items) {\n    if (items.length === 0) {\n        return [[]];\n    }\n    const [first, ...rest] = items;\n    const restSubsets = powerset(rest);\n    return restSubsets.map((subset) => [first, ...subset]);\n}\n\nconsole.log(powerset([1, 2, 3]).map((subset) => \"[\" + subset.join(\" \") + \"]\").join(\" \"));\n", "logic-js-subsets-without-first-dropped", 7, 1, "    return [...restSubsets, ...restSubsets.map((subset) => [first, ...subset])];" },
        { "powerset.ts", "function powerset(items: number[]): number[][] {\n    if (items.length === 0) {\n        return [[]];\n    }\n    const [first, ...rest] = items;\n    const restSubsets: number[][] = powerset(rest);\n    return restSubsets.map((subset: number[]) => [first, ...subset]);\n}\n\nconsole.log(powerset([1, 2, 3]).map((subset: number[]) => \"[\" + subset.join(\" \") + \"]\").join(\" \"));\n", "logic-js-subsets-without-first-dropped", 7, 1, "    return [...restSubsets, ...restSubsets.map((subset: number[]) => [first, ...subset])];" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheChangeMakesItGiveEverySubset(string fileName, string code, string check, int changeAt, int removed, string added)
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
