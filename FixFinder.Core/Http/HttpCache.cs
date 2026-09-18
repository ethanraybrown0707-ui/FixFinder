using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FixFinder.Core.Engine;

namespace FixFinder.Core.Http;

/// <summary>One stored HTTP response.</summary>
public sealed record CachedResponse(
    string Url,
    string Method,
    int StatusCode,
    string? ContentType,
    string Body,
    DateTimeOffset StoredUtc);

/// <summary>A plain-file cache of API responses under <c>%LOCALAPPDATA%\FixFinder\cache</c>.</summary>
public sealed class HttpCache
{
    private static readonly string[] CredentialParameters =
        ["key", "access_token", "client_secret", "token", "apikey", "api_key"];

    public string Directory { get; }

    public TimeSpan Ttl { get; init; } = TimeSpan.FromHours(24);

    public HttpCache(string? directory = null)
    {
        Directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FixFinder", "cache");

        System.IO.Directory.CreateDirectory(Directory);
    }

    public static string KeyFor(HttpMethod method, Uri uri)
    {
        var material = $"{method.Method.ToUpperInvariant()} {Canonicalize(uri)}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

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

        var kept = query
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(pair => !CredentialParameters.Contains(
                pair.Split('=', 2)[0], StringComparer.OrdinalIgnoreCase))
            .OrderBy(pair => pair, StringComparer.Ordinal);

        builder.Query = string.Join("&", kept);
        return builder.Uri.ToString();
    }

    private string PathFor(string key) => Path.Combine(Directory, key + ".json");

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
            return null;
        }
    }

    public void Write(string key, CachedResponse entry)
    {
        try
        {
            var path = PathFor(key);
            var temp = path + ".tmp";

            File.WriteAllText(temp, JsonSerializer.Serialize(entry, JsonOptions.Default));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
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
