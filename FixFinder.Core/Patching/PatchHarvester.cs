using FixFinder.Core.Http;
using FixFinder.Core.Sources;

namespace FixFinder.Core.Patching;

/// <summary>One patch URL that was fetched, and what came back.</summary>
/// <param name="Url">The <c>.patch</c> or <c>.diff</c> URL.</param>
/// <param name="Patch">The parsed result, usable or refused.</param>
/// <param name="FromCache">True when nothing went over the network.</param>
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
    /// <summary>Every patch that parsed, from the body and from linked commits alike.</summary>
    public IReadOnlyList<ParsedPatch> Patches =>
    [
        .. Fetched.Where(f => f.Ok).Select(f => f.Patch),
        .. Blocks.Where(b => b.IsAppliablePatch).Select(b => b.Patch!),
    ];

    /// <summary>Code that is worth reading but cannot be applied - the Tier B material.</summary>
    public IReadOnlyList<CodeBlock> Snippets => [.. Blocks.Where(b => !b.IsAppliablePatch)];

    public bool HasAppliablePatch => Patches.Count > 0;

    /// <summary>Plain account of what was found, shown above the preview.</summary>
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

/// <summary>
/// Collects the patches and code behind one candidate.
/// </summary>
/// <remarks>
/// Two sources of material, and only one of them can ever be applied. Code blocks inside the
/// body are read locally and cost nothing; linked commits and pull requests are fetched from
/// github.com as plain text, which is worth doing precisely because those URLs are public and
/// unauthenticated and therefore spend none of the API allowance that actually runs out.
/// <para>
/// Nothing here is fetched until the user asks to preview a specific candidate. A search returns
/// thirty results, and downloading the diffs for all of them on the chance one gets opened would
/// be both slow and rude to the servers involved.
/// </para>
/// </remarks>
public sealed class PatchHarvester(FixFinderHttpClient http)
{
    /// <summary>Hosts a patch may be fetched from. Nothing else is ever requested.</summary>
    /// <remarks>
    /// An allow-list rather than a scheme check. These URLs are assembled from data the GitHub
    /// API returned, so they are not arbitrary - but "not arbitrary" is not the same as
    /// "verified", and a tool that downloads a file and then writes its contents to source code
    /// should be explicit about where it is willing to download from.
    /// </remarks>
    private static readonly string[] AllowedHosts = ["github.com", "www.github.com", "patch-diff.githubusercontent.com"];

    /// <summary>Most patch URLs to follow for one candidate.</summary>
    private const int MaximumFetches = 4;

    /// <summary>Largest patch worth reading. A bigger one is a release, not a fix.</summary>
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

        foreach (var url in candidate.LinkedPatchUrls.Take(MaximumFetches))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await FetchAsync(url, cacheMode, cancellationToken);
            if (!result.FromCache) requests++;

            fetched.Add(result);
            Log?.Invoke($"{candidate.Id}: {result.Summary}");
        }

        var harvest = new HarvestResult(blocks, fetched, requests);

        // The one place a candidate is promoted. Carrying a link to a commit is not the same as
        // having a diff that parses, and until this point nothing has verified that it does.
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
