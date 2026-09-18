using FixFinder.Core.Http;
using FixFinder.Core.Patching;
using FixFinder.Core.Sources;

namespace FixFinder.Core.Engine;

/// <summary>One result, with whatever could be worked out about acting on it.</summary>
public sealed record ExaminedCandidate(
    FixCandidate Candidate,
    int Position,
    int Total,
    HarvestResult? Harvest,
    ApplyPlan? Plan)
{
    public InstalledPackage? Into { get; init; }

    public bool CanApply => Plan is { CanApply: true };

    public string WhyNotAppliable =>
        Harvest is null
            ? "This one has not been opened yet."
            : !Harvest.HasAppliablePatch
                ? "This one is an explanation rather than a patch - open the page and read it."
                : Plan is null
                    ? "FixFinder could not find your source code, so it has nothing to apply this to."
                    : $"Its patch will not apply here: {Plan.Explanation}";
}

/// <summary>Walks the ranked results one at a time, opening each only when it is reached.</summary>
public sealed class CandidateBrowser
{
    private readonly FixFinderHttpClient _http;
    private readonly SessionOutcome _outcome;
    private readonly CacheMode _cache;
    private readonly Dictionary<int, ExaminedCandidate> _examined = [];

    public event Action<string>? Log;

    public CandidateBrowser(FixFinderHttpClient http, SessionOutcome outcome, CacheMode cache)
    {
        _http = http;
        _outcome = outcome;
        _cache = cache;

        Count = outcome.Candidates.Count;

        Index = outcome.Best is null ? 0 : Math.Max(0, IndexOf(outcome, outcome.Best));

        if (outcome.Best is null) return;

        var plan = outcome.Plan;
        InstalledPackage? into = null;

        if (plan is not { CanApply: true } && outcome.Harvest is { } harvest)
            (plan, into) = PlanFor(harvest);

        _examined[Index] = new ExaminedCandidate(
            outcome.Best, Index + 1, Count, outcome.Harvest, plan) { Into = into };
    }

    public int Index { get; private set; }

    public int Count { get; }

    public bool HasNext => Index + 1 < Count;

    public int Remaining => Math.Max(0, Count - Index - 1);

    public Task<ExaminedCandidate> NextAsync(CancellationToken cancellationToken = default)
    {
        if (!HasNext) throw new InvalidOperationException("There is no next result.");

        Index++;

        return CurrentAsync(cancellationToken);
    }

    public async Task<ExaminedCandidate> CurrentAsync(CancellationToken cancellationToken = default)
    {
        if (_examined.TryGetValue(Index, out var already)) return already;

        var candidate = _outcome.Candidates[Index];

        var harvester = new PatchHarvester(_http);
        harvester.Log += Relay;

        HarvestResult harvest;

        try
        {
            harvest = await harvester.HarvestAsync(candidate, _cache, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Log?.Invoke($"{candidate.Id}: could not be opened - {ex.Message}");

            var unopened = new ExaminedCandidate(candidate, Index + 1, Count, null, null);
            _examined[Index] = unopened;

            return unopened;
        }
        finally
        {
            harvester.Log -= Relay;
        }

        var (plan, into) = PlanFor(harvest);

        var examined = new ExaminedCandidate(candidate, Index + 1, Count, harvest, plan) { Into = into };
        _examined[Index] = examined;

        return examined;
    }

    private (ApplyPlan? Plan, InstalledPackage? Into) PlanFor(HarvestResult harvest)
    {
        if (!harvest.HasAppliablePatch) return (null, null);

        var patch = harvest.Patches[0];
        ApplyPlan? planned = null;

        if (_outcome.SourceRoot is { } root)
        {
            planned = new PatchPlanner().Plan(
                patch, new SourcePathMapper(root, _outcome.StackTraceFiles), _outcome.StackTraceFiles);

            if (planned.CanApply) return (planned, null);
        }

        if (_outcome.Dependency is not { } package) return (planned, null);

        var intoPackage = new PatchPlanner().Plan(
            patch, new SourcePathMapper(package.Root, _outcome.StackTraceFiles), _outcome.StackTraceFiles);

        return intoPackage.CanApply ? (intoPackage, package) : (planned ?? intoPackage, null);
    }

    private static int IndexOf(SessionOutcome outcome, FixCandidate best)
    {
        for (var i = 0; i < outcome.Candidates.Count; i++)
            if (ReferenceEquals(outcome.Candidates[i], best)) return i;

        return 0;
    }

    private void Relay(string message) => Log?.Invoke(message);
}
