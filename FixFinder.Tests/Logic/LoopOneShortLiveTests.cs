using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Pascal's triangles whose loop stops one short, in Python, JavaScript and TypeScript, checked with what they should
/// print: none prints the rows, and the change the check offers is proved by a copy with it, which prints
/// 1|1 1|1 2 1|1 3 3 1|1 4 6 4 1. Each row is what FixFinder gave for the program on this computer when these were written. A
/// language this computer cannot run is passed over, and a copy Windows would not let start is not asked to be proved.
/// </summary>
/// <remarks>Each check runs the program and copies of it, so the rows run alone, after the other tests.</remarks>
[Collection(BuildsRunAlone.Name)]
public class LoopOneShortLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string Answer = "1|1 1|1 2 1|1 3 3 1|1 4 6 4 1\n";

    /// <summary>The file, the program, the check, the line the change starts on, how many it takes away, and the lines it writes, one a line.</summary>
    public static TheoryData<string, string, string, int, int, string> Programs => new()
    {
        { "pascal.py", "def pascal(n):\n    rows = [[1]]\n    for r in range(1, n):\n        row = []\n        for c in range(0, r):\n            upleft = rows[r - 1][c - 1] if c > 0 else 0\n            upright = rows[r - 1][c] if c < r else 0\n            row.append(upleft + upright)\n        rows.append(row)\n\n    return rows\n\n\nprint(\"|\".join(\" \".join(str(number) for number in row) for row in pascal(5)))\n", "logic-python-loop-one-short", 5, 1, "        for c in range(0, r + 1):" },
        { "pascal.js", "function pascal(n) {\n  const rows = [[1]];\n  for (let r = 1; r < n; r++) {\n    const row = [];\n    for (let c = 0; c < r; c++) {\n      const upleft = c > 0 ? rows[r - 1][c - 1] : 0;\n      const upright = c < r ? rows[r - 1][c] : 0;\n      row.push(upleft + upright);\n    }\n    rows.push(row);\n  }\n  return rows;\n}\n\nconsole.log(pascal(5).map((row) => row.join(\" \")).join(\"|\"));\n", "logic-js-loop-one-short", 5, 1, "    for (let c = 0; c < r + 1; c++) {" },
        { "pascal.ts", "function pascal(n: number): number[][] {\n  const rows: number[][] = [[1]];\n  for (let r = 1; r < n; r++) {\n    const row: number[] = [];\n    for (let c = 0; c < r; c++) {\n      const upleft: number = c > 0 ? rows[r - 1][c - 1] : 0;\n      const upright: number = c < r ? rows[r - 1][c] : 0;\n      row.push(upleft + upright);\n    }\n    rows.push(row);\n  }\n  return rows;\n}\n\nconsole.log(pascal(5).map((row: number[]) => row.join(\" \")).join(\"|\"));\n", "logic-js-loop-one-short", 5, 1, "    for (let c = 0; c < r + 1; c++) {" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheChangeMakesItPrintTheRows(string fileName, string code, string check, int changeAt, int removed, string added)
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
