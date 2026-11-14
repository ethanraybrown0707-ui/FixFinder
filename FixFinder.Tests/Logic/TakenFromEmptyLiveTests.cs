using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Breadth-first searches never asking whether their queue is empty, in Python, JavaScript and TypeScript, checked with what
/// they should print: each stops with an error, and the change the check offers is proved by a copy with it, which prints
/// not reached. Each row is what FixFinder gave for the program on this computer when these were written. A
/// language this computer cannot run is passed over, and a copy Windows would not let start is not asked to be proved.
/// </summary>
/// <remarks>Each check runs the program and copies of it, so the rows run alone, after the other tests.</remarks>
[Collection(BuildsRunAlone.Name)]
public class TakenFromEmptyLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string Answer = "not reached\n";

    /// <summary>The file, the program, the check, the line the change starts on, how many it takes away, and the lines it writes, one a line.</summary>
    public static TheoryData<string, string, string, int, int, string> Programs => new()
    {
        { "bfs.py", "from collections import deque\n\n\ndef reachable(graph, start, goal):\n    queue = deque([start])\n    seen = {start}\n    while True:\n        node = queue.popleft()\n        if node == goal:\n            return True\n        for successor in graph[node]:\n            if successor not in seen:\n                seen.add(successor)\n                queue.append(successor)\n    return False\n\n\ngraph = {0: [1, 2], 1: [3], 2: [3], 3: [], 4: [0]}\nprint(\"reached\" if reachable(graph, 0, 4) else \"not reached\")\n", "logic-python-taken-from-empty", 7, 1, "    while queue:" },
        { "search.py", "def reachable(graph, start, goal):\n    waiting = [start]\n    seen = [start]\n    while True:\n        node = waiting.pop(0)\n        if node == goal:\n            return True\n        for successor in graph[node]:\n            if successor not in seen:\n                seen.append(successor)\n                waiting.append(successor)\n    return False\n\n\ngraph = [[1, 2], [3], [3], [], [0]]\nprint(\"reached\" if reachable(graph, 0, 4) else \"not reached\")\n", "logic-python-taken-from-empty", 4, 1, "    while waiting:" },
        { "bfs.js", "function reachable(graph, start, goal) {\n  const queue = [start];\n  const seen = new Set([start]);\n  while (true) {\n    const node = queue.shift();\n    if (node === goal) return true;\n    for (const successor of graph[node]) {\n      if (!seen.has(successor)) {\n        seen.add(successor);\n        queue.push(successor);\n      }\n    }\n  }\n  return false;\n}\n\nconst graph = [[1, 2], [3], [3], [], [0]];\nconsole.log(reachable(graph, 0, 4) ? \"reached\" : \"not reached\");\n", "logic-js-taken-from-empty", 4, 1, "  while (queue.length > 0) {" },
        { "bfs.ts", "function reachable(graph: number[][], start: number, goal: number): boolean {\n  const queue: number[] = [start];\n  const seen = new Set<number>([start]);\n  while (true) {\n    const node = queue.shift() as number;\n    if (node === goal) {\n      return true;\n    }\n    for (const successor of graph[node]) {\n      if (!seen.has(successor)) {\n        seen.add(successor);\n        queue.push(successor);\n      }\n    }\n  }\n  return false;\n}\n\nconst graph: number[][] = [[1, 2], [3], [3], [], [0]];\nconsole.log(reachable(graph, 0, 4) ? \"reached\" : \"not reached\");\n", "logic-js-taken-from-empty", 4, 1, "  while (queue.length > 0) {" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheChangeMakesItSayWhatIsNotReached(string fileName, string code, string check, int changeAt, int removed, string added)
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
