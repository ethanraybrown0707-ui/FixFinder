using FixFinder.Core.Engine;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Parsing;
using FixFinder.Core.Sources;

namespace FixFinder.Core.Checking;

public enum CheckLane
{
    Syntax,
    Logic,
}

/// <summary>Everything one check found, and how the program ran.</summary>
public sealed record CheckReport(
    IReadOnlyList<Finding> Findings,
    IReadOnlyList<string> Notes,
    string SyntaxSummary,
    string LogicSummary,
    SessionOutcome? Run);

/// <summary>Checks a program's syntax and its logic at the same time, and reports every mistake found with how sure it is and how
/// to fix it.</summary>
public sealed class ProgramChecker(FixFinderHttpClient http, FixSourceRegistry sources)
{
    private const int MostErrorsFixed = 12;
    private const int MostWarningsFixed = 20;

    private readonly object _gate = new();
    private readonly List<Finding> _findings = [];
    private readonly List<string> _notes = [];

    public CodeLanguage Language { get; init; } = CodeLanguage.Any;

    public ExpectedBehaviour? Expected { get; init; }

    public event Action<IReadOnlyList<Finding>>? FindingsChanged;
    public event Action<CheckLane, string>? Progress;
    public event Action<CheckLane, string>? LaneFinished;
    public event Action<CapturedLine>? LineCaptured;
    public event Action<string>? Log;

    public async Task<CheckReport> CheckAsync(LaunchPlan launch, CancellationToken cancellationToken = default)
    {
        if (!launch.Ok || launch.Spec is null) return new CheckReport([], [launch.Problem ?? "That program cannot be run."], "Not checked", "Not checked", null);

        var files = launch.ChosenFile is { } chosen ? ProgramFiles.Of(chosen) : [];
        var builds = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var syntax = Task.Run(() => SyntaxLaneAsync(launch, files, builds, cancellationToken), CancellationToken.None);
        var logic = Task.Run(() => LogicLaneAsync(launch, files, builds.Task, cancellationToken), CancellationToken.None);

        try
        {
            await Task.WhenAll(syntax, logic);
        }
        finally
        {
            builds.TrySetResult(false);
        }

        var (syntaxSummary, run) = syntax.Result;

        lock (_gate)
        {
            var logicSummary = logic.Result;
            var fromCode = _findings.Count(f => f.RuleId.StartsWith("logic-", StringComparison.Ordinal));
            if (logicSummary.EndsWith("in the code", StringComparison.Ordinal))
                logicSummary = fromCode == 0 ? "No logic mistakes found in the code" : $"{Count(fromCode, "possible mistake")} in the code";

            return new CheckReport(Sorted(_findings), [.. _notes], syntaxSummary, logicSummary, run);
        }
    }

    private async Task<(string Summary, SessionOutcome? Run)> SyntaxLaneAsync(
        LaunchPlan launch, IReadOnlyList<string> files, TaskCompletionSource<bool> builds, CancellationToken cancellationToken)
    {
        try
        {
            var chosen = launch.ChosenFile ?? launch.Spec!.ExecutablePath;

            Progress?.Invoke(CheckLane.Syntax, launch.NeedsCompiling ? "Compiling..." : "Reading the code...");

            var report = files.Count > 0
                ? await CompilerDiagnostics.CollectAsync(launch, files, Language, LineCaptured, Log, cancellationToken)
                : CompilerReport.NotChecked();

            if (report.Problem is { } problem) Note(problem);

            var root = launch.SourceFolder ?? Path.GetDirectoryName(chosen);

            if (report.Errors.Count > 0)
            {
                builds.TrySetResult(false);
                Progress?.Invoke(CheckLane.Syntax, $"Found {Count(report.Errors.Count, "error")} - working out fixes...");

                await ForEachAsync(report.Errors.Take(MostErrorsFixed), async error =>
                {
                    var fix = await FixAsync(error, report.Errors, report.Output, root, fromBuild: true, cancellationToken);
                    Add(FindingFactory.FromError(error, FindingKind.Syntax, Severity.Error, Confidence.Certain, chosen, fix));
                }, cancellationToken);

                foreach (var error in report.Errors.Skip(MostErrorsFixed))
                    Add(FindingFactory.FromError(error, FindingKind.Syntax, Severity.Error, Confidence.Certain, chosen));
            }

            await ForEachAsync(report.Warnings.Take(MostWarningsFixed), async warning =>
            {
                var fix = await FixAsync(warning, [], report.Output, root, fromBuild: false, cancellationToken);
                Add(FindingFactory.FromWarning(warning, WarningRatings.For(warning), chosen, fix));
            }, cancellationToken);

            if (report.Errors.Count > 0 || report.Build is { Outcome: not RunOutcome.ExitedClean } || report.Failed && report.Checked)
            {
                builds.TrySetResult(false);

                return (report.Errors.Count > 0
                    ? $"{Count(report.Errors.Count, "error")} {(report.Errors.Count == 1 ? "stops" : "stop")} it building"
                    : "It did not build", null);
            }

            builds.TrySetResult(true);

            var outcome = await RunAsync(launch, report, cancellationToken);
            return (Summarise(outcome, report, chosen), outcome);
        }
        catch (OperationCanceledException)
        {
            builds.TrySetResult(false);
            throw;
        }
        finally
        {
            LaneFinished?.Invoke(CheckLane.Syntax, "done");
        }
    }

    private async Task<SessionOutcome> RunAsync(LaunchPlan launch, CompilerReport report, CancellationToken cancellationToken)
    {
        Progress?.Invoke(CheckLane.Syntax, $"Running {Path.GetFileName(launch.ChosenFile ?? launch.Spec!.ExecutablePath)}...");

        var session = new FixFinderSession(http, sources) { Language = Language, SearchOnline = false, CheckLogic = false };

        void Relay(string message) => Log?.Invoke(message);
        void Forward(CapturedLine line) => LineCaptured?.Invoke(line);
        void Status(string message) => Progress?.Invoke(CheckLane.Syntax, message);

        session.Log += Relay;
        session.LineCaptured += Forward;
        session.Progress += Status;

        try
        {
            var outcome = report.Build is { } build
                ? await session.RunAsync(launch.Spec!, null, cancellationToken, launch.SourceFolder, build.Lines, session.SanitizerFor(launch))
                : await session.RunAsync(launch, null, cancellationToken);

            RecordRun(outcome, launch);
            return outcome;
        }
        finally
        {
            session.Log -= Relay;
            session.LineCaptured -= Forward;
            session.Progress -= Status;
        }
    }

    private void RecordRun(SessionOutcome outcome, LaunchPlan launch)
    {
        var chosen = launch.ChosenFile ?? launch.Spec!.ExecutablePath;

        foreach (var warning in outcome.Warnings) Note(warning);

        if (outcome.Result == SessionResult.CouldNotRun)
        {
            Note($"{outcome.Headline} {outcome.Detail}");
            return;
        }

        if (outcome.Error is { } error)
        {
            if (error.ExceptionType is "EOFError" or "java.util.NoSuchElementException" && outcome.Result == SessionResult.NothingFound && outcome.Best is null)
            {
                Note($"{outcome.Headline} {outcome.Detail}");
                return;
            }

            var kind = LocalFixEngine.IsCompileError(error) || LocalFixEngine.IsSyntaxPhase(error) ? FindingKind.Syntax : FindingKind.Runtime;
            var local = outcome.Best is { } best && (best.LocalFix is not null || best.Id.EndsWith(":did-you-mean", StringComparison.Ordinal)) ? best : null;

            Add(FindingFactory.FromError(error, kind, Severity.Error, Confidence.Certain, chosen, local) with
            {
                Title = kind == FindingKind.Runtime ? $"It crashed: {FindingFactory.TitleOf(error)}" : FindingFactory.TitleOf(error),
            });

            return;
        }

        switch (outcome.Result)
        {
            case SessionResult.FailedSilently:
                Add(new Finding
                {
                    Kind = FindingKind.Runtime,
                    Severity = Severity.Error,
                    Confidence = Confidence.Certain,
                    File = chosen,
                    Title = outcome.Headline,
                    Explanation = outcome.Detail,
                    WhyItMatters = "The program stops partway through, and nothing it was meant to do after that point happens.",
                    SuggestedFix = "Run it with a debugger, or add prints to find the last line that runs. For C and C++, look for a pointer " +
                                   "or index that goes out of bounds.",
                    CorrectedExample = "",
                    RuleId = "failed-silently",
                });
                break;

            case SessionResult.RanFine when outcome.Run?.Outcome == RunOutcome.TimedOut:
                Add(new Finding
                {
                    Kind = FindingKind.Runtime,
                    Severity = Severity.Warning,
                    Confidence = Confidence.Possible,
                    File = chosen,
                    Title = "It was still running when the time ran out",
                    Explanation = outcome.Detail,
                    WhyItMatters = "A program that is meant to finish but never does has a loop that cannot end, or is waiting for input nobody gave it.",
                    SuggestedFix = "If it asks questions, type the answers into the input box. Otherwise look for a loop whose condition never changes.",
                    CorrectedExample = "",
                    RuleId = "timed-out",
                });
                break;
        }
    }

    private static string Summarise(SessionOutcome outcome, CompilerReport report, string chosen)
    {
        var warnings = report.Warnings.Count > 0 ? $", with {Count(report.Warnings.Count, "warning")}" : "";
        var reads = Path.GetExtension(chosen).ToLowerInvariant() is ".py" or ".pyw" or ".js" or ".mjs" or ".cjs" ? "No syntax errors" : "It builds";

        return outcome.Result switch
        {
            SessionResult.CouldNotRun => "It could not be started",
            _ when outcome.Error is not null => $"{reads}{warnings}, but it stops with an error when run",
            SessionResult.FailedSilently => $"{reads}{warnings}, but it crashes when run",
            _ when outcome.Run?.Outcome == RunOutcome.TimedOut => $"{reads}{warnings}, but it never finished",
            _ => $"{reads}{warnings}, and it runs to the end",
        };
    }

    private async Task<FixCandidate?> FixAsync(
        ParsedError error, IReadOnlyList<ParsedError> all, IReadOnlyList<CapturedLine> output, string? root, bool fromBuild, CancellationToken cancellationToken)
    {
        var context = new LocalFixContext
        {
            Error = error,
            Others = all.Where(e => !ReferenceEquals(e, error)).ToList(),
            Output = output,
            SourceRoot = root,
            FromBuild = fromBuild,
            Language = Language,
        };

        try
        {
            return (await LocalFixEngine.FindAsync(context, message => Log?.Invoke(message), cancellationToken))?.Candidate;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log?.Invoke($"Working out a fix for {error.Summary} failed: {ex.Message}");
            return null;
        }
    }

    private async Task<string> LogicLaneAsync(LaunchPlan launch, IReadOnlyList<string> files, Task<bool> builds, CancellationToken cancellationToken)
    {
        try
        {
            Progress?.Invoke(CheckLane.Logic, "Reading the code for logic mistakes...");

            var found = 0;
            var sourcesRead = files.Select(SourceFile.Read).OfType<SourceFile>().ToList();
            var patterns = sourcesRead.SelectMany(source => LogicPatterns.Scan(source, Log).Select(finding => (Source: source, Finding: finding))).ToList();

            await ForEachAsync(patterns, async item =>
            {
                var (checkedBy, compiles) = await CheckPatternFixAsync(item.Source, item.Finding.Fix, cancellationToken);
                Add(FindingFactory.FromPattern(item.Finding, item.Source, checkedBy, compiles));
                Interlocked.Increment(ref found);
            }, cancellationToken);

            if (Expected is not { IsEmpty: false } expected || launch.ChosenFile is not { } chosen)
                return found == 0 ? "No logic mistakes found in the code" : $"{Count(found, "possible mistake")} in the code";

            Progress?.Invoke(CheckLane.Logic, "Waiting for it to build before checking what it prints...");

            if (!await builds)
            {
                Note("What it prints was not checked, because it has to build first. Fix the errors that stop it building, then check it again.");
                return found == 0 ? "Output not checked - it does not build yet" : $"{Count(found, "possible mistake")}; output not checked";
            }

            var repair = new LogicRepair();
            repair.Log += message => Log?.Invoke(message);
            repair.Progress += message => Progress?.Invoke(CheckLane.Logic, message);

            var result = await repair.RunAsync(chosen, expected, launch.Spec!.Timeout, cancellationToken);

            if (result is null) return found == 0 ? "It printed what you expected" : $"Printed what you expected; {Count(found, "possible mistake")} in the code";

            foreach (var note in result.Notes) Note(note);

            Add(FindingFactory.FromWrongOutput(result, expected.Runs.Count, chosen, SourceFile.Read(chosen)));

            return result.Fix is not null ? "Wrong output - FixFinder found the change that fixes it" : "Wrong output";
        }
        finally
        {
            LaneFinished?.Invoke(CheckLane.Logic, "done");
        }
    }

    private async Task<(string? CheckedBy, bool Compiles)> CheckPatternFixAsync(SourceFile source, LocalFix? fix, CancellationToken cancellationToken)
    {
        if (fix is null || fix.ApplyTo(source) is not { } changed) return (null, false);
        if (!CompileCheck.CanCheck(source.Path)) return (null, true);

        var after = await CompileCheck.RunAsync(source, changed, null, cancellationToken);
        if (!after.Ran) return ("Not checked - there was nothing to compile it with.", true);

        var name = Path.GetFileName(source.Path);
        if (after.Clean) return ($"A copy of {name} with this change compiles.", true);

        var before = await CompileCheck.RunAsync(source, source.Lines, null, cancellationToken);
        var editedTo = fix.StartLine + Math.Max(fix.NewLines.Count, 1) - 1;
        var inEdit = after.Errors.Any(e => LocalFixContext.OwnFrame(e)?.Line is { } line && line >= fix.StartLine - 1 && line <= editedTo + 1);

        return before.Ran && after.Errors.Count <= before.Errors.Count && !inEdit
            ? ($"A copy of {name} with this change adds no new errors.", true)
            : (null, false);
    }

    private void Add(Finding finding)
    {
        IReadOnlyList<Finding> snapshot;

        lock (_gate)
        {
            var twin = _findings.FirstOrDefault(f => string.Equals(f.File, finding.File, StringComparison.OrdinalIgnoreCase) &&
                                                     (SameFamily(f, finding) || SameFix(f, finding)));

            if (twin is not null)
            {
                if (Rank(finding) <= Rank(twin)) return;
                _findings.Remove(twin);
            }

            _findings.Add(finding);
            snapshot = Sorted(_findings);
        }

        FindingsChanged?.Invoke(snapshot);
    }

    private static bool SameFamily(Finding a, Finding b) => a.Family is { } family && family == b.Family && a.Line == b.Line;

    private static bool SameFix(Finding a, Finding b) =>
        a.Fix is { } x && b.Fix is { } y && x.StartLine == y.StartLine && x.RemoveCount == y.RemoveCount && x.NewLines.SequenceEqual(y.NewLines);

    private static int Rank(Finding finding) =>
        (finding.Fix is not null ? 1000 : 0) + (2 - (int)finding.Severity) * 100 + (finding.ExampleIsFromYourCode ? 10 : 0) + (2 - (int)finding.Confidence);

    private void Note(string note)
    {
        lock (_gate)
        {
            if (!_notes.Contains(note)) _notes.Add(note);
        }
    }

    private static List<Finding> Sorted(IEnumerable<Finding> findings) =>
        findings
            .OrderBy(f => f.Severity)
            .ThenBy(f => f.File, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Line ?? 0)
            .ToList();

    private static async Task ForEachAsync<T>(IEnumerable<T> items, Func<T, Task> body, CancellationToken cancellationToken)
    {
        using var slots = new SemaphoreSlim(2);

        var tasks = items.Select(async item =>
        {
            await slots.WaitAsync(cancellationToken);

            try
            {
                await body(item);
            }
            finally
            {
                slots.Release();
            }
        }).ToList();

        await Task.WhenAll(tasks);
    }

    private static string Count(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";
}
