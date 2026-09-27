using FixFinder.Core.Checking;
using FixFinder.Core.Checking.Guides;

namespace FixFinder.Tests;

/// <summary>
/// Every guide explains its mistake three ways - for a beginner, for a student and in technical terms - so the slider
/// always changes what is said. Each table is checked on its own, so a failure names it, and every guide FixFinder has is
/// checked as well, so a guide added later cannot be written at one depth only.
/// </summary>
public class GuideDepthTests
{
    private static readonly Dictionary<string, IReadOnlyList<GuideEntry>> Tables = new()
    {
        ["Python"] = PythonGuides.All,
        ["Java"] = JavaGuides.All,
        ["CSharp"] = CSharpGuides.All,
        ["Native"] = NativeGuides.All,
        ["JavaScript"] = JavaScriptGuides.All,
        ["Go"] = GoGuides.All,
        ["Shared logic"] = LogicGuides.SharedGuides,
        ["Python patterns"] = PythonPatternGuides.All,
        ["Brace patterns"] = BracePatternGuides.All,
        ["Managed patterns"] = ManagedPatternGuides.All,
        ["Python analyses"] = AnalysisGuides.All,
        ["Go analyses"] = AnalysisGuides.Go,
        ["JavaScript analyses"] = AnalysisGuides.JavaScript,
        ["C and C++ analyses"] = AnalysisGuides.Native,
        ["Java analyses"] = AnalysisGuides.Java,
        ["C# analyses"] = AnalysisGuides.CSharp,
    };

    public static TheoryData<string> Written
    {
        get
        {
            var written = new TheoryData<string>();
            foreach (var table in Tables.Keys) written.Add(table);
            return written;
        }
    }

    [Theory]
    [MemberData(nameof(Written))]
    public void EveryGuideIsWrittenAtEveryDepth(string table)
    {
        foreach (var entry in Tables[table])
        {
            var guide = entry.Guide;
            var named = guide.Title ?? guide.Explanation;

            Assert.False(string.IsNullOrWhiteSpace(guide.ForBeginners), $"no beginner's explanation for: {named}");
            Assert.False(string.IsNullOrWhiteSpace(guide.ForTechnical), $"no technical explanation for: {named}");
            Assert.True(guide.Explanations.VariesByLevel, $"the same words at every depth for: {named}");
            Assert.NotEqual(guide.ForBeginners, guide.Explanation);
            Assert.NotEqual(guide.ForTechnical, guide.Explanation);
            Assert.NotEqual(guide.ForBeginners, guide.ForTechnical);
        }
    }

    [Fact]
    public void EveryGuideFixFinderHasIsWrittenAtEveryDepth()
    {
        foreach (var guide in Guidebook.Every())
        {
            var named = guide.Title ?? guide.Explanation;
            Assert.True(guide.Explanations.VariesByLevel, $"the same words at every depth for: {named}");
            Assert.False(string.IsNullOrWhiteSpace(guide.ForBeginners), $"no beginner's explanation for: {named}");
            Assert.False(string.IsNullOrWhiteSpace(guide.ForTechnical), $"no technical explanation for: {named}");
        }
    }

    /// <summary>The guide used when nothing more specific is known is written at every depth too, for every kind of finding.</summary>
    [Fact]
    public void EveryGeneralGuideIsWrittenAtEveryDepth()
    {
        foreach (var kind in Enum.GetValues<FindingKind>())
        {
            var guide = GeneralGuides.For("Python", kind);

            Assert.True(guide.Explanations.VariesByLevel, $"the same words at every depth for {kind}");
            Assert.False(string.IsNullOrWhiteSpace(guide.ForBeginners), $"no beginner's explanation for {kind}");
            Assert.False(string.IsNullOrWhiteSpace(guide.ForTechnical), $"no technical explanation for {kind}");
            Assert.True(guide.ForBeginners!.Length > guide.Explanation.Length, $"the beginner's explanation is shorter for {kind}");
        }
    }

    /// <summary>A beginner's explanation spells more out, so it is never the shortest of the three.</summary>
    [Theory]
    [MemberData(nameof(Written))]
    public void TheBeginnersExplanationSpellsMoreOut(string table)
    {
        foreach (var entry in Tables[table])
        {
            var guide = entry.Guide;
            Assert.True(guide.ForBeginners!.Length > guide.Explanation.Length, $"the beginner's explanation is shorter for: {guide.Explanation}");
        }
    }
}
