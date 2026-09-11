using System.Net;
using System.Net.Http.Headers;

namespace FixFinder.Core.Http;

/// <summary>The outcome of one request, in a shape a fix source can act on without try/catch.</summary>
/// <param name="Ok">True for a 2xx response that was read successfully.</param>
/// <param name="Body">The response body, or empty.</param>
/// <param name="FromCache">True when nothing went over the network.</param>
/// <param name="Failure">A plain-English account of what went wrong, for the log and the UI.</param>
public sealed record HttpResult(
    bool Ok, HttpStatusCode Status, string Body, bool FromCache, string? Failure = null)
{
    /// <summary>True when the failure was a CacheOnly miss rather than anything actually wrong.</summary>
    public bool WasCacheMiss => !Ok && Status == HttpStatusCode.GatewayTimeout;
}

/// <summary>
/// The one HTTP client FixFinder uses, with its cache, rate limiter and quota tracking wired up.
/// </summary>
/// <remarks>
/// The handler chain is <c>HttpCacheHandler -> RateLimitHandler -> SocketsHttpHandler</c>, and
/// that order is deliberate: the cache sits above the limiter so a cached answer costs no
/// permit and moves no quota figure.
/// <para>
/// <see cref="DecompressionMethods.All"/> is not optional. Stack Exchange gzips every response
/// whether or not you ask it to, so without automatic decompression you get binary noise and a
/// JSON parse error that gives no hint at all about the real cause.
/// </para>
/// <para>
/// Requests never throw out of here. A crash-fix tool that falls over because GitHub was slow
/// would be worse than useless, so every failure comes back as an <see cref="HttpResult"/> with
/// a description that can go straight into the run log.
/// </para>
/// </remarks>
public sealed class FixFinderHttpClient : IDisposable
{
    /// <summary>
    /// Sent on every request. GitHub rejects requests without a User-Agent outright.
    /// </summary>
    public const string UserAgent = "FixFinder/1.0 (+local-tool)";

    private readonly HttpClient _client;
    private bool _disposed;

    public HttpCache Cache { get; }
    public QuotaTracker Quota { get; }
    public RateLimitHandler Limiter { get; }

    /// <summary>Cache behaviour for requests that do not name their own.</summary>
    public CacheMode DefaultCacheMode { get; set; } = CacheMode.Normal;

    /// <summary>True once a GitHub token has been supplied.</summary>
    public bool HasGitHubToken { get; private set; }

    /// <summary>The Stack Exchange app key, appended as a query parameter by the source.</summary>
    public string? StackExchangeKey { get; private set; }

    public FixFinderHttpClient(
        HttpCache? cache = null, QuotaTracker? quota = null, HttpMessageHandler? transport = null)
    {
        Cache = cache ?? new HttpCache();
        Quota = quota ?? new QuotaTracker();

        transport ??= new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };

        Limiter = new RateLimitHandler(Quota) { InnerHandler = transport };
        var caching = new HttpCacheHandler(Cache) { InnerHandler = Limiter };

        _client = new HttpClient(caching) { Timeout = TimeSpan.FromSeconds(30) };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }

    /// <summary>
    /// Supplies a GitHub personal access token, or clears it.
    /// </summary>
    /// <remarks>
    /// Raises both GitHub budgets, because that is the entire reason to use one here: search
    /// goes from 10 requests a minute to 30, and the general pool from 60 an hour to 5000. A
    /// fine-grained token with <b>no permissions selected at all</b> achieves that, which is
    /// what the Settings window tells you to create - FixFinder only ever reads public data.
    /// </remarks>
    public void SetGitHubToken(string? token)
    {
        var present = !string.IsNullOrWhiteSpace(token);
        HasGitHubToken = present;

        _client.DefaultRequestHeaders.Authorization =
            present ? new AuthenticationHeaderValue("Bearer", token!.Trim()) : null;

        Limiter.SetBudget(new BucketBudget(
            "github-search", present ? 30 : 10, TimeSpan.FromMinutes(1), TimeSpan.Zero));

        Limiter.SetBudget(new BucketBudget(
            "github-core", present ? 5000 : 60, TimeSpan.FromHours(1), TimeSpan.Zero));
    }

    public void SetStackExchangeKey(string? key) =>
        StackExchangeKey = string.IsNullOrWhiteSpace(key) ? null : key.Trim();

    /// <summary>Issues a GET, charged to <paramref name="bucket"/>, and never throws.</summary>
    /// <param name="headers">
    /// Per-request headers. GitHub wants an <c>Accept</c> and an API version that Stack Exchange
    /// would reject, so these belong on the request rather than on the shared client.
    /// </param>
    public async Task<HttpResult> GetAsync(
        string url,
        string bucket,
        CacheMode? mode = null,
        IEnumerable<KeyValuePair<string, string>>? headers = null,
        CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return new HttpResult(false, HttpStatusCode.BadRequest, "", false, $"Not a valid URL: {url}");

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Options.Set(FixFinderRequestOptions.Bucket, bucket);
        request.Options.Set(FixFinderRequestOptions.Cache, mode ?? DefaultCacheMode);

        if (headers is not null)
        {
            foreach (var header in headers)
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        try
        {
            using var response = await _client.SendAsync(request, ct);

            var fromCache =
                response.Headers.TryGetValues(FixFinderRequestOptions.CacheStatusHeader, out var status) &&
                status.FirstOrDefault() == "hit";

            var body = await response.Content.ReadAsStringAsync(ct);

            return response.IsSuccessStatusCode
                ? new HttpResult(true, response.StatusCode, body, fromCache)
                : new HttpResult(false, response.StatusCode, body, fromCache, DescribeFailure(response, body));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return new HttpResult(false, HttpStatusCode.RequestTimeout, "", false,
                $"{uri.Host} did not answer within {_client.Timeout.TotalSeconds:0} seconds.");
        }
        catch (HttpRequestException ex)
        {
            return new HttpResult(false, HttpStatusCode.ServiceUnavailable, "", false,
                $"Could not reach {uri.Host}: {ex.Message}");
        }
    }

    private static string DescribeFailure(HttpResponseMessage response, string body)
    {
        var host = response.RequestMessage?.RequestUri?.Host ?? "the server";

        return (int)response.StatusCode switch
        {
            401 => $"{host} rejected the token. Check it in Settings, or clear it to search anonymously.",
            403 when body.Contains("rate limit", StringComparison.OrdinalIgnoreCase) =>
                $"{host} rate limit reached. Adding a token in Settings raises it substantially.",
            403 => $"{host} refused the request (403). {Excerpt(body)}",
            404 => $"{host} has no such resource (404).",
            422 => $"{host} could not process the query (422). {Excerpt(body)}",
            429 => $"{host} is throttling requests (429). Try again shortly.",
            504 => response.ReasonPhrase ?? "Not cached.",
            _ => $"{host} returned {(int)response.StatusCode} {response.ReasonPhrase}. {Excerpt(body)}",
        };
    }

    private static string Excerpt(string body)
    {
        var flat = body.ReplaceLineEndings(" ").Trim();
        return flat.Length <= 200 ? flat : flat[..199] + "…";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _client.Dispose();
    }
}
