using System.Collections.Concurrent;
using System.Net;

namespace FixFinder.Core.Http;

/// <summary>How many requests one metered pool allows, and how fast.</summary>
public sealed record BucketBudget(
    string Name, int PermitsPerWindow, TimeSpan Window, TimeSpan MinimumSpacing);

/// <summary>Keeps FixFinder inside every published rate limit, and inside any the server asks for.</summary>
public sealed class RateLimitHandler : DelegatingHandler
{
    private readonly ConcurrentDictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);
    private readonly QuotaTracker _quota;

    public Func<DateTimeOffset> Now { get; init; } = () => DateTimeOffset.UtcNow;

    public Func<TimeSpan, CancellationToken, Task> DelayAsync { get; init; } =
        (span, ct) => Task.Delay(span, ct);

    private static readonly TimeSpan MaximumRetryAfter = TimeSpan.FromSeconds(60);

    private static readonly BucketBudget Fallback =
        new("default", 60, TimeSpan.FromMinutes(1), TimeSpan.FromMilliseconds(50));

    public RateLimitHandler(QuotaTracker quota)
    {
        _quota = quota;

        SetBudget(new BucketBudget("github-search", 10, TimeSpan.FromMinutes(1), TimeSpan.Zero));
        SetBudget(new BucketBudget("github-core", 60, TimeSpan.FromHours(1), TimeSpan.Zero));
        SetBudget(new BucketBudget("github-raw", 30, TimeSpan.FromMinutes(1), TimeSpan.FromMilliseconds(200)));
        SetBudget(new BucketBudget("stackexchange", 20, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(100)));
    }

    public void SetBudget(BucketBudget budget) =>
        _buckets.AddOrUpdate(
            budget.Name,
            _ => new Bucket(budget),
            (_, existing) => { existing.Budget = budget; return existing; });

    public void RequireBackoff(string bucket, TimeSpan wait)
    {
        var target = _buckets.GetOrAdd(bucket, name => new Bucket(Fallback with { Name = name }));
        target.BlockUntil(Now() + wait);
    }

    public TimeSpan PeekWait(string bucket) =>
        _buckets.TryGetValue(bucket, out var target) ? target.PeekWait(Now()) : TimeSpan.Zero;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var name = request.Options.TryGetValue(FixFinderRequestOptions.Bucket, out var tagged)
            ? tagged
            : request.RequestUri?.Host ?? "default";

        var bucket = _buckets.GetOrAdd(name, key => new Bucket(Fallback with { Name = key }));

        var response = await SendThroughBucketAsync(bucket, request, cancellationToken);

        ReadRateLimitHeaders(name, response);

        if (IsThrottled(response) && RetryAfter(response) is { } wait && wait <= MaximumRetryAfter)
        {
            bucket.BlockUntil(Now() + wait);
            response.Dispose();

            var retry = await CloneAsync(request);
            response = await SendThroughBucketAsync(bucket, retry, cancellationToken);
            ReadRateLimitHeaders(name, response);
        }

        return response;
    }

    private async Task<HttpResponseMessage> SendThroughBucketAsync(
        Bucket bucket, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await bucket.EnterAsync(Now, DelayAsync, cancellationToken);
        return await base.SendAsync(request, cancellationToken);
    }

    private static bool IsThrottled(HttpResponseMessage response) =>
        response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden;

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter is { } header)
        {
            if (header.Delta is { } delta) return delta;
            if (header.Date is { } date) return date - DateTimeOffset.UtcNow;
        }

        if (response.Headers.TryGetValues("x-ratelimit-reset", out var values) &&
            long.TryParse(values.FirstOrDefault(), out var epoch) &&
            response.Headers.TryGetValues("x-ratelimit-remaining", out var remaining) &&
            remaining.FirstOrDefault() == "0")
        {
            return DateTimeOffset.FromUnixTimeSeconds(epoch) - DateTimeOffset.UtcNow;
        }

        return null;
    }

    private void ReadRateLimitHeaders(string bucket, HttpResponseMessage response)
    {
        int? Header(string name) =>
            response.Headers.TryGetValues(name, out var values) &&
            int.TryParse(values.FirstOrDefault(), out var parsed) ? parsed : null;

        var remaining = Header("x-ratelimit-remaining");
        if (remaining is null) return;

        var reset = Header("x-ratelimit-reset") is { } epoch
            ? DateTimeOffset.FromUnixTimeSeconds(epoch)
            : (DateTimeOffset?)null;

        _quota.Report(new QuotaStatus(bucket, remaining, Header("x-ratelimit-limit"), reset));
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
        };

        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        foreach (var option in (IDictionary<string, object?>)request.Options)
            clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(bytes);

            foreach (var header in request.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }

    /// <summary>One metered pool: a sliding window of recent requests plus any imposed block.</summary>
    private sealed class Bucket(BucketBudget budget)
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly Queue<DateTimeOffset> _recent = new();
        private DateTimeOffset _blockedUntil = DateTimeOffset.MinValue;
        private DateTimeOffset _lastEntered = DateTimeOffset.MinValue;

        public BucketBudget Budget { get; set; } = budget;

        public void BlockUntil(DateTimeOffset until)
        {
            lock (_recent)
            {
                if (until > _blockedUntil) _blockedUntil = until;
            }
        }

        public TimeSpan PeekWait(DateTimeOffset now)
        {
            lock (_recent) return ComputeWait(now);
        }

        public async Task EnterAsync(
            Func<DateTimeOffset> now, Func<TimeSpan, CancellationToken, Task> delay, CancellationToken ct)
        {
            await _gate.WaitAsync(ct);

            try
            {
                while (true)
                {
                    TimeSpan wait;
                    lock (_recent) wait = ComputeWait(now());

                    if (wait <= TimeSpan.Zero) break;
                    await delay(wait, ct);
                }

                lock (_recent)
                {
                    var stamp = now();
                    _recent.Enqueue(stamp);
                    _lastEntered = stamp;
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        private TimeSpan ComputeWait(DateTimeOffset now)
        {
            while (_recent.Count > 0 && now - _recent.Peek() >= Budget.Window) _recent.Dequeue();

            var wait = TimeSpan.Zero;

            if (_blockedUntil > now) wait = _blockedUntil - now;

            if (_recent.Count >= Budget.PermitsPerWindow)
            {
                var untilFree = _recent.Peek() + Budget.Window - now;
                if (untilFree > wait) wait = untilFree;
            }

            if (Budget.MinimumSpacing > TimeSpan.Zero && _lastEntered > DateTimeOffset.MinValue)
            {
                var untilSpaced = _lastEntered + Budget.MinimumSpacing - now;
                if (untilSpaced > wait) wait = untilSpaced;
            }

            return wait;
        }
    }
}
