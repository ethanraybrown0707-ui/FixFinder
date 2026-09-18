using System.Text.Json;
using System.Text.RegularExpressions;
using FixFinder.Core.Engine;
using FixFinder.Core.Fingerprinting;
using FixFinder.Core.Http;

namespace FixFinder.Core.Sources;

/// <summary>Searches GitHub issues, and follows each one to the commits and pull requests that closed it.</summary>
public sealed partial class GitHubFixSource(FixFinderHttpClient http) : IFixSource
{
    public const string SearchBucket = "github-search";
    public const string CoreBucket = "github-core";
    public const string RawBucket = "github-raw";

    private const string ApiRoot = "https://api.github.com";

    private const int IssuesToFollow = 5;

    private static readonly KeyValuePair<string, string>[] ApiHeaders =
    [
        new("Accept", "application/vnd.github+json"),
        new("X-GitHub-Api-Version", "2022-11-28"),
    ];

    [GeneratedRegex(@"api\.github\.com/repos/(?<owner>[^/]+)/(?<repo>[^/]+)")]
    private static partial Regex RepositoryUrlPattern();

    public string Name => "GitHub";
    public bool RequiresNetwork => true;

    public bool IsConfigured => true;

    public string QuotaBucket => SearchBucket;
    public QuotaStatus? Quota => http.Quota.Get(SearchBucket);

    public event Action<string>? Log;

    public async Task<FixSearchResult> SearchAsync(
        ErrorFingerprint fingerprint, SearchBudget budget, CancellationToken cancellationToken)
    {
        var queries = new List<string>();
        var requests = 0;

        var repository = budget.RepositoryFilter is { Length: > 0 } typed
            ? typed.Trim()
            : KnownRepoMap.Resolve(fingerprint.NearestThirdPartyModule);

        if (repository is not null)
            Log?.Invoke($"GitHub: confining the search to {repository}.");

        var search = await RunSearchAsync(fingerprint.Tight.Text, repository, budget, queries, cancellationToken);
        requests += search.NetworkRequests;

        if (search.Failure is not null)
            return FixSearchResult.Failed(Name, search.Failure, queries);

        if (search.Issues.Count == 0 && queries.Count < budget.MaxSearchCalls)
        {
            Log?.Invoke("GitHub: nothing for the tight query, retrying with the relaxed one.");

            var relaxed = await RunSearchAsync(
                fingerprint.Relaxed.Text, repository, budget, queries, cancellationToken);

            requests += relaxed.NetworkRequests;

            if (relaxed.Failure is not null)
                return FixSearchResult.Failed(Name, relaxed.Failure, queries);

            search = relaxed;
        }

        if (search.Issues.Count == 0)
            return new FixSearchResult(Name, [], queries, requests);

        var candidates = search.Issues.Take(budget.MaxCandidates).ToList();

        var follow = Math.Min(Math.Min(IssuesToFollow, budget.MaxDetailCalls), candidates.Count);

        for (var index = 0; index < follow; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (links, spent) = await FetchLinkedPatchesAsync(candidates[index], budget, cancellationToken);
            requests += spent;

            if (links.Count > 0) candidates[index].Candidate.LinkedPatchUrls = links;
        }

        var built = candidates.Select(issue => issue.Candidate).ToArray();

        Log?.Invoke(
            $"GitHub: {built.Length} candidates, " +
            $"{built.Count(c => c.LinkedPatchUrls.Count > 0)} with linked commits or pull requests, " +
            $"from {requests} network request(s).");

        return new FixSearchResult(Name, built, queries, requests);
    }

    /// <summary>An issue plus the two facts needed to follow it: which repository, and which number.</summary>
    private sealed record IssueRecord(FixCandidate Candidate, string? Repository, int Number);

    private sealed record SearchOutcome(
        IReadOnlyList<IssueRecord> Issues, int NetworkRequests, string? Failure);

    private async Task<SearchOutcome> RunSearchAsync(
        string query, string? repository, SearchBudget budget, List<string> queries, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new SearchOutcome([], 0, "There was nothing in the error worth searching for.");

        var full = repository is { Length: > 0 }
            ? $"{Trim(query)} repo:{repository} is:issue"
            : $"{Trim(query)} is:issue";

        queries.Add(full);

        var url =
            $"{ApiRoot}/search/issues" +
            $"?q={Uri.EscapeDataString(full)}" +
            "&advanced_search=true&sort=updated&order=desc&per_page=30";

        var result = await http.GetAsync(url, SearchBucket, budget.Cache, ApiHeaders, ct);
        var network = result.FromCache ? 0 : 1;

        if (!result.Ok) return new SearchOutcome([], network, Describe(result));

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(result.Body);
        }
        catch (JsonException)
        {
            return new SearchOutcome([], network, "GitHub returned something that was not JSON.");
        }

        using (document)
        {
            var issues = document.RootElement
                .ArrayOrEmpty("items")
                .Select(ReadIssue)
                .Where(issue => issue is not null)
                .Select(issue => issue!)
                .ToArray();

            return new SearchOutcome(issues, network, null);
        }
    }

    private IssueRecord? ReadIssue(JsonElement item)
    {
        var number = item.IntOrZero("number");
        var url = item.StringOrEmpty("html_url");

        if (number == 0 || url.Length == 0) return null;

        var repositoryUrl = item.StringOrEmpty("repository_url");
        var match = RepositoryUrlPattern().Match(repositoryUrl);
        var repository = match.Success ? $"{match.Groups["owner"].Value}/{match.Groups["repo"].Value}" : null;

        var body = item.StringOrEmpty("body");

        var reactions = item.TryGet("reactions", out var block) ? block.IntOrZero("total_count") : 0;
        var stateReason = item.StringOrNull("state_reason");
        var closed = string.Equals(item.StringOrEmpty("state"), "closed", StringComparison.OrdinalIgnoreCase);

        var candidate = new FixCandidate
        {
            SourceName = Name,
            Id = repository is not null ? $"{repository}#{number}" : $"gh#{number}",
            Title = item.StringOrEmpty("title"),
            Url = url,
            BodyText = body,

            RawBody = body,
            RawBodyIsHtml = false,

            Tags = item.ArrayOrEmpty("labels")
                .Select(label => label.StringOrEmpty("name"))
                .Where(name => name.Length > 0)
                .ToArray(),

            CreatedAt = item.DateOrNull("created_at"),
            LastActivityAt = item.DateOrNull("updated_at"),

            Votes = reactions,
            AnswerCount = item.IntOrZero("comments"),

            IsClosed = closed,
            ClosedReason = stateReason,

            DuplicateOfUrl = string.Equals(stateReason, "duplicate", StringComparison.OrdinalIgnoreCase)
                ? url
                : null,

            Repository = repository,
            Tier = FixTier.Advisory,
        };

        return new IssueRecord(candidate, repository, number);
    }

    private async Task<(IReadOnlyList<string> Links, int Requests)> FetchLinkedPatchesAsync(
        IssueRecord issue, SearchBudget budget, CancellationToken ct)
    {
        if (issue.Repository is null) return ([], 0);

        var url = $"{ApiRoot}/repos/{issue.Repository}/issues/{issue.Number}/timeline?per_page=100";

        var result = await http.GetAsync(url, CoreBucket, budget.Cache, ApiHeaders, ct);
        var network = result.FromCache ? 0 : 1;

        if (!result.Ok)
        {
            Log?.Invoke($"GitHub: could not read the timeline of {issue.Candidate.Id} - {Describe(result)}");
            return ([], network);
        }

        try
        {
            using var document = JsonDocument.Parse(result.Body);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return ([], network);

            var links = new List<string>();

            foreach (var entry in document.RootElement.EnumerateArray())
            {
                var kind = entry.StringOrEmpty("event");

                switch (kind)
                {
                    case "closed" or "referenced" or "merged":
                        if (entry.StringOrNull("commit_id") is { Length: > 0 } sha)
                            Add(links, $"https://github.com/{issue.Repository}/commit/{sha}.patch");
                        break;

                    case "cross-referenced":
                        if (entry.TryGet("source", out var source) &&
                            source.TryGet("issue", out var referencing) &&
                            referencing.TryGet("pull_request", out _) &&
                            referencing.StringOrNull("html_url") is { Length: > 0 } pull)
                        {
                            Add(links, pull + ".diff");
                        }
                        break;
                }
            }

            if (links.Count > 0)
                Log?.Invoke($"GitHub: {issue.Candidate.Id} links to {links.Count} patch URL(s).");

            return (links, network);
        }
        catch (JsonException)
        {
            return ([], network);
        }

        static void Add(List<string> links, string url)
        {
            if (links.Count < 8 && !links.Contains(url, StringComparer.Ordinal)) links.Add(url);
        }
    }

    private static string Trim(string query) => query.Length <= 200 ? query : query[..200];

    private static string Describe(HttpResult result)
    {
        if (result.WasCacheMiss) return "Cached results only is on, and this request is not cached.";

        try
        {
            using var document = JsonDocument.Parse(result.Body);

            if (document.RootElement.StringOrNull("message") is { Length: > 0 } message)
            {
                var detail = document.RootElement
                    .ArrayOrEmpty("errors")
                    .Select(error => error.StringOrNull("message"))
                    .FirstOrDefault(text => text is { Length: > 0 });

                return detail is null
                    ? $"GitHub refused the request: {message}"
                    : $"GitHub refused the request: {message} - {detail}";
            }
        }
        catch (JsonException)
        {
        }

        return result.Failure ?? $"GitHub returned {(int)result.Status}.";
    }
}
