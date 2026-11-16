using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Square roots stopping when the guess is near the number itself, in Python, JavaScript and TypeScript, checked with what
/// they should print: none stops within its 20 seconds, and the change the check offers is proved by a copy with it, which
/// prints the square root of 2, 1.414. Each row is what FixFinder gave for the program on this computer when these were written. A
/// language this computer cannot run is passed over, and a copy Windows would not let start is not asked to be proved.
/// </summary>
/// <remarks>Each check runs the program and copies of it, so the rows run alone, after the other tests.</remarks>
[Collection(BuildsRunAlone.Name)]
public class RootGuessNotSquaredLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string Answer = "1.414\n";

    /// <summary>The file, the program, the check, the line the change starts on, how many it takes away, and the lines it writes, one a line.</summary>
    public static TheoryData<string, string, string, int, int, string> Programs => new()
    {
        { "sqrt.py", "def sqrt(x, epsilon):\n    approx = x / 2\n    while abs(x - approx) > epsilon:\n        approx = 0.5 * (approx + x / approx)\n    return approx\n\n\nprint(f\"{sqrt(2, 0.001):.3f}\")\n", "logic-python-root-guess-not-squared", 3, 1, "    while abs(x - approx ** 2) > epsilon:" },
        { "roots.js", "function squareRoot(x, epsilon) {\n    let guess = x / 2;\n    while (Math.abs(x - guess) > epsilon) {\n        guess = (guess + x / guess) / 2;\n    }\n    return guess;\n}\n\nconsole.log(squareRoot(2, 0.001).toFixed(3));\n", "logic-js-root-guess-not-squared", 3, 1, "    while (Math.abs(x - guess * guess) > epsilon) {" },
        { "roots.ts", "function squareRoot(x: number, epsilon: number): number {\n    let guess = x / 2;\n    while (Math.abs(x - guess) > epsilon) {\n        guess = (guess + x / guess) / 2;\n    }\n    return guess;\n}\n\nconsole.log(squareRoot(2, 0.001).toFixed(3));\n", "logic-js-root-guess-not-squared", 3, 1, "    while (Math.abs(x - guess * guess) > epsilon) {" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheChangeMakesItStopAtTheRoot(string fileName, string code, string check, int changeAt, int removed, string added)
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
