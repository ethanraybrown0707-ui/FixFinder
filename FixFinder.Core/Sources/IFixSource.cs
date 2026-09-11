using FixFinder.Core.Fingerprinting;
using FixFinder.Core.Http;

namespace FixFinder.Core.Sources;

/// <summary>
/// The ceiling on what one search may spend.
/// </summary>
/// <param name="MaxSearchCalls">Search requests allowed, including the relaxed retry.</param>
/// <param name="MaxDetailCalls">Follow-up requests for linked commits and answer bodies.</param>
/// <param name="MaxCandidates">Candidates returned per source.</param>
/// <param name="Cache">Cache behaviour for every request this search makes.</param>
/// <param name="RepositoryFilter">Optional "owner/name" to confine a GitHub search to.</param>
/// <remarks>
/// A budget rather than a set of constants because the scarce resource here is other people's
/// API allowance, and it is scarce enough to be worth being explicit about: ten search requests
/// a minute unauthenticated is about four crashes before you are locked out.
/// </remarks>
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
/// <param name="QueriesTried">Every query string sent, in order, for the log and the UI.</param>
/// <param name="RequestsMade">Requests that actually went to the network.</param>
/// <param name="Failure">Set when the source could not search at all.</param>
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

/// <summary>
/// A place FixFinder can look for a fix.
/// </summary>
/// <remarks>
/// An interface from day one even though only two implementations exist, because the two that
/// were deliberately deferred - a local rule catalogue and a search of your own git history -
/// are the ones most likely to produce an appliable patch, and they need to drop in without
/// touching the ranker or the window.
/// </remarks>
public interface IFixSource
{
    /// <summary>Display name, shown on the checkbox and against every candidate.</summary>
    string Name { get; }

    bool RequiresNetwork { get; }

    /// <summary>
    /// False when the source cannot run at all, such as a missing credential it truly needs.
    /// </summary>
    /// <remarks>
    /// Both current sources are configured out of the box - GitHub search and the Stack
    /// Exchange API both work unauthenticated. A credential only raises the allowance.
    /// </remarks>
    bool IsConfigured { get; }

    /// <summary>The rate-limit bucket this source spends from, for the quota label.</summary>
    string QuotaBucket { get; }

    QuotaStatus? Quota { get; }

    Task<FixSearchResult> SearchAsync(
        ErrorFingerprint fingerprint, SearchBudget budget, CancellationToken cancellationToken);
}
