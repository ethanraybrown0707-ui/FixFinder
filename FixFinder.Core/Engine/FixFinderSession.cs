using FixFinder.Core.Execution;
using FixFinder.Core.Fingerprinting;
using FixFinder.Core.Http;
using FixFinder.Core.Parsing;
using FixFinder.Core.Patching;
using FixFinder.Core.Ranking;
using FixFinder.Core.Sources;

namespace FixFinder.Core.Engine;

/// <summary>How a whole run turned out, in the terms a person would use.</summary>
public enum SessionResult
{
    /// <summary>The program could not be started at all.</summary>
    CouldNotRun,

    /// <summary>It ran and did not go wrong. Nothing to fix.</summary>
    RanFine,

    /// <summary>
    /// It failed, but said nothing a parser could use.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="RanFine"/> because they are opposite answers to "did it work".
    /// A C program killed by an access violation prints absolutely nothing and exits
    /// 0xC0000005; folding that in with a clean exit reported a crash as a success, which is the
    /// one thing a tool for finding crashes must never do.
    /// </remarks>
    FailedSilently,

    /// <summary>It went wrong, but nothing published matches the error.</summary>
    NothingFound,

    /// <summary>Something relevant was found, but it is prose and cannot be applied.</summary>
    FoundAdvice,

    /// <summary>A patch was found that applies cleanly to this source tree.</summary>
    FoundFix,
}

/// <summary>Everything one run produced, and everything the window needs to talk about it.</summary>
public sealed record SessionOutcome
{
    public required SessionResult Result { get; init; }

    /// <summary>One line, written to be shown as the heading of a prompt.</summary>
    public required string Headline { get; init; }

    /// <summary>A short paragraph saying what happened and what can be done about it.</summary>
    public required string Detail { get; init; }

    /// <summary>The target that was run, kept so verification can repeat it exactly.</summary>
    public TargetSpec? Spec { get; init; }

    public TargetRunResult? Run { get; init; }
    public ParsedError? Error { get; init; }
    public ErrorFingerprint? Fingerprint { get; init; }

    public IReadOnlyList<FixCandidate> Candidates { get; init; } = [];

    /// <summary>The best candidate, and the one the prompt is about.</summary>
    public FixCandidate? Best { get; init; }

    public HarvestResult? Harvest { get; init; }

    /// <summary>Set when a patch was found and located; this is what Apply would carry out.</summary>
    public ApplyPlan? Plan { get; init; }

    public string? SourceRoot { get; init; }
    public IReadOnlyList<string> StackTraceFiles { get; init; } = [];

    /// <summary>Anything that went wrong on the way but did not stop the run.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>True when the failure was the build rather than the program.</summary>
    /// <remarks>
    /// Changes only the wording, not the handling: a compiler diagnostic is searched, ranked and
    /// answered exactly like a runtime crash, because from the search's point of view it is the
    /// same kind of thing - text identifying a problem somebody else has already had.
    /// </remarks>
    public bool FailedToCompile { get; init; }

    public bool CanApply => Plan is { CanApply: true };

    /// <summary>True when there is something worth putting in front of the user.</summary>
    public bool WorthShowing => Result is SessionResult.FoundFix or SessionResult.FoundAdvice;
}

/// <summary>
/// Runs the whole thing end to end: launch, read the crash, search, rank, and work out
/// whether the best answer is something that can actually be applied.
/// </summary>
/// <remarks>
/// Exists so the window can be a file picker and a button. Every decision the old panels asked
/// the user to make - where the source is, which query to send, which candidate to open, whether
/// its patch fits - has a defensible default, and the ones that do not are reported as warnings
/// rather than as questions asked up front. Keeping the sequence here rather than in the window
/// also means it can be tested without WPF.
/// <para>
/// Nothing in here writes to disk. The session decides what <i>could</i> be applied and stops;
/// applying stays behind the preview, the dry-run default and the typed confirmation.
/// </para>
/// </remarks>
public sealed class FixFinderSession(FixFinderHttpClient http, FixSourceRegistry sources)
{
    /// <summary>Candidates whose patches are fetched before giving up on finding an appliable one.</summary>
    /// <remarks>
    /// Only the top few. Each one costs requests, and a patch sitting tenth in a ranked list is
    /// not one anybody should be applying to their source tree unattended.
    /// </remarks>
    private const int CandidatesToOpen = 3;

    private readonly ParserRegistry _parsers = new();

    /// <summary>Raised as each stage starts, for the one status line the window shows.</summary>
    public event Action<string>? Progress;

    public event Action<string>? Log;
    public event Action<CapturedLine>? LineCaptured;

    /// <summary>
    /// Runs the whole sequence for a file that was picked, building it first if it needs it.
    /// </summary>
    public async Task<SessionOutcome> RunAsync(
        LaunchPlan launch,
        SearchBudget? budget = null,
        CancellationToken cancellationToken = default)
    {
        if (!launch.Ok)
        {
            return new SessionOutcome
            {
                Result = SessionResult.CouldNotRun,
                Headline = "That program could not be run.",
                Detail = launch.Problem ?? "No reason was given.",
            };
        }

        if (launch.Compile is { } compile)
        {
            var built = await BuildAsync(compile, launch, budget, cancellationToken);
            if (built is not null) return built;
        }

        return await RunAsync(launch.Spec!, budget, cancellationToken, launch.SourceFolder);
    }

    /// <summary>
    /// Builds the program, and turns a failed build into an error worth looking up.
    /// </summary>
    /// <remarks>
    /// Returns null when the build succeeded and the run should go ahead. A compiler diagnostic
    /// is a better search term than most runtime messages: <c>error C2065</c> and
    /// <c>error CS0103</c> are globally unique, and everyone who has hit one has pasted it
    /// verbatim into a search box.
    /// </remarks>
    private async Task<SessionOutcome?> BuildAsync(
        TargetSpec compile, LaunchPlan launch, SearchBudget? budget, CancellationToken cancellationToken)
    {
        Progress?.Invoke("Compiling...");

        var runner = new TargetRunner(_parsers);

        void ForwardLog(string message) => Log?.Invoke(message);
        void ForwardLine(CapturedLine line) => LineCaptured?.Invoke(line);

        runner.Log += ForwardLog;
        runner.LineCaptured += ForwardLine;

        TargetRunResult build;

        try
        {
            build = await runner.RunAsync(compile, cancellationToken);
        }
        finally
        {
            runner.Log -= ForwardLog;
            runner.LineCaptured -= ForwardLine;
        }

        if (build.Outcome == RunOutcome.LaunchFailed)
        {
            return new SessionOutcome
            {
                Result = SessionResult.CouldNotRun,
                Headline = "The compiler could not be started.",
                Detail = build.LaunchError ?? "Windows refused to start it, and did not say why.",
                Spec = compile, Run = build,
            };
        }

        if (build.Outcome == RunOutcome.ExitedClean)
        {
            Log?.Invoke("Build succeeded.");
            return null;
        }

        Log?.Invoke($"Build failed: {build.Explanation}");

        // A build that failed without printing anything a parser recognised leaves nothing to
        // search for, and saying so is better than searching for the compiler's exit code.
        if (build.Error is null)
        {
            return new SessionOutcome
            {
                Result = SessionResult.NothingFound,
                Headline = "It did not compile.",
                Detail =
                    $"The compiler exited {build.ExitCode}, but nothing in its output parsed as a " +
                    "diagnostic, so there is no reliable text to look up. The full output is below.",
                Spec = compile, Run = build, FailedToCompile = true,
                SourceRoot = launch.SourceFolder,
            };
        }

        return await SearchForAsync(
            build, build.Error, compile, budget, launch.SourceFolder, failedToCompile: true,
            cancellationToken: cancellationToken);
    }

    public async Task<SessionOutcome> RunAsync(
        TargetSpec spec,
        SearchBudget? budget = null,
        CancellationToken cancellationToken = default,
        string? sourceFolder = null)
    {
        var warnings = new List<string>();

        // ---------------------------------------------------------- run it
        Progress?.Invoke($"Running {Path.GetFileName(spec.ExecutablePath)}...");

        var runner = new TargetRunner(_parsers);

        // Named handlers, not lambdas. A "-=" against a freshly written lambda removes nothing,
        // because it is a different delegate instance from the one that was added - which is
        // harmless while the runner is a local, and becomes a leak the moment it is not.
        void ForwardLog(string message) => Log?.Invoke(message);
        void ForwardLine(CapturedLine line) => LineCaptured?.Invoke(line);

        runner.Log += ForwardLog;
        runner.LineCaptured += ForwardLine;

        TargetRunResult run;

        try
        {
            run = await runner.RunAsync(spec, cancellationToken);
        }
        finally
        {
            runner.Log -= ForwardLog;
            runner.LineCaptured -= ForwardLine;
        }

        if (run.Outcome == RunOutcome.LaunchFailed)
        {
            return new SessionOutcome
            {
                Result = SessionResult.CouldNotRun,
                Headline = "That program could not be started.",
                Detail = run.LaunchError ?? "Windows refused to start it, and did not say why.",
                Spec = spec, Run = run,
            };
        }

        if (run.Error is null)
        {
            var wentWrong = run.Outcome is RunOutcome.Crashed or RunOutcome.ExitedNonZero;

            return new SessionOutcome
            {
                Result = wentWrong ? SessionResult.FailedSilently : SessionResult.RanFine,
                Headline = NoErrorHeadline(run),
                Detail = NoErrorDetail(run),
                Spec = spec, Run = run,
            };
        }

        return await SearchForAsync(
            run, run.Error, spec, budget, sourceFolder, failedToCompile: false,
            warnings: warnings, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Everything after an error has been caught: fingerprint it, find the source, search,
    /// rank, and work out whether the best answer can actually be applied.
    /// </summary>
    /// <remarks>
    /// Shared by the two ways an error can arrive. A compiler diagnostic and a runtime crash
    /// are the same thing from here on - text that identifies a problem somebody else has
    /// probably already had - and giving them separate paths would mean two places to fix
    /// every time the search changes.
    /// </remarks>
    private async Task<SessionOutcome> SearchForAsync(
        TargetRunResult run,
        ParsedError error,
        TargetSpec spec,
        SearchBudget? budget,
        string? sourceFolder,
        bool failedToCompile,
        List<string>? warnings = null,
        CancellationToken cancellationToken = default)
    {
        warnings ??= [];

        // ---------------------------------------------------------- understand it
        var fingerprint = FingerprintBuilder.Build(error);
        var stackFiles = FilesIn(error);

        Log?.Invoke($"Detected: {error.Summary} (confidence {error.Confidence})");

        // ---------------------------------------------------------- where does it live
        var rootResult = SourceRootResolver.Resolve(userSpecified: null, error, spec);

        // A compiled program runs from a build folder, so when nothing in the trace points at
        // real source the folder the file was picked from is the only honest answer.
        var sourceRoot = rootResult.Path ?? (Directory.Exists(sourceFolder ?? "") ? sourceFolder : null);

        if (sourceRoot is null)
        {
            warnings.Add(
                "FixFinder could not work out where this program's source code is, so it can " +
                "show you a fix but cannot apply one.");
        }
        else
        {
            Log?.Invoke($"Source root: {sourceRoot} ({rootResult.Explanation})");
        }

        // ---------------------------------------------------------- look for a fix
        Progress?.Invoke("Looking for a published fix...");

        var search = await sources.SearchAsync(
            fingerprint, budget ?? SearchBudget.Default, enabled: null, cancellationToken);

        foreach (var failure in search.Failures) warnings.Add(failure);

        var ranked = CandidateRanker.Rank(search.Candidates, fingerprint);

        if (ranked.Count == 0)
        {
            return new SessionOutcome
            {
                Result = SessionResult.NothingFound,
                Headline = failedToCompile ? $"It did not compile: {error.Summary}" : $"It crashed: {error.Summary}",
                Detail = NothingFoundDetail(fingerprint, search.Failures, (budget ?? SearchBudget.Default).Cache),
                Spec = spec, Run = run, Error = error, Fingerprint = fingerprint, FailedToCompile = failedToCompile,
                SourceRoot = sourceRoot, StackTraceFiles = stackFiles, Warnings = warnings,
            };
        }

        // ---------------------------------------------------------- can any of it be applied
        Progress?.Invoke($"Checking the best of {ranked.Count} results...");

        var harvester = new PatchHarvester(http);
        harvester.Log += message => Log?.Invoke(message);

        HarvestResult? bestHarvest = null;
        ApplyPlan? bestPlan = null;
        var best = ranked[0];

        foreach (var candidate in ranked.Take(CandidatesToOpen))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var harvest = await harvester.HarvestAsync(candidate, budget?.Cache, cancellationToken);
            bestHarvest ??= harvest;

            if (!harvest.HasAppliablePatch || sourceRoot is null) continue;

            var plan = new PatchApplier().Plan(
                harvest.Patches[0], new SourcePathMapper(sourceRoot, stackFiles), stackFiles);

            if (!plan.CanApply)
            {
                Log?.Invoke($"{candidate.Id}: has a patch, but it will not apply here - {plan.Explanation}");
                continue;
            }

            // The floor the ranker documents, enforced here as well as in the ordering. A patch
            // that fits is not the same as a patch that belongs: a weakly-matched diff applying
            // cleanly by coincidence is more dangerous than an obviously irrelevant one, because
            // it arrives looking like an answer.
            if (candidate.Score < CandidateRanker.AutoAppliableFloor)
            {
                Log?.Invoke(
                    $"{candidate.Id}: its patch applies, but it only scored {candidate.Score:0} - " +
                    $"below the {CandidateRanker.AutoAppliableFloor:0} needed to offer a change. Advisory only.");

                continue;
            }

            // The first candidate whose patch genuinely fits wins, and it becomes the one the
            // prompt is about even if it was not top of the ranked list.
            best = candidate;
            bestHarvest = harvest;
            bestPlan = plan;
            break;
        }

        var common = new
        {
            Run = run,
            Error = error,
            Fingerprint = fingerprint,
            SourceRoot = sourceRoot,
            StackFiles = stackFiles,
        };

        if (bestPlan is not null)
        {
            var files = bestPlan.Files.Count;

            return new SessionOutcome
            {
                Result = SessionResult.FoundFix,
                Headline = "Found a fix that applies to your code.",
                // The title is shown directly above this in the prompt, so repeating it here
                // just pushes the part that matters further down the window.
                Detail =
                    $"It changes {files} file{(files == 1 ? "" : "s")} " +
                    $"(+{bestPlan.Files.Sum(f => f.Patch.AddedCount)} " +
                    $"-{bestPlan.Files.Sum(f => f.Patch.RemovedCount)} lines). " +
                    "Nothing has been written yet.",
                Spec = spec, Run = common.Run, Error = common.Error, Fingerprint = common.Fingerprint,
                FailedToCompile = failedToCompile,
                Candidates = ranked, Best = best, Harvest = bestHarvest, Plan = bestPlan,
                SourceRoot = common.SourceRoot, StackTraceFiles = common.StackFiles, Warnings = warnings,
            };
        }

        return new SessionOutcome
        {
            Result = SessionResult.FoundAdvice,
            Headline = "Found something that looks relevant.",
            Detail = AdviceDetail(best, fingerprint, sourceRoot),
            Spec = spec, Run = common.Run, Error = common.Error, Fingerprint = common.Fingerprint,
                FailedToCompile = failedToCompile,
            Candidates = ranked, Best = best, Harvest = bestHarvest,
            SourceRoot = common.SourceRoot, StackTraceFiles = common.StackFiles, Warnings = warnings,
        };
    }

    // ------------------------------------------------------------------ wording

    private static string NoErrorHeadline(TargetRunResult run) => run.Outcome switch
    {
        RunOutcome.Crashed => "It crashed, and printed nothing about why.",
        RunOutcome.ExitedClean => "It ran without a problem.",
        RunOutcome.TimedOut => "It was still running, so it was stopped.",
        RunOutcome.Cancelled => "Stopped before it finished.",
        _ => "It finished unhappily, but printed no error.",
    };

    private static string NoErrorDetail(TargetRunResult run) => run.Outcome switch
    {
        RunOutcome.ExitedClean =>
            "The program finished and printed nothing that looks like an error, so there is " +
            "nothing to look up.",

        RunOutcome.TimedOut =>
            "It was still going when the time ran out, and it had not printed an error. For " +
            "something meant to keep running - an app or a server - that is normal. Reproduce " +
            "the crash while it runs, then try again.",

        RunOutcome.Cancelled =>
            "Nothing in what it printed before you stopped it looks like an error.",

        RunOutcome.Crashed =>
            $"It was killed with code {run.ExitCode} and printed nothing that parsed as an error. " +
            "A native crash on Windows - an access violation, say - prints nothing at all, so " +
            "there is genuinely no text to look up. Running it under a debugger is the next step.",

        _ =>
            $"It exited with code {run.ExitCode}, but nothing in its output parsed as an error, " +
            "so there is no reliable text to search with. Whatever went wrong, the program did " +
            "not say so in a form anything could look up.",
    };

    /// <summary>
    /// Error types that are about the machine rather than about the code.
    /// </summary>
    /// <remarks>
    /// These break the first-party heuristic, and the warning it produces is not merely unhelpful
    /// but backwards. A missing module is raised on the <c>import</c> line in your own file, so
    /// the culprit frame is yours - yet "no issue or answer exists for a bug only your program
    /// has" is exactly wrong: <c>No module named 'yaml'</c> is one of the most answered
    /// questions there is, and the tool reliably finds an exact match for it.
    /// </remarks>
    private static readonly string[] EnvironmentalTypes =
    [
        "ModuleNotFoundError", "ImportError", "FileNotFoundException", "DllNotFoundException",
        "TypeLoadException", "BadImageFormatException", "MissingMethodException",
        "FileLoadException", "LoadError", "PackageNotFoundError",
    ];

    /// <summary>True when the crash is about a missing dependency rather than about your logic.</summary>
    private static bool IsEnvironmental(ErrorFingerprint fingerprint) =>
        fingerprint.ShortExceptionType is { Length: > 0 } type &&
        EnvironmentalTypes.Contains(type, StringComparer.OrdinalIgnoreCase);

    private static string NothingFoundDetail(
        ErrorFingerprint fingerprint, IReadOnlyList<string> failures, CacheMode cache)
    {
        // Said before anything else, because in this mode "nothing was found" is not a fact
        // about the error at all - nothing was looked for. Reporting it as though the services
        // were unreachable would be a straightforwardly misleading answer to a deliberate choice.
        if (cache == CacheMode.CacheOnly && failures.Count > 0)
        {
            return
                "Offline is on, so nothing was searched for - this error has no answer stored on " +
                "this machine yet. Untick Offline and run it again to look properly.";
        }

        if (fingerprint.CulpritIsFirstParty && !IsEnvironmental(fingerprint))
        {
            return
                "Nothing published matches it, which is the expected answer here: the crash is " +
                "inside your own code, and no issue or answer exists anywhere for a bug that " +
                "only your program has. This one is yours to fix.";
        }

        return failures.Count > 0
            ? "Nothing usable came back. One of the search services could not be reached, so it " +
              "is worth trying again in a moment."
            : "Nothing on GitHub or Stack Overflow matches this error closely enough to be worth " +
              "showing you.";
    }

    private static string AdviceDetail(FixCandidate best, ErrorFingerprint fingerprint, string? sourceRoot)
    {
        var why =
            best.SourceName == "Stack Overflow"
                ? "It is an answer written as prose, so FixFinder cannot apply it for you - " +
                  "turning an explanation into a patch needs a language model, and there is " +
                  "deliberately not one in this tool."
                : sourceRoot is null
                    ? "FixFinder could not find your source code, so it has nothing to apply a patch to."
                    : "Nothing attached to it is a patch that fits your copy of the code.";

        var extra = fingerprint.CulpritIsFirstParty && !IsEnvironmental(fingerprint)
            ? "\n\nWorth knowing: the crash is in your own code rather than in a library, so " +
              "even a close match is describing someone else's version of the problem."
            : "";

        return $"{why} You can read it and make the change yourself.{extra}";
    }

    private static IReadOnlyList<string> FilesIn(ParsedError error)
    {
        var files = new List<string>();

        void Walk(ParsedError node)
        {
            foreach (var frame in node.Frames)
                if (frame.File is { Length: > 0 } file) files.Add(file);

            foreach (var cause in node.Causes) Walk(cause);
        }

        Walk(error);

        return [.. files.Distinct(StringComparer.OrdinalIgnoreCase)];
    }
}
