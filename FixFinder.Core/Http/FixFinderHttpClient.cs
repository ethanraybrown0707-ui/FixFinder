using System.Net;
using System.Net.Http.Headers;

namespace FixFinder.Core.Http;

/// <summary>The outcome of one request, in a shape a fix source can act on without try/catch.</summary>
public sealed record HttpResult(
    bool Ok, HttpStatusCode Status, string Body, bool FromCache, string? Failure = null)
{
    public bool WasCacheMiss => !Ok && Status == HttpStatusCode.GatewayTimeout;
}

/// <summary>The one HTTP client FixFinder uses, with its cache, rate limiter and quota tracking wired up.</summary>
public sealed class FixFinderHttpClient : IDisposable
{
    public const string UserAgent = "FixFinder/1.0 (+local-tool)";

    private readonly HttpClient _client;
    private bool _disposed;

    public HttpCache Cache { get; }
    public QuotaTracker Quota { get; }
    public RateLimitHandler Limiter { get; }

    public CacheMode DefaultCacheMode { get; set; } = CacheMode.Normal;

    public bool HasGitHubToken { get; private set; }

    public string? StackExchangeKey { get; private set; }

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public FixFinderHttpClient(
        HttpCache? cache = null, QuotaTracker? quota = null, HttpMessageHandler? transport = null, TimeSpan? timeout = null)
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

        _client = new HttpClient(caching) { Timeout = timeout ?? DefaultTimeout };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }

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
