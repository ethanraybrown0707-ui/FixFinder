using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Linked lists reversed without moving on the node before, in Python, JavaScript and TypeScript, checked with what they
/// should print: each prints an empty line, and the change the check offers is proved by a copy with it, which prints 3 2 1. Each row is what FixFinder gave for the program on this computer when these were written. A
/// language this computer cannot run is passed over, and a copy Windows would not let start is not asked to be proved.
/// </summary>
/// <remarks>Each check runs the program and copies of it, so the rows run alone, after the other tests.</remarks>
[Collection(BuildsRunAlone.Name)]
public class PreviousNeverMovedLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string Reversed = "3 2 1\n";

    /// <summary>The file, the program, the check, the line the change starts on, how many it takes away, and the lines it writes, one a line.</summary>
    public static TheoryData<string, string, string, int, int, string> Programs => new()
    {
        { "reverse.py", "class Node:\n    def __init__(self, value, successor=None):\n        self.value = value\n        self.successor = successor\n\n\ndef reverse_linked_list(node):\n    prevnode = None\n    while node:\n        nextnode = node.successor\n        node.successor = prevnode\n        node = nextnode\n    return prevnode\n\n\nvalues = []\nnode = reverse_linked_list(Node(1, Node(2, Node(3))))\nwhile node:\n    values.append(str(node.value))\n    node = node.successor\nprint(\" \".join(values))\n", "logic-python-previous-never-moved", 12, 0, "        prevnode = node" },
        { "reverse.js", "function reverse(node) {\n  let previous = null;\n  while (node !== null) {\n    const following = node.next;\n    node.next = previous;\n    node = following;\n  }\n  return previous;\n}\n\nconst values = [];\nfor (let node = reverse({ value: 1, next: { value: 2, next: { value: 3, next: null } } }); node !== null; node = node.next) {\n  values.push(node.value);\n}\nconsole.log(values.join(\" \"));\n", "logic-js-previous-never-moved", 6, 0, "    previous = node;" },
        { "reverse.ts", "interface ListNode {\n  value: number;\n  next: ListNode | null;\n}\n\nfunction reverse(node: ListNode | null): ListNode | null {\n  let previous: ListNode | null = null;\n  while (node !== null) {\n    const following: ListNode | null = node.next;\n    node.next = previous;\n    node = following;\n  }\n  return previous;\n}\n\nconst values: number[] = [];\nfor (let node = reverse({ value: 1, next: { value: 2, next: { value: 3, next: null } } }); node !== null; node = node.next) {\n  values.push(node.value);\n}\nconsole.log(values.join(\" \"));\n", "logic-js-previous-never-moved", 11, 0, "    previous = node;" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheChangeMakesItReverseTheList(string fileName, string code, string check, int changeAt, int removed, string added)
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
            Expected = ExpectedBehaviour.From([new ExpectedRun(null, Reversed)], ""),
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
