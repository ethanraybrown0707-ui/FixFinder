using FixFinder.Core.Execution;

namespace FixFinder.Tests;

/// <summary>
/// Covers the rule the whole tool leans on: a parsed stack trace decides whether something
/// crashed, and the exit code only corroborates. Both halves matter, because exit codes lie
/// in both directions.
/// </summary>
public class RunClassifierTests
{
    [Fact]
    public void ExitZeroWithNoError_IsClean()
    {
        var (outcome, explanation) = RunClassifier.Classify(0, hasParsedError: false);

        Assert.Equal(RunOutcome.ExitedClean, outcome);
        Assert.Contains("nothing to search for", explanation, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Test runners and plenty of frameworks print a full traceback and then exit 0. The
    /// outcome must not be "clean" just because the process was polite on the way out.
    /// </summary>
    [Fact]
    public void ExitZeroWithAParsedError_IsNotTreatedAsClean()
    {
        var (outcome, explanation) = RunClassifier.Classify(0, hasParsedError: true);

        Assert.NotEqual(RunOutcome.ExitedClean, outcome);
        Assert.Contains("still exited successfully", explanation);
    }

    [Fact]
    public void NonZeroWithAParsedError_IsACrash()
    {
        var (outcome, _) = RunClassifier.Classify(1, hasParsedError: true);

        Assert.Equal(RunOutcome.Crashed, outcome);
    }

    /// <summary>
    /// The inverse, and the more important half: a non-zero exit on its own is usually a
    /// handled failure - a bad argument, a missing file the program reported properly - and
    /// calling it a crash would send FixFinder hunting for a fix that does not exist.
    /// </summary>
    [Fact]
    public void NonZeroWithNoParsedError_IsNotACrash()
    {
        var (outcome, explanation) = RunClassifier.Classify(1, hasParsedError: false);

        Assert.Equal(RunOutcome.ExitedNonZero, outcome);
        Assert.Contains("not a crash", explanation);
    }

    [Theory]
    // Verified against the real thing: an unhandled managed exception exits with 0xE0434352,
    // which arrives through Process.ExitCode as this negative int.
    [InlineData(unchecked((int)0xE0434352))]
    [InlineData(unchecked((int)0xC0000005))]
    [InlineData(unchecked((int)0xC0000409))]
    [InlineData(139)]
    [InlineData(134)]
    public void KnownFatalExitCodes_AreCrashesEvenWithNothingParsed(int exitCode)
    {
        var (outcome, _) = RunClassifier.Classify(exitCode, hasParsedError: false);

        Assert.Equal(RunOutcome.Crashed, outcome);
    }

    /// <summary>
    /// Exit code 2 is deliberately NOT in the fatal table, even though a Go panic uses it.
    /// Almost every command-line program exits 2 for a usage error, and this table is only
    /// consulted when there is no trace to confirm what happened - so including it would
    /// relabel a mistyped argument as a crash. A real Go panic is caught by its parser
    /// recognising "panic:" instead.
    /// </summary>
    [Fact]
    public void ExitCodeTwo_IsNotTreatedAsACrashOnItsOwn()
    {
        var (outcome, _) = RunClassifier.Classify(2, hasParsedError: false);

        Assert.Equal(RunOutcome.ExitedNonZero, outcome);
    }
}
