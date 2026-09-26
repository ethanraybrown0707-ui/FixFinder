using FixFinder.Core.Checking.Guides;

namespace FixFinder.Tests;

/// <summary>
/// Every guide explains its mistake three ways - for a beginner, for a student and in technical terms - so the slider
/// always changes what is said. The tables are listed here as each is written at every depth.
/// </summary>
public class GuideDepthTests
{
    private static readonly Dictionary<string, IReadOnlyList<GuideEntry>> Tables = new()
    {
        ["Python"] = PythonGuides.All,
        ["Java"] = JavaGuides.All,
        ["CSharp"] = CSharpGuides.All,
        ["Native"] = NativeGuides.All,
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
