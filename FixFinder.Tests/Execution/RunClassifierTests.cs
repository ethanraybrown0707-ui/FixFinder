using FixFinder.Core.Execution;

namespace FixFinder.Tests;

/// <summary>Covers the rule the whole tool leans on: a parsed stack trace decides whether something crashed, and the exit code
/// only corroborates.</summary>
public class RunClassifierTests
{
    [Fact]
    public void ExitZeroWithNoError_IsClean()
    {
        var (outcome, explanation) = RunClassifier.Classify(0, hasParsedError: false);

        Assert.Equal(RunOutcome.ExitedClean, outcome);
        Assert.Contains("nothing to search for", explanation, StringComparison.OrdinalIgnoreCase);
    }

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

    [Fact]
    public void NonZeroWithNoParsedError_IsNotACrash()
    {
        var (outcome, explanation) = RunClassifier.Classify(1, hasParsedError: false);

        Assert.Equal(RunOutcome.ExitedNonZero, outcome);
        Assert.Contains("not a crash", explanation);
    }

    [Theory]
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

    [Fact]
    public void ExitCodeTwo_IsNotTreatedAsACrashOnItsOwn()
    {
        var (outcome, _) = RunClassifier.Classify(2, hasParsedError: false);

        Assert.Equal(RunOutcome.ExitedNonZero, outcome);
    }
}
