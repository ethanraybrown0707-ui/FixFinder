namespace FixFinder.Core.Http;

/// <summary>How a request should treat the on-disk response cache.</summary>
public enum CacheMode
{
    /// <summary>Serve from cache while the entry is inside its TTL, otherwise fetch and store.</summary>
    Normal,

    /// <summary>
    /// Never touch the network. A hit is served no matter how old it is; a miss fails.
    /// </summary>
    /// <remarks>
    /// Not a nicety - it is what makes this tool developable. GitHub allows ten search requests
    /// a minute unauthenticated, which disappears in about a minute of tuning the ranker, and
    /// once it is gone you are locked out of testing for the rest of that minute. Recording a
    /// handful of real responses and then working entirely against them removes the network
    /// from the loop completely.
    /// </remarks>
    CacheOnly,

    /// <summary>Always fetch, ignoring any stored entry, and overwrite it with the result.</summary>
    Refresh,
}

/// <summary>
/// Per-request switches read by <see cref="HttpCacheHandler"/> and <see cref="RateLimitHandler"/>.
/// </summary>
/// <remarks>
/// Carried in <see cref="System.Net.Http.HttpRequestMessage.Options"/> rather than in custom
/// headers, because these never travel over the wire - they are instructions to our own handler
/// chain, and a header would be sent to GitHub along with everything else.
/// </remarks>
public static class FixFinderRequestOptions
{
    /// <summary>
    /// Which rate-limit bucket this request is charged to.
    /// </summary>
    /// <remarks>
    /// Cannot be the host name, and this is the detail that makes the limiter correct rather
    /// than approximate: <c>api.github.com</c> serves two independently metered pools. Search
    /// allows 10 requests a minute unauthenticated, while everything else shares a separate
    /// 60-an-hour budget. Charging both to one bucket would either throttle search far harder
    /// than GitHub does, or let the timeline calls quietly exhaust the hourly allowance.
    /// </remarks>
    public static readonly HttpRequestOptionsKey<string> Bucket = new("FixFinder.Bucket");

    /// <summary>Cache behaviour for this request. Defaults to <see cref="CacheMode.Normal"/>.</summary>
    public static readonly HttpRequestOptionsKey<CacheMode> Cache = new("FixFinder.CacheMode");

    /// <summary>Response header stamped by the cache handler: <c>hit</c>, <c>miss</c> or <c>bypass</c>.</summary>
    /// <remarks>
    /// Read by the sources so a cached Stack Exchange response does not re-report its (stale)
    /// quota figures, which would make the quota label in the window drift downwards while no
    /// requests were being made at all.
    /// </remarks>
    public const string CacheStatusHeader = "X-FixFinder-Cache";
}
