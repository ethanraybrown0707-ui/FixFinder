using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

public class ProgramCheckerTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Write(string name, string text)
    {
        var path = Path.Combine(_temp.Path, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text.ReplaceLineEndings("\n"));
        return path;
    }

    private async Task<CheckReport> CheckAsync(string file, CodeLanguage language, ExpectedBehaviour? expected = null, string? input = null)
    {
        using var http = new FixFinderHttpClient();
        var launch = TargetFactory.FromFile(file);
        Assert.True(launch.Ok, launch.Problem);

        if (input is not null) launch = launch with { Spec = launch.Spec!.WithInput(input) };

        var checker = new ProgramChecker(http, new FixSourceRegistry()) { Language = language, Expected = expected };
        var report = await checker.CheckAsync(launch);

        output.WriteLine($"syntax: {report.SyntaxSummary}");
        output.WriteLine($"logic:  {report.LogicSummary}");
        foreach (var note in report.Notes) output.WriteLine($"note: {note}");

        foreach (var finding in report.Findings)
        {
            output.WriteLine("");
            output.WriteLine($"[{finding.Severity}/{finding.Confidence}/{finding.Kind}] {finding.Location}: {finding.Title}");
            output.WriteLine($"  explanation: {finding.Explanation}");
            output.WriteLine($"  why:         {finding.WhyItMatters}");
            output.WriteLine($"  fix:         {finding.SuggestedFix}");
            output.WriteLine($"  checked:     {finding.FixCheckedBy}");
            output.WriteLine($"  example ({(finding.ExampleIsFromYourCode ? "yours" : "general")}):");
            foreach (var line in finding.CorrectedExample.Split('\n')) output.WriteLine($"      {line}");
        }

        return report;
    }

    [Fact]
    public async Task PythonSyntaxErrorIsReportedWithAFix()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var file = Write("grades.py", """
            scores = [70, 45, 90]
            for score in scores
                print(score)
            """);

        var report = await CheckAsync(file, CodeLanguage.Python);

        var error = Assert.Single(report.Findings, f => f.Severity == Severity.Error);
        Assert.Equal(FindingKind.Syntax, error.Kind);
        Assert.Equal(2, error.Line);
        Assert.True(error.ExampleIsFromYourCode);
        Assert.Contains("for score in scores:", error.CorrectedExample);
    }

    [Fact]
    public async Task PythonLogicMistakesAreFoundAlongsideTheRun()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var file = Write("totals.py", """
            def add(item, items=[]):
                items.append(item)
                return items

            prices = [3, 4, 5]
            for price in prices:
                total = 0
                total += price
            print(total)
            name = "sam"
            name.upper()
            if len(prices) is 3:
                print("three")
            """);

        var report = await CheckAsync(file, CodeLanguage.Python);

        Assert.Contains(report.Findings, f => f.RuleId == "logic-python-mutable-default");
        Assert.Contains(report.Findings, f => f.RuleId == "logic-python-reset-in-loop");
        Assert.Contains(report.Findings, f => f.RuleId == "logic-python-result-discarded");
        Assert.Single(report.Findings, f => f.Line == 12);
    }

    [Fact]
    public async Task JavaErrorsAndWarningsAreAllReported()
    {
        if (!LocalFixLiveTests.Available("java")) return;

        var file = Write("Main.java", """
            public class Main {
                public static void main(String[] args) {
                    int count = 3
                    String name = "sam";
                    System.out.println(nme);
                }
            }
            """);

        var report = await CheckAsync(file, CodeLanguage.Java);

        Assert.True(report.Findings.Count(f => f.Severity == Severity.Error) >= 1);
    }

    [Fact]
    public async Task JavaLintWarningsBecomeLogicFindings()
    {
        if (!LocalFixLiveTests.Available("java")) return;

        var file = Write("Menu.java", """
            public class Menu {
                public static void main(String[] args) {
                    int choice = 1;
                    switch (choice) {
                        case 1:
                            System.out.println("one");
                        case 2:
                            System.out.println("two");
                            break;
                    }
                    String answer = new String("yes");
                    if (answer == "yes") {
                        System.out.println("agreed");
                    }
                }
            }
            """);

        var report = await CheckAsync(file, CodeLanguage.Java);

        Assert.Contains(report.Findings, f => f.Kind == FindingKind.Logic && f.Line == 7);
        Assert.Contains(report.Findings, f => f.RuleId == "logic-java-string-equals");
    }

    [Fact]
    public async Task CSharpWarningsAndCrashAreReported()
    {
        if (!LocalFixLiveTests.Available("dotnet")) return;

        var file = Write(Path.Combine("cs", "Program.cs"), """
            int unused;
            int[] scores = { 1, 2, 3 };
            for (var i = 0; i <= scores.Length; i++)
            {
                Console.WriteLine(scores[i]);
            }
            """);

        var report = await CheckAsync(file, CodeLanguage.CSharp);

        Assert.Contains(report.Findings, f => f.Kind == FindingKind.Runtime);
    }

    [Fact]
    public async Task WrongOutputIsCheckedOnceItBuilds()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var file = Write("count.py", """
            n = int(input())
            for i in range(1, n):
                print(i)
            """);

        var expected = ExpectedBehaviour.From([new ExpectedRun("3", "1\n2\n3\n")], null);
        var report = await CheckAsync(file, CodeLanguage.Python, expected, input: "3");

        var wrong = Assert.Single(report.Findings, f => f.RuleId == "wrong-output");
        Assert.NotNull(wrong.Fix);
    }
}
