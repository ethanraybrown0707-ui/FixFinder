using FixFinder.Core.Checking;
using FixFinder.Core.Parsing;

namespace FixFinder.Tests;

/// <summary>
/// What stands behind a finding's confidence. A compiler refusing to build the file and a pattern that is usually a
/// mistake can both be written "Certain"; the difference matters, and these say it out loud.
/// </summary>
public class EvidenceTests
{
    private static Finding Finding(
        FindingKind kind = FindingKind.Logic,
        Confidence confidence = Confidence.Likely,
        ParsedError? error = null,
        string? foundBy = null,
        string? witness = null,
        string? confirmation = null,
        string ruleId = "") => new()
    {
        Kind = kind,
        Severity = Severity.Error,
        Confidence = confidence,
        File = @"C:\work\thing.py",
        Line = 4,
        Title = "Title",
        Explanation = "what is wrong",
        WhyItMatters = "why",
        SuggestedFix = "fix",
        CorrectedExample = "",
        RuleId = ruleId,
        Error = error,
        FoundBy = foundBy,
        Witness = witness,
        Confirmation = confirmation,
    };

    private static ParsedError Said(string? code = null) => new()
    {
        LanguageId = "python",
        Confidence = 90,
        FirstLineSequence = 0,
        RawText = "",
        Message = "something",
        ErrorCode = code,
        Frames = [],
    };

    [Fact]
    public void ARunThatBrokeExactlyAsPredictedIsTheStrongestThingThereIs()
    {
        var said = Evidence.For(Finding(confirmation: "running share(1, 0) stopped with ZeroDivisionError"));

        Assert.Contains("the program was run", said, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("broke", said, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ACompilerRefusingTheFileSaysSoAndNamesItsCode()
    {
        var said = Evidence.For(Finding(kind: FindingKind.Syntax, error: Said("CS0103")));

        Assert.Contains("compiler", said, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CS0103", said, StringComparison.Ordinal);
    }

    [Fact]
    public void ACompilerWithNoCodeToGiveStillSaysItWasTheCompiler()
    {
        var said = Evidence.For(Finding(kind: FindingKind.Syntax, error: Said()));

        Assert.Contains("compiler", said, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WrongOutputSaysTheProgramRanAndPrintedSomethingElse()
    {
        var said = Evidence.For(Finding(ruleId: "wrong-output"));

        Assert.Contains("printed", said, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnAnalysisSaysWhichAnalysisItWas()
    {
        var said = Evidence.For(Finding(foundBy: "abstract interpretation"));

        Assert.Contains("abstract interpretation", said, StringComparison.Ordinal);
    }

    /// <summary>
    /// The honest floor. A rule that recognises a shape has recognised a shape, and saying anything stronger about it
    /// would be dressing a pattern match up as proof.
    /// </summary>
    [Fact]
    public void APatternWithNothingBehindItSaysExactlyThat()
    {
        var said = Evidence.For(Finding());

        Assert.Contains("usually a mistake", said, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("compiler", said, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("was run", said, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheInputsThatBreakItAreWorthSaying()
    {
        var said = Evidence.For(Finding(foundBy: "following the paths", witness: "`items` is empty"));

        Assert.Contains("items", said, StringComparison.Ordinal);
    }

    [Fact]
    public void AVerifiedFixIsPartOfWhyTheFindingIsBelieved()
    {
        var verified = Finding(foundBy: "abstract interpretation") with
        {
            Verified = Verification.NotTested
                .With(VerificationStage.Compiled, StageResult.Passed, "compiles")
                .With(VerificationStage.Ran, StageResult.Passed, "ran without failing"),
        };

        Assert.Contains("failure did not come back", Evidence.For(verified), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AFixThatOnlyCompilesIsNotDescribedAsMoreThanThat()
    {
        var compiled = Finding(foundBy: "abstract interpretation") with
        {
            Verified = Verification.NotTested.With(VerificationStage.Compiled, StageResult.Passed, "compiles"),
        };

        var said = Evidence.For(compiled);

        Assert.Contains("compiles", said, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("did not come back", said, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheConfidenceWordIsGivenWithWhatStandsBehindIt()
    {
        var behind = Evidence.Behind(Finding(confidence: Confidence.Possible));

        Assert.StartsWith("Possible:", behind, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryFindingHasSomethingToSayForItself()
    {
        foreach (var kind in Enum.GetValues<FindingKind>())
        {
            foreach (var confidence in Enum.GetValues<Confidence>())
            {
                var said = Evidence.For(Finding(kind, confidence));

                Assert.False(string.IsNullOrWhiteSpace(said), $"{kind}/{confidence} had nothing to say");
                Assert.EndsWith(".", said, StringComparison.Ordinal);
            }
        }
    }
}
