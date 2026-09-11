using System.Collections.Concurrent;
using System.Net;

namespace FixFinder.Core.Http;

/// <summary>How many requests one metered pool allows, and how fast.</summary>
/// <param name="Name">Bucket name, matching <see cref="FixFinderRequestOptions.Bucket"/>.</param>
/// <param name="PermitsPerWindow">Requests allowed inside <paramref name="Window"/>.</param>
/// <param name="Window">The sliding window the permits are counted over.</param>
/// <param name="MinimumSpacing">A floor on the gap between two consecutive requests.</param>
public sealed record BucketBudget(
    string Name, int PermitsPerWindow, TimeSpan Window, TimeSpan MinimumSpacing);

/// <summary>
/// Keeps FixFinder inside every published rate limit, and inside any the server asks for.
/// </summary>
/// <remarks>
/// A sliding window rather than a fixed delay between calls, and the difference is the whole
/// point. GitHub's unauthenticated search allowance is ten requests a minute; enforcing that as
/// "one every six seconds" would make a two-call search take six seconds for no reason, because
/// GitHub itself is perfectly happy to take both immediately. A window only makes you wait once
/// you have genuinely spent the allowance.
/// <para>
/// Three things can make a request wait: the window, the minimum spacing, and an explicit
/// instruction from the server - a <c>Retry-After</c> header, or Stack Exchange's
/// <c>backoff</c> field, which arrives in the response body and is pushed in here by the source
/// via <see cref="RequireBackoff"/>. Ignoring that last one earns a temporary ban rather than an
/// error, which is exactly the kind of failure that is miserable to diagnose.
/// </para>
/// </remarks>
public sealed class RateLimitHandler : DelegatingHandler
{
    private readonly ConcurrentDictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);
    private readonly QuotaTracker _quota;

    /// <summary>Injected so tests can drive the limiter without spending real seconds.</summary>
    public Func<DateTimeOffset> Now { get; init; } = () => DateTimeOffset.UtcNow;

    public Func<TimeSpan, CancellationToken, Task> DelayAsync { get; init; } =
        (span, ct) => Task.Delay(span, ct);

    /// <summary>Longest server-requested wait we will sit through rather than giving up.</summary>
    private static readonly TimeSpan MaximumRetryAfter = TimeSpan.FromSeconds(60);

    /// <summary>Fallback for a request that names no bucket.</summary>
    private static readonly BucketBudget Fallback =
        new("default", 60, TimeSpan.FromMinutes(1), TimeSpan.FromMilliseconds(50));

    public RateLimitHandler(QuotaTracker quota)
    {
        _quota = quota;

        // Unauthenticated defaults. FixFinderHttpClient raises the GitHub ones when a token is
        // present - a fine-grained PAT with no permissions at all is enough to do that.
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

    /// <summary>
    /// Blocks a bucket for a period the server asked for, outside of any HTTP response.
    /// </summary>
    /// <remarks>
    /// Stack Exchange puts its <c>backoff</c> in the JSON body, so nothing in the handler chain
    /// can see it. The source reads it and calls this.
    /// </remarks>
    public void RequireBackoff(string bucket, TimeSpan wait)
    {
        var target = _buckets.GetOrAdd(bucket, name => new Bucket(Fallback with { Name = name }));
        target.BlockUntil(Now() + wait);
    }

    /// <summary>How long a request to this bucket would have to wait right now.</summary>
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

        // One retry, and only when the server itself named a short wait. Retrying a limit the
        // server did not ask us to retry is how a rate-limited client becomes a banned one.
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

        // GitHub's primary limit sends no Retry-After; it sends a reset timestamp and a
        // remaining count of zero. Treat that pair as the same instruction.
        if (response.Headers.TryGetValues("x-ratelimit-reset", out var values) &&
            long.TryParse(values.FirstOrDefault(), out var epoch) &&
            response.Headers.TryGetValues("x-ratelimit-remaining", out var remaining) &&
            remaining.FirstOrDefault() == "0")
        {
            return DateTimeOffset.FromUnixTimeSeconds(epoch) - DateTimeOffset.UtcNow;
        }

        return null;
    }

    /// <summary>Records GitHub's per-response allowance figures for the quota labels.</summary>
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

    /// <summary>
    /// A sent <see cref="HttpRequestMessage"/> cannot be sent twice, so a retry needs a copy.
    /// </summary>
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

        /// <summary>
        /// Waits until this request is allowed, then records it.
        /// </summary>
        /// <remarks>
        /// The semaphore is held across the wait on purpose: requests to one bucket are
        /// serialised, so two callers cannot both look at an allowance of one, both decide they
        /// may proceed, and both spend it.
        /// </remarks>
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
