using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// What each variable of a Python program held where it stopped, line by line, recorded by a second run - for a crash on
/// None and for one that is not. Each row is what FixFinder gave for the program on this computer when these were written:
/// every variable, every value it was given and the line that gave it. A computer without Python passes them over.
/// </summary>
public class PythonTimelineLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>The file, the program, the line it stopped on, and each variable's values: "name: line 2 = 0; line 5 = 55", split by |.</summary>
    public static TheoryData<string, string, int, string> Programs => new()
    {
        { "timeline.py", "marks = [55, 72, 40]\ntotal = 0\npassed = []\nfor mark in marks:\n    total += mark\n    if mark >= 50:\n        passed.append(mark)\nbest = None\naverage = total / len(marks)\nprint(\"Best:\", best + 1)\n", 10,
            "marks: line 1 = [55, 72, 40]|total: line 2 = 0; line 5 = 55; line 5 = 127; line 5 = 167|passed: line 3 = []; line 7 = [55]; line 7 = [55, 72]|mark: line 4 = 55; line 4 = 72; line 4 = 40|best: line 8 = None|average: line 9 = 55.666666666666664" },
        { "zero_division.py", "def share(total, people):\n    return total / people\n\n\nbill = 120\nguests = [\"ann\", \"bob\"]\nleft = len(guests) - 2\nprint(share(bill, left))\n", 2,
            "total: the call on line 8 = 120|people: the call on line 8 = 0" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task EachVariablesValuesAreThoseTheRunRecorded(string fileName, string code, int crashLine, string timeline)
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

        var stopped = report.Findings.SingleOrDefault(finding => finding.Kind == FindingKind.Runtime && finding.Timeline.Count > 0);
        Assert.True(stopped is not null, $"no timeline among {string.Join(", ", report.Findings.Select(finding => finding.RuleId))}");
        Assert.Equal(crashLine, stopped!.Line);

        var said = stopped.Timeline.Select(variable => $"{variable.Name}: " + string.Join("; ", variable.Changes.Select(change => $"{change.GivenBy} = {change.Value}")));
        Assert.Equal(timeline.Split('|'), said);
        Assert.All(stopped.Timeline, variable => Assert.False(variable.Cut));
    }
}
