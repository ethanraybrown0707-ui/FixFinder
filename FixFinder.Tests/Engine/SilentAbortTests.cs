using FixFinder.Core.Engine;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>A C or C++ program that ends through abort() without printing why is run again to find out.</summary>
public class SilentAbortTests
{
    private static TargetRunResult Exited(int code) => new()
    {
        Outcome = RunOutcome.ExitedNonZero,
        ExitCode = code,
        Lines = [],
        Duration = TimeSpan.FromMilliseconds(40),
        Explanation = $"Exited {code}, but nothing in the output parsed as an error.",
    };

    private static async Task<int> RerunsAfter(int exitCode)
    {
        using var folder = new TempFolder();
        using var http = new FixFinderHttpClient(new HttpCache(folder.Path));
        var session = new FixFinderSession(http, new FixSourceRegistry());
        var spec = new TargetSpec { ExecutablePath = Path.Combine(folder.Path, "app.exe"), WorkingDirectory = folder.Path };
        var reruns = 0;

        await session.ContinueFromAsync(Exited(exitCode), spec, rerunWithSanitizer: _ =>
        {
            reruns++;
            return Task.FromResult<TargetRunResult?>(null);
        });

        return reruns;
    }

    [Fact]
    public async Task ExitingWithAbortsCodeAndNoWordIsCheckedAgain() => Assert.Equal(1, await RerunsAfter(3));

    [Fact]
    public async Task AnOrdinaryFailureCodeIsLeftAsAHandledFailure() => Assert.Equal(0, await RerunsAfter(1));
}
