using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Euclid's greatest common divisor going on with a % b and b, in Python - a call and a loop - JavaScript and TypeScript,
/// checked with what they should print: none prints it, and the change the check offers is proved by a copy with it, which
/// prints 7 17 6. Each row is what FixFinder gave for the program on this computer when these were written. A
/// language this computer cannot run is passed over, and a copy Windows would not let start is not asked to be proved.
/// </summary>
/// <remarks>Each check runs the program and copies of it, so the rows run alone, after the other tests.</remarks>
[Collection(BuildsRunAlone.Name)]
public class EuclidPairNotMovedLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string Answer = "7 17 6\n";

    /// <summary>The file, the program, the check, the line the change starts on, how many it takes away, and the lines it writes, one a line.</summary>
    public static TheoryData<string, string, string, int, int, string> Programs => new()
    {
        { "gcd.py", "def gcd(a, b):\n    if b == 0:\n        return a\n    else:\n        return gcd(a % b, b)\n\n\nprint(gcd(35, 21), gcd(17, 0), gcd(48, 18))\n", "logic-python-euclid-pair-not-moved", 5, 1, "        return gcd(b, a % b)" },
        { "gcd_loop.py", "def gcd(a, b):\n    while b != 0:\n        a, b = a % b, b\n    return a\n\n\nprint(gcd(35, 21), gcd(17, 0), gcd(48, 18))\n", "logic-python-euclid-pair-not-moved", 3, 1, "        a, b = b, a % b" },
        { "gcd.js", "function gcd(a, b) {\n  return b === 0 ? a : gcd(a % b, b);\n}\n\nconsole.log(gcd(35, 21), gcd(17, 0), gcd(48, 18));\n", "logic-js-euclid-pair-not-moved", 2, 1, "  return b === 0 ? a : gcd(b, a % b);" },
        { "gcd.ts", "function gcd(a: number, b: number): number {\n  if (b === 0) return a;\n  return gcd(a % b, b);\n}\n\nconsole.log(gcd(35, 21), gcd(17, 0), gcd(48, 18));\n", "logic-js-euclid-pair-not-moved", 3, 1, "  return gcd(b, a % b);" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheChangeMakesItFindTheDivisors(string fileName, string code, string check, int changeAt, int removed, string added)
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
