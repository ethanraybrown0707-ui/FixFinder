using FixFinder.Core.Execution;

namespace FixFinder.Tests;

/// <summary>Integration coverage for the process supervisor.</summary>
public class TargetRunnerTests
{
    private static bool OnWindows => OperatingSystem.IsWindows();

    private static TargetSpec Cmd(string arguments, int timeoutSeconds = 30) => new()
    {
        ExecutablePath = Environment.GetEnvironmentVariable("ComSpec") ?? @"C:\Windows\System32\cmd.exe",
        Arguments = arguments,
        WorkingDirectory = Path.GetTempPath(),
        Timeout = TimeSpan.FromSeconds(timeoutSeconds),
    };

    [Fact]
    public async Task CapturesStdOutAndStdErrSeparately()
    {
        if (!OnWindows) return;

        var result = await new TargetRunner().RunAsync(
            Cmd("/c echo hello-out & echo hello-err 1>&2"), CancellationToken.None);

        Assert.Equal(RunOutcome.ExitedClean, result.Outcome);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains(result.Lines, l => l.Stream == StreamKind.StdOut && l.Text.Contains("hello-out"));
        Assert.Contains(result.Lines, l => l.Stream == StreamKind.StdErr && l.Text.Contains("hello-err"));
    }

    [Fact]
    public async Task DoesNotDeadlockOnLargeOutputAcrossBothStreams()
    {
        if (!OnWindows) return;

        var result = await new TargetRunner().RunAsync(
            Cmd("/c for /l %i in (1,1,2000) do @(echo out-%i & echo err-%i 1>&2)", timeoutSeconds: 120),
            CancellationToken.None);

        Assert.Equal(RunOutcome.ExitedClean, result.Outcome);
        Assert.Equal(2000, result.Lines.Count(l => l.Stream == StreamKind.StdOut));
        Assert.Equal(2000, result.Lines.Count(l => l.Stream == StreamKind.StdErr));
    }

    [Fact]
    public async Task AssignsAContiguousSequenceAcrossBothStreams()
    {
        if (!OnWindows) return;

        var result = await new TargetRunner().RunAsync(
            Cmd("/c for /l %i in (1,1,200) do @(echo out-%i & echo err-%i 1>&2)", timeoutSeconds: 60),
            CancellationToken.None);

        var sequences = result.Lines.Select(l => l.Sequence).ToArray();

        Assert.Equal(sequences.Length, sequences.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, sequences.Length), sequences);
    }

    [Fact]
    public async Task ClosesStdInSoAProgramWaitingForInputDoesNotHang()
    {
        if (!OnWindows) return;

        var result = await new TargetRunner().RunAsync(
            Cmd("/c set /p LINE= & echo done", timeoutSeconds: 20), CancellationToken.None);

        Assert.NotEqual(RunOutcome.TimedOut, result.Outcome);
        Assert.True(result.Duration < TimeSpan.FromSeconds(10),
            $"took {result.Duration.TotalSeconds:0.#}s - stdin was probably left open");
    }

    [Fact]
    public async Task KillsAProgramThatOutlivesTheTimeout()
    {
        if (!OnWindows) return;

        var spec = new TargetSpec
        {
            ExecutablePath = "powershell",
            Arguments = "-NoProfile -Command \"Start-Sleep -Seconds 120\"",
            WorkingDirectory = Path.GetTempPath(),
            Timeout = TimeSpan.FromSeconds(3),
        };

        var result = await new TargetRunner().RunAsync(spec, CancellationToken.None);

        Assert.Equal(RunOutcome.TimedOut, result.Outcome);
        Assert.Null(result.ExitCode);
        Assert.True(result.Duration < TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task DistinguishesUserCancellationFromATimeout()
    {
        if (!OnWindows) return;

        var spec = new TargetSpec
        {
            ExecutablePath = "powershell",
            Arguments = "-NoProfile -Command \"Start-Sleep -Seconds 120\"",
            WorkingDirectory = Path.GetTempPath(),
            Timeout = TimeSpan.FromMinutes(5),
        };

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var result = await new TargetRunner().RunAsync(spec, cancellation.Token);

        Assert.Equal(RunOutcome.Cancelled, result.Outcome);
    }

    [Fact]
    public async Task ReportsLaunchFailureInsteadOfThrowing()
    {
        var spec = new TargetSpec
        {
            ExecutablePath = Path.Combine(Path.GetTempPath(), "fixfinder-no-such-program.exe"),
            WorkingDirectory = Path.GetTempPath(),
        };

        var result = await new TargetRunner().RunAsync(spec, CancellationToken.None);

        Assert.Equal(RunOutcome.LaunchFailed, result.Outcome);
        Assert.NotNull(result.LaunchError);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public async Task PassesExtraEnvironmentVariablesToTheTarget()
    {
        if (!OnWindows) return;

        var spec = new TargetSpec
        {
            ExecutablePath = Environment.GetEnvironmentVariable("ComSpec") ?? @"C:\Windows\System32\cmd.exe",
            Arguments = "/c echo [%FIXFINDER_TEST_VAR%]",
            WorkingDirectory = Path.GetTempPath(),
            ExtraEnvironment = new Dictionary<string, string> { ["FIXFINDER_TEST_VAR"] = "carried-through" },
        };

        var result = await new TargetRunner().RunAsync(spec, CancellationToken.None);

        Assert.Contains(result.Lines, l => l.Text.Contains("[carried-through]"));
    }

    [Fact]
    public async Task RaisesLineCapturedLiveRatherThanOnlyAtTheEnd()
    {
        if (!OnWindows) return;

        var runner = new TargetRunner();
        var seen = new List<string>();
        runner.LineCaptured += line => { lock (seen) seen.Add(line.Text); };

        var result = await runner.RunAsync(Cmd("/c echo one & echo two & echo three"), CancellationToken.None);

        Assert.Equal(result.Lines.Count, seen.Count);
        Assert.Contains(seen, t => t.Contains("two"));
    }
}
