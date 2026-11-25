using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// JavaScript and TypeScript programs that check themselves with console.assert, which only prints "Assertion failed" and
/// carries on: each console.assert the program reaches is said as a check of its own, on its own line, the program running
/// as it would - and the change that makes every check hold is found, never a change to a console.assert.
/// </summary>
public class ConsoleAssertLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Write(string relative, string content)
    {
        var path = Path.Combine(_temp.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.ReplaceLineEndings("\n"));
        return path;
    }

    private async Task<CheckReport> CheckAsync(string file)
    {
        var launch = TargetFactory.FromFile(file);
        Assert.True(launch.Ok, launch.Problem);
        output.WriteLine($"how: {launch.Explanation}");

        using var http = new FixFinderHttpClient();
        var checker = new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Of(file) ?? CodeLanguage.Any };
        checker.Log += message => output.WriteLine($"log: {message}");
        var report = await checker.CheckAsync(launch);

        output.WriteLine($"syntax: {report.SyntaxSummary}");
        foreach (var finding in report.Findings) output.WriteLine($"[{finding.RuleId}] {finding.Line}: {finding.Title} :: {finding.Fix?.Title} :: {finding.Verified.Summary}");
        return report;
    }

    private const string Average = """
        function average(marks) {
          let total = 0;
          for (const mark of marks) {
            total += mark;
          }
          return total / (marks.length + 1);
        }

        console.assert(average([60, 80]) === 70, "the average of 60 and 80");
        console.assert(average([50]) === 50);
        console.log("done");
        """;

    [Fact]
    public async Task EachConsoleAssertIsACheckAndTheChangeThatMakesThemHoldIsFound()
    {
        if (!LocalFixLiveTests.Available("node")) return;

        var program = Write(@"marks\average.js", Average);
        var report = await CheckAsync(program);

        if (report.SyntaxSummary == "It could not be started") return;
        Assert.Equal("No syntax errors; 2 of 2 checks failed", report.SyntaxSummary);

        var failed = report.Findings.Where(finding => finding.RuleId == "test-failed").OrderBy(finding => finding.Line).ToList();
        Assert.Equal([9, 10], failed.Select(finding => finding.Line));
        Assert.Equal("The console.assert on line 9 found what it checks false: the average of 60 and 80", failed[0].Title);
        Assert.Equal("The console.assert on line 10 found what it checks false", failed[1].Title);
        Assert.Contains("console.assert only prints \"Assertion failed\" and carries on", failed[1].Explanation, StringComparison.Ordinal);

        Assert.Equal(["  return total / (marks.length);"], failed[0].Fix?.NewLines);
        Assert.True(failed[0].Verified.IsVerified, failed[0].Verified.Summary);
        Assert.Equal(Average.ReplaceLineEndings("\n"), File.ReadAllText(program));
    }

    /// <summary>A console.assert in a function, reached from a loop, as an ES module in TypeScript: said on the line it is on.</summary>
    [Fact]
    public async Task AConsoleAssertInAFunctionOfATypeScriptModuleIsSaidOnItsOwnLine()
    {
        if (TargetFactory.FindOnPath("node") is not { } node || !TypeScriptCheck.NodeRunsTypeScript(node)) return;

        var program = Write(@"grades\grades.mts", """
            export function grade(mark: number): string {
              return mark > 70 ? "First" : "Fail";
            }

            function check(mark: number, expected: string): void {
              console.assert(grade(mark) === expected, `grade(${mark}) should be ${expected}`);
            }

            for (const mark of [75, 30]) {
              check(mark, mark >= 70 ? "First" : "Fail");
            }
            check(70, "First");
            """);
        var report = await CheckAsync(program);

        if (report.SyntaxSummary == "It could not be started") return;
        var failed = Assert.Single(report.Findings, finding => finding.RuleId == "test-failed");
        Assert.Equal(6, failed.Line);
        Assert.Equal("The console.assert on line 6 found what it checks false: grade(70) should be First", failed.Title);
    }

    [Fact]
    public async Task WhenEveryConsoleAssertHoldsThatIsSaid()
    {
        if (!LocalFixLiveTests.Available("node")) return;

        var report = await CheckAsync(Write(@"double\double.js", "const double = (x) => x * 2;\n\nconsole.assert(double(2) === 4);\nconsole.assert(double(0) === 0);\n"));

        if (report.SyntaxSummary == "It could not be started") return;
        Assert.Equal("No syntax errors, and all 2 of its checks pass", report.SyntaxSummary);
        Assert.DoesNotContain(report.Findings, finding => finding.RuleId == "test-failed");
    }
}
