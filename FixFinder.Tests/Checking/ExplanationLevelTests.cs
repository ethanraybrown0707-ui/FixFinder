using FixFinder.Core.Checking;
using FixFinder.Core.Checking.Guides;
using FixFinder.Core.Engine;

namespace FixFinder.Tests;

/// <summary>
/// How much is explained, and the rule that governs it: the level changes the words and nothing else. A finding says
/// the same thing about the same program whichever depth it is read at.
/// </summary>
public class ExplanationLevelTests
{
    [Fact]
    public void StudentIsTheMiddleAndTheOneChosenWhenNobodyHasChosen()
    {
        Assert.Equal(ExplanationLevel.Student, new Preferences().Explanations);

        var explained = Explained.Of("the middle", "the plain one", "the exact one");

        Assert.Equal("the middle", explained.At(ExplanationLevel.Student));
        Assert.Equal("the plain one", explained.At(ExplanationLevel.Beginner));
        Assert.Equal("the exact one", explained.At(ExplanationLevel.Technical));
    }

    [Fact]
    public void AnExplanationWrittenOnlyOnceIsGivenAtEveryLevel()
    {
        var explained = Explained.Of("the only wording");

        Assert.Equal("the only wording", explained.At(ExplanationLevel.Beginner));
        Assert.Equal("the only wording", explained.At(ExplanationLevel.Student));
        Assert.Equal("the only wording", explained.At(ExplanationLevel.Technical));
        Assert.False(explained.VariesByLevel);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyWordingIsNotTreatedAsOneThatWasWritten(string missing)
    {
        var explained = Explained.Of("the only wording", missing, missing);

        Assert.Equal("the only wording", explained.At(ExplanationLevel.Beginner));
        Assert.Equal("the only wording", explained.At(ExplanationLevel.Technical));
    }

    [Fact]
    public void AFindingThatSaysNothingAboutLevelsRepeatsTheOneExplanationItHas()
    {
        var finding = new Finding
        {
            Kind = FindingKind.Logic,
            Severity = Severity.Error,
            Confidence = Confidence.Certain,
            File = @"C:\work\thing.py",
            Title = "Title",
            Explanation = "what is wrong",
            WhyItMatters = "why",
            SuggestedFix = "fix",
            CorrectedExample = "",
        };

        Assert.Equal("what is wrong", finding.Explanations.At(ExplanationLevel.Beginner));
        Assert.Equal("what is wrong", finding.Explanations.At(ExplanationLevel.Technical));
    }

    /// <summary>
    /// The rule the whole feature rests on: everything except the wording is the same at every level. A reader who
    /// switches to Beginner must not be told a different severity, a different line or a different fix.
    /// </summary>
    [Fact]
    public void NothingButTheWordingDependsOnTheLevel()
    {
        var guide = new MistakeGuide("the middle", "why", "fix", "example")
        {
            Title = "Title",
            ForBeginners = "the plain one",
            ForTechnical = "the exact one",
        };

        var finding = new Finding
        {
            Kind = FindingKind.Runtime,
            Severity = Severity.Warning,
            Confidence = Confidence.Likely,
            File = @"C:\work\thing.py",
            Line = 12,
            Title = guide.Title,
            Explanation = guide.Explanation,
            Explanations = guide.Explanations,
            WhyItMatters = guide.WhyItMatters,
            SuggestedFix = guide.SuggestedFix,
            CorrectedExample = guide.Example,
        };

        foreach (var level in Enum.GetValues<ExplanationLevel>())
        {
            // Reading at a level is a read of one string; the finding itself is untouched by it.
            _ = finding.Explanations.At(level);

            Assert.Equal(Severity.Warning, finding.Severity);
            Assert.Equal(Confidence.Likely, finding.Confidence);
            Assert.Equal(12, finding.Line);
            Assert.Equal("fix", finding.SuggestedFix);
            Assert.Equal("example", finding.CorrectedExample);
        }

        Assert.True(finding.Explanations.VariesByLevel);
    }

    [Fact]
    public void APreferenceSurvivesBeingPutAwayAndFetchedBack()
    {
        using var temp = new TempFolder();
        var file = Path.Combine(temp.Path, "preferences.json");

        Assert.True(new Preferences { Explanations = ExplanationLevel.Technical }.Save(file));
        Assert.Equal(ExplanationLevel.Technical, Preferences.Load(file).Explanations);
    }

    /// <summary>Following Windows is the choice for anybody who has not made one, so it is what an empty file means.</summary>
    [Fact]
    public void AppearanceFollowsWindowsUntilSomebodySaysOtherwise()
    {
        using var temp = new TempFolder();
        var file = Path.Combine(temp.Path, "preferences.json");

        Assert.Equal(AppearanceChoice.System, new Preferences().Appearance);

        Assert.True(new Preferences { Appearance = AppearanceChoice.Dark }.Save(file));
        Assert.Equal(AppearanceChoice.Dark, Preferences.Load(file).Appearance);
    }

    /// <summary>The two preferences are kept in one file, so writing one must not lose the other.</summary>
    [Fact]
    public void ChoosingColoursDoesNotForgetHowMuchToExplain()
    {
        using var temp = new TempFolder();
        var file = Path.Combine(temp.Path, "preferences.json");

        new Preferences { Explanations = ExplanationLevel.Beginner, Appearance = AppearanceChoice.Dark }.Save(file);

        var read = Preferences.Load(file);

        Assert.Equal(ExplanationLevel.Beginner, read.Explanations);
        Assert.Equal(AppearanceChoice.Dark, read.Appearance);
    }

    [Fact]
    public void APreferenceFileThatCannotBeReadIsTreatedAsOneThatWasNeverWritten()
    {
        using var temp = new TempFolder();

        var missing = Path.Combine(temp.Path, "not-there.json");
        Assert.Equal(ExplanationLevel.Student, Preferences.Load(missing).Explanations);

        var nonsense = Path.Combine(temp.Path, "nonsense.json");
        File.WriteAllText(nonsense, "{ this is not json");
        Assert.Equal(ExplanationLevel.Student, Preferences.Load(nonsense).Explanations);
    }

    /// <summary>Every guide that offers a level must actually differ at it, or the offer is empty.</summary>
    [Fact]
    public void AGuideWrittenAtThreeLevelsSaysSomethingDifferentAtEach()
    {
        var written = Guidebook.Every()
            .Where(guide => guide.ForBeginners is not null || guide.ForTechnical is not null)
            .ToList();

        Assert.NotEmpty(written);

        foreach (var guide in written)
        {
            Assert.True(guide.Explanations.VariesByLevel, $"'{guide.Title}' offers a level that reads the same as the middle one.");
            Assert.NotEqual(guide.ForBeginners, guide.ForTechnical);
        }
    }
}
