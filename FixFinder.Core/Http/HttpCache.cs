using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FixFinder.Core.Engine;

namespace FixFinder.Core.Http;

/// <summary>One stored HTTP response.</summary>
/// <param name="Url">The request URL, with any credential query parameter stripped out.</param>
/// <param name="StatusCode">The status the server returned.</param>
/// <param name="Body">The response body as text. Only text responses are cached.</param>
public sealed record CachedResponse(
    string Url,
    string Method,
    int StatusCode,
    string? ContentType,
    string Body,
    DateTimeOffset StoredUtc);

/// <summary>
/// A plain-file cache of API responses under <c>%LOCALAPPDATA%\FixFinder\cache</c>.
/// </summary>
/// <remarks>
/// Deliberately simple and inspectable: one indented JSON file per request, named after a hash
/// of the request, that you can open in Notepad to see exactly what GitHub sent back. For a
/// tool whose whole premise is "no AI, just code you can read", an opaque binary cache would be
/// the wrong shape.
/// <para>
/// Only text responses are stored. Nothing FixFinder fetches is binary - JSON from two APIs and
/// unified diffs as plain text - and refusing to cache anything else keeps the files readable.
/// </para>
/// </remarks>
public sealed class HttpCache
{
    /// <summary>
    /// Query parameters that carry a credential and must never reach the disk.
    /// </summary>
    /// <remarks>
    /// The Stack Exchange app key travels as a query parameter rather than a header, so the
    /// request URL itself is sensitive. It is stripped before the URL is hashed and before it
    /// is written, which also means the cache stays valid when you add or remove a key - the
    /// key changes your quota, never the results.
    /// </remarks>
    private static readonly string[] CredentialParameters =
        ["key", "access_token", "client_secret", "token", "apikey", "api_key"];

    public string Directory { get; }

    /// <summary>How long an entry is served before it is refetched in <see cref="CacheMode.Normal"/>.</summary>
    public TimeSpan Ttl { get; init; } = TimeSpan.FromHours(24);

    public HttpCache(string? directory = null)
    {
        Directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FixFinder", "cache");

        System.IO.Directory.CreateDirectory(Directory);
    }

    /// <summary>
    /// The cache key for a request: SHA-256 of the method and a canonical form of the URL.
    /// </summary>
    /// <remarks>
    /// Canonical means lower-cased scheme and host, credential parameters dropped, and the
    /// remaining query parameters sorted. Sorting matters because the same logical request
    /// assembled by two different code paths would otherwise miss its own cache entry, and it
    /// is what lets the recorded test fixtures be named by this same function.
    /// </remarks>
    public static string KeyFor(HttpMethod method, Uri uri)
    {
        var material = $"{method.Method.ToUpperInvariant()} {Canonicalize(uri)}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

    /// <summary>The URL as it is hashed and stored: sorted query, credentials stripped.</summary>
    public static string Canonicalize(Uri uri)
    {
        var builder = new UriBuilder(uri) { Fragment = "" };
        builder.Scheme = builder.Scheme.ToLowerInvariant();
        builder.Host = builder.Host.ToLowerInvariant();

        var query = uri.Query.TrimStart('?');

        if (query.Length == 0)
        {
            builder.Query = "";
            return builder.Uri.ToString();
        }

        // Dropped outright rather than replaced with a placeholder. A placeholder would still be
        // a parameter, so the keyed and unkeyed forms of the same request would hash differently
        // and adding an app key would silently discard every entry already cached - for a
        // credential that changes the size of your allowance and nothing whatsoever about the
        // results. Dropping it also means no trace of a secret reaches the disk at all.
        var kept = query
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(pair => !CredentialParameters.Contains(
                pair.Split('=', 2)[0], StringComparer.OrdinalIgnoreCase))
            .OrderBy(pair => pair, StringComparer.Ordinal);

        builder.Query = string.Join("&", kept);
        return builder.Uri.ToString();
    }

    private string PathFor(string key) => Path.Combine(Directory, key + ".json");

    /// <summary>
    /// Reads a stored response, or null.
    /// </summary>
    /// <param name="ignoreAge">
    /// True in <see cref="CacheMode.CacheOnly"/>, where a stale answer is the only answer
    /// available and is far more useful than a failure.
    /// </param>
    public CachedResponse? TryRead(string key, bool ignoreAge = false)
    {
        var path = PathFor(key);
        if (!File.Exists(path)) return null;

        try
        {
            var entry = JsonSerializer.Deserialize<CachedResponse>(
                File.ReadAllText(path), JsonOptions.Default);

            if (entry is null) return null;
            if (ignoreAge) return entry;

            return DateTimeOffset.UtcNow - entry.StoredUtc <= Ttl ? entry : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt or half-written entry is not worth a crash: treat it as a miss and let
            // the request go to the network, which will overwrite it.
            return null;
        }
    }

    public void Write(string key, CachedResponse entry)
    {
        try
        {
            // Temp file plus a move, so a cancelled run can never leave a truncated entry that
            // a later run would read back as a real response.
            var path = PathFor(key);
            var temp = path + ".tmp";

            File.WriteAllText(temp, JsonSerializer.Serialize(entry, JsonOptions.Default));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Caching is an optimisation. Failing to store must never fail the request.
        }
    }

    public int Count => System.IO.Directory.EnumerateFiles(Directory, "*.json").Count();

    public void Clear()
    {
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*.json"))
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }
}
