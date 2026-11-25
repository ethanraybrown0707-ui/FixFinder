using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Prime factorings giving back nothing when no factor is found, in Python, JavaScript and TypeScript, checked with what
/// they should print: each prints 2 2|2 2 3||2 2 5|, and the change the check offers is proved by a copy with it, which
/// prints 2 2 3|2 2 3 5|13|2 2 5 5|97. Each row is what FixFinder gave for the program on this computer when these were written. A
/// language this computer cannot run is passed over, and a copy Windows would not let start is not asked to be proved.
/// </summary>
/// <remarks>Each check runs the program and copies of it, so the rows run alone, after the other tests.</remarks>
[Collection(BuildsRunAlone.Name)]
public class LastFactorDroppedLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string Answer = "2 2 3|2 2 3 5|13|2 2 5 5|97\n";

    /// <summary>The file, the program, the check, the line the change starts on, how many it takes away, and the lines it writes, one a line.</summary>
    public static TheoryData<string, string, string, int, int, string> Programs => new()
    {
        { "get_factors.py", "def get_factors(n):\n    if n == 1:\n        return []\n\n    for i in range(2, int(n ** 0.5) + 1):\n        if n % i == 0:\n            return [i] + get_factors(n // i)\n\n    return []\n\n\nprint(\"|\".join(\" \".join(str(factor) for factor in get_factors(n)) for n in [12, 60, 13, 100, 97]))\n", "logic-python-last-factor-dropped", 9, 1, "    return [n]" },
        { "factors.js", "function factors(n) {\n  if (n === 1) return [];\n  for (let i = 2; i <= Math.sqrt(n); i++) {\n    if (n % i === 0) return [i, ...factors(n / i)];\n  }\n  return [];\n}\n\nconsole.log([12, 60, 13, 100, 97].map((n) => factors(n).join(\" \")).join(\"|\"));\n", "logic-js-last-factor-dropped", 6, 1, "  return [n];" },
        { "factors.ts", "function factors(n: number): number[] {\n  if (n === 1) {\n    return [];\n  }\n  for (let i = 2; i * i <= n; i++) {\n    if (n % i === 0) {\n      return [i, ...factors(n / i)];\n    }\n  }\n  return [];\n}\n\nconsole.log([12, 60, 13, 100, 97].map((n: number) => factors(n).join(\" \")).join(\"|\"));\n", "logic-js-last-factor-dropped", 10, 1, "  return [n];" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheChangeMakesItKeepTheLastFactor(string fileName, string code, string check, int changeAt, int removed, string added)
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
