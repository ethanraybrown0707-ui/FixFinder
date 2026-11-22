using FixFinder.Core;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Reporting;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// A loop that runs one past the end of a list, and the position it asks for that does not exist: two findings, one
/// mistake. Reading the file again with the loop's fix made, in a copy, finds the position no longer asked for, so the
/// second is shown as removed by the first one's fix - in Java, C# and Go - and a mistake the fix leaves is left alone.
/// </summary>
public class FixEffectsLiveTests(ITestOutputHelper output) : IDisposable
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

    private async Task<CheckReport> CheckAsync(string file, bool codeOnly)
    {
        var launch = TargetFactory.FromFile(file);
        Assert.True(launch.Ok, launch.Problem);

        using var http = new FixFinderHttpClient();
        var checker = new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Of(file) ?? CodeLanguage.Any };
        checker.Log += message => output.WriteLine($"log: {message}");
        var report = codeOnly ? await checker.CheckCodeAsync(launch) : await checker.CheckAsync(launch);

        foreach (var finding in report.Findings)
            output.WriteLine($"[{finding.RuleId}] {finding.Line}: {finding.Title} :: {finding.Fix?.NewLines.FirstOrDefault()} :: {finding.CausedBy}");
        return report;
    }

    private const string Marks = """
        public class Marks {
            static int total(int[] marks) {
                int result = 0;
                for (int index = 0; index <= marks.length; index++) {
                    result += marks[index];
                }
                return result;
            }

            static int average(int[] marks) {
                return total(marks) / marks.length;
            }

            public static void main(String[] args) {
                System.out.println(average(new int[] {60, 70, 80}));
            }
        }
        """;

    /// <summary>The finding a fix removes, with the finding whose fix it is, checking the link is the one a reading showed.</summary>
    private static (Finding Root, Finding Removed) RemovedByTheLoopsFix(CheckReport report, string rootRule, int rootLine, int removedLine)
    {
        var root = Assert.Single(report.Findings, finding => finding.RuleId == rootRule);
        Assert.Equal(rootLine, root.Line);
        Assert.Null(root.CausedBy);

        var removed = Assert.Single(report.Findings, finding => finding.RuleId == "analysis-index-out-of-range");
        Assert.Equal(removedLine, removed.Line);
        Assert.Equal(new Relation(root.Id, RelationKind.RemovedByItsFix, Confidence.Certain), removed.CausedBy);
        return (root, removed);
    }

    [Fact]
    public async Task InJavaTheLoopsFixRemovesThePositionItAskedForAndNotTheDivision()
    {
        if (Toolchains.FindJavac() is null) return;

        var marks = Write(@"marks\Marks.java", Marks);
        var report = await CheckAsync(marks, codeOnly: true);

        var (root, removed) = RemovedByTheLoopsFix(report, "logic-off-by-one-length", 4, 5);
        Assert.Equal(["        for (int index = 0; index < marks.length; index++) {"], root.Fix!.NewLines);
        Assert.Null(Assert.Single(report.Findings, finding => finding.RuleId == "analysis-division-by-zero").CausedBy);

        var links = FindingLinks.Of(report.Findings);
        Assert.Equal([removed], links.LeadsTo(root));
        Assert.Equal("Its fix removes the problem on line 5 too: with the fix made, in a copy, the file was read again and that problem was no longer found.",
                     FindingText.Explains(links.LeadsTo(root)));
        Assert.Equal("Fixing the problem on line 4 removes this one too: with that fix made, in a copy, the file was read again and this was no longer found.",
                     FindingText.Follows(links.FollowsFrom(removed)!, removed));
        Assert.Equal(Marks.ReplaceLineEndings("\n"), File.ReadAllText(marks));
    }

    /// <summary>Run, the program crashes on the position, and the crash's fix - the loop's - removes the analysis's finding the same way.</summary>
    [Fact]
    public async Task InJavaTheCrashsFixRemovesTheSameMistakeFoundByFollowingTheValues()
    {
        if (!LocalFixLiveTests.Available("java") || Toolchains.FindJavac() is null) return;

        var report = await CheckAsync(Write(@"marks\Marks.java", Marks), codeOnly: false);

        var (root, _) = RemovedByTheLoopsFix(report, "java-off-by-one-loop", 5, 5);
        Assert.Equal(["        for (int index = 0; index < marks.length; index++) {"], root.Fix!.NewLines);
        Assert.Null(Assert.Single(report.Findings, finding => finding.RuleId == "analysis-division-by-zero").CausedBy);
    }

    [Fact]
    public async Task InCSharpTheLoopsFixRemovesThePositionItAskedFor()
    {
        var program = Write(@"prices\PastEnd.cs", """
            using System;

            class PastEnd
            {
                static int Total(int[] prices)
                {
                    int result = 0;
                    for (int index = 0; index <= prices.Length; index++)
                    {
                        result += prices[index];
                    }
                    return result;
                }

                static void Main()
                {
                    Console.WriteLine(Total(new[] { 3, 4, 5 }));
                }
            }
            """);

        var report = await CheckAsync(program, codeOnly: true);

        RemovedByTheLoopsFix(report, "logic-off-by-one-length", 8, 10);
    }

    [Fact]
    public async Task InGoTheLoopsFixRemovesThePositionItAskedFor()
    {
        var program = Write(@"prices\main.go", """
            package main

            import "fmt"

            func total(prices []int) int {
            	result := 0
            	for index := 0; index <= len(prices); index++ {
            		result += prices[index]
            	}
            	return result
            }

            func main() {
            	fmt.Println(total([]int{3, 4, 5}))
            }
            """);
        if (GoFrontend.FindGo(program) is null) return;

        var report = await CheckAsync(program, codeOnly: true);

        RemovedByTheLoopsFix(report, "logic-go-loop-to-length", 7, 8);
    }

    /// <summary>
    /// A binary search whose low bound is set to the middle, not past it, can come round with nothing changed: its fix,
    /// mid + 1, removes the loop the analyses found could get stuck, on the while line above it - read alone, and when it ran.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InPythonTheBinarySearchsFixRemovesTheLoopThatCouldGetStuck(bool codeOnly)
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var program = Write(@"search\search.py", """
            def find(items, target):
                low = 0
                high = len(items) - 1
                while low <= high:
                    mid = (low + high) // 2
                    if items[mid] == target:
                        return mid
                    elif items[mid] < target:
                        low = mid
                    else:
                        high = mid - 1
                return -1


            print(find([2, 4, 6, 8], 6))
            """);

        var report = await CheckAsync(program, codeOnly);

        var root = Assert.Single(report.Findings, finding => finding.RuleId == "logic-python-binary-search");
        Assert.Equal(9, root.Line);
        Assert.Equal(["            low = mid + 1"], root.Fix!.NewLines);
        var stuck = Assert.Single(report.Findings, finding => finding.RuleId == "analysis-loop-can-get-stuck");
        Assert.Equal(4, stuck.Line);
        Assert.Equal(new Relation(root.Id, RelationKind.RemovedByItsFix, Confidence.Certain), stuck.CausedBy);
        Assert.Equal("Its fix removes the problem on line 4 too: with the fix made, in a copy, the file was read again and that problem was no longer found.",
                     FindingText.Explains(FindingLinks.Of(report.Findings).LeadsTo(root)));
    }
}
