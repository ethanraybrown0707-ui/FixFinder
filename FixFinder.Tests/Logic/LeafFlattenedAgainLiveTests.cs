using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Flattenings giving an item that is not a list back to themselves, in Python, JavaScript and TypeScript, checked with
/// what they should print: none prints the numbers, and the change the check offers is proved by a copy with it, which
/// prints 1 2 3 4 5. Each row is what FixFinder gave for the program on this computer when these were written. A
/// language this computer cannot run is passed over, and a copy Windows would not let start is not asked to be proved.
/// </summary>
/// <remarks>Each check runs the program and copies of it, so the rows run alone, after the other tests.</remarks>
[Collection(BuildsRunAlone.Name)]
public class LeafFlattenedAgainLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string Answer = "1 2 3 4 5\n";

    /// <summary>The file, the program, the check, the line the change starts on, how many it takes away, and the lines it writes, one a line.</summary>
    public static TheoryData<string, string, string, int, int, string> Programs => new()
    {
        { "flatten.py", "def flatten(arr):\n    for x in arr:\n        if isinstance(x, list):\n            for y in flatten(x):\n                yield y\n        else:\n            yield flatten(x)\n\n\nprint(*flatten([[1, [2, 3]], 4, [[5]], []]))\n", "logic-python-leaf-flattened-again", 7, 1, "            yield x" },
        { "flatten.js", "function* flatten(items) {\n  for (const item of items) {\n    if (Array.isArray(item)) {\n      yield* flatten(item);\n    } else {\n      yield flatten(item);\n    }\n  }\n}\n\nconsole.log([...flatten([[1, [2, 3]], 4, [[5]], []])].join(\" \"));\n", "logic-js-leaf-flattened-again", 6, 1, "      yield item;" },
        { "flatten.ts", "type Nested = number | Nested[];\n\nfunction flatten(items: Nested[]): Nested[] {\n  const flat: Nested[] = [];\n  for (const item of items) {\n    if (Array.isArray(item)) {\n      flat.push(...flatten(item));\n    } else {\n      flat.push(...flatten([item]));\n    }\n  }\n  return flat;\n}\n\nconsole.log(flatten([[1, [2, 3]], 4, [[5]], []]).join(\" \"));\n", "logic-js-leaf-flattened-again", 9, 1, "      flat.push(item);" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheChangeMakesItFlattenTheList(string fileName, string code, string check, int changeAt, int removed, string added)
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
