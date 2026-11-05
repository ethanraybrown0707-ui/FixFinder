using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Parsing;
using FixFinder.Core.Reporting;

namespace FixFinder.Tests;

/// <summary>
/// A finding in the order it is read - what happened, its cause in this program, the most likely fix and how sure FixFinder
/// is of it, why it thinks so, and then what the mistake is in general: this program first, never the language first.
/// </summary>
public class FindingLayoutTests
{
    private const string Guide = "Square brackets pick one item out of a list, a string or a dictionary.";

    private static Finding Stopped(string? found = null, string explanation = Guide) => new()
    {
        Kind = FindingKind.Runtime,
        Severity = Severity.Error,
        Confidence = Confidence.Certain,
        File = @"C:\work\members.py",
        Line = 10,
        Title = "It crashed: TypeError: 'NoneType' object is not subscriptable",
        Explanation = explanation,
        Found = found,
        WhyItMatters = "The line crashes every time it runs.",
        SuggestedFix = "Index the list or string itself.",
        CorrectedExample = "",
        RuleId = "TypeError",
        Error = new ParsedError
        {
            LanguageId = "python",
            Confidence = 90,
            FirstLineSequence = 0,
            RawText = "",
            ExceptionType = "TypeError",
            Message = "'NoneType' object is not subscriptable",
            Frames = [],
        },
    };

    private static RootCause Traced => new()
    {
        Name = "member",
        Value = "None",
        CrashLine = 10,
        IntroducedLine = 9,
        IntroducedCode = "member = find_member(members, 2)",
        Steps = ["The program stopped on line 10.", "`member` became None on line 9."],
        Evidence = ["`member` held None when line 10 ran.", "Line 9 was the last line to change `member` before line 10."],
        LinesInvolved = [9],
        WhatToDo = "Decide what `find_member` should give back on line 5.",
    };

    private static LocalFix Fix(bool onlyStopsTheFailure = false) => new()
    {
        RuleId = "python-none-guard",
        Title = "Run line 10 only when member is not None",
        Explanation = "",
        File = @"C:\work\members.py",
        StartLine = 10,
        RemoveCount = 1,
        NewLines = ["if member is not None:", "    print(member[\"age\"])"],
        OnlyStopsTheFailure = onlyStopsTheFailure,
    };

    [Fact]
    public void TheCauseIsWhatHappenedInThisProgramNotWhatTheErrorMeans()
    {
        Assert.Equal("The program stopped on line 10 with TypeError: 'NoneType' object is not subscriptable.", FindingLayout.Cause(Stopped()));

        // What it means in general is still there, after.
        Assert.Equal(Guide, FindingLayout.LearnMore(Stopped()));
    }

    [Fact]
    public void WhatWasFoundInThisCodeComesFirstAndIsNotSaidTwice()
    {
        const string found = "`count` can be 0 here.";
        var finding = Stopped(found, $"{found} {Guide}");

        Assert.Equal(found, FindingLayout.Cause(finding));
        Assert.Equal(Guide, FindingLayout.LearnMore(finding));
    }

    [Fact]
    public void AnExplanationThatIsAllAboutThisProgramIsNotRepeatedUnderLearnMore()
    {
        var finding = Stopped() with { Error = null, Explanation = "The program printed 7 where you said 8." };

        Assert.Equal("The program printed 7 where you said 8.", FindingLayout.Cause(finding));
        Assert.Equal("", FindingLayout.LearnMore(finding));
    }

    [Fact]
    public void ACompilerErrorSaysTheCompilerRefusedTheLine()
    {
        var finding = Stopped() with
        {
            Kind = FindingKind.Syntax,
            Line = 7,
            Error = new ParsedError { LanguageId = "java", Confidence = 90, FirstLineSequence = 0, RawText = "", Message = "';' expected", Frames = [] },
        };

        Assert.Equal("The compiler refused the code on line 7: ';' expected.", FindingLayout.Cause(finding));
    }

    [Fact]
    public void WhenTheOnlyChangeStopsTheFailureTheMostLikelyFixIsWhatTheTraceSays()
    {
        var traced = Stopped() with { RootCause = Traced, Fix = Fix(onlyStopsTheFailure: true), SuggestedFix = "Run line 10 only when member is not None." };

        Assert.Equal(Traced.WhatToDo, FindingLayout.MostLikelyFix(traced));
        Assert.Equal(Traced.WhatToDo, FindingLayout.MostLikelyFix(traced with { Fix = null }));

        // A rule's own fix for the mistake itself says it best.
        var mended = traced with { Fix = Fix(), SuggestedFix = "Return result from average." };
        Assert.Equal("Return result from average.", FindingLayout.MostLikelyFix(mended));
    }

    [Fact]
    public void TheSummaryIsWhereItStoppedAndWhereItWentWrong()
    {
        Assert.Equal("Crash on line 10. Root cause introduced on line 9.", FindingLayout.Summary(Stopped() with { RootCause = Traced }));
        Assert.Null(FindingLayout.Summary(Stopped()));
    }

    [Fact]
    public void FixConfidenceIsReadOffHowFarTheChangeWasTaken()
    {
        var ran = Verification.NotTested.With(VerificationStage.Ran, StageResult.Passed, "The copy ran.");

        Assert.Null(FindingLayout.FixConfidence(Stopped()));
        Assert.Equal(Confidence.Possible, FindingLayout.FixConfidence(Stopped() with { Fix = Fix() })!.Level);
        Assert.Equal("the change was not tried", FindingLayout.FixConfidence(Stopped() with { Fix = Fix() })!.Because);

        var likely = FindingLayout.FixConfidence(Stopped() with { Fix = Fix(), Verified = ran })!;
        Assert.Equal(Confidence.Likely, likely.Level);
        Assert.Equal("a copy with the change ran without the error", likely.Because);
        Assert.Equal("Likely. A copy with the change ran without the error.", likely.Labelled);

        var printed = ran.With(VerificationStage.MatchedExpectedOutput, StageResult.Passed, "It printed what you said it should.");
        Assert.Equal(Confidence.Certain, FindingLayout.FixConfidence(Stopped() with { Fix = Fix(), Verified = printed })!.Level);

        var built = Verification.NotTested.With(VerificationStage.Compiled, StageResult.Passed, "It built.");
        Assert.Equal("a copy with the change builds, but was not run", FindingLayout.FixConfidence(Stopped() with { Fix = Fix(), Verified = built })!.Because);
    }

    [Fact]
    public void AChangeThatOnlyStopsTheFailureIsNeverMoreThanPossible()
    {
        var ran = Verification.NotTested.With(VerificationStage.Ran, StageResult.Passed, "The copy ran.");
        var certainty = FindingLayout.FixConfidence(Stopped() with { Fix = Fix(onlyStopsTheFailure: true), Verified = ran })!;

        Assert.Equal(Confidence.Possible, certainty.Level);
        Assert.Equal("a copy with the change ran without the error, but the change only stops the failure: what should happen instead is yours to decide",
            certainty.Because);
    }

    [Fact]
    public void WhyItThinksSoIsWhatTheRunRecordedThenHowTheFixWent()
    {
        var ran = Verification.NotTested.With(VerificationStage.Ran, StageResult.Passed, "The copy ran and did not stop with TypeError the way it did before.");
        var reasons = FindingLayout.WhyItThinksThis(Stopped() with { RootCause = Traced, Fix = Fix(true), Verified = ran });

        Assert.Equal([.. Traced.Evidence, "The copy ran and did not stop with TypeError the way it did before."], reasons.Select(reason => reason.Text));
        Assert.All(reasons, reason => Assert.Equal(StageResult.Passed, reason.Mark));

        // With nothing traced, what stands behind the finding.
        Assert.Equal([Evidence.For(Stopped())], FindingLayout.WhyItThinksThis(Stopped()).Select(reason => reason.Text));
    }

    [Fact]
    public void AnEditorsLineSaysWhatHappenedInThisProgram()
    {
        var line = DiagnosticLines.Line(Stopped(), DiagnosticFormat.Gcc);

        Assert.Contains("The program stopped on line 10 with TypeError: 'NoneType' object is not subscriptable.", line, StringComparison.Ordinal);
        Assert.DoesNotContain("Square brackets", line, StringComparison.Ordinal);
    }
}
