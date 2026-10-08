using FixFinder.Core.Checking;
using FixFinder.Core.Checking.Guides;
using FixFinder.Core.Engine;

namespace FixFinder.Tests;

/// <summary>
/// Every finding is explained one way, for someone new to programming - and what it found in this program always comes
/// first, before the account of this kind of mistake.
/// </summary>
public class BeginnerExplanationTests
{
    /// <summary>The program's own words lead, and the guide's beginner explanation follows them.</summary>
    [Fact]
    public void AFindingOfACheckSaysWhatItFoundThenWhatThisKindOfMistakeIs()
    {
        const string found = "`names` is a list, so `person in names` looks through it from the start on every pass";
        var analysis = new FixFinder.Core.Analysis.Checks.AnalysisFinding(
            "analysis-repeated-search", new FixFinder.Core.Analysis.Ir.SourceSpan(@"C:\work\shop.py", 5), found,
            Severity.Suggestion, Confidence.Likely, FindingKind.Performance, "looking at how the work grows with the data");
        var guide = Guidebook.For(@"C:\work\shop.py", FindingKind.Performance, "analysis-repeated-search");

        var finding = FindingFactory.FromAnalysis(analysis);

        Assert.StartsWith(found, finding.Explanation, StringComparison.Ordinal);
        Assert.EndsWith(guide.Explanation, finding.Explanation, StringComparison.Ordinal);
        Assert.Equal(found + ".", finding.Found);
    }

    /// <summary>An error's own message is its title, so what is wrong is the guide's beginner explanation of it.</summary>
    [Fact]
    public void AnErrorIsExplainedByItsGuide()
    {
        var error = new Core.Parsing.ParsedError
        {
            LanguageId = "python", Confidence = 90, RawText = "", FirstLineSequence = 0, Frames = [],
            ExceptionType = "ZeroDivisionError", Message = "division by zero",
        };
        var guide = Guidebook.For(@"C:\work\marks.py", FindingKind.Runtime, error: error);

        var finding = FindingFactory.FromError(error, FindingKind.Runtime, Severity.Error, Confidence.Certain, @"C:\work\marks.py");

        Assert.Equal(guide.Explanation, finding.Explanation);
        Assert.Null(finding.Found);
    }

    /// <summary>
    /// What the check said stays apart from the explanation after it, so code reading the names a finding quotes reads the
    /// check's words and not a general explanation that happens to say "has no attribute".
    /// </summary>
    [Fact]
    public void WhatTheCheckFoundIsKeptApartFromTheExplanationAfterIt()
    {
        var analysis = new FixFinder.Core.Analysis.Checks.AnalysisFinding(
            "analysis-null-used", new FixFinder.Core.Analysis.Ir.SourceSpan(@"C:\work\search.py", 9), "`found` can be None here",
            Severity.Warning, Confidence.Likely, FindingKind.Runtime, "following every value through the code");

        var finding = FindingFactory.FromAnalysis(analysis);

        Assert.Equal("`found` can be None here.", finding.Found);
        Assert.True(finding.Explanation.Length > finding.Found!.Length);
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

    /// <summary>
    /// A preferences file written before there was one way of explaining still names a depth. It is read as it always
    /// was, with the depth passed over, so nobody's other choices are lost by the change.
    /// </summary>
    [Fact]
    public void AFileThatStillNamesAnExplanationDepthKeepsTheOtherChoicesInIt()
    {
        using var temp = new TempFolder();
        var file = Path.Combine(temp.Path, "preferences.json");
        File.WriteAllText(file, """{ "Explanations": 2, "Appearance": 2, "RunSeconds": 120 }""");

        var read = Preferences.Load(file);

        Assert.Equal(AppearanceChoice.Dark, read.Appearance);
        Assert.Equal(TimeSpan.FromMinutes(2), read.RunTimeLimit);
    }

    [Fact]
    public void APreferenceFileThatCannotBeReadIsTreatedAsOneThatWasNeverWritten()
    {
        using var temp = new TempFolder();

        var missing = Path.Combine(temp.Path, "not-there.json");
        Assert.Equal(AppearanceChoice.System, Preferences.Load(missing).Appearance);

        var nonsense = Path.Combine(temp.Path, "nonsense.json");
        File.WriteAllText(nonsense, "{ this is not json");
        Assert.Equal(AppearanceChoice.System, Preferences.Load(nonsense).Appearance);
    }
}
