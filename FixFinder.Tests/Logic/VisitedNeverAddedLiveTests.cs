using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Searches whose set of places visited is never given anything, in Python, JavaScript and TypeScript, checked with what they
/// should print: each calls itself until it runs out of room for its calls, or walks round a loop until its time runs out, and
/// the change the check offers is proved by a copy with it, which prints 4. Each row is what FixFinder gave for the program on
/// this computer when these were written. A language this computer cannot run is passed over, and a copy Windows would not
/// let start is not asked to be proved.
/// </summary>
/// <remarks>The walk runs until its time runs out, keeping a processor busy all that time - so the rows run alone, after the other tests.</remarks>
[Collection(BuildsRunAlone.Name)]
public class VisitedNeverAddedLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string PlacesReached = "4\n";

    /// <summary>The file, the program, the check, the line the change starts on, how many it takes away, and the lines it writes.</summary>
    public static TheoryData<string, string, string, int, int, string> Programs => new()
    {
        { "dfs.py", "graph = {0: [1], 1: [2], 2: [0, 3], 3: []}\n\n\ndef count_reached(start):\n    visited = set()\n\n    def reach(node):\n        if node in visited:\n            return 0\n        else:\n            return 1 + sum(reach(next_node) for next_node in graph[node])\n\n    return reach(start)\n\n\nprint(count_reached(0))\n", "logic-python-visited-never-added", 11, 0, "            visited.add(node)" },
        { "walk.py", "links = {0: 1, 1: 2, 2: 3, 3: 1}\nvisited = set()\nnode = 0\nsteps = 0\nwhile True:\n    if node in visited:\n        break\n    steps += 1\n    node = links[node]\nprint(steps)\n", "logic-python-visited-never-added", 8, 0, "    visited.add(node)" },
        { "reach.js", "const graph = { a: [\"b\"], b: [\"c\"], c: [\"a\", \"d\"], d: [] };\nconst seen = {};\n\nfunction reach(node) {\n    if (seen[node]) {\n        return 0;\n    }\n    let count = 1;\n    for (const next of graph[node]) {\n        count += reach(next);\n    }\n    return count;\n}\n\nconsole.log(reach(\"a\"));\n", "logic-js-visited-never-added", 8, 0, "    seen[node] = true;" },
        { "reach.ts", "const graph: number[][] = [[1], [2], [0, 3], []];\n\nfunction reach(node: number, visited: Set<number>): number {\n    if (visited.has(node)) return 0;\n    let count = 1;\n    for (const next of graph[node]) count += reach(next, visited);\n    return count;\n}\n\nconsole.log(reach(0, new Set<number>()));\n", "logic-js-visited-never-added", 5, 0, "    visited.add(node);" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheChangeMakesItFinishWithTheAnswer(string fileName, string code, string check, int changeAt, int removed, string added)
    {
        var program = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(program, code);

        // Twenty seconds a run: the walk only stops when its time runs out, and so does each copy that does not mend it, so
        // FixFinder's own minute - or the three minutes the tests that build give - keeps a row running minutes on end; the
        // copy with the change finishes at once.
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
            Expected = ExpectedBehaviour.From([new ExpectedRun(null, PlacesReached)], ""),
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
        Assert.Equal(added.Split('|'), kept.Fix.NewLines);
        Assert.True(kept.Verified.IsVerified, kept.Verified.Summary);

        Assert.Equal(code, File.ReadAllText(program));
    }
}
