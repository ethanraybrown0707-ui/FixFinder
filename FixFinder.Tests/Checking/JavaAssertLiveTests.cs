using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// A Java program's own assert statements are checked: java checks none unless started with -ea, so a program whose assert
/// is false would carry on with the wrong value. FixFinder starts a program that has asserts with -ea, and says so.
/// </summary>
public class JavaAssertLiveTests(ITestOutputHelper output) : IDisposable
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

    private const string Marks = """
        public class Marks {
            static int total(int[] marks) {
                int sum = 0;
                for (int i = 1; i < marks.length; i++) {
                    sum += marks[i];
                }
                return sum;
            }

            public static void main(String[] args) {
                int[] marks = {60, 70, 80};
                assert total(marks) == 210 : "the total of 60, 70 and 80";
                System.out.println("Total: " + total(marks));
            }
        }
        """;

    [Fact]
    public async Task AFalseAssertInAJavaProgramIsCheckedAndSaid()
    {
        if (!LocalFixLiveTests.Available("java")) return;

        var program = Write(@"marks\Marks.java", Marks);
        var launch = TargetFactory.FromFile(program);
        Assert.True(launch.Ok, launch.Problem);
        output.WriteLine($"how: {launch.Explanation}");
        Assert.Contains("running it with java -ea, so its assert statements are checked", launch.Explanation, StringComparison.Ordinal);

        using var http = new FixFinderHttpClient();
        var checker = new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Java };
        checker.Log += message => output.WriteLine($"log: {message}");
        var report = await checker.CheckAsync(launch);
        foreach (var finding in report.Findings) output.WriteLine($"[{finding.RuleId}] {finding.Line}: {finding.Title} :: {finding.Explanation}");

        Assert.Equal("It builds, but it stops with an error when run", report.SyntaxSummary);
        var stopped = Assert.Single(report.Findings, finding => finding.Line == 12);
        Assert.Contains("AssertionError", stopped.Title, StringComparison.Ordinal);
        Assert.Contains("the total of 60, 70 and 80", stopped.Title, StringComparison.Ordinal);
        Assert.Contains("Java checks assert statements only when it is started with -ea", stopped.Explanation, StringComparison.Ordinal);
    }

    /// <summary>A program with no assert statements is started as plain java starts it.</summary>
    [Fact]
    public void AProgramWithoutAssertsIsStartedWithoutEa()
    {
        if (!LocalFixLiveTests.Available("java")) return;

        var program = Write(@"hello\Hello.java", "public class Hello {\n    public static void main(String[] args) {\n        // assert nothing here\n        System.out.println(\"assert\");\n    }\n}\n");
        var launch = TargetFactory.FromFile(program);

        Assert.True(launch.Ok, launch.Problem);
        Assert.DoesNotContain("-ea", launch.Spec!.Arguments, StringComparison.Ordinal);
        Assert.Contains("running it with java", launch.Explanation, StringComparison.Ordinal);
    }
}
