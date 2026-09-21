using FixFinder.Core.Fingerprinting;
using FixFinder.Core.Http;

namespace FixFinder.Core.Sources;

/// <summary>The ceiling on what one search may spend.</summary>
public sealed record SearchBudget(
    int MaxSearchCalls = 2,
    int MaxDetailCalls = 5,
    int MaxCandidates = 30,
    CacheMode Cache = CacheMode.Normal,
    string? RepositoryFilter = null)
{
    public static readonly SearchBudget Default = new();
}

/// <summary>What one source found, and what it cost.</summary>
public sealed record FixSearchResult(
    string SourceName,
    IReadOnlyList<FixCandidate> Candidates,
    IReadOnlyList<string> QueriesTried,
    int RequestsMade,
    string? Failure = null)
{
    public static FixSearchResult Failed(string source, string failure, IReadOnlyList<string>? queries = null) =>
        new(source, [], queries ?? [], 0, failure);

    public bool Ok => Failure is null;
}

/// <summary>A place FixFinder can look for a fix.</summary>
public interface IFixSource
{
    string Name { get; }

    bool RequiresNetwork { get; }

    bool IsConfigured { get; }

    string QuotaBucket { get; }

    QuotaStatus? Quota { get; }

    Task<FixSearchResult> SearchAsync(
        ErrorFingerprint fingerprint, SearchBudget budget, CancellationToken cancellationToken);
}
