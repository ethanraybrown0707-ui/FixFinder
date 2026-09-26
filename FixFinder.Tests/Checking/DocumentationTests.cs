using FixFinder.Core.Checking;
using FixFinder.Core.Parsing;

namespace FixFinder.Tests;

/// <summary>
/// Where a reader is sent to read more. Every address is a search on an official site, never a link to a particular
/// page: a page link claims the page exists and says what it is cited for, and pages move.
/// </summary>
public class DocumentationTests
{
    private static Finding About(string file, string ruleId = "", ParsedError? error = null) => new()
    {
        Kind = FindingKind.Runtime,
        Severity = Severity.Error,
        Confidence = Confidence.Likely,
        File = file,
        Line = 3,
        Title = "Title",
        Explanation = "what is wrong",
        WhyItMatters = "why",
        SuggestedFix = "fix",
        CorrectedExample = "",
        RuleId = ruleId,
        Error = error,
    };

    /// <summary>The short name is worked out from the full one, so that is what has to be given.</summary>
    private static ParsedError Threw(string type) => new()
    {
        LanguageId = "python",
        Confidence = 90,
        FirstLineSequence = 0,
        RawText = "",
        Message = "something",
        ExceptionType = type,
        Frames = [],
    };

    [Theory]
    [InlineData(@"C:\work\thing.py", "docs.python.org")]
    [InlineData(@"C:\work\thing.js", "developer.mozilla.org")]
    [InlineData(@"C:\work\Thing.cs", "learn.microsoft.com")]
    [InlineData(@"C:\work\thing.c", "cppreference.com")]
    [InlineData(@"C:\work\thing.cpp", "cppreference.com")]
    [InlineData(@"C:\work\thing.go", "pkg.go.dev")]
    [InlineData(@"C:\work\Thing.java", "docs.oracle.com")]
    public void EachLanguageIsSentToItsOwnDocumentation(string file, string expected)
    {
        var reading = Documentation.For(About(file, "analysis-division-by-zero"));

        Assert.NotNull(reading);
        Assert.Contains(expected, reading.Url, StringComparison.Ordinal);
    }

    /// <summary>A language with no documentation search that was checked gets no link, rather than a guessed one.</summary>
    /// <summary>A loop searching a list is sped up with the language's set, so the set is what is looked up.</summary>
    [Theory]
    [InlineData(@"C:\work\thing.py", "set")]
    [InlineData(@"C:\work\Thing.java", "HashSet")]
    [InlineData(@"C:\work\Thing.cs", "HashSet")]
    [InlineData(@"C:\work\thing.js", "Set")]
    public void ASearchInALoopSendsTheReaderToTheLanguagesSet(string file, string term)
    {
        var reading = Documentation.For(About(file, "analysis-repeated-search"));

        Assert.NotNull(reading);
        Assert.Equal(term, reading.Term);
    }

    [Theory]
    [InlineData(@"C:\work\thing.rb")]
    [InlineData(@"C:\work\thing.php")]
    [InlineData(@"C:\work\thing.txt")]
    public void ALanguageWithNowhereCheckedToSendAnybodyGetsNoLink(string file)
    {
        Assert.Null(Documentation.For(About(file, "analysis-division-by-zero")));
    }

    [Fact]
    public void WhatTheLanguageCalledTheFailureIsWhatGetsSearchedFor()
    {
        var reading = Documentation.For(About(@"C:\work\thing.py", "analysis-division-by-zero", Threw("ZeroDivisionError")));

        Assert.NotNull(reading);
        Assert.Equal("ZeroDivisionError", reading.Term);
        Assert.Contains("ZeroDivisionError", reading.Url, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutAnExceptionTheRuleNameIsUsedAsPlainWords()
    {
        var reading = Documentation.For(About(@"C:\work\thing.py", "analysis-division-by-zero"));

        Assert.NotNull(reading);
        Assert.Equal("division by zero", reading.Term);
    }

    [Fact]
    public void AFindingWithNothingToSearchForGetsNoLink()
    {
        Assert.Null(Documentation.For(About(@"C:\work\thing.py")));
    }

    /// <summary>The term goes into a web address, so anything awkward in it has to be escaped rather than pasted.</summary>
    [Fact]
    public void ATermIsEscapedBeforeItGoesIntoTheAddress()
    {
        var reading = Documentation.For(About(@"C:\work\thing.py", "analysis-index-out-of-range"));

        Assert.NotNull(reading);
        Assert.DoesNotContain(" ", reading.Url, StringComparison.Ordinal);
        Assert.Contains("index%20out%20of%20range", reading.Url, StringComparison.Ordinal);
    }

    /// <summary>Everything offered is a search on an official site over https, which is what makes it safe to offer.</summary>
    [Theory]
    [InlineData(@"C:\work\thing.py")]
    [InlineData(@"C:\work\thing.js")]
    [InlineData(@"C:\work\Thing.cs")]
    [InlineData(@"C:\work\thing.c")]
    [InlineData(@"C:\work\thing.go")]
    [InlineData(@"C:\work\Thing.java")]
    public void EveryAddressIsAnOrdinarySecureWebAddress(string file)
    {
        var reading = Documentation.For(About(file, "analysis-division-by-zero"));

        Assert.NotNull(reading);
        Assert.True(Uri.TryCreate(reading.Url, UriKind.Absolute, out var uri));
        Assert.Equal("https", uri!.Scheme);
    }
}
