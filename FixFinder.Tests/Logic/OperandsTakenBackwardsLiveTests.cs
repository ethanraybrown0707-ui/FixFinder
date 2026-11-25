using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Reverse Polish evaluators using the value taken first on the left, in Python, JavaScript and TypeScript, checked with
/// what they should print: each prints 11 10, and the change the check offers is proved by a copy with it, which prints
/// 5 -10. Each row is what FixFinder gave for the program on this computer when these were written. A
/// language this computer cannot run is passed over, and a copy Windows would not let start is not asked to be proved.
/// </summary>
/// <remarks>Each check runs the program and copies of it, so the rows run alone, after the other tests.</remarks>
[Collection(BuildsRunAlone.Name)]
public class OperandsTakenBackwardsLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string Answer = "5 -10\n";

    /// <summary>The file, the program, the check, the line the change starts on, how many it takes away, and the lines it writes, one a line.</summary>
    public static TheoryData<string, string, string, int, int, string> Programs => new()
    {
        { "rpn_eval.py", "def rpn_eval(tokens):\n    def op(symbol, a, b):\n        return {\n            '+': lambda a, b: a + b,\n            '-': lambda a, b: a - b,\n            '*': lambda a, b: a * b,\n            '/': lambda a, b: a / b\n        }[symbol](a, b)\n\n    stack = []\n\n    for token in tokens:\n        if isinstance(token, int):\n            stack.append(token)\n        else:\n            a = stack.pop()\n            b = stack.pop()\n            stack.append(\n                op(token, a, b)\n            )\n\n    return stack.pop()\n\n\nprint(rpn_eval([10, 2, '-', 3, '-']), rpn_eval([2, 3, 4, '*', '-']))\n", "logic-python-operands-taken-backwards", 19, 1, "                op(token, b, a)" },
        { "rpn.js", "function evaluate(tokens) {\n  const stack = [];\n  for (const token of tokens) {\n    if (!isNaN(Number(token))) {\n      stack.push(Number(token));\n      continue;\n    }\n    const a = stack.pop();\n    const b = stack.pop();\n    if (token === \"+\") stack.push(a + b);\n    else if (token === \"-\") stack.push(a - b);\n    else stack.push(a * b);\n  }\n  return stack.pop();\n}\n\nconsole.log(evaluate([\"10\", \"2\", \"-\", \"3\", \"-\"]), evaluate([\"2\", \"3\", \"4\", \"*\", \"-\"]));\n", "logic-js-operands-taken-backwards", 8, 2, "    const b = stack.pop();\n    const a = stack.pop();" },
        { "rpn.ts", "const operations: Record<string, (x: number, y: number) => number> = {\n  \"+\": (x, y) => x + y,\n  \"-\": (x, y) => x - y,\n  \"*\": (x, y) => x * y,\n};\n\nfunction evaluate(tokens: string[]): number {\n  const stack: number[] = [];\n  for (const token of tokens) {\n    if (token in operations) {\n      const a = stack.pop()!;\n      const b = stack.pop()!;\n      stack.push(operations[token](a, b));\n    } else {\n      stack.push(Number(token));\n    }\n  }\n  return stack.pop()!;\n}\n\nconsole.log(evaluate([\"10\", \"2\", \"-\", \"3\", \"-\"]), evaluate([\"2\", \"3\", \"4\", \"*\", \"-\"]));\n", "logic-js-operands-taken-backwards", 13, 1, "      stack.push(operations[token](b, a));" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheChangeMakesItSubtractTheRightWayRound(string fileName, string code, string check, int changeAt, int removed, string added)
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
