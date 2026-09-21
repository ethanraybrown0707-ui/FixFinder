using FixFinder.Core.Execution;
using FixFinder.Core.Fingerprinting;
using FixFinder.Core.Http;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;
using FixFinder.Core.Logic;
using FixFinder.Core.Parsing;
using FixFinder.Core.Parsing.Parsers;
using FixFinder.Core.Patching;
using FixFinder.Core.Ranking;
using FixFinder.Core.Sources;

namespace FixFinder.Core.Engine;

/// <summary>How a whole run turned out, in the terms a person would use.</summary>
public enum SessionResult
{
    CouldNotRun,

    RanFine,

    FailedSilently,

    NothingFound,

    FoundAdvice,

    FoundFix,
}

/// <summary>Everything one run produced, and everything the window needs to talk about it.</summary>
public sealed record SessionOutcome
{
    public required SessionResult Result { get; init; }

    public required string Headline { get; init; }

    public required string Detail { get; init; }

    public TargetSpec? Spec { get; init; }

    public TargetRunResult? Run { get; init; }
    public ParsedError? Error { get; init; }
    public ErrorFingerprint? Fingerprint { get; init; }

    public IReadOnlyList<FixCandidate> Candidates { get; init; } = [];

    public FixCandidate? Best { get; init; }

    public HarvestResult? Harvest { get; init; }

    public ApplyPlan? Plan { get; init; }

    public string? SourceRoot { get; init; }
    public IReadOnlyList<string> StackTraceFiles { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public InstalledPackage? Dependency { get; init; }

    public bool FailedToCompile { get; init; }

    public bool CanApply => Plan is { CanApply: true };
}

/// <summary>Runs the program, reads the crash, searches and ranks answers, and works out whether the best one fits the program.</summary>
public sealed class FixFinderSession(FixFinderHttpClient http, FixSourceRegistry sources)
{
    private const int CandidatesToOpen = 3;

    // abort() ends a C or C++ program with 3 on Windows, and a race with another thread can stop it printing why first.
    private const int WindowsAbortExitCode = 3;

    public CodeLanguage Language { get; init; } = CodeLanguage.Any;

    public ExpectedBehaviour? Expected { get; init; }

    public bool SearchOnline { get; init; } = true;

    public bool CheckLogic { get; init; } = true;

    private string? _chosen;

    private ParserRegistry? _registry;

    private ParserRegistry Parsers => _registry ??= Language.Parsers();

    public event Action<string>? Progress;

    public event Action<string>? Log;
    public event Action<CapturedLine>? LineCaptured;

    public async Task<SessionOutcome> RunAsync(
        LaunchPlan launch,
        SearchBudget? budget = null,
        CancellationToken cancellationToken = default)
    {
        _chosen = launch.ChosenFile;

        if (!launch.Ok)
        {
            return new SessionOutcome
            {
                Result = SessionResult.CouldNotRun,
                Headline = "That program could not be run.",
                Detail = launch.Problem ?? "No reason was given.",
            };
        }

        IReadOnlyList<CapturedLine>? buildOutput = null;

        if (launch.Compile is { } compile)
        {
            var (built, build) = await BuildAsync(compile, launch, budget, cancellationToken);
            if (built is not null) return built;

            buildOutput = build?.Lines;
        }

        var sanitizer = SanitizerFor(launch);

        return await RunAsync(launch.Spec!, budget, cancellationToken, launch.SourceFolder, buildOutput, sanitizer);
    }

    public Func<CancellationToken, Task<TargetRunResult?>>? SanitizerFor(LaunchPlan launch) =>
        launch is { Compile: not null, ChosenFile: { } chosen, Spec: { } run } ? SanitizerFor(chosen, run) : null;

    private Func<CancellationToken, Task<TargetRunResult?>>? SanitizerFor(string source, TargetSpec run)
    {
        if (CompiledLanguages.PrepareSanitized(source, run) is not { } plan) return null;

        return async cancellationToken =>
        {
            var runner = new TargetRunner(Parsers);

            void ForwardLog(string message) => Log?.Invoke(message);
            runner.Log += ForwardLog;

            try
            {
                Log?.Invoke(plan.Explanation);

                var build = await runner.RunAsync(plan.Compile, cancellationToken);

                if (build.Outcome != RunOutcome.ExitedClean)
                {
                    Log?.Invoke($"The AddressSanitizer build did not work, so the crash stays unlocated: {build.Explanation}");
                    return null;
                }

                return await runner.RunAsync(plan.Run, cancellationToken);
            }
            finally
            {
                runner.Log -= ForwardLog;
            }
        };
    }

    private async Task<(SessionOutcome? Outcome, TargetRunResult? Build)> BuildAsync(
        TargetSpec compile, LaunchPlan launch, SearchBudget? budget, CancellationToken cancellationToken)
    {
        Progress?.Invoke("Compiling...");

        var runner = new TargetRunner(Parsers);

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
            return (new SessionOutcome
            {
                Result = SessionResult.CouldNotRun,
                Headline = "The compiler could not be started.",
                Detail = build.LaunchError ?? "Windows refused to start it, and did not say why.",
                Spec = compile, Run = build,
            }, build);
        }

        if (build.Outcome == RunOutcome.ExitedClean)
        {
            Log?.Invoke("Build succeeded.");
            return (null, build);
        }

        Log?.Invoke($"Build failed: {build.Explanation}");

        if (build.Error is null)
        {
            return (new SessionOutcome
            {
                Result = SessionResult.NothingFound,
                Headline = "It did not compile.",
                Detail =
                    $"The compiler exited {build.ExitCode}, but nothing in its output parsed as a " +
                    "diagnostic, so there is no reliable text to look up. The full output is below.",
                Spec = compile, Run = build, FailedToCompile = true,
                SourceRoot = launch.SourceFolder,
            }, build);
        }

        return (await SearchForAsync(
            build, build.Error, compile, budget, launch.SourceFolder, failedToCompile: true,
            cancellationToken: cancellationToken), build);
    }

    public async Task<SessionOutcome> RunAsync(
        TargetSpec spec,
        SearchBudget? budget = null,
        CancellationToken cancellationToken = default,
        string? sourceFolder = null,
        IReadOnlyList<CapturedLine>? buildOutput = null,
        Func<CancellationToken, Task<TargetRunResult?>>? rerunWithSanitizer = null)
    {
        var warnings = new List<string>();

        Progress?.Invoke($"Running {Path.GetFileName(spec.ExecutablePath)}...");

        var runner = new TargetRunner(Parsers);

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
            warnings: warnings, cancellationToken: cancellationToken,
            buildOutput: buildOutput, rerunWithSanitizer: rerunWithSanitizer);
    }

    public async Task<SessionOutcome> ContinueFromAsync(
        TargetRunResult run,
        TargetSpec spec,
        SearchBudget? budget = null,
        string? sourceFolder = null,
        bool failedToCompile = false,
        List<string>? warnings = null,
        CancellationToken cancellationToken = default,
        IReadOnlyList<CapturedLine>? buildOutput = null,
        Func<CancellationToken, Task<TargetRunResult?>>? rerunWithSanitizer = null)
    {
        if (run.Error is null)
        {
            var wentWrong = run.Outcome is RunOutcome.Crashed or RunOutcome.ExitedNonZero;
            var abortedSilently = run.Outcome == RunOutcome.ExitedNonZero && run.ExitCode == WindowsAbortExitCode;

            if ((run.Outcome == RunOutcome.Crashed || abortedSilently) && rerunWithSanitizer is not null)
            {
                Progress?.Invoke("It crashed without a word - rebuilding it to find where and why...");

                var sanitized = await rerunWithSanitizer(cancellationToken);
                NoteRefused(sanitized, ref warnings);

                if (sanitized is { Error: { } located })
                {
                    (warnings ??= []).Add(
                        "It crashed without printing anything, so FixFinder rebuilt it to find where and why - with " +
                        "AddressSanitizer, and for C++ a handler that names an exception nothing caught - and ran it once " +
                        "more. The error shown is from that second run.");

                    return await SearchForAsync(
                        sanitized, located, spec, budget, sourceFolder, failedToCompile: false,
                        warnings: warnings, cancellationToken: cancellationToken, buildOutput: buildOutput);
                }
            }

            if (wentWrong && buildOutput is { Count: > 0 } && CrashingWarning(buildOutput) is { } warned)
            {
                return await SearchForAsync(
                    run, warned, spec, budget, sourceFolder, failedToCompile: false,
                    warnings: warnings, cancellationToken: cancellationToken, buildOutput: buildOutput);
            }

            if (run.Outcome == RunOutcome.ExitedClean && (buildOutput is { Count: > 0 } || rerunWithSanitizer is not null))
            {
                if (buildOutput is { Count: > 0 } && SilentBugWarning(buildOutput) is { } bug)
                {
                    (warnings ??= []).Add(
                        "It ran without failing, but the compiler warned about a mistake that does not always show when " +
                        "the program runs - it depends on what happens to be in memory - so it is treated as the error.");

                    return await SearchForAsync(
                        run, bug, spec, budget, sourceFolder, failedToCompile: false,
                        warnings: warnings, cancellationToken: cancellationToken, buildOutput: buildOutput, ranWithoutFailing: true);
                }

                if (rerunWithSanitizer is not null)
                {
                    Progress?.Invoke("It ran without failing - checking it once more under AddressSanitizer...");

                    var checkedRun = await rerunWithSanitizer(cancellationToken);
                    NoteRefused(checkedRun, ref warnings);

                    if (checkedRun is { Error: { } found })
                    {
                        (warnings ??= []).Add(
                            "It ran without failing, so FixFinder rebuilt it with AddressSanitizer, which checks every memory " +
                            "access, and ran it once more. That run found a mistake the first one happened to survive. The " +
                            "error shown is from that second run.");

                        return await SearchForAsync(
                            checkedRun, found, spec, budget, sourceFolder, failedToCompile: false,
                            warnings: warnings, cancellationToken: cancellationToken, buildOutput: buildOutput, ranWithoutFailing: true);
                    }
                }
            }

            if (CheckLogic && _chosen is { } chosen && run.Outcome is RunOutcome.ExitedClean or RunOutcome.TimedOut or RunOutcome.Crashed)
            {
                if (run.Outcome == RunOutcome.ExitedClean && Expected is { IsEmpty: false } expected)
                    return await LogicAsync(run, spec, chosen, expected, budget, sourceFolder, warnings, cancellationToken);

                if (StaticLogicError(chosen, run.Outcome) is { } logic)
                {
                    (warnings ??= []).Add(run.Outcome switch
                    {
                        RunOutcome.TimedOut => "It never finished, and the code has a loop that cannot end - nothing inside it moves it on.",
                        RunOutcome.Crashed => "It crashed without a word, and the code does something that crashes on some systems and not others.",
                        _ => "It ran without failing, but the code has a logic mistake that makes it do something other than what it sets out to.",
                    });

                    return await SearchForAsync(
                        run, logic, spec, budget, sourceFolder, failedToCompile: false,
                        warnings: warnings, cancellationToken: cancellationToken, ranWithoutFailing: true);
                }
            }

            return new SessionOutcome
            {
                Result = wentWrong ? SessionResult.FailedSilently : SessionResult.RanFine,
                Headline = NoErrorHeadline(run),
                Detail = NoErrorDetail(run),
                Spec = spec, Run = run, Warnings = warnings ?? [],
                SourceRoot = Directory.Exists(sourceFolder ?? "") ? sourceFolder : null,
            };
        }

        if (run.Error.LanguageId == "generic" && run.ExitCode == 0 && buildOutput is { Count: > 0 } && SilentBugWarning(buildOutput) is { } warnedInstead)
        {
            (warnings ??= []).Add(
                "It exited normally, but the compiler warned about a mistake that does not always show when the program runs, so " +
                "that warning is treated as the error rather than the output it printed.");

            return await SearchForAsync(
                run, warnedInstead, spec, budget, sourceFolder, failedToCompile: false,
                warnings: warnings, cancellationToken: cancellationToken, buildOutput: buildOutput, ranWithoutFailing: true);
        }

        if (AskedForInput(run.Error)) return NeedsInput(run, run.Error, spec, sourceFolder);

        return await SearchForAsync(
            run, run.Error, spec, budget, sourceFolder, failedToCompile,
            warnings: warnings, cancellationToken: cancellationToken);
    }

    private static bool AskedForInput(ParsedError error) => error.LanguageId switch
    {
        "python" => error.ExceptionType == "EOFError" &&
                    (error.Message ?? "").Contains("EOF when reading a line", StringComparison.Ordinal),
        "java" => error.ExceptionType == "java.util.NoSuchElementException" &&
                  error.Frames.Any(frame => frame.Symbol?.Contains("Scanner", StringComparison.Ordinal) == true),
        _ => false,
    };

    private static SessionOutcome NeedsInput(TargetRunResult run, ParsedError error, TargetSpec spec, string? sourceFolder)
    {
        var typed = spec.StandardInput is { Length: > 0 } input
            ? input.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n').Length
            : 0;

        return new SessionOutcome
        {
            Result = SessionResult.NothingFound,
            Headline = typed == 0
                ? "It stopped to ask for input, and there was nobody to type it."
                : $"It asked for more input than the {typed} line{(typed == 1 ? "" : "s")} you gave it.",
            Detail = typed == 0
                ? "FixFinder runs the program without a keyboard, so the moment it asked for input it got none and " +
                  "stopped - before it could reach anything else that might be wrong. Type the answers it should read " +
                  "into the input box, one per line, and run it again."
                : "Every line in the input box was read, and then it asked for another. Add the rest of the answers, " +
                  "one per line, and run it again.",
            Spec = spec,
            Run = run,
            Error = error,
            SourceRoot = Directory.Exists(sourceFolder ?? "") ? sourceFolder : null,
        };
    }

    private async Task<SessionOutcome> SearchForAsync(
        TargetRunResult run,
        ParsedError error,
        TargetSpec spec,
        SearchBudget? budget,
        string? sourceFolder,
        bool failedToCompile,
        List<string>? warnings = null,
        IReadOnlyList<ParsedError>? remaining = null,
        CancellationToken cancellationToken = default,
        IReadOnlyList<CapturedLine>? buildOutput = null,
        bool ranWithoutFailing = false,
        LocalFixFound? decided = null)
    {
        warnings ??= [];

        var searchWeb = SearchOnline && error.LanguageId != "logic";

        var fingerprint = FingerprintBuilder.Build(error);
        var stackFiles = FilesIn(error);

        var others = remaining ?? Parsers.Others(error, run.Lines);
        var dependency = InstalledPackages.From(stackFiles);

        if (dependency is { } package)
            Log?.Invoke($"The crash went through {package.Name}, installed at {package.Root}");

        Log?.Invoke($"Detected: {error.Summary} (confidence {error.Confidence})");

        var rootResult = SourceRootResolver.Resolve(userSpecified: null, error, spec);

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

        var suggestion = decided is null ? RuntimeSuggestion.For(error, sourceRoot) : null;

        using var stopLocal = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var localFix = decided is not null
            ? Task.FromResult<LocalFixFound?>(decided)
            : suggestion is null
            ? Task.Run(
                () => LocalFixAsync(run, error, others, spec, sourceRoot, failedToCompile, buildOutput, stopLocal.Token),
                CancellationToken.None)
            : Task.FromResult<LocalFixFound?>(null);

        Progress?.Invoke(!searchWeb
            ? "Working out the fix from your code..."
            : suggestion is null
            ? "Looking for a published fix, and working one out from your code..."
            : "Looking for a published fix...");

        AggregateSearchResult search;

        try
        {
            search = searchWeb
                ? await sources.SearchAsync(fingerprint, budget ?? SearchBudget.Default, enabled: null, cancellationToken)
                : new AggregateSearchResult([], [], 0);
        }
        catch
        {
            stopLocal.Cancel();
            await Task.WhenAny(localFix);
            throw;
        }

        foreach (var failure in search.Failures) warnings.Add(failure);

        var ranked = CandidateRanker.Rank(search.Candidates, fingerprint);

        if (suggestion is not null)
        {
            Log?.Invoke($"{error.LanguageId} suggested a correction itself: {suggestion.Title}");

            ranked = [suggestion, .. ranked];
        }

        else if (await localFix is { } local)
        {
            Log?.Invoke($"Worked out a fix from the code itself: {local.Candidate.Title}");

            ranked = [local.Candidate, .. ranked];

            if (!stackFiles.Contains(local.File, StringComparer.OrdinalIgnoreCase)) stackFiles = [.. stackFiles, local.File];
        }

        if (MissingModule.For(error, spec) is { } install)
        {
            Log?.Invoke($"The interpreter is missing a package: {install.Command}");

            ranked = [install, .. ranked];
        }

        else if (MissingDependency.For(error, spec) is { } dependencyInstall)
        {
            Log?.Invoke($"A dependency is missing: {dependencyInstall.Title}");

            ranked = [dependencyInstall, .. ranked];
        }

        if (ranked.Count == 0)
        {
            return new SessionOutcome
            {
                Result = SessionResult.NothingFound,
                Headline = failedToCompile ? $"It did not compile: {error.Summary}"
                    : ranWithoutFailing ? $"It ran, but: {error.Summary}"
                    : $"It crashed: {error.Summary}",
                Detail = searchWeb
                    ? NothingFoundDetail(fingerprint, search.Failures, (budget ?? SearchBudget.Default).Cache)
                    : $"FixFinder found this in the code but could not work out a change it could check: {error.Message}.",
                Spec = spec, Run = run, Error = error, Fingerprint = fingerprint, FailedToCompile = failedToCompile,
                SourceRoot = sourceRoot, StackTraceFiles = stackFiles, Warnings = warnings, Dependency = dependency,
            };
        }

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

            var plan = new PatchPlanner().Plan(
                harvest.Patches[0], new SourcePathMapper(sourceRoot, stackFiles), stackFiles);

            if (!plan.CanApply)
            {
                Log?.Invoke($"{candidate.Id}: has a patch, but it will not apply here - {plan.Explanation}");
                continue;
            }

            if (candidate.Score < CandidateRanker.AutoAppliableFloor)
            {
                Log?.Invoke(
                    $"{candidate.Id}: its patch applies, but it only scored {candidate.Score:0} - " +
                    $"below the {CandidateRanker.AutoAppliableFloor:0} needed to offer a change. Advisory only.");

                continue;
            }

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
                Headline = error.ExceptionType == "wrong output"
                    ? "Found the change that makes it print what you expected."
                    : error.LanguageId == "logic"
                        ? "Found a logic mistake in the code, and a fix for it."
                        : "Found a fix that fits your code.",
                Detail =
                    $"It changes {files} file{(files == 1 ? "" : "s")} " +
                    $"(+{bestPlan.Files.Sum(f => f.Patch.AddedCount)} " +
                    $"-{bestPlan.Files.Sum(f => f.Patch.RemovedCount)} lines). " +
                    "Copy it and paste it in - FixFinder does not touch your files.",
                Spec = spec, Run = common.Run, Error = common.Error, Fingerprint = common.Fingerprint,
                FailedToCompile = failedToCompile,
                Candidates = ranked, Best = best, Harvest = bestHarvest, Plan = bestPlan,
                SourceRoot = common.SourceRoot, StackTraceFiles = common.StackFiles, Warnings = warnings, Dependency = dependency,
            };
        }

        if (best is { Tier: FixTier.Dependency, Command: { Length: > 0 } command })
        {
            return new SessionOutcome
            {
                Result = SessionResult.FoundFix,
                Headline = "A package is missing, and FixFinder can install it.",
                Detail =
                    $"Nothing in your code is wrong - the interpreter that ran it does not have this " +
                    $"package. Installing it changes no files:\n\n    {command}\n\n" +
                    "Copy it and run it in a terminal - FixFinder does not run anything for you.",
                Spec = spec, Run = common.Run, Error = common.Error, Fingerprint = common.Fingerprint,
                FailedToCompile = failedToCompile,
                Candidates = ranked, Best = best, Harvest = bestHarvest,
                SourceRoot = common.SourceRoot, StackTraceFiles = common.StackFiles, Warnings = warnings, Dependency = dependency,
            };
        }

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
            SourceRoot = common.SourceRoot, StackTraceFiles = common.StackFiles, Warnings = warnings, Dependency = dependency,
        };
    }

    private static ParsedError? CrashingWarning(IReadOnlyList<CapturedLine> buildOutput) =>
        MsvcParser.ParseWarnings(buildOutput).FirstOrDefault(w =>
            (w.ErrorCode == "C4013" && CStandardLibrary.ReturnsPointer.Any(name =>
                (w.Message ?? "").StartsWith($"'{name}' undefined", StringComparison.Ordinal))) ||
            (w.ErrorCode == "C4477" && ((w.Message ?? "").Contains("format string '%s'", StringComparison.Ordinal) || AddressExpected(w.Message))) ||
            w.ErrorCode == "C4172")
        ?? GccClangParser.ParseWarnings(buildOutput).FirstOrDefault(w =>
            (w.Message ?? "").StartsWith("format '%s' expects", StringComparison.Ordinal) ||
            (w.Message ?? "").StartsWith("format '", StringComparison.Ordinal) && AddressExpected(w.Message) ||
            (w.Message ?? "").StartsWith("reference to local variable '", StringComparison.Ordinal));

    private static ParsedError? SilentBugWarning(IReadOnlyList<CapturedLine> buildOutput)
    {
        static bool Gcc(string message) =>
            message.StartsWith("call to 'gets' declared with attribute warning", StringComparison.Ordinal) ||
            message.StartsWith("implicit declaration of function 'gets'", StringComparison.Ordinal) ||
            message.StartsWith("initializer-string for array of 'char' is too long", StringComparison.Ordinal) ||
            message.StartsWith("function returns address of local variable", StringComparison.Ordinal) ||
            message.StartsWith("reference to local variable '", StringComparison.Ordinal) ||
            message.Contains("called on pointer returned from a mismatched allocation function", StringComparison.Ordinal) ||
            message.StartsWith("format '%s' expects", StringComparison.Ordinal) ||
            message.StartsWith("format '", StringComparison.Ordinal) && AddressExpected(message) ||
            message.StartsWith("format '", StringComparison.Ordinal) && message.Contains("has type 'char (*)[", StringComparison.Ordinal) ||
            message.StartsWith("comparison with string literal results in unspecified behavio", StringComparison.Ordinal) ||
            message.StartsWith("passing argument 3 of 'pthread_create' from incompatible pointer type", StringComparison.Ordinal) ||
            message.StartsWith("passing argument 4 of 'qsort' from incompatible pointer type", StringComparison.Ordinal) ||
            message.Contains("which has non-virtual destructor", StringComparison.Ordinal) ||
            message.StartsWith("catching polymorphic type", StringComparison.Ordinal);

        return GccClangParser.ParseWarnings(buildOutput).FirstOrDefault(w => Gcc(w.Message ?? ""))
            ?? MsvcParser.ParseWarnings(buildOutput).FirstOrDefault(w =>
                w.ErrorCode is "C4172" or "C4045" or "C4700" ||
                (w.ErrorCode == "C4477" && ((w.Message ?? "").Contains("format string '%s'", StringComparison.Ordinal) || AddressExpected(w.Message))));
    }

    private static bool AddressExpected(string? message) =>
        message is not null &&
        System.Text.RegularExpressions.Regex.IsMatch(message, @"type '(?<want>[^']+?) ?\*', but (?:variadic )?argument \d+ has type '\k<want>'");

    private async Task<LocalFixFound?> LocalFixAsync(
        TargetRunResult run, ParsedError error, IReadOnlyList<ParsedError> others, TargetSpec spec,
        string? sourceRoot, bool failedToCompile, IReadOnlyList<CapturedLine>? buildOutput,
        CancellationToken cancellationToken)
    {
        var context = new LocalFixContext
        {
            Error = error,
            Others = others,
            Output = error.ExceptionType == "compile warning" && buildOutput is not null ? buildOutput : run.Lines,
            SourceRoot = sourceRoot,
            FromBuild = failedToCompile,
            PythonInterpreter = error.LanguageId == "python" ? spec.ExecutablePath : null,
            Language = Language,
        };

        void Relay(string message) => Log?.Invoke(message);

        if (await LocalFixEngine.FindAsync(context, Relay, cancellationToken) is { } found) return found;

        return buildOutput is { Count: > 0 } && !failedToCompile && error.ExceptionType != "compile warning"
            ? await LocalFixEngine.ForBuildWarningsAsync(buildOutput, sourceRoot, Relay, cancellationToken, Language)
            : null;
    }

    public Task<SessionOutcome> LookUpAsync(
        TargetRunResult run, ParsedError error, TargetSpec spec, string? sourceFolder, bool failedToCompile,
        SearchBudget? budget = null, CancellationToken cancellationToken = default) =>
        SearchForAsync(run, error, spec, budget, sourceFolder, failedToCompile, cancellationToken: cancellationToken);

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

    private static readonly string[] EnvironmentalTypes =
    [
        "ModuleNotFoundError", "ImportError", "FileNotFoundException", "DllNotFoundException",
        "TypeLoadException", "BadImageFormatException", "MissingMethodException",
        "FileLoadException", "LoadError", "PackageNotFoundError",
    ];

    private static bool IsEnvironmental(ErrorFingerprint fingerprint) =>
        fingerprint.ShortExceptionType is { Length: > 0 } type &&
        EnvironmentalTypes.Contains(type, StringComparer.OrdinalIgnoreCase);

    private void NoteRefused(TargetRunResult? rerun, ref List<string>? warnings)
    {
        if (rerun is not { Outcome: RunOutcome.LaunchFailed }) return;

        (warnings ??= []).Add(
            "FixFinder rebuilt it with AddressSanitizer to check every memory access, but Windows would not start that build" +
            (rerun.LaunchError is { Length: > 0 } reason ? $": {reason}" : ".") + " So this run has not been checked that way.");
    }

    private ParsedError? StaticLogicError(string file, RunOutcome outcome)
    {
        if (LocalFixes.SourceFile.Read(file) is not { } source || !Language.Reads(new LogicPatternRule())) return null;

        var findings = LogicPatterns.Scan(source, message => Log?.Invoke(message))
            .Where(f => f.Fix is not null && f.Kind == Checking.FindingKind.Logic && f.Severity != Checking.Severity.Suggestion)
            .Where(f => outcome switch
            {
                RunOutcome.TimedOut => f.PatternId.EndsWith("loop-never-advances", StringComparison.Ordinal),
                RunOutcome.Crashed => f.PatternId == "logic-string-literal-modified",
                _ => true,
            })
            .ToList();

        if (findings is not [var first, ..]) return null;

        Log?.Invoke($"Logic mistake in the code: {first.PatternId} at line {first.Line} - {first.Message}");
        return LogicPatterns.ToError(first, source);
    }

    private async Task<SessionOutcome> LogicAsync(
        TargetRunResult run, TargetSpec spec, string chosen, ExpectedBehaviour expected, SearchBudget? budget,
        string? sourceFolder, List<string>? warnings, CancellationToken cancellationToken)
    {
        warnings ??= [];

        var repair = new LogicRepair();
        repair.Log += message => Log?.Invoke(message);
        repair.Progress += message => Progress?.Invoke(message);

        var result = await repair.RunAsync(chosen, expected, spec.Timeout, cancellationToken);

        if (result is null)
        {
            if (StaticLogicError(chosen, RunOutcome.ExitedClean) is { } logic)
            {
                warnings.Add("Every run printed what you expected, but the code has a logic mistake that other input could show.");
                return await SearchForAsync(run, logic, spec, budget, sourceFolder, failedToCompile: false, warnings: warnings, cancellationToken: cancellationToken, ranWithoutFailing: true);
            }

            return new SessionOutcome
            {
                Result = SessionResult.RanFine,
                Headline = expected.Runs.Count == 1 ? "It ran, and printed what you expected." : $"It ran, and all {expected.Runs.Count} runs printed what you expected.",
                Detail = "The output matched line for line. Nothing to fix.",
                Spec = spec, Run = run,
                SourceRoot = Directory.Exists(sourceFolder ?? "") ? sourceFolder : null,
            };
        }

        foreach (var note in result.Notes) warnings.Add(note);

        var at = result.Fix?.StartLine ?? result.Suspicious.FirstOrDefault()?.Line;
        var error = LogicPatterns.WrongOutput(result.Mismatch, chosen, at);
        var runName = expected.Runs.Count == 1 ? "It" : $"Run {result.FailingRun} of {expected.Runs.Count}";

        Log?.Invoke($"Wrong output: {runName} - {result.Mismatch.Describe()}");

        if (result.Fix is { } fix && LocalFixes.SourceFile.Read(chosen) is { } source)
        {
            var widened = fix.Unambiguous(source);
            var root = Directory.Exists(sourceFolder ?? "") ? sourceFolder : Path.GetDirectoryName(chosen);

            if (LocalFixDiff.Render(source, widened, RuntimeSuggestion.RelativePath(source.Path, root)) is { } diff)
            {
                var candidate = LocalFixEngine.CandidateFor(
                    widened, source, diff, LogicRepair.Describe(result),
                    result.FromPattern ? fix.Explanation : $"{runName} printed the wrong thing: {result.Mismatch.Describe()}. {fix.Explanation}");

                return await SearchForAsync(
                    run, error, spec, budget, sourceFolder, failedToCompile: false, warnings: warnings, cancellationToken: cancellationToken,
                    ranWithoutFailing: true, decided: new LocalFixFound(candidate, chosen));
            }
        }

        var lines = result.Suspicious.Take(5).Select(s => $"line {s.Line} (Ochiai {s.Ochiai:0.00})").ToList();

        return new SessionOutcome
        {
            Result = SessionResult.NothingFound,
            Headline = $"{runName} printed the wrong thing: {result.Mismatch.Describe()}.",
            Detail =
                $"FixFinder tried {result.Tried} small change{(result.Tried == 1 ? "" : "s")} and none of them made every run print what you expected. " +
                (lines.Count > 0
                    ? $"The lines most likely to hold the mistake - the ones the wrong runs went through more than the right ones - are {string.Join(", ", lines)}."
                    : "The mistake may need more than one change, or a change bigger than one word or number."),
            Spec = spec, Run = run, Error = error, Warnings = warnings,
            SourceRoot = Directory.Exists(sourceFolder ?? "") ? sourceFolder : null,
        };
    }

    private static string NothingFoundDetail(
        ErrorFingerprint fingerprint, IReadOnlyList<string> failures, CacheMode cache)
    {
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

    private static string FirstPartyDetail(ErrorFingerprint fingerprint, int count)
    {
        var where = fingerprint.CulpritFile is { Length: > 0 } file ? $" in {file}" : "";

        return
            $"The error is{where}, in code you wrote, so nothing published anywhere is about it - " +
            "and none of it is going to be the fix, because nothing was ever written about your " +
            "file.\n\n" +
            $"The {count} result{(count == 1 ? "" : "s")} below are other people's versions of the " +
            "same kind of mistake. Read them if the kind is unfamiliar; the fix itself is yours.";
    }

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
