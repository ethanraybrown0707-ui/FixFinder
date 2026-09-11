using FixFinder.Core.Execution;
using FixFinder.Core.Sources;

namespace FixFinder.Core.Engine;

/// <summary>
/// The two things the loop asks of a session: start a run, and pick one up.
/// </summary>
/// <remarks>
/// Narrow on purpose. <see cref="FixLoop"/>'s own job is deciding what to do after each verdict -
/// whether to go round again, whether the errors are chasing each other, when to stop - and none
/// of that should need a real process, a real compiler or a real network to exercise. Everything
/// else about a session stays on the session.
/// </remarks>
public interface IFixSession
{
    /// <summary>Builds if needed, runs, and searches for whatever went wrong.</summary>
    Task<SessionOutcome> RunAsync(LaunchPlan launch, SearchBudget? budget, CancellationToken cancellationToken);

    /// <summary>Searches for the error in a run that has already happened.</summary>
    Task<SessionOutcome> ContinueFromAsync(
        TargetRunResult run,
        TargetSpec spec,
        SearchBudget? budget,
        string? sourceFolder,
        bool failedToCompile,
        CancellationToken cancellationToken);
}
