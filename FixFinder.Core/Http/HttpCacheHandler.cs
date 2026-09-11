using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace FixFinder.Core.Http;

/// <summary>
/// Serves GET requests from <see cref="HttpCache"/> before they reach the network.
/// </summary>
/// <remarks>
/// Sits above <see cref="RateLimitHandler"/> in the chain, which is the important part of the
/// arrangement: a cache hit never reaches the limiter, so it costs no permit and moves no quota
/// figure. That is what makes the CacheOnly switch in the window a genuine guarantee rather
/// than a hint.
/// <para>
/// Only successful responses are stored. Caching a 403 from a rate limit would lock the tool
/// out for the whole 24-hour TTL and, worse, would look exactly like a real answer.
/// </para>
/// </remarks>
public sealed class HttpCacheHandler(HttpCache cache) : DelegatingHandler
{
    /// <summary>Largest body worth storing. Anything above this is streamed straight through.</summary>
    private const int MaximumCachedBytes = 4 * 1024 * 1024;

    public HttpCache Cache { get; } = cache;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var mode = request.Options.TryGetValue(FixFinderRequestOptions.Cache, out var requested)
            ? requested
            : CacheMode.Normal;

        // Only GET is cacheable. FixFinder sends nothing else, but a POST served from a cache
        // would be a genuinely dangerous bug rather than a stale one.
        if (request.Method != HttpMethod.Get || request.RequestUri is null)
            return Stamp(await base.SendAsync(request, cancellationToken), "bypass");

        var key = HttpCache.KeyFor(request.Method, request.RequestUri);

        if (mode != CacheMode.Refresh)
        {
            var stored = Cache.TryRead(key, ignoreAge: mode == CacheMode.CacheOnly);
            if (stored is not null) return Stamp(ToResponse(stored, request), "hit");
        }

        if (mode == CacheMode.CacheOnly)
        {
            // A miss under CacheOnly is a refusal, not a failure to be retried. Says so plainly,
            // because the alternative is a connection error that reads like the network is down.
            return Stamp(new HttpResponseMessage(HttpStatusCode.GatewayTimeout)
            {
                RequestMessage = request,
                ReasonPhrase = "Not in the FixFinder cache",
                Content = new StringContent(
                    "Cached results only is switched on, and this request has no stored response. " +
                    "Untick it to fetch this one from the network.",
                    Encoding.UTF8, "text/plain"),
            }, "miss");
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode && IsText(response.Content.Headers.ContentType))
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (Encoding.UTF8.GetByteCount(body) <= MaximumCachedBytes)
            {
                Cache.Write(key, new CachedResponse(
                    HttpCache.Canonicalize(request.RequestUri),
                    request.Method.Method,
                    (int)response.StatusCode,
                    response.Content.Headers.ContentType?.MediaType,
                    body,
                    DateTimeOffset.UtcNow));
            }

            // The body has already been read to the end, so hand back a fresh content object
            // rather than a stream the caller would find empty.
            var replacement = new StringContent(body, Encoding.UTF8,
                response.Content.Headers.ContentType?.MediaType ?? "text/plain");

            response.Content.Dispose();
            response.Content = replacement;
        }

        return Stamp(response, "miss");
    }

    /// <summary>
    /// True for the content types FixFinder actually fetches: JSON from both APIs, and unified
    /// diffs served as plain text.
    /// </summary>
    private static bool IsText(MediaTypeHeaderValue? contentType)
    {
        var media = contentType?.MediaType;
        if (media is null) return false;

        return media.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
               media.Contains("json", StringComparison.OrdinalIgnoreCase) ||
               media.Contains("xml", StringComparison.OrdinalIgnoreCase) ||
               media.Contains("diff", StringComparison.OrdinalIgnoreCase) ||
               media.Contains("patch", StringComparison.OrdinalIgnoreCase);
    }

    private static HttpResponseMessage ToResponse(CachedResponse entry, HttpRequestMessage request) =>
        new((HttpStatusCode)entry.StatusCode)
        {
            RequestMessage = request,
            Content = new StringContent(entry.Body, Encoding.UTF8, entry.ContentType ?? "text/plain"),
        };

    private static HttpResponseMessage Stamp(HttpResponseMessage response, string status)
    {
        response.Headers.TryAddWithoutValidation(FixFinderRequestOptions.CacheStatusHeader, status);
        return response;
    }
}
