using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Checks of a string's brackets saying yes whatever the count, in Python, JavaScript and TypeScript, checked with what
/// they should print: each says (( is valid, and the change the check offers is proved by a copy with it, which prints
/// valid invalid invalid. Each row is what FixFinder gave for the program on this computer when these were written. A
/// language this computer cannot run is passed over, and a copy Windows would not let start is not asked to be proved.
/// </summary>
/// <remarks>Each check runs the program and copies of it, so the rows run alone, after the other tests.</remarks>
[Collection(BuildsRunAlone.Name)]
public class BracketsLeftOpenLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string Answer = "valid invalid invalid\n";

    /// <summary>The file, the program, the check, the line the change starts on, how many it takes away, and the lines it writes, one a line.</summary>
    public static TheoryData<string, string, string, int, int, string> Programs => new()
    {
        { "parens.py", "def is_valid_parenthesization(parens):\n    depth = 0\n    for paren in parens:\n        if paren == '(':\n            depth += 1\n        else:\n            depth -= 1\n            if depth < 0:\n                return False\n\n    return True\n\n\nprint(*(\"valid\" if is_valid_parenthesization(text) else \"invalid\" for text in [\"((()()))()\", \")()(\", \"((\"]))\n", "logic-python-brackets-left-open", 11, 1, "    return depth == 0" },
        { "parens.js", "function balanced(text) {\n  let depth = 0;\n  for (const bracket of text) {\n    if (bracket === \"(\") {\n      depth++;\n    } else {\n      depth--;\n      if (depth < 0) return false;\n    }\n  }\n  return true;\n}\n\nconsole.log([\"((()()))()\", \")()(\", \"((\"].map((text) => (balanced(text) ? \"valid\" : \"invalid\")).join(\" \"));\n", "logic-js-brackets-left-open", 11, 1, "  return depth === 0;" },
        { "parens.ts", "function balanced(text: string): boolean {\n  let depth: number = 0;\n  for (const bracket of text) {\n    if (bracket === \"(\") {\n      depth += 1;\n    } else {\n      depth -= 1;\n      if (depth < 0) {\n        return false;\n      }\n    }\n  }\n  return true;\n}\n\nconsole.log([\"((()()))()\", \")()(\", \"((\"].map((text: string) => (balanced(text) ? \"valid\" : \"invalid\")).join(\" \"));\n", "logic-js-brackets-left-open", 13, 1, "  return depth === 0;" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheChangeMakesItCatchBracketsLeftOpen(string fileName, string code, string check, int changeAt, int removed, string added)
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
