using FixFinder.Core.Checking;

namespace FixFinder.Tests;

/// <summary>
/// Where a fix came from. The point of it is that the reader can go and check the fix is right, so what matters most
/// here is that nothing is credited to a source it did not come from, and that no link is offered that should not be.
/// </summary>
public class FixOriginTests
{
    [Fact]
    public void AFixFromOneOfFixFindersOwnRulesNamesTheRule()
    {
        var origin = FixOrigin.OwnRule("python-missing-colon");

        Assert.NotNull(origin);
        Assert.Equal("FixFinder", origin.SourceName);
        Assert.Equal("python-missing-colon", origin.Title);
        Assert.False(origin.HasLink);
        Assert.Contains("python-missing-colon", origin.Describe, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AFixWithNoRuleToNameIsLeftUnattributed(string? ruleId)
    {
        Assert.Null(FixOrigin.OwnRule(ruleId));
    }

    [Fact]
    public void AFixTakenFromAPageCarriesThePageItCameFrom()
    {
        var origin = new FixOrigin
        {
            SourceName = "Stack Overflow",
            Title = "Why does my loop go one too far?",
            Url = "https://stackoverflow.com/questions/4712",
        };

        Assert.True(origin.HasLink);
        Assert.Contains("Stack Overflow", origin.Describe, StringComparison.Ordinal);
    }

    /// <summary>
    /// The address came off a page somebody else wrote. Anything that is not plain web browsing is not offered as a
    /// link at all, rather than offered and hopefully refused later.
    /// </summary>
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("ms-settings:windowsdefender")]
    [InlineData("not a url at all")]
    [InlineData("")]
    [InlineData(null)]
    public void OnlyOrdinaryWebAddressesAreEverOfferedAsALink(string? url)
    {
        var origin = new FixOrigin { SourceName = "Somewhere", Title = "Something", Url = url };

        Assert.False(origin.HasLink);
    }

    [Theory]
    [InlineData("http://example.com/a")]
    [InlineData("https://example.com/a")]
    public void PlainWebAddressesAreOffered(string url)
    {
        Assert.True(new FixOrigin { SourceName = "Somewhere", Title = "Something", Url = url }.HasLink);
    }

    [Fact]
    public void AFindingWithNoFixClaimsNoSource()
    {
        var finding = new Finding
        {
            Kind = FindingKind.Logic,
            Severity = Severity.Warning,
            Confidence = Confidence.Likely,
            File = @"C:\work\thing.py",
            Title = "Title",
            Explanation = "what is wrong",
            WhyItMatters = "why",
            SuggestedFix = "fix",
            CorrectedExample = "",
        };

        Assert.Null(finding.CameFrom);
    }
}
