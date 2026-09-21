namespace FixFinder.Core.Http;

/// <summary>How a request should treat the on-disk response cache.</summary>
public enum CacheMode
{
    Normal,

    CacheOnly,

    Refresh,
}

/// <summary>Per-request switches read by <c>HttpCacheHandler</c> and <c>RateLimitHandler</c>.</summary>
public static class FixFinderRequestOptions
{
    public static readonly HttpRequestOptionsKey<string> Bucket = new("FixFinder.Bucket");

    public static readonly HttpRequestOptionsKey<CacheMode> Cache = new("FixFinder.CacheMode");

    public const string CacheStatusHeader = "X-FixFinder-Cache";
}
