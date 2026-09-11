using FixFinder.Core.Security;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>
/// Covers credential storage and, more importantly, credential display.
/// </summary>
/// <remarks>
/// The masking tests carry the real weight. A token that round-trips wrongly is an annoyance
/// you notice within seconds; a token accidentally rendered in full into a window, a log file or
/// a screenshot is a credential that has to be revoked, and nothing in the tool would tell you
/// it had happened.
/// </remarks>
public class TokenStoreTests
{
    [Fact]
    public void ASecretIsNeverShownInFull()
    {
        const string token = "github_pat_11ABCDEFG0abcdefghijklmnop";

        var masked = TokenStore.Mask(token);

        Assert.DoesNotContain("ABCDEFG0abcdefghij", masked, StringComparison.Ordinal);
        Assert.Contains("•", masked, StringComparison.Ordinal);
    }

    /// <summary>
    /// The prefix stays visible on purpose: it is not secret, and it is how you tell at a glance
    /// that you pasted a GitHub token into the GitHub box.
    /// </summary>
    [Fact]
    public void TheMaskKeepsThePrefixAndTheLastFourCharacters()
    {
        var masked = TokenStore.Mask("github_pat_11ABCDEFG0wxyz");

        Assert.StartsWith("github_pat_", masked, StringComparison.Ordinal);
        Assert.EndsWith("wxyz", masked, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnAbsentSecretSaysSoRatherThanShowingEmptyBullets(string? secret) =>
        Assert.Equal("(not set)", TokenStore.Mask(secret));

    [Fact]
    public void AShortSecretIsStillNotRevealed()
    {
        var masked = TokenStore.Mask("abc");

        Assert.DoesNotContain("abc", masked, StringComparison.Ordinal);
    }

    /// <summary>
    /// Round-trips through DPAPI on Windows. Skipped elsewhere, where nothing is persisted.
    /// </summary>
    /// <remarks>
    /// Writes to the real credential path, so it restores whatever was there first. A test that
    /// silently discarded a token the user had already saved would be a poor trade for coverage.
    /// </remarks>
    [Fact]
    public void CredentialsSurviveARoundTripThroughDpapi()
    {
        if (!TokenStore.IsSupported) return;

        var existing = TokenStore.Load();

        try
        {
            var saved = TokenStore.Save(new StoredCredentials("github_pat_testvalue1234", "so_key_test"));
            Assert.True(saved);

            var read = TokenStore.Load();

            Assert.Equal("github_pat_testvalue1234", read.GitHubToken);
            Assert.Equal("so_key_test", read.StackExchangeKey);
        }
        finally
        {
            if (existing.GitHubToken is null && existing.StackExchangeKey is null) TokenStore.Clear();
            else TokenStore.Save(existing);
        }
    }

    /// <summary>The encrypted file must not contain the token as readable text.</summary>
    [Fact]
    public void TheStoredFileDoesNotContainThePlainTextToken()
    {
        if (!TokenStore.IsSupported) return;

        var existing = TokenStore.Load();

        try
        {
            TokenStore.Save(new StoredCredentials("github_pat_needle_value_9876"));

            var bytes = File.ReadAllBytes(TokenStore.FilePath);
            var asText = System.Text.Encoding.UTF8.GetString(bytes);

            Assert.DoesNotContain("needle_value_9876", asText, StringComparison.Ordinal);
        }
        finally
        {
            if (existing.GitHubToken is null && existing.StackExchangeKey is null) TokenStore.Clear();
            else TokenStore.Save(existing);
        }
    }

    // ------------------------------------------------------------------ candidate display

    [Fact]
    public void ACandidateDisplayLineLeadsWithItsTier()
    {
        var advisory = new FixCandidate
        {
            SourceName = "Stack Overflow", Id = "SO 4712", Title = "Fix it", Url = "https://x.test",
            AnswerCount = 2, Score = 61,
        };

        var appliable = new FixCandidate
        {
            SourceName = "GitHub", Id = "owner/repo#12", Title = "Patched", Url = "https://y.test",
            IsClosed = true, ClosedReason = "completed", Score = 78, Tier = FixTier.AutoAppliable,
        };

        Assert.StartsWith("[B]  61", advisory.Display, StringComparison.Ordinal);
        Assert.StartsWith("[A]  78", appliable.Display, StringComparison.Ordinal);
        Assert.Contains("closed (completed)", appliable.Display, StringComparison.Ordinal);
    }
}
