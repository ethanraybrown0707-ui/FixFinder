using FixFinder.Core.Checking;
using FixFinder.Core.Parsing;

namespace FixFinder.Tests;

/// <summary>
/// Which findings are consequences of another, and - far more of these - which are not. Grouping two real problems
/// together hides one of them, so the rules here are mostly about refusing to group.
/// </summary>
public class RootCauseTests
{
    private static Finding Missing(int line, string name, string file = @"C:\work\Readings.cs", string code = "CS0103") => new()
    {
        Kind = FindingKind.Syntax,
        Severity = Severity.Error,
        Confidence = Confidence.Certain,
        File = file,
        Line = line,
        Title = "The name does not exist",
        Explanation = $"The name '{name}' does not exist in the current context",
        WhyItMatters = "why",
        SuggestedFix = "fix",
        CorrectedExample = "",
        RuleId = code,
        Error = new ParsedError
        {
            LanguageId = "csharp",
            Confidence = 90,
            FirstLineSequence = 0,
            RawText = "",
            Message = $"The name '{name}' does not exist in the current context",
            ErrorCode = code,
            Frames = [],
        },
    };

    private static Finding Unrelated(int line, string title) => new()
    {
        Kind = FindingKind.Logic,
        Severity = Severity.Warning,
        Confidence = Confidence.Likely,
        File = @"C:\work\Readings.cs",
        Line = line,
        Title = title,
        Explanation = "Something else entirely",
        WhyItMatters = "why",
        SuggestedFix = "fix",
        CorrectedExample = "",
        RuleId = "analysis-division-by-zero",
    };

    [Fact]
    public void TheSameNameUndefinedFourTimesIsOneProblem()
    {
        var linked = RootCauses.Link([Missing(17, "total"), Missing(19, "total"), Missing(22, "total"), Missing(26, "total")]);

        Assert.Null(linked[0].CausedBy);
        Assert.Equal(17, linked[0].Line);

        Assert.All(linked.Skip(1), f => Assert.Equal(linked[0].Id, f.CausedBy!.RootId));
        Assert.All(linked.Skip(1), f => Assert.Equal(RelationKind.SameMissingName, f.CausedBy!.Kind));
    }

    [Fact]
    public void TheEarliestLineIsTheRootWhateverOrderTheyArriveIn()
    {
        var linked = RootCauses.Link([Missing(26, "total"), Missing(17, "total"), Missing(19, "total")]);

        var root = Assert.Single(linked, f => f.CausedBy is null);
        Assert.Equal(17, root.Line);
    }

    [Fact]
    public void RootsComeBeforeWhatFollowsFromThem()
    {
        var linked = RootCauses.Link([Missing(26, "total"), Missing(17, "total")]);

        Assert.Equal(17, linked[0].Line);
        Assert.Equal(26, linked[1].Line);
    }

    [Fact]
    public void TwoDifferentMissingNamesAreTwoProblems()
    {
        var linked = RootCauses.Link([Missing(17, "total"), Missing(19, "count")]);

        Assert.All(linked, f => Assert.Null(f.CausedBy));
    }

    [Fact]
    public void TheSameNameInADifferentFileIsADifferentProblem()
    {
        var linked = RootCauses.Link([Missing(17, "total"), Missing(19, "total", @"C:\work\Other.cs")]);

        Assert.All(linked, f => Assert.Null(f.CausedBy));
    }

    [Fact]
    public void OneMissingNameOnItsOwnHasNothingToFollowFrom()
    {
        var linked = RootCauses.Link([Missing(17, "total")]);

        Assert.Null(Assert.Single(linked).CausedBy);
    }

    /// <summary>Proximity is not evidence. Two findings on neighbouring lines are still two findings.</summary>
    [Fact]
    public void FindingsAreNotGroupedForBeingNearEachOther()
    {
        var linked = RootCauses.Link([Unrelated(17, "Dividing by something that can be zero"), Unrelated(18, "Dividing by something that can be zero")]);

        Assert.All(linked, f => Assert.Null(f.CausedBy));
    }

    /// <summary>Nor is similar wording: the same rule firing twice about different values is two problems.</summary>
    [Fact]
    public void FindingsAreNotGroupedForReadingAlike()
    {
        var linked = RootCauses.Link([Unrelated(4, "Asking for a position that does not exist"), Unrelated(90, "Asking for a position that does not exist")]);

        Assert.All(linked, f => Assert.Null(f.CausedBy));
    }

    [Fact]
    public void AnUnrelatedFindingKeepsItsPlaceAmongTheGroupedOnes()
    {
        var linked = RootCauses.Link([Missing(17, "total"), Unrelated(18, "Dividing by something that can be zero"), Missing(22, "total")]);

        Assert.Equal(3, linked.Count);
        Assert.Single(linked, f => f.CausedBy is not null);
        Assert.Contains(linked, f => f.Title == "Dividing by something that can be zero" && f.CausedBy is null);
    }

    [Theory]
    [InlineData("cannot find symbol\n  symbol:   variable total", "java")]
    [InlineData("undefined: total", "go")]
    [InlineData("name 'total' is not defined", "python")]
    public void CompilersThatGiveNoCodeForItAreUnderstoodToo(string message, string language)
    {
        Finding Said(int line) => new()
        {
            Kind = FindingKind.Syntax,
            Severity = Severity.Error,
            Confidence = Confidence.Certain,
            File = @"C:\work\Main.java",
            Line = line,
            Title = "The name does not exist",
            Explanation = message,
            WhyItMatters = "why",
            SuggestedFix = "fix",
            CorrectedExample = "",
            RuleId = "",
            Error = new ParsedError { LanguageId = language, Confidence = 90, FirstLineSequence = 0, RawText = "", Message = message, Frames = [] },
        };

        var linked = RootCauses.Link([Said(17), Said(19)]);

        Assert.Null(linked[0].CausedBy);
        Assert.NotNull(linked[1].CausedBy);
    }
}
