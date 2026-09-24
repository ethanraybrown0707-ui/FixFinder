using System.Diagnostics;
using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>
/// Times a real check, so where the time goes is measured rather than assumed. Temporary: driven by FIXFINDER_TIME
/// and does nothing without it.
/// </summary>
public class ZzTiming
{
    [Fact]
    public async Task WhereTheTimeGoes()
    {
        if (Environment.GetEnvironmentVariable("FIXFINDER_TIME") is not { Length: > 0 } file) return;

        var language = Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".py" => CodeLanguage.Python,
            ".java" => CodeLanguage.Java,
            ".cs" => CodeLanguage.CSharp,
            ".c" => CodeLanguage.C,
            ".go" => CodeLanguage.Go,
            ".js" => CodeLanguage.JavaScript,
            _ => CodeLanguage.Any,
        };

        using var http = new FixFinderHttpClient();

        var launch = TargetFactory.FromFile(file);
        Assert.True(launch.Ok, launch.Problem);

        var checker = new ProgramChecker(http, new FixSourceRegistry()) { Language = language };

        var whole = Stopwatch.StartNew();
        var report = await checker.CheckAsync(launch);
        whole.Stop();

        var lines = File.ReadAllLines(file).Length;

        var summary =
            $"{Path.GetFileName(file)}\t{lines} lines\ttotal {whole.ElapsedMilliseconds} ms\tfindings {report.Findings.Count}";

        File.AppendAllText(
            Environment.GetEnvironmentVariable("FIXFINDER_TIME_REPORT") ?? Path.Combine(Path.GetTempPath(), "timing.txt"),
            summary + Environment.NewLine);
    }
}
