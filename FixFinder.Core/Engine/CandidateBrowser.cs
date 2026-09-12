using FixFinder.Core.Http;
using FixFinder.Core.Patching;
using FixFinder.Core.Sources;

namespace FixFinder.Core.Engine;

/// <summary>One result, with whatever could be worked out about acting on it.</summary>
/// <param name="Position">Which result this is, counting from one.</param>
/// <param name="Plan">What applying it would do, or null when there is nothing to apply.</param>
public sealed record ExaminedCandidate(
    FixCandidate Candidate,
    int Position,
    int Total,
    HarvestResult? Harvest,
    ApplyPlan? Plan)
{
    public bool CanApply => Plan is { CanApply: true };

    /// <summary>Why Apply is unavailable, in a sentence fit to put on the disabled button.</summary>
    public string WhyNotAppliable =>
        Harvest is null
            ? "This one has not been opened yet."
            : !Harvest.HasAppliablePatch
                ? "This one is an explanation rather than a patch, so it cannot be applied automatically."
                : Plan is null
                    ? "FixFinder could not find your source code, so it has nothing to apply this to."
                    : $"Its patch will not apply here: {Plan.Explanation}";
}

/// <summary>
/// Walks the ranked results one at a time, opening each only when it is reached.
/// </summary>
/// <remarks>
/// Thirty-odd results are found and ranked on every run and exactly one was ever shown, which
/// made "the top result is no use to me" the end of the road rather than the start of looking.
/// The rest are already scored and already in hand; what was missing was a way to go and see one.
/// <para>
/// <b>Opened lazily, and only on request.</b> Fetching a candidate's linked commits costs
/// requests from an hourly allowance, so doing it for all thirty up front would spend the budget
/// on results nobody will look at. The first is free - the session already opened it - and each
/// step afterwards costs one candidate's worth.
/// </para>
/// <para>
/// Results are kept once examined, so stepping back and forth does not refetch and does not
/// re-spend. The cache underneath would usually absorb it, but relying on that would make the
/// cost of a button depend on a setting the user can turn off.
/// </para>
/// </remarks>
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

        // The prompt opens on whichever candidate the session settled on, which is not always the
        // top of the list: a lower one whose patch actually fits is promoted ahead of it. Starting
        // anywhere else would make the first Skip appear to go backwards.
        Index = outcome.Best is null ? 0 : Math.Max(0, IndexOf(outcome, outcome.Best));

        if (outcome.Best is not null)
        {
            _examined[Index] = new ExaminedCandidate(
                outcome.Best, Index + 1, Count, outcome.Harvest, outcome.Plan);
        }
    }

    /// <summary>Zero-based position in the ranked list.</summary>
    public int Index { get; private set; }

    public int Count { get; }

    /// <summary>True when there is another result to step to.</summary>
    public bool HasNext => Index + 1 < Count;

    /// <summary>How many results have not been looked at yet.</summary>
    public int Remaining => Math.Max(0, Count - Index - 1);

    /// <summary>Moves to the next result and opens it.</summary>
    /// <exception cref="InvalidOperationException">Thrown when there is no next result.</exception>
    public Task<ExaminedCandidate> NextAsync(CancellationToken cancellationToken = default)
    {
        if (!HasNext) throw new InvalidOperationException("There is no next result.");

        Index++;

        return CurrentAsync(cancellationToken);
    }

    /// <summary>Opens the result now being shown, or returns what was already worked out about it.</summary>
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
            // A result that cannot be fetched is advisory, not fatal. Reporting it as "no patch"
            // would be a lie; leaving Harvest null says the truth - it was never opened.
            Log?.Invoke($"{candidate.Id}: could not be opened - {ex.Message}");

            var unopened = new ExaminedCandidate(candidate, Index + 1, Count, null, null);
            _examined[Index] = unopened;

            return unopened;
        }
        finally
        {
            harvester.Log -= Relay;
        }

        var plan = PlanFor(harvest);

        var examined = new ExaminedCandidate(candidate, Index + 1, Count, harvest, plan);
        _examined[Index] = examined;

        return examined;
    }

    private ApplyPlan? PlanFor(HarvestResult harvest)
    {
        if (!harvest.HasAppliablePatch || _outcome.SourceRoot is null) return null;

        return new PatchApplier().Plan(
            harvest.Patches[0],
            new SourcePathMapper(_outcome.SourceRoot, _outcome.StackTraceFiles),
            _outcome.StackTraceFiles);
    }

    private static int IndexOf(SessionOutcome outcome, FixCandidate best)
    {
        for (var i = 0; i < outcome.Candidates.Count; i++)
            if (ReferenceEquals(outcome.Candidates[i], best)) return i;

        return 0;
    }

    private void Relay(string message) => Log?.Invoke(message);
}
