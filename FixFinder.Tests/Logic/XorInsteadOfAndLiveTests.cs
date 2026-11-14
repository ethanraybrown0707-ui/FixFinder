using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Counts of set bits taking the lowest away with xor, in Python, JavaScript and TypeScript, checked with what they should
/// print: each never finishes, and the change the check offers is proved by a copy with it, which prints 7 1 9. Each row is what FixFinder gave for the program on this computer when these were written. A
/// language this computer cannot run is passed over, and a copy Windows would not let start is not asked to be proved.
/// </summary>
/// <remarks>Each check runs the program and copies of it, so the rows run alone, after the other tests.</remarks>
[Collection(BuildsRunAlone.Name)]
public class XorInsteadOfAndLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string Answer = "7 1 9\n";

    /// <summary>The file, the program, the check, the line the change starts on, how many it takes away, and the lines it writes, one a line.</summary>
    public static TheoryData<string, string, string, int, int, string> Programs => new()
    {
        { "bitcount.py", "def bitcount(n):\n    count = 0\n    while n:\n        n ^= n - 1\n        count += 1\n    return count\n\n\nprint(bitcount(127), bitcount(128), bitcount(3005))\n", "logic-python-xor-instead-of-and", 4, 1, "        n &= n - 1" },
        { "bitcount.js", "function bitcount(n) {\n  let count = 0;\n  while (n !== 0) {\n    n = n ^ (n - 1);\n    count++;\n  }\n  return count;\n}\n\nconsole.log(bitcount(127), bitcount(128), bitcount(3005));\n", "logic-js-xor-instead-of-and", 4, 1, "    n = n & (n - 1);" },
        { "bitcount.ts", "function bitcount(n: number): number {\n  let count: number = 0;\n  while (n) {\n    n ^= n - 1;\n    count += 1;\n  }\n  return count;\n}\n\nconsole.log(bitcount(127), bitcount(128), bitcount(3005));\n", "logic-js-xor-instead-of-and", 4, 1, "    n &= n - 1;" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheChangeMakesItCountTheBits(string fileName, string code, string check, int changeAt, int removed, string added)
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
