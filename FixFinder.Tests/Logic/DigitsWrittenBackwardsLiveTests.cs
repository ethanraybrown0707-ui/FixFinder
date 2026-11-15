using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Numbers written in another base with each digit put after the ones before it, in Python, JavaScript and TypeScript,
/// checked with what they should print: each prints F1 100101 DBB, and the change the check offers is proved by a copy with
/// it, which prints 1F 101001 BBD. Each row is what FixFinder gave for the program on this computer when these were written. A
/// language this computer cannot run is passed over, and a copy Windows would not let start is not asked to be proved.
/// </summary>
/// <remarks>Each check runs the program and copies of it, so the rows run alone, after the other tests.</remarks>
[Collection(BuildsRunAlone.Name)]
public class DigitsWrittenBackwardsLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string Answer = "1F 101001 BBD\n";

    /// <summary>The file, the program, the check, the line the change starts on, how many it takes away, and the lines it writes, one a line.</summary>
    public static TheoryData<string, string, string, int, int, string> Programs => new()
    {
        { "to_base.py", "import string\ndef to_base(num, b):\n    result = ''\n    alphabet = string.digits + string.ascii_uppercase\n    while num > 0:\n        i = num % b\n        num = num // b\n        result = result + alphabet[i]\n    return result\n\n\nprint(to_base(31, 16), to_base(41, 2), to_base(3005, 16))\n", "logic-python-digits-written-backwards", 8, 1, "        result = alphabet[i] + result" },
        { "to_base.js", "const DIGITS = \"0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ\";\n\nfunction toBase(num, base) {\n  let result = \"\";\n  while (num > 0) {\n    const digit = num % base;\n    result += DIGITS[digit];\n    num = Math.floor(num / base);\n  }\n  return result;\n}\n\nconsole.log(toBase(31, 16), toBase(41, 2), toBase(3005, 16));\n", "logic-js-digits-written-backwards", 7, 1, "    result = DIGITS[digit] + result;" },
        { "to_base.ts", "const DIGITS: string = \"0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ\";\n\nfunction toBase(num: number, base: number): string {\n  let result: string = \"\";\n  while (num > 0) {\n    result = result + DIGITS[num % base];\n    num = Math.floor(num / base);\n  }\n  return result;\n}\n\nconsole.log(toBase(31, 16), toBase(41, 2), toBase(3005, 16));\n", "logic-js-digits-written-backwards", 6, 1, "    result = DIGITS[num % base] + result;" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheChangeMakesItWriteTheDigitsInOrder(string fileName, string code, string check, int changeAt, int removed, string added)
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
