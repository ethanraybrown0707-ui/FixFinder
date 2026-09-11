using System.Net;
using FixFinder.Core.Http;

namespace FixFinder.Tests;

/// <summary>
/// Covers the rate limiter against a virtual clock, so the suite never spends a real second.
/// </summary>
/// <remarks>
/// The clock is injected rather than mocked with a framework: the limiter takes a
/// <c>Now</c> and a <c>DelayAsync</c>, and the tests supply a pair where sleeping simply moves
/// the clock forward. That makes a test of a one-hour window run instantly and, more usefully,
/// makes the assertion exact - the test can say the limiter waited precisely sixty seconds
/// rather than that it waited "a while".
/// </remarks>
public class RateLimitTests
{
    /// <summary>Builds a limiter over a virtual clock. Returns the client and the sleeps it took.</summary>
    private static (HttpClient Client, List<TimeSpan> Sleeps, RateLimitHandler Limiter, QuotaTracker Quota) Build(
        BucketBudget budget, Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
    {
        var now = DateTimeOffset.UnixEpoch;
        var sleeps = new List<TimeSpan>();
        var quota = new QuotaTracker();

        var limiter = new RateLimitHandler(quota)
        {
            InnerHandler = new StubHandler(respond ?? (_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            })),

            Now = () => now,
            DelayAsync = (span, _) =>
            {
                sleeps.Add(span);
                now += span;
                return Task.CompletedTask;
            },
        };

        limiter.SetBudget(budget);

        return (new HttpClient(limiter), sleeps, limiter, quota);
    }

    private static HttpRequestMessage Request(string bucket, string url = "https://example.test/x")
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Options.Set(FixFinderRequestOptions.Bucket, bucket);
        return request;
    }

    /// <summary>
    /// The behaviour that separates a window from a fixed delay: a burst inside the allowance
    /// goes straight through, and only the request that would exceed it waits.
    /// </summary>
    [Fact]
    public async Task ABurstInsideTheAllowanceIsNotSlowedDownAtAll()
    {
        var (client, sleeps, _, _) = Build(new BucketBudget("test", 3, TimeSpan.FromMinutes(1), TimeSpan.Zero));

        for (var i = 0; i < 3; i++) await client.SendAsync(Request("test"));

        Assert.Empty(sleeps);
    }

    [Fact]
    public async Task TheRequestThatWouldExceedTheAllowanceWaitsForTheWindowToRollOver()
    {
        var (client, sleeps, _, _) = Build(new BucketBudget("test", 3, TimeSpan.FromMinutes(1), TimeSpan.Zero));

        for (var i = 0; i < 4; i++) await client.SendAsync(Request("test"));

        Assert.Single(sleeps);
        Assert.Equal(TimeSpan.FromMinutes(1), sleeps[0]);
    }

    [Fact]
    public async Task MinimumSpacingIsAppliedBetweenConsecutiveRequests()
    {
        var (client, sleeps, _, _) = Build(
            new BucketBudget("test", 100, TimeSpan.FromMinutes(1), TimeSpan.FromMilliseconds(250)));

        await client.SendAsync(Request("test"));
        await client.SendAsync(Request("test"));

        Assert.Single(sleeps);
        Assert.Equal(TimeSpan.FromMilliseconds(250), sleeps[0]);
    }

    /// <summary>
    /// GitHub meters search and everything else separately, so the buckets must not share.
    /// </summary>
    /// <remarks>
    /// The failure this prevents is quiet and expensive: charge both to one bucket and the five
    /// timeline lookups after a search would each be throttled against the search allowance,
    /// making a normal run take minutes for no reason at all.
    /// </remarks>
    [Fact]
    public async Task SpendingOneBucketDoesNotThrottleAnother()
    {
        var (client, sleeps, limiter, _) = Build(new BucketBudget("github-search", 2, TimeSpan.FromMinutes(1), TimeSpan.Zero));
        limiter.SetBudget(new BucketBudget("github-core", 2, TimeSpan.FromHours(1), TimeSpan.Zero));

        await client.SendAsync(Request("github-search"));
        await client.SendAsync(Request("github-search"));
        await client.SendAsync(Request("github-core"));
        await client.SendAsync(Request("github-core"));

        Assert.Empty(sleeps);
    }

    /// <summary>
    /// Stack Exchange sends its backoff in the response body, so the source pushes it in here.
    /// </summary>
    [Fact]
    public async Task ABackoffPushedInFromAResponseBodyBlocksTheNextRequest()
    {
        var (client, sleeps, limiter, _) = Build(
            new BucketBudget("stackexchange", 100, TimeSpan.FromSeconds(1), TimeSpan.Zero));

        await client.SendAsync(Request("stackexchange"));
        limiter.RequireBackoff("stackexchange", TimeSpan.FromSeconds(10));

        await client.SendAsync(Request("stackexchange"));

        Assert.Single(sleeps);
        Assert.Equal(TimeSpan.FromSeconds(10), sleeps[0]);
    }

    [Fact]
    public void PeekWaitReportsThePendingBackoffWithoutSpendingAPermit()
    {
        var (_, _, limiter, _) = Build(new BucketBudget("stackexchange", 100, TimeSpan.FromSeconds(1), TimeSpan.Zero));

        Assert.Equal(TimeSpan.Zero, limiter.PeekWait("stackexchange"));

        limiter.RequireBackoff("stackexchange", TimeSpan.FromSeconds(30));

        Assert.Equal(TimeSpan.FromSeconds(30), limiter.PeekWait("stackexchange"));
    }

    // ------------------------------------------------------------------ quota headers

    [Fact]
    public async Task RateLimitHeadersAreReadIntoTheQuotaForThatBucket()
    {
        var (client, _, _, quota) = Build(
            new BucketBudget("github-search", 100, TimeSpan.FromMinutes(1), TimeSpan.Zero),
            _ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
                };

                response.Headers.TryAddWithoutValidation("x-ratelimit-limit", "30");
                response.Headers.TryAddWithoutValidation("x-ratelimit-remaining", "27");
                response.Headers.TryAddWithoutValidation("x-ratelimit-reset", "4102444800");

                return response;
            });

        await client.SendAsync(Request("github-search"));

        var status = quota.Get("github-search");

        Assert.NotNull(status);
        Assert.Equal(27, status!.Remaining);
        Assert.Equal(30, status.Limit);
        Assert.Contains("27/30 left", status.Display, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AResponseWithoutRateLimitHeadersLeavesTheQuotaUnknown()
    {
        var (client, _, _, quota) = Build(new BucketBudget("test", 100, TimeSpan.FromMinutes(1), TimeSpan.Zero));

        await client.SendAsync(Request("test"));

        Assert.Null(quota.Get("test"));
    }

    [Fact]
    public void QuotaCountsAsLowOnlyWhenLittleIsLeft()
    {
        Assert.False(new QuotaStatus("x", 27, 30, null).IsLow);
        Assert.True(new QuotaStatus("x", 2, 30, null).IsLow);
        Assert.True(new QuotaStatus("x", 0, 5000, null).IsLow);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = respond(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
