using System.Net;
using System.Text;
using System.Text.Json;
using FixFinder.Core.Engine;
using FixFinder.Core.Http;

namespace FixFinder.Tests;

/// <summary>Raised when a test asks for a URL that was never recorded.</summary>
public sealed class PlaybackMissException(string message) : Exception(message);

/// <summary>One recorded exchange, as written in a provider's index.json.</summary>
/// <param name="Url">The request URL. Matched after canonicalisation, so parameter order is free.</param>
/// <param name="File">The file beside index.json holding the response body verbatim.</param>
/// <param name="Status">HTTP status to replay. Defaults to 200.</param>
/// <param name="Headers">Response headers worth replaying, such as the rate-limit ones.</param>
/// <param name="Note">Why this fixture exists. Ignored at run time; read by people.</param>
public sealed record RecordedExchange(
    string Url,
    string File,
    int Status = 200,
    Dictionary<string, string>? Headers = null,
    string? ContentType = "application/json",
    string? Note = null);

/// <summary>
/// Replays recorded API responses, and throws on anything not recorded.
/// </summary>
/// <remarks>
/// Throwing on a miss is the entire point. A handler that fell through to the real network on an
/// unrecorded URL would give a suite that passes on this machine, fails in a tunnel, and quietly
/// spends a rate-limit allowance every time it runs - and the failure would show up as a
/// mysterious timeout in an unrelated test months later.
/// <para>
/// Fixtures are indexed by URL in a readable <c>index.json</c> rather than stored under hashed
/// file names. A hash is stable and needs no index, but it makes the fixture folder unreadable:
/// working out which recorded response a failing test wanted means computing hashes by hand.
/// The names here say what they hold, and matching still goes through
/// <see cref="HttpCache.Canonicalize"/>, so query-parameter order cannot break a match.
/// </para>
/// </remarks>
public sealed class PlaybackHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, (RecordedExchange Exchange, string Directory)> _byUrl = new(StringComparer.Ordinal);

    /// <summary>Every URL asked for, in order, so a test can assert how many calls were made.</summary>
    public List<string> Requested { get; } = [];

    public int RequestCount => Requested.Count;

    /// <param name="providers">
    /// Folder names under <c>Fixtures\Http</c>, such as "stackoverflow" or "github".
    /// </param>
    public PlaybackHttpMessageHandler(params string[] providers)
    {
        foreach (var provider in providers) Load(provider);
    }

    private void Load(string provider)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Http", provider);
        var indexPath = Path.Combine(directory, "index.json");

        if (!File.Exists(indexPath))
            throw new FileNotFoundException($"No HTTP fixture index for '{provider}'.", indexPath);

        var exchanges = JsonSerializer.Deserialize<List<RecordedExchange>>(
            File.ReadAllText(indexPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

        foreach (var exchange in exchanges)
        {
            var key = HttpCache.Canonicalize(new Uri(exchange.Url));
            _byUrl[key] = (exchange, directory);
        }
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var canonical = HttpCache.Canonicalize(request.RequestUri!);
        // Locked because a candidate's patches are fetched at the same time.
        lock (Requested) Requested.Add(canonical);

        if (!_byUrl.TryGetValue(canonical, out var found))
        {
            // Lists what was available. A playback miss is nearly always a query the source now
            // builds slightly differently, and seeing both forms side by side turns a puzzle
            // into a diff.
            var known = string.Join("\n  ", _byUrl.Keys.OrderBy(k => k, StringComparer.Ordinal));

            throw new PlaybackMissException(
                $"No recorded response for:\n  {canonical}\n\nRecorded URLs:\n  {known}");
        }

        var (exchange, directory) = found;
        var bodyPath = Path.Combine(directory, exchange.File);

        if (!File.Exists(bodyPath))
            throw new FileNotFoundException($"Fixture body missing for {exchange.Url}.", bodyPath);

        var response = new HttpResponseMessage((HttpStatusCode)exchange.Status)
        {
            RequestMessage = request,
            Content = new StringContent(
                File.ReadAllText(bodyPath), Encoding.UTF8, exchange.ContentType ?? "application/json"),
        };

        foreach (var header in exchange.Headers ?? [])
        {
            if (!response.Headers.TryAddWithoutValidation(header.Key, header.Value))
                response.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return Task.FromResult(response);
    }

    /// <summary>
    /// Builds a client wired to this handler, with a cache in a throwaway folder.
    /// </summary>
    /// <remarks>
    /// The cache directory must be per-test. Sharing the real one would let a response recorded
    /// by one test satisfy another test's request, which is the same silent-pass problem the
    /// miss exception exists to prevent.
    /// </remarks>
    public FixFinderHttpClient CreateClient(string cacheDirectory) =>
        new(new HttpCache(cacheDirectory), new QuotaTracker(), this);
}

/// <summary>A temporary folder that deletes itself, for tests that write a cache or a source tree.</summary>
public sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "fixfinder-tests-" + Guid.NewGuid().ToString("N")[..8]);

    public TempFolder() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>Ignores the JsonOptions indentation setting when reading fixtures written by hand.</summary>
internal static class FixtureJson
{
    public static readonly JsonSerializerOptions Reading = new(JsonOptions.Default)
    {
        PropertyNameCaseInsensitive = true,
    };
}
