using System.Net;
using System.Text;
using System.Text.Json;
using FixFinder.Core.Engine;
using FixFinder.Core.Http;

namespace FixFinder.Tests;

/// <summary>Raised when a test asks for a URL that was never recorded.</summary>
public sealed class PlaybackMissException(string message) : Exception(message);

/// <summary>One recorded exchange, as written in a provider's index.json.</summary>
public sealed record RecordedExchange(
    string Url,
    string File,
    int Status = 200,
    Dictionary<string, string>? Headers = null,
    string? ContentType = "application/json",
    string? Note = null);

/// <summary>Replays recorded API responses, and throws on anything not recorded.</summary>
public sealed class PlaybackHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, (RecordedExchange Exchange, string Directory)> _byUrl = new(StringComparer.Ordinal);

    public List<string> Requested { get; } = [];

    public int RequestCount => Requested.Count;

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
        lock (Requested) Requested.Add(canonical);

        if (!_byUrl.TryGetValue(canonical, out var found))
        {
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
