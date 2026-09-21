using System.Net;
using FixFinder.Core.Http;

namespace FixFinder.Tests;

/// <summary>Covers the response cache, which is what keeps development off the network.</summary>
public class HttpCacheTests
{
    private static Uri Url(string url) => new(url);

    [Fact]
    public void TheSameRequestWrittenTwoWaysGetsOneKey()
    {
        var first = HttpCache.KeyFor(HttpMethod.Get, Url("https://api.github.com/x?b=2&a=1"));
        var second = HttpCache.KeyFor(HttpMethod.Get, Url("https://API.github.com/x?a=1&b=2"));

        Assert.Equal(first, second);
    }

    [Fact]
    public void DifferentQueriesGetDifferentKeys()
    {
        var first = HttpCache.KeyFor(HttpMethod.Get, Url("https://api.github.com/x?q=one"));
        var second = HttpCache.KeyFor(HttpMethod.Get, Url("https://api.github.com/x?q=two"));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ACredentialInTheQueryStringNeverReachesTheDisk()
    {
        var canonical = HttpCache.Canonicalize(
            Url("https://api.stackexchange.com/2.3/search/advanced?site=stackoverflow&key=SuperSecret123"));

        Assert.DoesNotContain("SuperSecret123", canonical, StringComparison.Ordinal);
        Assert.DoesNotContain("key=", canonical, StringComparison.Ordinal);
        Assert.Contains("site=stackoverflow", canonical, StringComparison.Ordinal);
    }

    [Fact]
    public void AddingAnApiKeyDoesNotInvalidateEverythingAlreadyCached()
    {
        var without = HttpCache.KeyFor(HttpMethod.Get, Url("https://api.stackexchange.com/2.3/x?site=stackoverflow"));
        var with = HttpCache.KeyFor(HttpMethod.Get, Url("https://api.stackexchange.com/2.3/x?site=stackoverflow&key=abc"));

        Assert.Equal(without, with);
    }

    [Fact]
    public void AnEntryPastItsTtlReadsAsAMissUnlessAgeIsIgnored()
    {
        using var folder = new TempFolder();
        var cache = new HttpCache(folder.Path) { Ttl = TimeSpan.FromHours(1) };

        cache.Write("abc", new CachedResponse(
            "https://example.test/x", "GET", 200, "application/json", "{}",
            DateTimeOffset.UtcNow.AddHours(-5)));

        Assert.Null(cache.TryRead("abc"));
        Assert.NotNull(cache.TryRead("abc", ignoreAge: true));
    }

    [Fact]
    public void ACorruptEntryIsAMissRatherThanACrash()
    {
        using var folder = new TempFolder();
        var cache = new HttpCache(folder.Path);

        File.WriteAllText(Path.Combine(folder.Path, "broken.json"), "{ this is not json");

        Assert.Null(cache.TryRead("broken"));
    }

    private static (FixFinderHttpClient Client, CountingHandler Transport) Build(
        string cacheDirectory, HttpStatusCode status = HttpStatusCode.OK, string body = "{\"ok\":true}")
    {
        var transport = new CountingHandler(status, body);
        var client = new FixFinderHttpClient(new HttpCache(cacheDirectory), new QuotaTracker(), transport, PlaybackHttpMessageHandler.ReplayTimeout);

        return (client, transport);
    }

    [Fact]
    public async Task ASecondIdenticalRequestIsServedWithoutTouchingTheNetwork()
    {
        using var folder = new TempFolder();
        var (client, transport) = Build(folder.Path);
        using var _ = client;

        var first = await client.GetAsync("https://example.test/data", "test");
        var second = await client.GetAsync("https://example.test/data", "test");

        Assert.True(first.Ok);
        Assert.True(second.Ok);
        Assert.False(first.FromCache);
        Assert.True(second.FromCache);
        Assert.Equal(1, transport.Calls);
        Assert.Equal(first.Body, second.Body);
    }

    [Fact]
    public async Task CachedResultsOnlyRefusesAMissInsteadOfFetchingIt()
    {
        using var folder = new TempFolder();
        var (client, transport) = Build(folder.Path);
        using var _ = client;

        var result = await client.GetAsync("https://example.test/never-fetched", "test", CacheMode.CacheOnly);

        Assert.False(result.Ok);
        Assert.True(result.WasCacheMiss);
        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task CachedResultsOnlyServesAStoredEntryHoweverOldItIs()
    {
        using var folder = new TempFolder();
        var cache = new HttpCache(folder.Path) { Ttl = TimeSpan.FromSeconds(1) };
        var transport = new CountingHandler(HttpStatusCode.OK, "{\"stale\":true}");
        using var client = new FixFinderHttpClient(cache, new QuotaTracker(), transport, PlaybackHttpMessageHandler.ReplayTimeout);

        await client.GetAsync("https://example.test/data", "test");

        var key = HttpCache.KeyFor(HttpMethod.Get, Url("https://example.test/data"));
        var stored = cache.TryRead(key, ignoreAge: true)!;
        cache.Write(key, stored with { StoredUtc = DateTimeOffset.UtcNow.AddDays(-30) });

        var result = await client.GetAsync("https://example.test/data", "test", CacheMode.CacheOnly);

        Assert.True(result.Ok);
        Assert.True(result.FromCache);
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task RefreshGoesBackToTheNetworkEvenWithAFreshEntry()
    {
        using var folder = new TempFolder();
        var (client, transport) = Build(folder.Path);
        using var _ = client;

        await client.GetAsync("https://example.test/data", "test");
        await client.GetAsync("https://example.test/data", "test", CacheMode.Refresh);

        Assert.Equal(2, transport.Calls);
    }

    [Fact]
    public async Task AFailureResponseIsNeverStored()
    {
        using var folder = new TempFolder();
        var (client, transport) = Build(folder.Path, HttpStatusCode.Forbidden, "{\"message\":\"rate limit exceeded\"}");
        using var _ = client;

        var first = await client.GetAsync("https://example.test/data", "test");
        var second = await client.GetAsync("https://example.test/data", "test");

        Assert.False(first.Ok);
        Assert.False(second.FromCache);
        Assert.Equal(2, transport.Calls);
        Assert.Equal(0, new HttpCache(folder.Path).Count);
    }

    [Fact]
    public async Task ARateLimitRefusalSaysHowToRaiseTheLimit()
    {
        using var folder = new TempFolder();
        var (client, _) = Build(folder.Path, HttpStatusCode.Forbidden, "{\"message\":\"API rate limit exceeded\"}");
        using var __ = client;

        var result = await client.GetAsync("https://api.github.com/search/issues?q=x", "github-search");

        Assert.False(result.Ok);
        Assert.Contains("rate limit", result.Failure!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("token", result.Failure!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A transport that answers everything the same way and counts how often it was asked.</summary>
    private sealed class CountingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;

            return Task.FromResult(new HttpResponseMessage(status)
            {
                RequestMessage = request,
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
