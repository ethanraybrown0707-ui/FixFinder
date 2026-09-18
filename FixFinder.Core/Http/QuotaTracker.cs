using System.Collections.Concurrent;

namespace FixFinder.Core.Http;

/// <summary>What is left of one API allowance, as the server last reported it.</summary>
public sealed record QuotaStatus(
    string Bucket,
    int? Remaining,
    int? Limit,
    DateTimeOffset? ResetsAt,
    string? Note = null)
{
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

    public bool IsLow => Remaining is { } left && Limit is { } limit && limit > 0
        ? left <= Math.Max(2, limit / 10)
        : Remaining is { } bare && bare <= 2;
}

/// <summary>The last known state of every API allowance, shared by the handlers, the sources and the UI.</summary>
public sealed class QuotaTracker
{
    private readonly ConcurrentDictionary<string, QuotaStatus> _byBucket = new(StringComparer.Ordinal);

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
