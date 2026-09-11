using FixFinder.Core.Fingerprinting;
using FixFinder.Core.Parsing;

namespace FixFinder.Tests;

/// <summary>
/// Covers the step between "we read the crash" and "we search for it" - the one where a search
/// quietly stops working if a rule is wrong.
/// </summary>
public class FingerprintTests
{
    private static readonly ParserRegistry Registry = new();

    private static ErrorFingerprint Fingerprint(string fixture)
    {
        var parsed = Registry.Parse(Fixtures.LoadStackTrace(fixture));
        Assert.NotNull(parsed);
        return FingerprintBuilder.Build(parsed);
    }

    // ------------------------------------------------------------------ normalisation rules

    [Theory]
    [InlineData(@"Could not find C:\Users\ethan\src\app\Program.cs", "Program.cs")]
    [InlineData("Could not find /srv/deploy/2026/app/main.py", "main.py")]
    [InlineData("Job 3f2504e0-4f89-11d3-9a0c-0305e82c3301 failed", "<guid>")]
    [InlineData("Access violation at 0x00007ffd2a1b", "<addr>")]
    [InlineData("Timed out at 2026-09-10T09:00:02Z", "<time>")]
    [InlineData("Worker pid 48122 died", "<num>")]
    [InlineData("Batch of 128000 rows failed", "<num>")]
    public void ScrubsThingsThatAreUniqueToThisMachineAndRun(string input, string expectedFragment)
    {
        var (text, trace) = ErrorNormalizer.Normalize(input, relaxed: false);

        Assert.Contains(expectedFragment, text);
        Assert.NotEmpty(trace);
    }

    /// <summary>
    /// A path becomes its file name rather than disappearing. "Program.cs" is a useful search
    /// term; the absolute path it came from is not.
    /// </summary>
    [Fact]
    public void KeepsTheFileNameWhenStrippingAPath()
    {
        var (text, _) = ErrorNormalizer.Normalize(@"in C:\Users\ethan\src\Cart.cs line 12", relaxed: false);

        Assert.Contains("Cart.cs", text);
        Assert.DoesNotContain("ethan", text);
    }

    /// <summary>
    /// The single most consequential rule in the file. In all three of these the quoted token
    /// IS the error, so the tight query must keep it - stripping it turns a precise search into
    /// a generic one for the exception type.
    /// </summary>
    [Theory]
    [InlineData("'user_id'")]
    [InlineData("No module named 'requests'")]
    [InlineData("Could not load file or assembly 'Newtonsoft.Json'")]
    public void TightQueryKeepsQuotedValuesAndRelaxedDropsThem(string message)
    {
        var (tight, _) = ErrorNormalizer.Normalize(message, relaxed: false);
        var (relaxed, _) = ErrorNormalizer.Normalize(message, relaxed: true);

        Assert.Contains("'", tight);
        Assert.DoesNotContain("'", relaxed);
        Assert.Contains("<val>", relaxed);
    }

    [Fact]
    public void RecordsWhatEachRuleChangedSoTheUserCanSeeIt()
    {
        var (_, trace) = ErrorNormalizer.Normalize(
            @"Worker pid 991 failed reading C:\srv\data\rows.csv at 12:00:01", relaxed: false);

        Assert.Contains(trace, t => t.RuleName == "WindowsPath");
        Assert.Contains(trace, t => t.RuleName == "ProcessId");
        Assert.All(trace, t => Assert.False(string.IsNullOrWhiteSpace(t.Reason)));
    }

    // ------------------------------------------------------------------ hash stability

    /// <summary>
    /// The hash is what tells FixVerifier whether a patch fixed the crash or merely moved it.
    /// Since applying a patch changes line numbers, a hash that varied with them would report
    /// every single applied patch as "a different error now".
    /// </summary>
    [Fact]
    public void HashIgnoresPathsLineNumbersPidsAndTimestamps()
    {
        var first = FingerprintOf(
            @"Object reference not set at C:\build\a\Cart.cs:line 12 (pid 4001) 2026-09-10T09:00:00Z");
        var second = FingerprintOf(
            @"Object reference not set at D:\other\b\Cart.cs:line 88 (pid 9312) 2026-01-02T17:44:10Z");

        Assert.Equal(first.Hash, second.Hash);
    }

    [Fact]
    public void HashDiffersForGenuinelyDifferentErrors()
    {
        Assert.NotEqual(
            FingerprintOf("Object reference not set to an instance of an object").Hash,
            FingerprintOf("The process cannot access the file because it is being used").Hash);
    }

    // ------------------------------------------------------------------ query building

    [Fact]
    public void TightQueryLeadsWithTheExactTypeAndTheQuotedValue()
    {
        var fingerprint = Fingerprint("python/keyerror.txt");

        Assert.Contains("\"KeyError\"", fingerprint.Tight.Text);
        Assert.Contains("user_id", fingerprint.Tight.Text);
        Assert.DoesNotContain("user_id", fingerprint.Relaxed.Text);
    }

    /// <summary>An exact CS/C error code beats any number of message words, so it leads the query.</summary>
    [Fact]
    public void ErrorCodeIsCarriedIntoBothQueries()
    {
        var fingerprint = Fingerprint("msvc/cs0103.txt");

        Assert.Equal("CS0103", fingerprint.ErrorCode);
        Assert.Contains("CS0103", fingerprint.Tight.Text);
        Assert.Contains("CS0103", fingerprint.Relaxed.Text);
    }

    [Fact]
    public void BoilerplateWordsAreNotUsedAsSearchTerms()
    {
        var fingerprint = FingerprintOf("An unexpected error occurred while trying to process the request");

        Assert.DoesNotContain("error", fingerprint.Tokens);
        Assert.DoesNotContain("occurred", fingerprint.Tokens);
        Assert.DoesNotContain("the", fingerprint.Tokens);
    }

    /// <summary>Both queries carry an explanation, because a person has to be able to tune them.</summary>
    [Fact]
    public void BothQueriesExplainThemselves()
    {
        var fingerprint = Fingerprint("python/keyerror.txt");

        Assert.False(string.IsNullOrWhiteSpace(fingerprint.Tight.Explanation));
        Assert.False(string.IsNullOrWhiteSpace(fingerprint.Relaxed.Explanation));
    }

    // ------------------------------------------------------------------ the honesty signals

    /// <summary>
    /// The check that stops FixFinder feeling like a random-link generator: a crash inside the
    /// user's own file has no published fix anywhere, and the tool has to know that.
    /// </summary>
    [Fact]
    public void FlagsACrashInTheUsersOwnCodeAsFirstParty()
    {
        var parsed = Registry.Parse(
            Fixtures.LoadStackTrace("csharp/inner-exception.txt"),
            sourceRoots: [@"C:\src"]);

        Assert.NotNull(parsed);
        var fingerprint = FingerprintBuilder.Build(parsed);

        Assert.True(fingerprint.CulpritIsFirstParty);
        Assert.Equal("Program.cs", fingerprint.CulpritFile);
    }

    [Fact]
    public void FindsTheNearestLibraryToNarrowTheSearch()
    {
        var fingerprint = Fingerprint("node/typeerror.txt");

        Assert.Equal("express", fingerprint.NearestThirdPartyModule);
    }

    /// <summary>
    /// The wrapper's message is text only this program has ever printed. Searching for it finds
    /// nothing, so the fingerprint has to be built from the inner exception instead.
    /// </summary>
    [Fact]
    public void FingerprintsTheRootCauseNotTheWrapper()
    {
        var fingerprint = Fingerprint("csharp/inner-exception.txt");

        Assert.Equal("System.NullReferenceException", fingerprint.ExceptionType);
        Assert.DoesNotContain("basket", fingerprint.Tight.Text, StringComparison.OrdinalIgnoreCase);
    }

    private static ErrorFingerprint FingerprintOf(string message) =>
        FingerprintBuilder.Build(new ParsedError
        {
            LanguageId = "csharp",
            Confidence = 90,
            RawText = message,
            FirstLineSequence = 1,
            ExceptionType = "System.NullReferenceException",
            Message = message,
            Frames = [],
        });
}
