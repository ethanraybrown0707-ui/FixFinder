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

    /// <summary>
    /// The installed dependency this crash went through, when it went through one.
    /// </summary>
    /// <remarks>
    /// Not a place FixFinder writes by default, and never without being asked for that run. It is
    /// carried because the commonest published fix in existence is a fix to a library, and a
    /// patch for a library changes that library's files - which are on this disk, outside the
    /// project, and were previously resolving to nothing at all.
    /// </remarks>
    public InstalledPackage? Dependency { get; init; }

    /// <summary>
    /// Other independent errors in the same output, not yet looked at.
    /// </summary>
    /// <remarks>
    /// Only ever populated for compiler output, where the tool reported everything it found
    /// before exiting. A crashed program contributes nothing here, because the error that
    /// stopped it is the only one that exists to be read.
    /// </remarks>
    public IReadOnlyList<ParsedError> OtherErrors { get; init; } = [];

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
public sealed class FixFinderSession(FixFinderHttpClient http, FixSourceRegistry sources) : IFixSession
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

        return await ContinueFromAsync(
            run, spec, budget, sourceFolder, failedToCompile: false,
            warnings: warnings, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Does everything after the running: read the error, find the source, search, rank, plan.
    /// </summary>
    /// <remarks>
    /// Public so a run that has already happened elsewhere can be picked up without repeating it.
    /// The loop needs exactly this: after a patch, the verifier has already built and re-run the
    /// program to reach its verdict, and launching a third time to search for the error it just
    /// reported would be both slower and less honest - a fresh run can fail differently, and the
    /// search would then be about an error nobody was shown.
    /// </remarks>
    public async Task<SessionOutcome> ContinueFromAsync(
        TargetRunResult run,
        TargetSpec spec,
        SearchBudget? budget = null,
        string? sourceFolder = null,
        bool failedToCompile = false,
        List<string>? warnings = null,
        CancellationToken cancellationToken = default)
    {
        if (run.Error is null)
        {
            var wentWrong = run.Outcome is RunOutcome.Crashed or RunOutcome.ExitedNonZero;

            return new SessionOutcome
            {
                Result = wentWrong ? SessionResult.FailedSilently : SessionResult.RanFine,
                Headline = NoErrorHeadline(run),
                Detail = NoErrorDetail(run),
                Spec = spec, Run = run,
                SourceRoot = Directory.Exists(sourceFolder ?? "") ? sourceFolder : null,
            };
        }

        return await SearchForAsync(
            run, run.Error, spec, budget, sourceFolder, failedToCompile,
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
        IReadOnlyList<ParsedError>? remaining = null,
        CancellationToken cancellationToken = default)
    {
        warnings ??= [];

        // ---------------------------------------------------------- understand it
        var fingerprint = FingerprintBuilder.Build(error);
        var stackFiles = FilesIn(error);

        // What else this run reported, so a diagnostic nobody can act on can be stepped past
        // rather than ending everything. Computed once here, and narrowed on each skip.
        var others = remaining ?? _parsers.Others(error, run.Lines);
        var dependency = InstalledPackages.From(stackFiles);

        // Recorded because it is a place FixFinder may now be asked to write, and the log is the
        // audit trail for everything it writes. Detecting it changes nothing on its own: the
        // project is still tried first, and a patch only lands here if the user types the
        // package's name to confirm it.
        if (dependency is { } package)
            Log?.Invoke($"The crash went through {package.Name}, installed at {package.Root}");

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

        // Put in front of the search results rather than ranked among them, because it is not one
        // of them. Every weight in the ranker estimates how likely a stranger's post is to be
        // about this crash; this came out of this crash, and names this file and this line. It is
        // also the only fix in the tool that can address code nobody else has ever seen.
        if (RuntimeSuggestion.For(error, sourceRoot) is { } suggestion)
        {
            Log?.Invoke($"{error.LanguageId} suggested a correction itself: {suggestion.Title}");

            ranked = [suggestion, .. ranked];
        }

        if (ranked.Count == 0)
        {
            return new SessionOutcome
            {
                Result = SessionResult.NothingFound,
                Headline = failedToCompile ? $"It did not compile: {error.Summary}" : $"It crashed: {error.Summary}",
                Detail = NothingFoundDetail(fingerprint, search.Failures, (budget ?? SearchBudget.Default).Cache),
                Spec = spec, Run = run, Error = error, Fingerprint = fingerprint, FailedToCompile = failedToCompile,
                SourceRoot = sourceRoot, StackTraceFiles = stackFiles, Warnings = warnings,
                OtherErrors = others, Dependency = dependency,
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
            OtherErrors = others, Dependency = dependency,
            };
        }

        // The same bar, used for the second of the two things it should govern. A score below the
        // point at which FixFinder would act on a result is also below the point at which it
        // should claim the result looks relevant - the top of a weak set is still the top of a
        // weak set, and announcing it as a find is how a search tool teaches people to stop
        // believing it. It is still shown; only the claim about it changes.
        // Nobody has written about a mistake only your program has, so when the culprit is your
        // own code nothing found deserves to be called relevant - however well its title matches.
        // The detail pane has always said as much, directly underneath a headline announcing a
        // find, leaving the reader to work out which of the two to believe.
        var yours = fingerprint.CulpritIsFirstParty && !IsEnvironmental(fingerprint);

        var convincing = !yours && best.Score >= CandidateRanker.AutoAppliableFloor;

        return new SessionOutcome
        {
            Result = SessionResult.FoundAdvice,
            Headline = yours
                ? "This one is in your own code."
                : convincing
                    ? "Found something that looks relevant."
                    : "Nothing matched this well.",
            Detail = yours
                ? FirstPartyDetail(fingerprint, ranked.Count)
                : convincing
                    ? AdviceDetail(best, fingerprint, sourceRoot)
                    : WeakMatchDetail(best, fingerprint, ranked.Count),
            Spec = spec, Run = common.Run, Error = common.Error, Fingerprint = common.Fingerprint,
                FailedToCompile = failedToCompile,
            Candidates = ranked, Best = best, Harvest = bestHarvest,
            SourceRoot = common.SourceRoot, StackTraceFiles = common.StackFiles, Warnings = warnings,
            OtherErrors = others, Dependency = dependency,
        };
    }

    /// <summary>
    /// Looks up one of the other errors from a run that has already happened.
    /// </summary>
    /// <remarks>
    /// What Skip calls. The program is not run again - it reported all of these at once, and
    /// running it a second time to reach an error already sitting in the output would be both
    /// slower and a different run. The errors left after this one are passed through, so each
    /// skip narrows what remains rather than offering the same list forever.
    /// </remarks>
    public Task<SessionOutcome> SearchForOtherAsync(
        SessionOutcome from,
        ParsedError error,
        SearchBudget? budget = null,
        CancellationToken cancellationToken = default)
    {
        if (from.Run is null || from.Spec is null)
            throw new InvalidOperationException("That outcome has no run to take another error from.");

        var remaining = from.OtherErrors.Where(e => !ReferenceEquals(e, error)).ToList();

        return SearchForAsync(
            from.Run, error, from.Spec, budget, from.SourceRoot, from.FailedToCompile,
            warnings: null, remaining: remaining, cancellationToken: cancellationToken);
    }

    // ------------------------------------------------------------------ the loop's view

    // Explicit, because the interface carries no warnings list and the public methods do. Hiding
    // the parameter here rather than dropping it from the session keeps the loop's dependency as
    // small as it really is without narrowing what a caller holding the session itself can ask.

    Task<SessionOutcome> IFixSession.RunAsync(
        LaunchPlan launch, SearchBudget? budget, CancellationToken cancellationToken) =>
        RunAsync(launch, budget, cancellationToken);

    Task<SessionOutcome> IFixSession.ContinueFromAsync(
        TargetRunResult run, TargetSpec spec, SearchBudget? budget, string? sourceFolder,
        bool failedToCompile, CancellationToken cancellationToken) =>
        ContinueFromAsync(run, spec, budget, sourceFolder, failedToCompile, null, cancellationToken);

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

    /// <summary>
    /// Said when the error is in the user's own code, where searching cannot answer it.
    /// </summary>
    /// <remarks>
    /// The results are still shown, and still worth a glance - somebody else's version of the
    /// same kind of mistake often explains the kind well. What is dropped is the claim that any
    /// of them is about <i>this</i> program, because none of them can be, and saying so while the
    /// paragraph below admits the opposite asks the reader to referee the tool against itself.
    /// <para>
    /// It is also the only honest answer to "why can I not apply any of these". A typo in one
    /// file has no published patch anywhere; no ranking change would ever produce one.
    /// </para>
    /// </remarks>
    private static string FirstPartyDetail(ErrorFingerprint fingerprint, int count)
    {
        var where = fingerprint.CulpritFile is { Length: > 0 } file ? $" in {file}" : "";

        return
            $"The error is{where}, in code you wrote, so nothing published anywhere is about it - " +
            "and none of it can be applied, because a patch for your program does not exist to be " +
            "found.\n\n" +
            $"The {count} result{(count == 1 ? "" : "s")} below are other people's versions of the " +
            "same kind of mistake. Read them if the kind is unfamiliar; the fix itself is yours.";
    }

    /// <summary>
    /// Said when the best of everything found is still not much, so the window does not oversell it.
    /// </summary>
    /// <remarks>
    /// Written for the case that produced it: a Python syntax error, where the closest result was
    /// a question Stack Overflow had itself closed as unsuitable. The score is quoted because a
    /// number with the reasons behind it - which the detail pane lists - is something a person can
    /// disagree with, where "looks relevant" is only an assertion.
    /// </remarks>
    private static string WeakMatchDetail(FixCandidate best, ErrorFingerprint fingerprint, int count)
    {
        var yours = fingerprint.CulpritIsFirstParty && !IsEnvironmental(fingerprint)
            ? " That is the expected answer here: the error is in your own code, and nothing is " +
              "published anywhere about a mistake only your file has. This one is yours to fix."
            : "";

        return
            $"The closest of {count} result{(count == 1 ? "" : "s")} scored {best.Score:0} out of 100, " +
            $"below the {CandidateRanker.AutoAppliableFloor:0} FixFinder wants before treating " +
            $"something as an answer.{yours}\n\n" +
            "It is shown below as the best of a weak set, not as a match. Read it if you like - " +
            "just do not expect it to be about your program.";
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
