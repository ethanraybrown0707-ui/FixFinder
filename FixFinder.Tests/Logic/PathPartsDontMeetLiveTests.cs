using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Floyd-Warshall searches whose two parts do not meet, in Python, JavaScript and TypeScript, checked with what they should
/// print: each leaves a place unreached, and the change the check offers is proved by a copy with it, which prints 0 3 1 4. Each row is what FixFinder gave for the program on this computer when these were written. A
/// language this computer cannot run is passed over, and a copy Windows would not let start is not asked to be proved.
/// </summary>
/// <remarks>Each check runs the program and copies of it, so the rows run alone, after the other tests.</remarks>
[Collection(BuildsRunAlone.Name)]
public class PathPartsDontMeetLiveTests(ITestOutputHelper output) : IDisposable
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
        { "floyd.py", "INF = float(\"inf\")\nedges = {(0, 1): 4, (0, 2): 1, (2, 1): 2, (1, 3): 1, (2, 3): 5}\nn = 4\ndist = [[0 if i == j else edges.get((i, j), INF) for j in range(n)] for i in range(n)]\nfor k in range(n):\n    for i in range(n):\n        for j in range(n):\n            dist[i][j] = min(dist[i][j], dist[i][k] + dist[j][k])\nprint(*dist[0])\n", "logic-python-path-parts-dont-meet", 8, 1, "            dist[i][j] = min(dist[i][j], dist[i][k] + dist[k][j])" },
        { "paths.py", "from collections import defaultdict\n\nedges = {(0, 1): 4, (0, 2): 1, (2, 1): 2, (1, 3): 1, (2, 3): 5}\nn = 4\nlength_by_path = defaultdict(lambda: float(\"inf\"))\nlength_by_path.update({(i, i): 0 for i in range(n)})\nlength_by_path.update(edges)\nfor k in range(n):\n    for i in range(n):\n        for j in range(n):\n            if length_by_path[i, k] + length_by_path[j, k] < length_by_path[i, j]:\n                length_by_path[i, j] = length_by_path[i, k] + length_by_path[j, k]\nprint(*(length_by_path[0, j] for j in range(n)))\n", "logic-python-path-parts-dont-meet", 11, 2, "            if length_by_path[i, k] + length_by_path[k, j] < length_by_path[i, j]:\n                length_by_path[i, j] = length_by_path[i, k] + length_by_path[k, j]" },
        { "floyd.js", "const n = 4;\nconst dist = [[0, 4, 1, Infinity], [Infinity, 0, Infinity, 1], [Infinity, 2, 0, 5], [Infinity, Infinity, Infinity, 0]];\nfor (let k = 0; k < n; k++) {\n  for (let i = 0; i < n; i++) {\n    for (let j = 0; j < n; j++) {\n      dist[i][j] = Math.min(dist[i][j], dist[i][k] + dist[j][k]);\n    }\n  }\n}\nconsole.log(dist[0].join(\" \"));\n", "logic-js-path-parts-dont-meet", 6, 1, "      dist[i][j] = Math.min(dist[i][j], dist[i][k] + dist[k][j]);" },
        { "floyd.ts", "const n: number = 4;\nconst dist: number[][] = [[0, 4, 1, Infinity], [Infinity, 0, Infinity, 1], [Infinity, 2, 0, 5], [Infinity, Infinity, Infinity, 0]];\nfor (let k = 0; k < n; k++) {\n  for (let i = 0; i < n; i++) {\n    for (let j = 0; j < n; j++) {\n      if (dist[i][k] + dist[j][k] < dist[i][j]) {\n        dist[i][j] = dist[i][k] + dist[j][k];\n      }\n    }\n  }\n}\nconsole.log(dist[0].join(\" \"));\n", "logic-js-path-parts-dont-meet", 6, 2, "      if (dist[i][k] + dist[k][j] < dist[i][j]) {\n        dist[i][j] = dist[i][k] + dist[k][j];" },
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
