using FixFinder.Core.Checking;

namespace FixFinder.Tests;

/// <summary>
/// What FixFinder is allowed to claim about a fix it tested. The rule the whole thing exists to enforce is that
/// compiling is not working: a fix is verified once the program has been run and the failure has stopped happening.
/// </summary>
public class VerificationTests
{
    [Fact]
    public void AFixNobodyTestedClaimsNothing()
    {
        Assert.False(Verification.NotTested.WasTested);
        Assert.False(Verification.NotTested.IsVerified);
        Assert.Equal("Not tested", Verification.NotTested.Summary);
    }

    [Fact]
    public void CompilingIsNotWorking()
    {
        var compiled = Verification.NotTested
            .With(VerificationStage.Compiled, StageResult.Passed, "A copy with the change compiles.");

        Assert.True(compiled.WasTested);
        Assert.False(compiled.IsVerified);
        Assert.Equal("Partly checked", compiled.Summary);
    }

    [Fact]
    public void RunningWithoutTheFailureIsWorking()
    {
        var ran = Verification.NotTested
            .With(VerificationStage.Compiled, StageResult.Passed, "A copy with the change compiles.")
            .With(VerificationStage.Ran, StageResult.Passed, "The copy ran and did not fail the way it did before.");

        Assert.True(ran.IsVerified);
        Assert.Equal("Fix verified", ran.Summary);
    }

    [Fact]
    public void CompilingAndThenFailingToRunIsNotVerified()
    {
        var broken = Verification.NotTested
            .With(VerificationStage.Compiled, StageResult.Passed, "A copy with the change compiles.")
            .With(VerificationStage.Ran, StageResult.Failed, "The copy still stopped with the same error.");

        Assert.False(broken.IsVerified);
        Assert.True(broken.SomethingFailed);
        Assert.Equal("Not verified", broken.Summary);
    }

    [Fact]
    public void RunningButPrintingTheWrongThingIsNotVerified()
    {
        var wrong = Verification.NotTested
            .With(VerificationStage.Compiled, StageResult.Passed, "A copy with the change compiles.")
            .With(VerificationStage.Ran, StageResult.Passed, "The copy ran and did not fail the way it did before.")
            .With(VerificationStage.MatchedExpectedOutput, StageResult.Failed, "It printed 40 where you said 30.");

        Assert.False(wrong.IsVerified);
        Assert.Equal("Not verified", wrong.Summary);
    }

    [Fact]
    public void MatchingWhatWasAskedForIsTheWholeThing()
    {
        var matched = Verification.NotTested
            .With(VerificationStage.Compiled, StageResult.Passed, "A copy with the change compiles.")
            .With(VerificationStage.Ran, StageResult.Passed, "The copy ran and did not fail the way it did before.")
            .With(VerificationStage.MatchedExpectedOutput, StageResult.Passed, "It printed what you said it should.");

        Assert.True(matched.IsVerified);
        Assert.Equal("Fix verified", matched.Summary);
    }

    [Fact]
    public void NoExpectedOutputToCheckAgainstDoesNotCountAgainstTheFix()
    {
        var skipped = Verification.NotTested
            .With(VerificationStage.Ran, StageResult.Passed, "The copy ran and did not fail the way it did before.")
            .With(VerificationStage.MatchedExpectedOutput, StageResult.Skipped, "You did not say what it should print.");

        Assert.True(skipped.IsVerified);
    }

    [Fact]
    public void AnAnswerThatCannotBeTrustedIsNotTakenAsAPass()
    {
        var timedOut = Verification.NotTested
            .With(VerificationStage.Compiled, StageResult.Passed, "A copy with the change compiles.")
            .With(VerificationStage.Ran, StageResult.Inconclusive, "The copy was still going after 60 seconds.");

        Assert.False(timedOut.IsVerified);
        Assert.False(timedOut.SomethingFailed);
        Assert.Equal("Partly checked", timedOut.Summary);
    }

    [Fact]
    public void AStageRunAgainReplacesWhatItSaidBefore()
    {
        var twice = Verification.NotTested
            .With(VerificationStage.Ran, StageResult.Failed, "first go")
            .With(VerificationStage.Ran, StageResult.Passed, "second go");

        Assert.Equal(StageResult.Passed, twice.ResultOf(VerificationStage.Ran));
        Assert.Single(twice.Steps);
    }

    [Fact]
    public void AFindingCarriesNoClaimUntilOneIsMade()
    {
        var finding = new Finding
        {
            Kind = FindingKind.Runtime,
            Severity = Severity.Error,
            Confidence = Confidence.Certain,
            File = @"C:\work\thing.py",
            Title = "Title",
            Explanation = "what is wrong",
            WhyItMatters = "why",
            SuggestedFix = "fix",
            CorrectedExample = "",
        };

        Assert.False(finding.Verified.WasTested);
        Assert.False(finding.Verified.IsVerified);
    }
}
