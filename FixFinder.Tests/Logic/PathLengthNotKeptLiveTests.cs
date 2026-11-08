using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Bellman-Ford searches keeping each shorter length in the edge, in Python, JavaScript and TypeScript, checked with what they
/// should print: each leaves every place but the start unreached, and the change the check offers is proved by a copy with it,
/// which prints 0 3 1 4. Each row is what FixFinder gave for the program on this computer when these were written. A
/// language this computer cannot run is passed over, and a copy Windows would not let start is not asked to be proved.
/// </summary>
/// <remarks>Each check runs the program and copies of it, so the rows run alone, after the other tests.</remarks>
[Collection(BuildsRunAlone.Name)]
public class PathLengthNotKeptLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string Lengths = "0 3 1 4\n";

    /// <summary>The file, the program, the check, the line the change starts on, how many it takes away, and the lines it writes, one a line.</summary>
    public static TheoryData<string, string, string, int, int, string> Programs => new()
    {
        { "bellman.py", "INF = float(\"inf\")\nedges = [[0, 1, 4], [0, 2, 1], [2, 1, 2], [1, 3, 1], [2, 3, 5]]\ndist = [0, INF, INF, INF]\nfor _ in range(len(dist) - 1):\n    for edge in edges:\n        u, v, length = edge\n        edge[2] = min(dist[u] + length, dist[v])\nprint(*dist)\n", "logic-python-path-length-not-kept", 7, 1, "        dist[v] = min(dist[u] + length, dist[v])" },
        { "weights.py", "def shortest_paths(source, weight_by_edge):\n    weight_by_node = {v: float(\"inf\") for u, v in weight_by_edge}\n    weight_by_node[source] = 0\n    for _ in range(len(weight_by_node) - 1):\n        for (u, v), weight in weight_by_edge.items():\n            weight_by_edge[u, v] = min(\n                weight_by_node[u] + weight,\n                weight_by_node[v]\n            )\n    return weight_by_node\n\n\nlengths = shortest_paths(0, {(0, 1): 4, (0, 2): 1, (2, 1): 2, (1, 3): 1, (2, 3): 5, (3, 0): 9})\nprint(*(lengths[node] for node in sorted(lengths)))\n", "logic-python-path-length-not-kept", 6, 1, "            weight_by_node[v] = min(" },
        { "bellman.js", "const edges = [[0, 1, 4], [0, 2, 1], [2, 1, 2], [1, 3, 1], [2, 3, 5]];\nconst dist = [0, Infinity, Infinity, Infinity];\nfor (let round = 0; round < dist.length - 1; round++) {\n  for (const edge of edges) {\n    const [u, v, length] = edge;\n    edge[2] = Math.min(dist[u] + length, dist[v]);\n  }\n}\nconsole.log(dist.join(\" \"));\n", "logic-js-path-length-not-kept", 6, 1, "    dist[v] = Math.min(dist[u] + length, dist[v]);" },
        { "bellman.ts", "const edges: number[][] = [[0, 1, 4], [0, 2, 1], [2, 1, 2], [1, 3, 1], [2, 3, 5]];\nconst dist: number[] = [0, Infinity, Infinity, Infinity];\nfor (let round = 0; round < dist.length - 1; round++) {\n  for (const edge of edges) {\n    const [u, v, length] = edge;\n    const shorter: number = Math.min(dist[u] + length, dist[v]);\n    edge[2] = shorter;\n  }\n}\nconsole.log(dist.join(\" \"));\n", "logic-js-path-length-not-kept", 7, 1, "    dist[v] = shorter;" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheChangeMakesItFindTheShortestLengths(string fileName, string code, string check, int changeAt, int removed, string added)
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
            Expected = ExpectedBehaviour.From([new ExpectedRun(null, Lengths)], ""),
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
