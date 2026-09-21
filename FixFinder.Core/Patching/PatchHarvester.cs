using FixFinder.Core.Http;
using FixFinder.Core.Sources;

namespace FixFinder.Core.Patching;

/// <summary>One patch URL that was fetched, and what came back.</summary>
public sealed record FetchedPatch(string Url, ParsedPatch Patch, bool FromCache, string? Failure = null)
{
    public bool Ok => Failure is null && Patch.Ok;

    public string Summary => Failure is not null
        ? $"{Url} — could not be fetched: {Failure}"
        : $"{Url} — {Patch.Summary}";
}

/// <summary>Everything appliable or readable that could be got out of one candidate.</summary>
public sealed record HarvestResult(
    IReadOnlyList<CodeBlock> Blocks,
    IReadOnlyList<FetchedPatch> Fetched,
    int NetworkRequests)
{
    public IReadOnlyList<ParsedPatch> Patches =>
    [
        .. Fetched.Where(f => f.Ok).Select(f => f.Patch),
        .. Blocks.Where(b => b.IsAppliablePatch).Select(b => b.Patch!),
    ];

    public IReadOnlyList<CodeBlock> Snippets => [.. Blocks.Where(b => !b.IsAppliablePatch)];

    public bool HasAppliablePatch => Patches.Count > 0;

    public string Summary
    {
        get
        {
            var parts = new List<string>();

            if (Patches.Count > 0)
                parts.Add($"{Patches.Count} patch(es) totalling {Patches.Sum(p => p.TotalHunks)} hunk(s)");

            var refused = Fetched.Count(f => !f.Ok) + Blocks.Count(b => b.IsFailedPatch);
            if (refused > 0) parts.Add($"{refused} that looked like a diff but could not be used");

            var snippets = Snippets.Count(b => !b.IsFailedPatch);
            if (snippets > 0) parts.Add($"{snippets} code block(s) to read");

            return parts.Count == 0 ? "Nothing to apply and no code blocks in it." : string.Join(", ", parts);
        }
    }
}

/// <summary>Collects the patches and code behind one candidate.</summary>
public sealed class PatchHarvester(FixFinderHttpClient http)
{
    private static readonly string[] AllowedHosts = ["github.com", "www.github.com", "patch-diff.githubusercontent.com"];

    private const int MaximumFetches = 4;

    private const int MaximumPatchBytes = 2 * 1024 * 1024;

    public event Action<string>? Log;

    public async Task<HarvestResult> HarvestAsync(
        FixCandidate candidate, CacheMode? cacheMode = null, CancellationToken cancellationToken = default)
    {
        var blocks = CodeBlockExtractor.Extract(candidate.RawBody, candidate.RawBodyIsHtml);

        if (blocks.Count > 0)
            Log?.Invoke($"{candidate.Id}: {blocks.Count} code block(s) in the body.");

        var fetched = new List<FetchedPatch>();
        var requests = 0;

        cancellationToken.ThrowIfCancellationRequested();

        var urls = candidate.LinkedPatchUrls.Take(MaximumFetches).ToList();
        var fetches = new Dictionary<string, Task<FetchedPatch>>(StringComparer.Ordinal);

        foreach (var url in urls)
        {
            if (!fetches.ContainsKey(url)) fetches[url] = FetchAsync(url, cacheMode, cancellationToken);
        }

        await Task.WhenAll(fetches.Values);

        var counted = new HashSet<string>(StringComparer.Ordinal);

        foreach (var url in urls)
        {
            var result = await fetches[url];
            if (!result.FromCache && counted.Add(url)) requests++;

            fetched.Add(result);
            Log?.Invoke($"{candidate.Id}: {result.Summary}");
        }

        var harvest = new HarvestResult(blocks, fetched, requests);

        if (harvest.HasAppliablePatch && candidate.Tier == FixTier.Advisory)
        {
            candidate.Tier = FixTier.AutoAppliable;
            Log?.Invoke($"{candidate.Id}: promoted to auto-appliable - {harvest.Patches.Count} patch(es) parsed.");
        }

        return harvest;
    }

    private async Task<FetchedPatch> FetchAsync(string url, CacheMode? cacheMode, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return new FetchedPatch(url, ParsedPatch.Refused("not a valid URL"), false, "not a valid URL");

        if (uri.Scheme != Uri.UriSchemeHttps)
            return new FetchedPatch(url, ParsedPatch.Refused("refused"), false, "only https URLs are fetched");

        if (!AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
        {
            return new FetchedPatch(url, ParsedPatch.Refused("refused"), false,
                $"{uri.Host} is not a host FixFinder fetches patches from");
        }

        var result = await http.GetAsync(url, GitHubFixSource.RawBucket, cacheMode, ct: ct);

        if (!result.Ok)
            return new FetchedPatch(url, ParsedPatch.Refused("could not be fetched"), result.FromCache, result.Failure);

        if (result.Body.Length > MaximumPatchBytes)
        {
            return new FetchedPatch(url,
                ParsedPatch.Refused($"the patch is {result.Body.Length / 1024} KB, far larger than any single fix"),
                result.FromCache);
        }

        return new FetchedPatch(url, UnifiedDiffParser.Parse(result.Body), result.FromCache);
    }
}
