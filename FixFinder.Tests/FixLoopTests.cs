using FixFinder.Core.Engine;
using FixFinder.Core.Execution;
using FixFinder.Core.Fingerprinting;
using FixFinder.Core.Parsing;
using FixFinder.Core.Patching;
using FixFinder.Core.Sources;
using FixFinder.Core.Verification;

namespace FixFinder.Tests;

/// <summary>
/// The round routing, with no process, compiler or network anywhere near it.
/// </summary>
/// <remarks>
/// Everything here supplies the apply through <see cref="RoundDecision.AlreadyDone"/>, exactly as
/// the interactive window does, so the loop is exercised on the only question that is really its
/// own: given a verdict, is there another round to do, and if not, why not.
/// </remarks>
public class FixLoopTests
{
    // ================================================================== scaffolding

    /// <summary>A session that hands back a queue of prepared outcomes.</summary>
    private sealed class ScriptedSession(params SessionOutcome[] outcomes) : IFixSession
    {
        private readonly Queue<SessionOutcome> _queue = new(outcomes);

        public int Continues { get; private set; }

        public int Skips { get; private set; }

        public Task<SessionOutcome> RunAsync(LaunchPlan launch, SearchBudget? budget, CancellationToken ct) =>
            Task.FromResult(_queue.Dequeue());

        /// <summary>Answers a skip from a queue the test set up, the way a compiler run would.</summary>
        public Task<SessionOutcome> SearchForOtherAsync(
            SessionOutcome from, ParsedError error, SearchBudget? budget, CancellationToken ct)
        {
            Skips++;

            return Task.FromResult(_queue.Count > 0 ? _queue.Dequeue() : Clean());
        }

        public Task<SessionOutcome> ContinueFromAsync(
            TargetRunResult run, TargetSpec spec, SearchBudget? budget, string? sourceFolder,
            bool failedToCompile, CancellationToken ct)
        {
            Continues++;

            return Task.FromResult(_queue.Count > 0 ? _queue.Dequeue() : Clean());
        }
    }

    private static readonly TargetSpec Spec = new()
    {
        ExecutablePath = "python",
        Arguments = "crash.py",
        WorkingDirectory = @"C:\work",
    };

    private static TargetRunResult Run(int? exitCode = 1) => new()
    {
        Outcome = exitCode == 0 ? RunOutcome.ExitedClean : RunOutcome.Crashed,
        ExitCode = exitCode,
        Lines = [],
        Duration = TimeSpan.Zero,
        Explanation = exitCode == 0 ? "Exited 0." : $"Exited {exitCode}.",
    };

    /// <summary>An outcome carrying other errors behind it, the way a failed build does.</summary>
    private static SessionOutcome WithOthers(SessionOutcome outcome, params string[] types) =>
        outcome with
        {
            OtherErrors = [.. types.Select(t => new ParsedError
            {
                LanguageId = "msvc",
                Confidence = 90,
                RawText = t,
                FirstLineSequence = 0,
                ExceptionType = "compile error",
                ErrorCode = t,
                Message = $"something about {t}",
                Frames = [],
            })],
        };

    /// <summary>An outcome with a patch ready to apply, keyed by a distinct error.</summary>
    private static SessionOutcome Fixable(string type, string message, string candidateId = "gh#1") =>
        new()
        {
            Result = SessionResult.FoundFix,
            Headline = $"It crashed: {type}",
            Detail = "",
            Spec = Spec,
            Run = Run(),
            Fingerprint = Fingerprint(type, message),
            Best = new FixCandidate
            {
                Id = candidateId,
                SourceName = "GitHub",
                Title = $"Fix for {type}",
                Url = $"https://example.invalid/{candidateId}",
                Tier = FixTier.AutoAppliable,
            },
            Plan = AppliablePlan(),
            SourceRoot = @"C:\work",
        };

    private static SessionOutcome Advisory(string type) => new()
    {
        Result = SessionResult.FoundAdvice,
        Headline = $"It crashed: {type}",
        Detail = "Read it yourself.",
        Spec = Spec,
        Run = Run(),
        Fingerprint = Fingerprint(type, "something"),
        Best = new FixCandidate
        {
            Id = "so#1",
            SourceName = "Stack Overflow",
            Title = "An explanation",
            Url = "https://example.invalid/so",
            Tier = FixTier.Advisory,
        },
    };

    private static SessionOutcome Clean() => new()
    {
        Result = SessionResult.RanFine,
        Headline = "It ran without a problem.",
        Detail = "",
        Spec = Spec,
        Run = Run(0),
    };

    private static ErrorFingerprint Fingerprint(string type, string message) =>
        FingerprintBuilder.Build(new ParsedError
        {
            LanguageId = "python",
            Confidence = 90,
            RawText = $"{type}: {message}",
            ExceptionType = type,
            Message = message,
            FirstLineSequence = 0,
            Frames = [],
        });

    /// <summary>
    /// A plan that reports itself appliable. Nothing here ever writes it.
    /// </summary>
    /// <remarks>
    /// Built from a real parsed diff rather than a hand-made object, because ApplyPlan.CanApply
    /// insists on at least one file that planned cleanly - and that is exactly the property the
    /// loop reads to decide whether a round has something to offer.
    /// </remarks>
    private static ApplyPlan AppliablePlan()
    {
        var patch = UnifiedDiffParser.Parse(
            """
            --- a/cart.py
            +++ b/cart.py
            @@ -1 +1 @@
            -old
            +new
            """).Files.Single();

        var path = new MappedPath("a/cart.py", @"C:\work\cart.py", MapOutcome.Mapped, "matched");

        return new ApplyPlan(
            [new FilePlan(patch, path, [], ApplyOutcome.Planned, "ready")],
            ApplyOutcome.Planned,
            "ready");
    }

    private static StepResult Step(FixVerdict verdict, TargetRunResult? rerun = null) =>
        new(new ApplyResult(AppliablePlan(), false, @"C:\backups\one", ["cart.py"]),
            new VerificationResult(verdict, verdict.ToString(), "before", "after", null, rerun ?? Run()));

    private static FixLoop Loop(
        IFixSession session,
        Func<SessionOutcome, int, CancellationToken, Task<RoundDecision>> ask,
        int? maxRounds = null) =>
        maxRounds is { } limit
            ? new FixLoop(session) { Ask = ask, MaxRounds = limit }
            : new FixLoop(session) { Ask = ask };

    /// <summary>Answers every round the same way, and records how many there were.</summary>
    private static Func<SessionOutcome, int, CancellationToken, Task<RoundDecision>> Always(
        RoundDecision decision, List<int> asked) =>
        (_, round, _) =>
        {
            asked.Add(round);
            return Task.FromResult(decision);
        };

    // ================================================================== the loop

    [Fact]
    public async Task A_program_that_runs_fine_never_prompts()
    {
        var asked = new List<int>();
        var loop = Loop(new ScriptedSession(Clean()), Always(RoundDecision.Stop, asked));

        var result = await loop.RunAsync(new LaunchPlan(Spec, null, "ok"));

        Assert.Equal(LoopEnd.NothingWrong, result.End);
        Assert.Empty(asked);
        Assert.Empty(result.Rounds);
    }

    [Fact]
    public async Task Closing_the_prompt_stops_after_one_round()
    {
        var asked = new List<int>();
        var loop = Loop(new ScriptedSession(Fixable("KeyError", "one")), Always(RoundDecision.Stop, asked));

        var result = await loop.RunAsync(new LaunchPlan(Spec, null, "ok"));

        Assert.Equal(LoopEnd.Stopped, result.End);
        Assert.Equal([1], asked);
        Assert.Equal(0, result.AppliedCount);
    }

    [Fact]
    public async Task A_fixed_verdict_ends_the_loop()
    {
        var asked = new List<int>();
        var session = new ScriptedSession(Fixable("KeyError", "one"));

        var loop = Loop(session, Always(new RoundDecision(RoundChoice.Apply, Step(FixVerdict.Fixed)), asked));

        var result = await loop.RunAsync(new LaunchPlan(Spec, null, "ok"));

        Assert.Equal(LoopEnd.Fixed, result.End);
        Assert.Equal([1], asked);
        Assert.Equal(1, result.AppliedCount);
        Assert.Equal(0, session.Continues);
    }

    /// <summary>The case the whole thing exists for.</summary>
    [Fact]
    public async Task A_different_error_starts_another_round()
    {
        var asked = new List<int>();

        var session = new ScriptedSession(
            Fixable("KeyError", "one", "gh#1"),
            Fixable("TypeError", "two", "gh#2"),
            Clean());

        var verdicts = new Queue<FixVerdict>([FixVerdict.DifferentError, FixVerdict.Fixed]);

        var loop = Loop(session, (_, round, _) =>
        {
            asked.Add(round);
            return Task.FromResult(new RoundDecision(RoundChoice.Apply, Step(verdicts.Dequeue())));
        });

        var result = await loop.RunAsync(new LaunchPlan(Spec, null, "ok"));

        Assert.Equal(LoopEnd.Fixed, result.End);
        Assert.Equal([1, 2], asked);
        Assert.Equal(2, result.AppliedCount);
        Assert.True(result.Looped);
        Assert.Equal(1, session.Continues);
        Assert.Contains("2 changes", result.Detail);
    }

    [Fact]
    public async Task Apply_everything_stops_asking()
    {
        var asked = new List<int>();

        var session = new ScriptedSession(
            Fixable("KeyError", "one", "gh#1"),
            Fixable("TypeError", "two", "gh#2"),
            Fixable("ValueError", "three", "gh#3"),
            Clean());

        var verdicts = new Queue<FixVerdict>(
            [FixVerdict.DifferentError, FixVerdict.DifferentError, FixVerdict.Fixed]);

        // Only the first round is answered by the caller. Every round after it applies through
        // the loop itself, which is the whole point of the button.
        var loop = new FixLoop(session)
        {
            Ask = (_, round, _) =>
            {
                asked.Add(round);
                return Task.FromResult(new RoundDecision(RoundChoice.ApplyEverything, Step(verdicts.Dequeue())));
            },
            Apply = (_, _) => Task.FromResult(Step(verdicts.Dequeue())),
        };

        var result = await loop.RunAsync(new LaunchPlan(Spec, null, "ok"));

        Assert.Equal([1], asked);
        Assert.Equal(LoopEnd.Fixed, result.End);
        Assert.Equal(3, result.Rounds.Count);
    }

    [Fact]
    public async Task An_error_that_comes_back_stops_the_loop()
    {
        var asked = new List<int>();

        // Round 3 is the same error as round 1: the two patches are undoing each other.
        var session = new ScriptedSession(
            Fixable("KeyError", "one", "gh#1"),
            Fixable("TypeError", "two", "gh#2"),
            Fixable("KeyError", "one", "gh#3"));

        var loop = Loop(session, Always(
            new RoundDecision(RoundChoice.Apply, Step(FixVerdict.DifferentError)), asked));

        var result = await loop.RunAsync(new LaunchPlan(Spec, null, "ok"));

        Assert.Equal(LoopEnd.WentInCircles, result.End);
        Assert.Equal([1, 2], asked);
        Assert.Contains("already come up", result.Detail);
    }

    [Fact]
    public async Task The_same_candidate_is_never_applied_twice()
    {
        var asked = new List<int>();

        // Two genuinely different errors, but the ranker put the same issue top for both.
        var session = new ScriptedSession(
            Fixable("KeyError", "one", "gh#1"),
            Fixable("TypeError", "two", "gh#1"));

        var loop = Loop(session, Always(
            new RoundDecision(RoundChoice.Apply, Step(FixVerdict.DifferentError)), asked));

        var result = await loop.RunAsync(new LaunchPlan(Spec, null, "ok"));

        Assert.Equal(LoopEnd.WentInCircles, result.End);
        Assert.Single(result.Rounds);
    }

    [Fact]
    public async Task The_round_limit_is_honoured()
    {
        var asked = new List<int>();

        var session = new ScriptedSession(
            Fixable("E1", "one", "gh#1"),
            Fixable("E2", "two", "gh#2"),
            Fixable("E3", "three", "gh#3"),
            Fixable("E4", "four", "gh#4"));

        var loop = Loop(
            session,
            Always(new RoundDecision(RoundChoice.Apply, Step(FixVerdict.DifferentError)), asked),
            maxRounds: 2);

        var result = await loop.RunAsync(new LaunchPlan(Spec, null, "ok"));

        Assert.Equal(LoopEnd.RoundLimit, result.End);
        Assert.Equal([1, 2], asked);
        Assert.Contains("Run it again", result.Detail);
    }

    [Theory]
    [InlineData(FixVerdict.SameErrorPersists, LoopEnd.RolledBack)]
    [InlineData(FixVerdict.BuildFailed, LoopEnd.RolledBack)]
    [InlineData(FixVerdict.Inconclusive, LoopEnd.Inconclusive)]
    public async Task Only_a_different_error_continues(FixVerdict verdict, LoopEnd expected)
    {
        var asked = new List<int>();

        var session = new ScriptedSession(
            Fixable("KeyError", "one", "gh#1"),
            Fixable("TypeError", "two", "gh#2"));

        var loop = Loop(session, Always(new RoundDecision(RoundChoice.Apply, Step(verdict)), asked));

        var result = await loop.RunAsync(new LaunchPlan(Spec, null, "ok"));

        Assert.Equal(expected, result.End);
        Assert.Equal([1], asked);
        Assert.Equal(0, session.Continues);
    }

    // ================================================================== skipping

    /// <summary>
    /// Skipping a diagnostic nobody can act on moves to the next one the build reported.
    /// </summary>
    /// <remarks>
    /// Nothing is applied and the program is not run again: every one of these errors was printed
    /// by the same build, so the next is already in hand.
    /// </remarks>
    [Fact]
    public async Task Skipping_moves_to_the_next_error_in_the_same_run()
    {
        var asked = new List<int>();

        var session = new ScriptedSession(
            WithOthers(Advisory("C2065"), "C2143"),
            Advisory("C2143"));

        var loop = Loop(session, (_, round, _) =>
        {
            asked.Add(round);
            return Task.FromResult(round == 1 ? new RoundDecision(RoundChoice.Skip) : RoundDecision.Stop);
        });

        var result = await loop.RunAsync(new LaunchPlan(Spec, null, "ok"));

        Assert.Equal([1, 2], asked);
        Assert.Equal(1, session.Skips);
        Assert.Equal(0, session.Continues);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.AppliedCount);
    }

    /// <summary>Skipping the last one ends the run, because there is nowhere further to go.</summary>
    [Fact]
    public async Task Skipping_with_nothing_behind_it_ends_the_run()
    {
        var asked = new List<int>();

        var loop = Loop(
            new ScriptedSession(Advisory("KeyError")),
            Always(new RoundDecision(RoundChoice.Skip), asked));

        var result = await loop.RunAsync(new LaunchPlan(Spec, null, "ok"));

        Assert.Equal(LoopEnd.Skipped, result.End);
        Assert.Equal([1], asked);
        Assert.Equal(1, result.Skipped);
        Assert.Contains("nothing further to move", result.Detail);
    }

    /// <summary>Several skips in a row work through the whole build and are counted.</summary>
    [Fact]
    public async Task Every_error_in_a_build_can_be_skipped_in_turn()
    {
        var asked = new List<int>();

        var session = new ScriptedSession(
            WithOthers(Advisory("C2065"), "C2143", "C2146"),
            WithOthers(Advisory("C2143"), "C2146"),
            Advisory("C2146"));

        var loop = Loop(session, Always(new RoundDecision(RoundChoice.Skip), asked));

        var result = await loop.RunAsync(new LaunchPlan(Spec, null, "ok"));

        Assert.Equal([1, 2, 3], asked);
        Assert.Equal(LoopEnd.Skipped, result.End);
        Assert.Equal(3, result.Skipped);
        Assert.Equal("Skipped 3 problems.", result.Headline);
    }

    /// <summary>A skipped round applies nothing, so nothing needs rolling back.</summary>
    [Fact]
    public async Task A_skipped_round_writes_nothing()
    {
        var asked = new List<int>();

        var session = new ScriptedSession(
            WithOthers(Fixable("C2065", "one", "gh#1"), "C2143"),
            Advisory("C2143"));

        var loop = new FixLoop(session)
        {
            Ask = (_, round, _) =>
            {
                asked.Add(round);
                return Task.FromResult(round == 1 ? new RoundDecision(RoundChoice.Skip) : RoundDecision.Stop);
            },
            Apply = (_, _) => throw new InvalidOperationException("a skipped round must not apply anything"),
        };

        var result = await loop.RunAsync(new LaunchPlan(Spec, null, "ok"));

        Assert.Equal(0, result.AppliedCount);
        Assert.All(result.Rounds.Where(r => r.Choice == RoundChoice.Skip), r => Assert.Null(r.Step));
    }

    [Fact]
    public async Task Advice_that_is_closed_is_reported_as_advice_not_as_stopping()
    {
        var asked = new List<int>();
        var loop = Loop(new ScriptedSession(Advisory("KeyError")), Always(RoundDecision.Stop, asked));

        var result = await loop.RunAsync(new LaunchPlan(Spec, null, "ok"));

        Assert.Equal(LoopEnd.AdviceOnly, result.End);
    }

    [Fact]
    public async Task Apply_everything_still_asks_when_a_later_round_has_only_advice()
    {
        var asked = new List<int>();

        var session = new ScriptedSession(
            Fixable("KeyError", "one", "gh#1"),
            Advisory("TypeError"));

        var loop = new FixLoop(session)
        {
            Ask = (_, round, _) =>
            {
                asked.Add(round);

                return Task.FromResult(round == 1
                    ? new RoundDecision(RoundChoice.ApplyEverything, Step(FixVerdict.DifferentError))
                    : RoundDecision.Stop);
            },
        };

        var result = await loop.RunAsync(new LaunchPlan(Spec, null, "ok"));

        // There is nothing for "all" to apply to prose, so the prompt comes back rather than the
        // run ending on a result nobody was shown.
        Assert.Equal([1, 2], asked);
        Assert.Equal(LoopEnd.AdviceOnly, result.End);
    }
}
