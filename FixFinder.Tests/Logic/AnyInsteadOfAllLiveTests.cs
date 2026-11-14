using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Prime searches asking any, in Python, JavaScript and TypeScript, checked with what they should print: each prints no
/// primes at all, and the change the check offers is proved by a copy with it, which prints 2 3 5 7 11 13 17 19 23 29. Each row is what FixFinder gave for the program on this computer when these were written. A
/// language this computer cannot run is passed over, and a copy Windows would not let start is not asked to be proved.
/// </summary>
/// <remarks>Each check runs the program and copies of it, so the rows run alone, after the other tests.</remarks>
[Collection(BuildsRunAlone.Name)]
public class AnyInsteadOfAllLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string Answer = "2 3 5 7 11 13 17 19 23 29\n";

    /// <summary>The file, the program, the check, the line the change starts on, how many it takes away, and the lines it writes, one a line.</summary>
    public static TheoryData<string, string, string, int, int, string> Programs => new()
    {
        { "sieve.py", "def sieve(max):\n    primes = []\n    for n in range(2, max + 1):\n        if any(n % p > 0 for p in primes):\n            primes.append(n)\n    return primes\n\n\nprint(*sieve(30))\n", "logic-python-any-instead-of-all", 4, 1, "        if all(n % p > 0 for p in primes):" },
        { "sieve.js", "function primesUpTo(max) {\n  const primes = [];\n  for (let n = 2; n <= max; n++) {\n    if (primes.some((p) => n % p !== 0)) primes.push(n);\n  }\n  return primes;\n}\n\nconsole.log(primesUpTo(30).join(\" \"));\n", "logic-js-any-instead-of-all", 4, 1, "    if (primes.every((p) => n % p !== 0)) primes.push(n);" },
        { "sieve.ts", "function primesUpTo(max: number): number[] {\n  const primes: number[] = [];\n  for (let n = 2; n <= max; n++) {\n    if (primes.some((p: number) => n % p > 0)) {\n      primes.push(n);\n    }\n  }\n  return primes;\n}\n\nconsole.log(primesUpTo(30).join(\" \"));\n", "logic-js-any-instead-of-all", 4, 1, "    if (primes.every((p: number) => n % p > 0)) {" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheChangeMakesItFindThePrimes(string fileName, string code, string check, int changeAt, int removed, string added)
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
