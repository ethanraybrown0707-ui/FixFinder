using FixFinder.Core.Execution;
using FixFinder.Core.Sources;
using FixFinder.Core.Verification;

namespace FixFinder.Core.Engine;

/// <summary>What the caller wants done with one round's findings.</summary>
public enum RoundChoice
{
    /// <summary>Stop here. The user closed the prompt, or there is nothing worth doing.</summary>
    Stop,

    /// <summary>Apply this one and ask again if another error appears.</summary>
    Apply,

    /// <summary>Apply this one and every one after it without asking again.</summary>
    ApplyEverything,

    /// <summary>
    /// Leave this error alone and look at the next one this run reported.
    /// </summary>
    /// <remarks>
    /// Only ever possible for compiler output, where every diagnostic was reported at once. The
    /// caller decides whether to offer it by looking at <see cref="SessionOutcome.OtherErrors"/>;
    /// choosing it when that is empty ends the loop, because there is genuinely nowhere to go.
    /// </remarks>
    Skip,
}

/// <summary>
/// A caller's answer to one round.
/// </summary>
/// <param name="Choice">What to do.</param>
/// <param name="AlreadyDone">
/// The apply, when the caller did it itself. The interactive window applies from behind its own
/// typed confirmation, and handing the result back stops the loop applying the same patch twice.
/// </param>
public sealed record RoundDecision(RoundChoice Choice, StepResult? AlreadyDone = null)
{
    public static readonly RoundDecision Stop = new(RoundChoice.Stop);
}

/// <summary>One pass of run, search, decide, apply, check.</summary>
public sealed record FixRound(
    int Number,
    SessionOutcome Outcome,
    RoundChoice Choice,
    StepResult? Step)
{
    /// <summary>The title of whatever was applied, for the summary.</summary>
    public string? Applied => Step is { Apply.Ok: true } ? Outcome.Best?.Title : null;
}

/// <summary>Why the loop stopped. Every one of these is a complete answer, not a failure.</summary>
public enum LoopEnd
{
    /// <summary>The program never went wrong in the first place.</summary>
    NothingWrong,

    /// <summary>A patch was applied and the program then ran without an error.</summary>
    Fixed,

    /// <summary>An error is left, and nothing published matches it.</summary>
    NothingMoreFound,

    /// <summary>What was found is prose. It cannot be applied by a tool with no model in it.</summary>
    AdviceOnly,

    /// <summary>The user closed the prompt without applying.</summary>
    Stopped,

    /// <summary>Every error left was skipped, so nothing was applied and nothing is pending.</summary>
    Skipped,

    /// <summary>The patch made things no better, and was undone.</summary>
    RolledBack,

    /// <summary>The change was applied but re-running proved nothing either way.</summary>
    Inconclusive,

    /// <summary>An error that had already been seen this run came back.</summary>
    WentInCircles,

    /// <summary>The round limit was reached with errors still to go.</summary>
    RoundLimit,

    /// <summary>The program could not be started at all.</summary>
    CouldNotRun,
}

/// <summary>Everything the loop did, and how it ended.</summary>
public sealed record LoopResult(
    IReadOnlyList<FixRound> Rounds,
    LoopEnd End,
    SessionOutcome Last)
{
    /// <summary>How many patches were written and left in place.</summary>
    public int AppliedCount => Rounds.Count(r => r.Step is { LeftChanged: true });

    /// <summary>True when more than one error was worked through.</summary>
    public bool Looped => Rounds.Count > 1;

    /// <summary>How many problems were stepped past rather than acted on.</summary>
    public int Skipped => Rounds.Count(r => r.Choice == RoundChoice.Skip);

    /// <summary>One line for the status bar.</summary>
    public string Headline => End switch
    {
        LoopEnd.Fixed => AppliedCount > 1
            ? $"Fixed, after {AppliedCount} changes."
            : "Fixed.",

        LoopEnd.RoundLimit => $"Stopped after {Rounds.Count} rounds.",
        LoopEnd.WentInCircles => "Stopped - it started going round in circles.",
        LoopEnd.RolledBack => "Rolled back.",
        LoopEnd.Skipped => Skipped > 1 ? $"Skipped {Skipped} problems." : "Skipped.",
        _ => Last.Headline,
    };

    /// <summary>A paragraph for the result pane, saying what was done and where it got to.</summary>
    public string Detail
    {
        get
        {
            var done = Rounds
                .Where(r => r.Step is { LeftChanged: true } && r.Applied is not null)
                .Select(r => r.Applied!)
                .ToList();

            var history = done.Count switch
            {
                0 => "",
                1 => $"Applied: {done[0]}\n\n",
                _ => "Applied " + done.Count + " changes, in order:\n" +
                     string.Join("\n", done.Select((title, index) => $"{index + 1}. {title}")) + "\n\n",
            };

            var ending = End switch
            {
                LoopEnd.Fixed =>
                    "The program then ran without an error. Every change is still in place, and the " +
                    "backups are kept.",

                LoopEnd.WentInCircles =>
                    "The next error was one that had already come up in this run, which means the " +
                    "changes are undoing each other rather than making progress. Nothing more was " +
                    "applied - this one needs a person.",

                LoopEnd.RoundLimit =>
                    $"There is still an error, but FixFinder stops after {Rounds.Count} rounds rather " +
                    "than working through a program unattended for as long as it keeps failing. Run it " +
                    "again to carry on from here.",

                LoopEnd.RolledBack =>
                    "That change did not help, so it was put back exactly as it was. Anything applied " +
                    "before it was left alone.",

                LoopEnd.Skipped =>
                    "That was the last error this run reported, so there is nothing further to move " +
                    "on to. The ones you skipped are still there - they need fixing by hand.",

                LoopEnd.Inconclusive =>
                    "Re-running could not show whether that helped, so the loop stopped rather than " +
                    "stacking another change on top of one it cannot vouch for.",

                _ => Last.Detail,
            };

            return history + ending;
        }
    }
}

/// <summary>
/// Works through a program that goes wrong more than once: fix, re-run, fix what comes next.
/// </summary>
/// <remarks>
/// One error per run was never the real shape of the problem. A file with two typos in it reports
/// the first, and only the first; correcting it reveals the second, which is a fresh error needing
/// a fresh search. Doing that by hand means pressing the same button four times and losing track
/// of which change was which, so the loop does it and keeps the list.
/// <para>
/// <b>The verdict drives everything.</b> <see cref="FixVerdict.DifferentError"/> already meant
/// "the original error is gone and another one has appeared", and the verifier already refused to
/// roll those back because they are usually progress. That is precisely the signal to go round
/// again, and every other verdict is a reason to stop: fixed is finished, the same error coming
/// back was rolled back, and a result the verifier would not vouch for is not one to build on.
/// </para>
/// <para>
/// <b>Two things stop it running away.</b> A round limit, because a tool that edits source code
/// should not keep doing so indefinitely while nobody is watching; and a set of every error seen
/// this run, because two patches that undo each other produce a different error each time and
/// would otherwise loop until the limit. An error that has come back is not progress, however
/// different it looks from the one immediately before it.
/// </para>
/// </remarks>
public sealed class FixLoop(IFixSession session, FixStep? step = null)
{
    /// <summary>How many errors are worked through before stopping to ask.</summary>
    /// <remarks>
    /// Five is a judgement, not a measurement: enough for the case this exists for - a handful of
    /// compiler errors in one file - and short enough that an unattended run cannot rewrite a
    /// source tree while somebody is making tea. Reaching it is reported, not hidden, and running
    /// again carries on from where it stopped.
    /// </remarks>
    public const int DefaultMaxRounds = 5;

    private readonly FixStep _step = step ?? new FixStep();

    public int MaxRounds { get; init; } = DefaultMaxRounds;

    public event Action<string>? Log;

    /// <summary>Raised as each round starts, for a window that wants to say "error 2 of ...".</summary>
    public event Action<int>? RoundStarting;

    /// <summary>
    /// Asked what to do about each round's findings.
    /// </summary>
    /// <remarks>
    /// Required, and deliberately not defaulted to "apply". Nothing in this class decides on its
    /// own that a patch is wanted; the loop is about what happens <i>after</i> that answer.
    /// </remarks>
    public required Func<SessionOutcome, int, CancellationToken, Task<RoundDecision>> Ask { get; init; }

    /// <summary>
    /// How a round applies its patch when the caller has not already done it.
    /// </summary>
    /// <remarks>
    /// Settable rather than a hard call into <see cref="FixStep"/> so the routing here can be
    /// checked without writing to a disk. Everything this class decides happens between an apply
    /// and the next search, and neither needs to be real to establish that a rolled-back change
    /// stops the loop or that a repeated error ends it.
    /// </remarks>
    public Func<SessionOutcome, CancellationToken, Task<StepResult>>? Apply { get; init; }

    public async Task<LoopResult> RunAsync(
        LaunchPlan launch,
        SearchBudget? budget = null,
        CancellationToken cancellationToken = default)
    {
        var first = await session.RunAsync(launch, budget, cancellationToken);

        return await ContinueAsync(first, budget, cancellationToken);
    }

    /// <summary>Runs the loop over an outcome that has already been produced.</summary>
    public async Task<LoopResult> ContinueAsync(
        SessionOutcome first,
        SearchBudget? budget = null,
        CancellationToken cancellationToken = default)
    {
        var rounds = new List<FixRound>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var appliedCandidates = new HashSet<string>(StringComparer.Ordinal);

        var outcome = first;
        var everything = false;

        for (var number = 1; ; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            RoundStarting?.Invoke(number);

            // Checked before anything is offered. An error seen before means the changes are
            // chasing each other, and the honest move is to stop rather than to apply a patch
            // that has already been shown not to settle anything.
            if (outcome.Fingerprint?.Hash is { Length: > 0 } hash && !seen.Add(hash))
            {
                Log?.Invoke($"Round {number}: {outcome.Error?.Summary} has already come up this run. Stopping.");

                return new LoopResult(rounds, LoopEnd.WentInCircles, outcome);
            }

            if (!outcome.WorthShowing)
                return new LoopResult(rounds, EndFor(outcome, rounds), outcome);

            // "Apply everything" only skips the question when there is actually something to
            // apply. A later round that turns up prose has nothing for it to do, so the prompt
            // comes back rather than the window closing on a result nobody saw.
            var canAutomate = everything && outcome.CanApply && outcome.Best is not null;

            var decision = canAutomate
                ? new RoundDecision(RoundChoice.ApplyEverything)
                : await Ask(outcome, number, cancellationToken);

            if (decision.Choice == RoundChoice.Stop)
            {
                rounds.Add(new FixRound(number, outcome, decision.Choice, decision.AlreadyDone));

                // Closing a prompt that had nothing appliable on it is not really a decision to
                // stop - there was nothing to say yes to - and reporting it as one would put the
                // outcome down to the user rather than to what was found.
                return new LoopResult(rounds, outcome.CanApply ? LoopEnd.Stopped : LoopEnd.AdviceOnly, outcome);
            }

            if (decision.Choice == RoundChoice.Skip)
            {
                rounds.Add(new FixRound(number, outcome, decision.Choice, null));

                // The caller asked to move past this one. Only compiler output has anywhere to
                // move to, and if it has run out there is nothing further to say - the program
                // stopped at this error, and what is behind it stays unreachable until it is
                // fixed by hand.
                if (outcome.OtherErrors.Count == 0)
                {
                    Log?.Invoke($"Round {number}: skipped, and no other error was reported by this run.");

                    return new LoopResult(rounds, LoopEnd.Skipped, outcome);
                }

                var other = outcome.OtherErrors[0];

                Log?.Invoke($"Round {number}: skipped. Moving to {other.Summary}");

                outcome = await session.SearchForOtherAsync(outcome, other, budget, cancellationToken);
                continue;
            }

            everything |= decision.Choice == RoundChoice.ApplyEverything;

            // The same patch twice would apply cleanly the first time and fail its context the
            // second, which reads as a broken patch rather than as a loop that lost its place.
            if (outcome.Best is { Id: { Length: > 0 } id } && !appliedCandidates.Add(id))
            {
                Log?.Invoke($"Round {number}: {id} has already been applied this run. Stopping.");

                return new LoopResult(rounds, LoopEnd.WentInCircles, outcome);
            }

            var step = decision.AlreadyDone ?? await (Apply is null
                ? _step.ApplyAsync(outcome, cancellationToken)
                : Apply(outcome, cancellationToken));

            rounds.Add(new FixRound(number, outcome, decision.Choice, step));

            Log?.Invoke($"Round {number}: {step.Summary}");

            if (!step.Apply.Ok)
                return new LoopResult(rounds, LoopEnd.Stopped, outcome);

            if (step.Verification is not { } verification)
                return new LoopResult(rounds, LoopEnd.Inconclusive, outcome);

            if (verification.Verdict != FixVerdict.DifferentError)
            {
                var end = verification.Verdict switch
                {
                    FixVerdict.Fixed => LoopEnd.Fixed,
                    FixVerdict.SameErrorPersists or FixVerdict.BuildFailed => LoopEnd.RolledBack,
                    _ => LoopEnd.Inconclusive,
                };

                return new LoopResult(rounds, end, outcome);
            }

            // ---------------------------------------------------------- go round again

            if (number >= MaxRounds)
            {
                Log?.Invoke($"Reached the limit of {MaxRounds} rounds with an error still showing.");

                return new LoopResult(rounds, LoopEnd.RoundLimit, outcome);
            }

            if (verification.Latest is not { } next || outcome.Spec is null)
                return new LoopResult(rounds, LoopEnd.Inconclusive, outcome);

            // The verifier already ran the program to reach its verdict, so the next round starts
            // from that run rather than launching a third time. Running again here would not only
            // be slower - it would be a different run, and the error just reported might not be
            // the one the search ends up being about.
            outcome = await session.ContinueFromAsync(
                next, outcome.Spec, budget, outcome.SourceRoot,
                verification.NextCameFromBuild, cancellationToken: cancellationToken);
        }
    }

    /// <summary>Reads a round that produced no prompt as a reason the loop is over.</summary>
    private static LoopEnd EndFor(SessionOutcome outcome, List<FixRound> rounds) => outcome.Result switch
    {
        SessionResult.CouldNotRun => LoopEnd.CouldNotRun,
        SessionResult.RanFine => rounds.Count > 0 ? LoopEnd.Fixed : LoopEnd.NothingWrong,
        _ => LoopEnd.NothingMoreFound,
    };
}
