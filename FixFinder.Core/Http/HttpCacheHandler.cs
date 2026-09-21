using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace FixFinder.Core.Http;

/// <summary>Serves GET requests from <c>HttpCache</c> before they reach the network.</summary>
public sealed class HttpCacheHandler(HttpCache cache) : DelegatingHandler
{
    private const int MaximumCachedBytes = 4 * 1024 * 1024;

    public HttpCache Cache { get; } = cache;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var mode = request.Options.TryGetValue(FixFinderRequestOptions.Cache, out var requested)
            ? requested
            : CacheMode.Normal;

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

            var replacement = new StringContent(body, Encoding.UTF8,
                response.Content.Headers.ContentType?.MediaType ?? "text/plain");

            response.Content.Dispose();
            response.Content = replacement;
        }

        return Stamp(response, "miss");
    }

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
