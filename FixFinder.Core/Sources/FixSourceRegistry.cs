using FixFinder.Core.Fingerprinting;

namespace FixFinder.Core.Sources;

/// <summary>Everything the enabled sources found, plus a per-source account of what happened.</summary>
/// <param name="PerSource">One entry per source that ran, including the ones that failed.</param>
public sealed record AggregateSearchResult(
    IReadOnlyList<FixCandidate> Candidates,
    IReadOnlyList<FixSearchResult> PerSource,
    int TotalRequests)
{
    public IReadOnlyList<string> Failures =>
        PerSource.Where(r => r.Failure is not null)
                 .Select(r => $"{r.SourceName}: {r.Failure}")
                 .ToArray();

    /// <summary>Plain summary for the status line, honest about an empty result.</summary>
    public string Summary
    {
        get
        {
            if (Candidates.Count == 0)
            {
                return Failures.Count > 0
                    ? $"No candidates. {string.Join("  ", Failures)}"
                    : "No candidates. Nothing on the searched sites matched this error.";
            }

            var perSource = PerSource
                .Where(r => r.Candidates.Count > 0)
                .Select(r => $"{r.Candidates.Count} from {r.SourceName}");

            return $"{Candidates.Count} candidates ({string.Join(", ", perSource)}) " +
                   $"· {TotalRequests} network request(s)";
        }
    }
}

/// <summary>
/// Runs the enabled fix sources and collects what they found.
/// </summary>
/// <remarks>
/// Sources run concurrently because they are separate services with separate allowances, so one
/// being slow should not hold up the other. A source that fails is reported as a failure and
/// the rest of the search continues - GitHub being rate-limited is no reason to discard a
/// perfectly good set of Stack Overflow answers.
/// </remarks>
public sealed class FixSourceRegistry
{
    private readonly List<IFixSource> _sources = [];

    public IReadOnlyList<IFixSource> Sources => _sources;

    public event Action<string>? Log;

    public void Add(IFixSource source) => _sources.Add(source);

    public IFixSource? ByName(string name) =>
        _sources.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <param name="enabled">
    /// Source names ticked in the window. Null runs every configured source.
    /// </param>
    public async Task<AggregateSearchResult> SearchAsync(
        ErrorFingerprint fingerprint,
        SearchBudget budget,
        IEnumerable<string>? enabled = null,
        CancellationToken cancellationToken = default)
    {
        var wanted = enabled?.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var running = _sources
            .Where(source => source.IsConfigured)
            .Where(source => wanted is null || wanted.Contains(source.Name))
            .ToArray();

        if (running.Length == 0)
        {
            Log?.Invoke("No fix sources are enabled, so nothing was searched.");
            return new AggregateSearchResult([], [], 0);
        }

        Log?.Invoke(
            $"Searching {string.Join(" and ", running.Select(s => s.Name))} " +
            $"for: {fingerprint.Tight.Text}");

        var results = await Task.WhenAll(running.Select(source => RunAsync(source, fingerprint, budget, cancellationToken)));

        var candidates = results.SelectMany(result => result.Candidates).ToArray();
        var requests = results.Sum(result => result.RequestsMade);

        return new AggregateSearchResult(candidates, results, requests);
    }

    /// <summary>
    /// Runs one source, converting an unexpected exception into a reported failure.
    /// </summary>
    /// <remarks>
    /// The sources already return failures rather than throwing, so reaching the catch means
    /// something genuinely unforeseen. Even then the right behaviour is to record it and let
    /// the other source finish: one source falling over is no reason to discard what the other
    /// one found.
    /// </remarks>
    private async Task<FixSearchResult> RunAsync(
        IFixSource source, ErrorFingerprint fingerprint, SearchBudget budget, CancellationToken ct)
    {
        try
        {
            var result = await source.SearchAsync(fingerprint, budget, ct);

            if (result.Failure is not null) Log?.Invoke($"{source.Name}: {result.Failure}");

            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return FixSearchResult.Failed(source.Name, "Search was cancelled.");
        }
        catch (Exception ex)
        {
            Log?.Invoke($"{source.Name} threw {ex.GetType().Name}: {ex.Message}");
            return FixSearchResult.Failed(source.Name, $"{source.Name} failed unexpectedly: {ex.Message}");
        }
    }
}
