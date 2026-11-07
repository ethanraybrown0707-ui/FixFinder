using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// fib(60) with a memo never given an answer, in Python and JavaScript, checked with what it should print: the program runs
/// until its time runs out, and the change the check offers is proved by a copy with it, which prints 1548008755920. Each row is
/// what FixFinder gave for the program on this computer when these were written. A language this computer cannot run is passed over, and a copy
/// Windows would not let start is not asked to be proved.
/// </summary>
/// <remarks>Each row's program runs until its time runs out, keeping a processor busy all that time - so the rows run alone, after the other tests.</remarks>
[Collection(BuildsRunAlone.Name)]
public class MemoNeverKeptLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string Fib60 = "1548008755920\n";

    /// <summary>The file, the program, the check, the line the change starts on, how many it takes away, and the lines it writes.</summary>
    public static TheoryData<string, string, string, int, int, string> Programs => new()
    {
        { "fib.py", "memo = {}\n\n\ndef fib(n):\n    if n in memo:\n        return memo[n]\n    if n <= 1:\n        return n\n    return fib(n - 1) + fib(n - 2)\n\n\nprint(fib(60))\n", "logic-python-memo-never-kept", 9, 1, "    memo[n] = fib(n - 1) + fib(n - 2)|    return memo[n]" },
        { "fib.js", "const memo = new Map();\n\nfunction fib(n) {\n    if (memo.has(n)) {\n        return memo.get(n);\n    }\n    if (n <= 1) {\n        return n;\n    }\n    return fib(n - 1) + fib(n - 2);\n}\n\nconsole.log(fib(60));\n", "logic-js-memo-never-kept", 10, 1, "    memo.set(n, fib(n - 1) + fib(n - 2));|    return memo.get(n);" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheChangeMakesItFinishWithTheAnswer(string fileName, string code, string check, int changeAt, int removed, string added)
    {
        var program = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(program, code);

        // Twenty seconds a run: the program only stops when its time runs out, and so does each copy that does not mend it,
        // so FixFinder's own minute - or the three minutes the tests that build give - keeps a row running minutes on end;
        // the copy with the change finishes at once.
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
            Expected = ExpectedBehaviour.From([new ExpectedRun(null, Fib60)], ""),
        }.CheckAsync(launch);
        foreach (var each in report.Findings)
            output.WriteLine($"[{each.RuleId}] {each.Line}: {each.Title} :: {string.Join("|", each.Fix?.NewLines ?? [])} :: {each.Verified.Summary}");

        if (report.Notes.Any(ApplicationControl.Refused) || report.Findings.Any(finding => ApplicationControl.Refused(finding.Verified.Summary)))
        {
            output.WriteLine("Windows did not let the program start here, so the change could not be proved.");
            return;
        }

        // The check's change, on whichever finding carries it: once a copy proves it, the program's own finding that it stopped
        // before printing what was expected is given it.
        var kept = report.Findings.SingleOrDefault(finding => finding.Fix?.RuleId == check);
        Assert.True(kept is not null, $"no {check} among {string.Join(", ", report.Findings.Select(finding => finding.RuleId))}");
        Assert.Equal(changeAt, kept!.Fix!.StartLine);
        Assert.Equal(removed, kept.Fix.RemoveCount);
        Assert.Equal(added.Split('|'), kept.Fix.NewLines);
        Assert.True(kept.Verified.IsVerified, kept.Verified.Summary);

        Assert.Equal(code, File.ReadAllText(program));
    }
}
