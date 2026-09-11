using System.Collections.Concurrent;

namespace FixFinder.Core.Http;

/// <summary>
/// What is left of one API allowance, as the server last reported it.
/// </summary>
/// <param name="Bucket">The pool this describes - "github-search", "stackexchange" and so on.</param>
/// <param name="Remaining">Requests left, or null if the server did not say.</param>
/// <param name="Limit">The size of the allowance, or null.</param>
/// <param name="ResetsAt">When the allowance refills, or null.</param>
/// <param name="Note">Anything else worth showing, such as "raise this by adding a token".</param>
public sealed record QuotaStatus(
    string Bucket,
    int? Remaining,
    int? Limit,
    DateTimeOffset? ResetsAt,
    string? Note = null)
{
    /// <summary>Short form for the label under the search box.</summary>
    public string Display
    {
        get
        {
            if (Remaining is null) return $"{Bucket}: unknown";

            var of = Limit is not null ? $"/{Limit}" : "";
            var resets = ResetsAt is { } at && at > DateTimeOffset.UtcNow
                ? $", resets {at.ToLocalTime():HH:mm:ss}"
                : "";

            return $"{Bucket}: {Remaining}{of} left{resets}";
        }
    }

    /// <summary>True once the allowance is low enough that the UI should say so.</summary>
    public bool IsLow => Remaining is { } left && Limit is { } limit && limit > 0
        ? left <= Math.Max(2, limit / 10)
        : Remaining is { } bare && bare <= 2;
}

/// <summary>
/// The last known state of every API allowance, shared by the handlers, the sources and the UI.
/// </summary>
/// <remarks>
/// Reported from two quite different places, which is why this is a single shared object rather
/// than a property on each source: GitHub sends its figures in <c>x-ratelimit-*</c> response
/// headers, so <see cref="RateLimitHandler"/> reads them for free on every response, while
/// Stack Exchange puts <c>quota_remaining</c> in the JSON body, so only the source can see it.
/// </remarks>
public sealed class QuotaTracker
{
    private readonly ConcurrentDictionary<string, QuotaStatus> _byBucket = new(StringComparer.Ordinal);

    /// <summary>Raised whenever a figure changes, so the window can update its labels.</summary>
    public event Action<QuotaStatus>? Changed;

    public void Report(QuotaStatus status)
    {
        _byBucket[status.Bucket] = status;
        Changed?.Invoke(status);
    }

    public QuotaStatus? Get(string bucket) =>
        _byBucket.TryGetValue(bucket, out var status) ? status : null;

    public IReadOnlyList<QuotaStatus> All() =>
        _byBucket.Values.OrderBy(q => q.Bucket, StringComparer.Ordinal).ToArray();
}
