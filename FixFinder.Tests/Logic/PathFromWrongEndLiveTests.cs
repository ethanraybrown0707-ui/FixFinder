using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Shortest-path searches that add each edge's length to the place it goes to, in Python, JavaScript and TypeScript, checked
/// with what they should print: each prints the lengths it started with, and the change the check offers is proved by a copy
/// with it, which prints 0 3 1 4. Each row is what FixFinder gave for the program on this computer when these were written. A
/// language this computer cannot run is passed over, and a copy Windows would not let start is not asked to be proved.
/// </summary>
/// <remarks>Each check runs the program and copies of it, so the rows run alone, after the other tests.</remarks>
[Collection(BuildsRunAlone.Name)]
public class PathFromWrongEndLiveTests(ITestOutputHelper output) : IDisposable
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
        { "dijkstra.py", "import heapq\n\ngraph = {0: [(1, 4), (2, 1)], 1: [(3, 1)], 2: [(1, 2), (3, 5)], 3: []}\n\n\ndef shortest(start):\n    dist = {node: float(\"inf\") for node in graph}\n    dist[start] = 0\n    heap = [(0, start)]\n    while heap:\n        _, node = heapq.heappop(heap)\n        for next_node, length in graph[node]:\n            if dist[next_node] + length < dist[next_node]:\n                dist[next_node] = dist[next_node] + length\n                heapq.heappush(heap, (dist[next_node], next_node))\n    return dist\n\n\nlengths = shortest(0)\nprint(*(lengths[node] for node in sorted(lengths)))\n", "logic-python-path-from-wrong-end", 13, 2, "            if dist[node] + length < dist[next_node]:\n                dist[next_node] = dist[node] + length" },
        { "relax.py", "import heapq\n\ngraph = {0: [(1, 4), (2, 1)], 1: [(3, 1)], 2: [(1, 2), (3, 5)], 3: []}\n\n\ndef shortest(start):\n    dist = {node: float(\"inf\") for node in graph}\n    dist[start] = 0\n    heap = [(0, start)]\n    while heap:\n        distance, node = heapq.heappop(heap)\n        if distance > dist[node]:\n            continue\n        for next_node, length in graph[node]:\n            dist[next_node] = min(dist[next_node], dist[next_node] + length)\n            heapq.heappush(heap, (dist[next_node], next_node))\n    return dist\n\n\nlengths = shortest(0)\nprint(*(lengths[node] for node in sorted(lengths)))\n", "logic-python-path-from-wrong-end", 15, 1, "            dist[next_node] = min(dist[next_node], dist[node] + length)" },
        { "dijkstra.js", "const INF = Infinity;\nconst graph = [[[1, 4], [2, 1]], [[3, 1]], [[1, 2], [3, 5]], []];\nconst dist = [0, INF, INF, INF];\nconst done = [false, false, false, false];\nfor (let round = 0; round < 4; round++) {\n  let node = -1;\n  for (let i = 0; i < 4; i++) {\n    if (!done[i] && (node < 0 || dist[i] < dist[node])) node = i;\n  }\n  done[node] = true;\n  for (const [next, length] of graph[node]) {\n    if (dist[next] + length < dist[next]) {\n      dist[next] = dist[next] + length;\n    }\n  }\n}\nconsole.log(dist.join(\" \"));\n", "logic-js-path-from-wrong-end", 12, 2, "    if (dist[node] + length < dist[next]) {\n      dist[next] = dist[node] + length;" },
        { "dijkstra.ts", "const graph: number[][][] = [[[1, 4], [2, 1]], [[3, 1]], [[1, 2], [3, 5]], []];\nconst dist: number[] = [0, Infinity, Infinity, Infinity];\nconst done: boolean[] = [false, false, false, false];\nfor (let round = 0; round < 4; round++) {\n  let node: number = -1;\n  for (let i = 0; i < 4; i++) {\n    if (!done[i] && (node < 0 || dist[i] < dist[node])) node = i;\n  }\n  done[node] = true;\n  for (const [next, length] of graph[node]) {\n    dist[next] = Math.min(dist[next], dist[next] + length);\n  }\n}\nconsole.log(dist.join(\" \"));\n", "logic-js-path-from-wrong-end", 11, 1, "    dist[next] = Math.min(dist[next], dist[node] + length);" },
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
