using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Reporting;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Python programs that stop inside an if, an elif chain's else, a while, and a function that calls itself. Why the line ran
/// is each decision around it, true or false, with what its condition read as the call that stopped reached it - recorded by
/// a second run, keyed to that call: in depth.py the call that stops is the one given 2, though the call given 0 reached the
/// if after it. Each row is what FixFinder showed for the program on this computer when these were written.
/// </summary>
public class PythonBranchesLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>The file, the program, the line it stopped on, and Why this line ran, a decision to a line as the report shows it.</summary>
    public static IEnumerable<object[]> Programs =>
    [
        new object[] { "grade.py", "def grade(score, bands):\n    if score >= 50:\n        return bands[score // 10]\n    return \"fail\"\n\n\nprint(grade(72, [\"E\", \"D\", \"C\", \"B\", \"A\"]))\n", 3,
            "`if score >= 50:` on line 2 was true - `score` was `72`" },
        new object[] { "describe.py", "def describe(temperature, labels):\n    if temperature < 0:\n        return labels[\"freezing\"]\n    elif temperature < 15:\n        return labels[\"cold\"]\n    else:\n        return labels[\"warm\"]\n\n\nlabels = {\"freezing\": \"Ice\", \"cold\": \"Chilly\"}\nprint(describe(21, labels))\n", 7,
            "`if temperature < 0:` on line 2 was false - `temperature` was `21`\n`elif temperature < 15:` on line 4 was false - `temperature` was `21`\n`else:` on line 6 ran, as the conditions above it were false" },
        new object[] { "total.py", "def total_of(prices):\n    total = 0\n    index = 0\n    while index <= len(prices):\n        total += prices[index]\n        index += 1\n    return total\n\n\nprint(total_of([3, 4, 5]))\n", 5,
            "`while index <= len(prices):` on line 4 was true - `index` was `3`, `prices` was `[3, 4, 5]`" },
        new object[] { "depth.py", "def depth(n, seen):\n    if n > 0:\n        depth(n - 1, seen)\n        return seen[n]\n    return 0\n\n\nprint(depth(3, [0, 1]))\n", 4,
            "`if n > 0:` on line 2 was true - `n` was `2`" },
    ];

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task EachDecisionIsShownWithWhatItsConditionRead(string fileName, string code, int crashLine, string why)
    {
        var program = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(program, code);

        var launch = TargetFactory.FromFile(program, LiveAllowance.For(program, TimeSpan.FromMinutes(2)));
        if (!launch.Ok)
        {
            output.WriteLine($"Not run on this computer: {launch.Problem}");
            return;
        }

        using var http = new FixFinderHttpClient();
        var report = await new ProgramChecker(http, new FixSourceRegistry()).CheckAsync(launch);
        var stopped = report.Findings.SingleOrDefault(finding => finding.Branches.Count > 0);
        Assert.True(stopped is not null, $"no decisions among {string.Join(", ", report.Findings.Select(finding => finding.RuleId))}");

        var shown = FindingLayout.WhyThisLineRan(stopped!);
        foreach (var decision in shown) output.WriteLine(decision);
        Assert.Equal(crashLine, stopped!.Line);
        Assert.Equal(why.Split('\n'), shown);
        Assert.Equal(code, File.ReadAllText(program));
    }
}
