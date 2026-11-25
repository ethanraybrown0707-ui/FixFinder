using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Cycle searches asking only about the hare's next node, in Python, JavaScript and TypeScript, checked with what they should
/// print: each stops with an error, and the change the check offers is proved by a copy with it, which prints cycle /
/// no cycle. Each row is what FixFinder gave for the program on this computer when these were written. A
/// language this computer cannot run is passed over, and a copy Windows would not let start is not asked to be proved.
/// </summary>
/// <remarks>Each check runs the program and copies of it, so the rows run alone, after the other tests.</remarks>
[Collection(BuildsRunAlone.Name)]
public class TwoStepsOneAskedLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string Answer = "cycle / no cycle\n";

    /// <summary>The file, the program, the check, the line the change starts on, how many it takes away, and the lines it writes, one a line.</summary>
    public static TheoryData<string, string, string, int, int, string> Programs => new()
    {
        { "cycle.py", "class Node:\n    def __init__(self, value, successor=None):\n        self.value = value\n        self.successor = successor\n\n\ndef detect_cycle(node):\n    hare = tortoise = node\n    while True:\n        if hare.successor is None:\n            return False\n        tortoise = tortoise.successor\n        hare = hare.successor.successor\n        if hare is tortoise:\n            return True\n\n\nlooped = Node(1, Node(2, Node(3)))\nlooped.successor.successor.successor = looped\nstraight = Node(1, Node(2, Node(3, Node(4))))\nprint(\"cycle\" if detect_cycle(looped) else \"no cycle\", \"/\", \"cycle\" if detect_cycle(straight) else \"no cycle\")\n", "logic-python-two-steps-one-asked", 10, 1, "        if hare is None or hare.successor is None:" },
        { "fast.py", "class Node:\n    def __init__(self, value, successor=None):\n        self.value = value\n        self.successor = successor\n\n\ndef has_cycle(node):\n    slow = fast = node\n    while fast.successor is not None:\n        slow = slow.successor\n        fast = fast.successor.successor\n        if fast is slow:\n            return True\n    return False\n\n\nlooped = Node(1, Node(2, Node(3)))\nlooped.successor.successor.successor = looped\nstraight = Node(1, Node(2, Node(3, Node(4))))\nprint(\"cycle\" if has_cycle(looped) else \"no cycle\", \"/\", \"cycle\" if has_cycle(straight) else \"no cycle\")\n", "logic-python-two-steps-one-asked", 9, 1, "    while fast is not None and fast.successor is not None:" },
        { "cycle.js", "function hasCycle(node) {\n  let hare = node;\n  let tortoise = node;\n  while (true) {\n    if (hare.next === null) return false;\n    tortoise = tortoise.next;\n    hare = hare.next.next;\n    if (hare === tortoise) return true;\n  }\n}\n\nfunction list(count, looped) {\n  const first = { next: null };\n  let last = first;\n  for (let i = 1; i < count; i++) {\n    last.next = { next: null };\n    last = last.next;\n  }\n  if (looped) last.next = first;\n  return first;\n}\n\nconsole.log(`${hasCycle(list(3, true)) ? \"cycle\" : \"no cycle\"} / ${hasCycle(list(4, false)) ? \"cycle\" : \"no cycle\"}`);\n", "logic-js-two-steps-one-asked", 5, 1, "    if (hare === null || hare.next === null) return false;" },
        { "cycle.ts", "interface ListNode {\n  next: ListNode | null;\n}\n\nfunction hasCycle(node: ListNode): boolean {\n  let hare: ListNode | null = node;\n  let tortoise: ListNode | null = node;\n  while (true) {\n    if (hare!.next === null) {\n      return false;\n    }\n    tortoise = tortoise!.next;\n    hare = hare!.next!.next;\n    if (hare === tortoise) {\n      return true;\n    }\n  }\n}\n\nfunction list(count: number, looped: boolean): ListNode {\n  const first: ListNode = { next: null };\n  let last = first;\n  for (let i = 1; i < count; i++) {\n    last.next = { next: null };\n    last = last.next;\n  }\n  if (looped) {\n    last.next = first;\n  }\n  return first;\n}\n\nconsole.log(`${hasCycle(list(3, true)) ? \"cycle\" : \"no cycle\"} / ${hasCycle(list(4, false)) ? \"cycle\" : \"no cycle\"}`);\n", "logic-js-two-steps-one-asked", 9, 1, "    if (hare === null || hare!.next === null) {" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheChangeMakesItTellTheCycle(string fileName, string code, string check, int changeAt, int removed, string added)
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
