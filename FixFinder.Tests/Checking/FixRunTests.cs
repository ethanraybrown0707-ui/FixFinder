using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Covers trying a fix for real on a copy of the program: a program in a compiled language is built before its copy is
/// run, and a copy that never ran - or crashed - is never taken as a fix that works.
/// </summary>
public class FixRunTests(ITestOutputHelper output) : IDisposable
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

    private async Task<CheckReport> CheckAsync(string file, CodeLanguage language)
    {
        var launch = TargetFactory.FromFile(file);
        Assert.True(launch.Ok, launch.Problem);

        using var http = new FixFinderHttpClient();
        var report = await new ProgramChecker(http, new FixSourceRegistry()) { Language = language }.CheckAsync(launch);

        foreach (var finding in report.Findings)
            output.WriteLine($"{finding.RuleId}: {finding.Title} | {finding.Verified.Summary}: {string.Join(" / ", finding.Verified.Steps.Select(step => step.Detail))}");

        return report;
    }

    [Fact]
    public async Task AFixToACProgramIsTriedOnACopyThatIsBuiltAndRun()
    {
        if (!LocalFixLiveTests.Available("c")) return;

        // The semicolon put back, the copy builds - and then divides by zero. A copy that was never built could not show that,
        // and was once taken as a copy that ran without failing.
        var program = Write(@"marks\average.c", """
            #include <stdio.h>

            int main(void) {
                int total = 5
                printf("%d\n", 10 / (total - 5));
                return 0;
            }
            """);

        var report = await CheckAsync(program, CodeLanguage.C);

        var semicolon = Assert.Single(report.Findings, finding => finding.RuleId == "c-missing-semicolon");
        Assert.False(semicolon.Verified.IsVerified);
        Assert.Equal(StageResult.Failed, semicolon.Verified.ResultOf(VerificationStage.Ran));
    }

    [Fact]
    public async Task ACopyThatCouldNotBeStartedIsNeverTakenAsAPass()
    {
        // A program that is not there cannot be started: Windows refusing a fresh build, say, ends the same way.
        var missing = Path.Combine(_temp.Path, "never-built.exe");
        var plan = new LaunchPlan(new TargetSpec { ExecutablePath = missing, Arguments = "", WorkingDirectory = _temp.Path }, null, "Running it.");

        var verified = await FixRun.JudgeAsync(Verification.NotTested, plan, original: null, expected: null, CancellationToken.None);

        Assert.False(verified.IsVerified);
        Assert.Equal(StageResult.Skipped, verified.ResultOf(VerificationStage.Ran));
    }

    [Fact]
    public async Task AFixToAJavaProgramIsVerifiedByBuildingAndRunningItsCopy()
    {
        if (!LocalFixLiveTests.Available("java")) return;

        var program = Write(@"marks\Total.java", """
            public class Total {
                public static void main(String[] args) {
                    int total = 5
                    System.out.println(total);
                }
            }
            """);

        var report = await CheckAsync(program, CodeLanguage.Java);

        var semicolon = Assert.Single(report.Findings, finding => finding.RuleId == "java-missing-semicolon");
        Assert.True(semicolon.Verified.IsVerified, string.Join(" / ", semicolon.Verified.Steps.Select(step => step.Detail)));
        Assert.Contains("The copy built, and ran without stopping with an error.", semicolon.Verified.Steps.Select(step => step.Detail));
    }
}
