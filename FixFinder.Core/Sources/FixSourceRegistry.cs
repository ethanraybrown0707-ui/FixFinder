using FixFinder.Core.Fingerprinting;

namespace FixFinder.Core.Sources;

/// <summary>Everything the enabled sources found, plus a per-source account of what happened.</summary>
public sealed record AggregateSearchResult(
    IReadOnlyList<FixCandidate> Candidates,
    IReadOnlyList<FixSearchResult> PerSource,
    int TotalRequests)
{
    public IReadOnlyList<string> Failures =>
        PerSource.Where(r => r.Failure is not null)
                 .Select(r => $"{r.SourceName}: {r.Failure}")
                 .ToArray();

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

/// <summary>Runs the enabled fix sources and collects what they found.</summary>
public sealed class FixSourceRegistry
{
    private readonly List<IFixSource> _sources = [];

    public IReadOnlyList<IFixSource> Sources => _sources;

    public event Action<string>? Log;

    public void Add(IFixSource source) => _sources.Add(source);

    public IFixSource? ByName(string name) =>
        _sources.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

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
