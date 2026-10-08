using FixFinder.Core.Checking;
using FixFinder.Core.Checking.Guides;

namespace FixFinder.Tests;

/// <summary>
/// Every guide explains its mistake for someone new to programming, says why it matters and how to fix it. Each table is
/// checked on its own, so a failure names it, and every guide FixFinder has is checked as well, so a guide added later
/// cannot leave any of them out.
/// </summary>
public class GuideWritingTests
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
    public void EveryGuideExplainsItsMistakeWhyItMattersAndHowToFixIt(string table)
    {
        foreach (var entry in Tables[table]) AssertWrittenInFull(entry.Guide);
    }

    [Fact]
    public void EveryGuideFixFinderHasIsWrittenInFull()
    {
        foreach (var guide in Guidebook.Every()) AssertWrittenInFull(guide);
    }

    /// <summary>The guide used when nothing more specific is known is written for every kind of finding too.</summary>
    [Fact]
    public void EveryGeneralGuideIsWrittenInFull()
    {
        foreach (var kind in Enum.GetValues<FindingKind>()) AssertWrittenInFull(GeneralGuides.For("Python", kind));
    }

    private static void AssertWrittenInFull(MistakeGuide guide)
    {
        var named = guide.Title ?? guide.Explanation;

        Assert.False(string.IsNullOrWhiteSpace(guide.Explanation), $"no explanation for: {named}");
        Assert.False(string.IsNullOrWhiteSpace(guide.WhyItMatters), $"no reason it matters for: {named}");
        Assert.False(string.IsNullOrWhiteSpace(guide.SuggestedFix), $"no fix for: {named}");
    }
}
