using System.Text;
using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>What looking for a logic fix found.</summary>
public sealed record LogicRepairResult(
    OutputMismatch Mismatch, int FailingRun, IReadOnlyList<LineSuspicion> Suspicious, LocalFix? Fix, bool FromPattern,
    int Tried, int Passed, int Runs, bool CoverageUsed, IReadOnlyList<string> Notes);

/// <summary>Finds the change that makes a program print what it should - by trying changes and running them, not by guessing.</summary>
public sealed class LogicRepair
{
    public event Action<string>? Log;
    public event Action<string>? Progress;

    /// <summary>How long the search goes on for once the changes it always tries have been - see <see cref="OutOfTime"/>.</summary>
    public TimeSpan Budget { get; init; } = TimeSpan.FromSeconds(120);

    /// <summary>
    /// How many changes, the likeliest first, are tried however long they take, so a busy computer does not decide how many
    /// are tried: a CI runner busy with other work once took fourteen seconds for runs that took half a second here.
    /// </summary>
    public const int AlwaysTried = 40;

    /// <summary>How long the search goes on for at most, however few changes it has tried.</summary>
    public static readonly TimeSpan MostTime = TimeSpan.FromMinutes(5);

    public int MostChanges { get; init; } = 400;

    public int MostLines { get; init; } = 40;

    public int AtOnce { get; init; } = LocalFixEngine.ChecksAtOnce;

    /// <summary>
    /// How many changes whose run was stopped for time are tried again, one at a time and with longer to run, straight after
    /// they were: a run stopped for time showed nothing either way, and on a busy computer even the right change can be slow.
    /// </summary>
    public int MostTriedAgainForTime { get; init; } = 3;

    /// <summary>What trying one change showed.</summary>
    private enum ChangeVerdict
    {
        /// <summary>Every run printed what was expected.</summary>
        PrintsWhatWasExpected,

        /// <summary>A run printed something else, or the change did not build.</summary>
        PrintsSomethingElse,

        /// <summary>A run was stopped for time before it finished, so whether the change is right is not known.</summary>
        RanOutOfTime,
    }

    public async Task<LogicRepairResult?> RunAsync(
        string chosenFile, ExpectedBehaviour expected, TimeSpan runTimeout, CancellationToken cancellationToken)
    {
        if (expected.IsEmpty || SourceFile.Read(chosenFile) is not { } source) return null;

        var started = DateTime.UtcNow;
        var notes = new List<string>();

        Progress?.Invoke("Checking every run against the output you expected...");

        var root = ProgramCopy.RootOf(chosenFile);
        var baseline = await EvaluateAsync(chosenFile, expected, runTimeout, runTimeout, stopAtFirstWrong: false, cancellationToken);
        if (baseline is null) return null;

        if (baseline.Results.Any(r => r.Outcome == RunOutcome.LaunchFailed))
        {
            Log?.Invoke("The program could not be started again to compare its output, so its logic was not checked.");
            return null;
        }

        var wrong = baseline.Results.Select((r, i) => (Result: r, Index: i)).Where(x => x.Result.Mismatch is not null).ToList();
        if (wrong.Count == 0) return null;

        var (first, firstIndex) = wrong[0];
        var slowest = baseline.Results.Max(r => r.Duration);

        var perRun = TimeSpan.FromSeconds(Math.Clamp(slowest.TotalSeconds * 5 + 3, 5, runTimeout.TotalSeconds));

        Progress?.Invoke("Working out which lines the wrong runs went through...");

        var coverage = new List<(bool Passed, IReadOnlySet<int> Lines)>();

        if (LineCoverage.Supports(chosenFile) && baseline.Spec is { } spec)
        {
            for (var i = 0; i < expected.Runs.Count; i++)
            {
                var lines = await LineCoverage.CollectAsync(chosenFile, spec, expected.Runs[i].Input, expected.Arguments, perRun, cancellationToken);
                if (lines is null) { coverage.Clear(); break; }
                coverage.Add((baseline.Results[i].Mismatch is null, lines));
            }
        }

        var coverageUsed = coverage.Count == expected.Runs.Count && coverage.Count > 0;
        var suspicious = coverageUsed ? Suspiciousness.Rank(coverage) : [];

        if (coverageUsed)
        {
            Log?.Invoke("Most suspicious lines (Ochiai): " + string.Join(", ", suspicious.Take(8).Select(s => $"{s.Line} ({s.Ochiai:0.00})")));
        }
        else
        {
            notes.Add("Which lines each run went through could not be recorded for this language, so every line was treated as equally likely and the likeliest kinds of mistake were tried first.");
        }

        // Suspiciousness decides the order changes are tried in, never which lines are tried at all: where a run's lines
        // were recorded imperfectly, the line holding the mistake can be missing from the ranking, and a change that is
        // never tried can never be the answer. Unranked lines simply come last.
        var ranked = coverageUsed ? suspicious.Take(MostLines).ToDictionary(s => s.Line, s => s.Ochiai) : [];
        var scores = Enumerable.Range(1, source.Count).ToDictionary(line => line, line => ranked.GetValueOrDefault(line, -1.0));

        // Of lines equally suspicious, the one a wrong run crashed on is tried first: two lines that score the same cannot be
        // told apart by which runs went through them, but the crash says which of the two the program went wrong on.
        var crashedOn = wrong.Select(w => w.Result.CrashedOn).OfType<int>().ToHashSet();
        if (crashedOn.Count > 0) Log?.Invoke("A wrong run crashed on line(s) " + string.Join(", ", crashedOn.Order()));

        var candidates = CandidatesFor(source, scores, crashedOn, Log);

        if (candidates.Count > MostChanges)
        {
            notes.Add($"There were {candidates.Count} small changes to try; the {MostChanges} on the most suspicious lines were tried.");
            candidates = candidates.Take(MostChanges).ToList();
        }

        Log?.Invoke($"Trying {candidates.Count} change(s), {AtOnce} at a time");

        var workers = Enumerable.Range(0, Math.Max(1, AtOnce)).Select(_ => new Workspace(root, chosenFile)).ToList();
        var wrongFirst = wrong.Select(w => w.Index).ToList();
        var triedAgainForTime = 0;
        var tried = 0;

        // Changes are tried several at once, so on a busy computer a right one can be stopped for time - which showed nothing
        // about it. Each is tried again straight away, on its own and with three times as long to run: one near the top of
        // the ranking is likelier to be right than the changes after it, which, left until last, the time could run out before.
        // Not when the changes tried beside it finished quickly, though: the computer was not busy, and the change ran on.
        var longerPerRun = TimeSpan.FromTicks(Math.Min(perRun.Ticks * 3, Math.Max(perRun.Ticks, runTimeout.Ticks)));

        try
        {
            foreach (var workspace in workers)
            {
                if (workspace.Create()) continue;

                notes.Add($"No changes were tried: {root} holds more than a program's worth of files to copy, so FixFinder does not " +
                          "copy it to try changes in. Moving the program into a folder of its own lets it.");
                return Result(null, false);
            }

            for (var batch = 0; batch < candidates.Count; batch += workers.Count)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (OutOfTime(DateTime.UtcNow - started, Budget, tried))
                {
                    notes.Add($"Stopped after {tried} changes: the search took longer than {(tried < AlwaysTried ? MostTime : Budget).TotalSeconds:0} seconds.");
                    break;
                }

                var slice = candidates.Skip(batch).Take(workers.Count).ToList();
                Progress?.Invoke($"Trying changes {batch + 1}-{batch + slice.Count} of {candidates.Count}...");

                var timed = await Task.WhenAll(slice.Select((c, i) => TimedAsync(() => TryAsync(workers[i], source, c.Fix, expected, perRun, runTimeout, wrongFirst, cancellationToken))));
                var verdicts = timed.Select(each => each.Verdict).ToArray();
                tried += slice.Count;

                for (var i = 0; i < slice.Count; i++)
                {
                    if (verdicts[i] != ChangeVerdict.PrintsWhatWasExpected) continue;

                    Log?.Invoke($"Accepted: {slice[i].Fix.Title} - every run now prints what was expected");
                    return Result(slice[i].Fix, slice[i].FromPattern);
                }

                for (var i = 0; i < slice.Count && triedAgainForTime < MostTriedAgainForTime; i++)
                {
                    if (verdicts[i] != ChangeVerdict.RanOutOfTime || !WorthTryingAgainForTime(timed.Where((_, other) => other != i).Select(each => each.Took).ToList(), perRun))
                        continue;

                    triedAgainForTime++;
                    Progress?.Invoke($"Trying again, with longer to run: {slice[i].Fix.Title}...");
                    if (await TryAsync(workers[0], source, slice[i].Fix, expected, longerPerRun, runTimeout, wrongFirst, cancellationToken) != ChangeVerdict.PrintsWhatWasExpected)
                        continue;

                    Log?.Invoke($"Accepted: {slice[i].Fix.Title} - every run now prints what was expected, given longer to run");
                    notes.Add($"A run of this change was stopped for time when it was first tried beside others, so it was tried again on its own, " +
                              $"with {longerPerRun.TotalSeconds:0} seconds a run - and printed what you expected.");
                    return Result(slice[i].Fix, slice[i].FromPattern);
                }
            }
        }
        finally
        {
            foreach (var workspace in workers) workspace.Dispose();
        }

        return Result(null, false);

        LogicRepairResult Result(LocalFix? fix, bool fromPattern) => new(
            first.Mismatch!, firstIndex + 1, suspicious, fix, fromPattern, tried,
            baseline.Results.Count(r => r.Mismatch is null), expected.Runs.Count, coverageUsed, notes);
    }

    /// <summary>
    /// Every change to try, in the order to try it: the fixes of mistakes recognised in the code first; then the small edits
    /// on each line - the most suspicious line first, of lines as suspicious as each other the one a run crashed on, then
    /// the likeliest kind of edit; then if / elif chains put in another order; then one edit made on several lines at once.
    /// </summary>
    /// <param name="scores">How suspicious each line of the file is - every line, -1 for one with nothing to say of it.</param>
    internal static List<(LocalFix Fix, bool FromPattern)> CandidatesFor(
        SourceFile source, IReadOnlyDictionary<int, double> scores, IReadOnlySet<int> crashedOn, Action<string>? log)
    {
        var candidates = new List<(LocalFix Fix, bool FromPattern)>();

        foreach (var finding in LogicPatterns.Scan(source, log))
        {
            if (finding is { Fix: { } fix, Kind: Checking.FindingKind.Logic } && finding.Severity != Checking.Severity.Suggestion) candidates.Add((fix, true));
        }

        var edits = scores.Keys
            .SelectMany(line => Mutations.For(source, line))
            .OrderByDescending(m => Math.Round(scores[m.Line], 6))
            .ThenByDescending(m => crashedOn.Contains(m.Line))
            .ThenBy(m => m.Cost)
            .ThenBy(m => m.Line)
            .ToList();

        foreach (var mutation in edits)
        {
            candidates.Add((mutation.Placement is { } placement
                ? new LocalFix
                {
                    RuleId = "logic-edit", Title = $"Add {mutation.To} {placement} line {mutation.Line}",
                    Explanation = $"Line {mutation.Line} looks like {mutation.Kind}.", File = source.Path,
                    StartLine = mutation.Line, RemoveCount = 1, NewLines = mutation.NewText.Split('\n'),
                }
                : LocalFix.ReplaceLine(
                    "logic-edit", mutation.To.Trim().Length == 0 ? $"Take out {mutation.From.Trim()} on line {mutation.Line}" : $"Change {mutation.From} to {mutation.To} on line {mutation.Line}",
                    $"Line {mutation.Line} looks like {mutation.Kind}.", source.Path, mutation.Line, mutation.NewText), false));
        }

        foreach (var reordered in BranchOrder.For(source, scores.Keys.ToHashSet()))
            candidates.Add((reordered, false));

        var repeated = edits
            .Where(m => m.Placement is null)
            .GroupBy(m => (m.From, m.To, m.Kind))
            .Select(g => g.GroupBy(m => m.Line).Select(l => l.First()).OrderBy(m => m.Line).ToList())
            .Where(g => g.Count is >= 2 and <= 4 && g[^1].Line - g[0].Line <= 12)
            .OrderBy(g => g.Min(m => m.Cost))
            .ThenBy(g => g[0].Line);

        foreach (var group in repeated)
        {
            var changed = group.ToDictionary(m => m.Line, m => m.NewText);
            var (from, to) = (group[0].Line, group[^1].Line);
            var lines = string.Join(", ", group.Select(m => m.Line));

            candidates.Add((new LocalFix
            {
                RuleId = "logic-edit",
                Title = $"Change {group[0].From} to {group[0].To} on lines {lines}",
                Explanation = $"Lines {lines} each look like {group[0].Kind} - the same mistake, made more than once.",
                File = source.Path, StartLine = from, RemoveCount = to - from + 1,
                NewLines = Enumerable.Range(from, to - from + 1).Select(l => changed.TryGetValue(l, out var text) ? text : source.Lines[l - 1]).ToList(),
            }, false));
        }

        return candidates;
    }

    /// <summary>One run's verdict: <paramref name="CrashedOn"/> is the line of the program's own file it crashed on, if it crashed.</summary>
    private sealed record RunVerdict(OutputMismatch? Mismatch, TimeSpan Duration, RunOutcome Outcome, int? CrashedOn);

    /// <summary>
    /// The innermost line of <paramref name="file"/> in a crash's stack trace - the frame nearest the crash that is in the
    /// program's own file, not a library's.
    /// </summary>
    private static int? CrashLineIn(string file, TargetRunResult result)
    {
        if (result.Error?.RootCause is not { } crash) return null;

        var full = Path.GetFullPath(file);
        return crash.Frames
            .Where(frame => frame is { File: { } path, Line: not null } &&
                            (Path.IsPathRooted(path)
                                ? string.Equals(Path.GetFullPath(path), full, StringComparison.OrdinalIgnoreCase)
                                : string.Equals(Path.GetFileName(path), Path.GetFileName(file), StringComparison.OrdinalIgnoreCase)))
            .Select(frame => frame.Line)
            .FirstOrDefault();
    }

    private sealed record Evaluation(TargetSpec? Spec, IReadOnlyList<RunVerdict> Results);

    /// <summary>
    /// Builds the program and runs it for each expected run. The build is given the whole run time limit, as the program's
    /// own build is: how long the program took to run says nothing of how long javac or gcc take to build it, and on a busy
    /// machine a build held to the time of a run timed out, so a change that was right was taken for one that was not.
    /// </summary>
    private async Task<Evaluation?> EvaluateAsync(
        string file, ExpectedBehaviour expected, TimeSpan timeout, TimeSpan buildTimeout, bool stopAtFirstWrong, CancellationToken cancellationToken,
        IReadOnlyList<int>? order = null)
    {
        var plan = TargetFactory.FromFile(file, timeout);
        if (!plan.Ok || plan.Spec is null) return null;

        var runner = new TargetRunner();

        if (plan.Compile is { } compile)
        {
            var built = await runner.RunAsync(compile.WithTimeout(buildTimeout), cancellationToken);
            if (built.ExitCode != 0 || built.Outcome is RunOutcome.LaunchFailed or RunOutcome.TimedOut) return new Evaluation(plan.Spec, []);
        }

        var results = new RunVerdict?[expected.Runs.Count];
        var sequence = (order ?? []).Concat(Enumerable.Range(0, expected.Runs.Count)).Distinct().ToList();

        foreach (var i in sequence)
        {
            var run = expected.Runs[i];
            var spec = plan.Spec.WithArguments(expected.Arguments).WithInput(run.Input).WithTimeout(timeout);
            var result = await runner.RunAsync(spec, cancellationToken);
            for (var attempt = 0; attempt < 2 && result.Outcome == RunOutcome.LaunchFailed; attempt++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
                result = await runner.RunAsync(spec, cancellationToken);
            }

            var mismatch = OutputComparison.Compare(OutputComparison.Printed(result), run.ExpectedOutput, OutputComparison.EndingOf(result.Outcome));

            results[i] = new RunVerdict(mismatch, result.Duration, result.Outcome, mismatch is null ? null : CrashLineIn(file, result));
            if (mismatch is not null && stopAtFirstWrong) return new Evaluation(plan.Spec, results.Where(r => r is not null).ToList()!);
        }

        return new Evaluation(plan.Spec, results!);
    }

    private async Task<ChangeVerdict> TryAsync(
        Workspace workspace, SourceFile source, LocalFix fix, ExpectedBehaviour expected, TimeSpan perRun, TimeSpan buildTimeout,
        IReadOnlyList<int> wrongFirst, CancellationToken cancellationToken)
    {
        if (fix.ApplyTo(source) is not { } lines) return ChangeVerdict.PrintsSomethingElse;

        try
        {
            await File.WriteAllBytesAsync(workspace.File, source.Render(lines), cancellationToken);

            var evaluation = await EvaluateAsync(workspace.File, expected, perRun, buildTimeout, stopAtFirstWrong: true, cancellationToken, wrongFirst);

            if (evaluation is { Results.Count: > 0 } && evaluation.Results.Count == expected.Runs.Count && evaluation.Results.All(r => r.Mismatch is null))
                return ChangeVerdict.PrintsWhatWasExpected;

            return evaluation?.Results.Any(r => r.Outcome == RunOutcome.TimedOut) == true ? ChangeVerdict.RanOutOfTime : ChangeVerdict.PrintsSomethingElse;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ChangeVerdict.PrintsSomethingElse;
        }
    }

    /// <summary>A private copy of the program, where one change at a time is made, built and run.</summary>
    /// <summary>
    /// Whether a search has run out of time: past its budget once it has tried the changes it always tries, or past five
    /// minutes however few it has tried.
    /// </summary>
    internal static bool OutOfTime(TimeSpan taken, TimeSpan budget, int tried) =>
        taken > MostTime || taken > budget && tried >= AlwaysTried;

    /// <summary>What trying a change found, and how long trying it took.</summary>
    internal static async Task<(T Verdict, TimeSpan Took)> TimedAsync<T>(Func<Task<T>> trying)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var verdict = await trying();
        return (verdict, clock.Elapsed);
    }

    /// <summary>
    /// Whether a change whose run was stopped for time is worth trying again, on its own and with longer: when the changes
    /// tried beside it were slow too, the computer was busy and the stop showed nothing about it; when they finished in a
    /// fifth of the time each run was given, the computer had time to spare, and the change itself ran on - as one that
    /// makes a loop endless does.
    /// </summary>
    internal static bool WorthTryingAgainForTime(IReadOnlyList<TimeSpan> triedBeside, TimeSpan perRun) =>
        triedBeside.Count == 0 || triedBeside.All(took => took >= perRun / 5);

    internal sealed class Workspace(string root, string chosen) : IDisposable
    {
        public string Folder { get; } = Path.Combine(Path.GetTempPath(), "FixFinder-logic", Guid.NewGuid().ToString("N")[..12]);

        public string File => ProgramCopy.InCopy(root, chosen, Folder);

        public bool Create() => ProgramCopy.TryCopyWhole(root, Folder);

        public void Dispose()
        {
            ProgramCopy.Forget(Folder);

            try
            {
                var build = CompiledLanguages.Handles(Path.GetExtension(File)) ? CompiledLanguages.OutputDirectory(File) : null;
                if (build is not null && Directory.Exists(build)) Directory.Delete(build, recursive: true);
                if (Directory.Exists(Folder)) Directory.Delete(Folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    public static string Describe(LogicRepairResult result)
    {
        var text = new StringBuilder();

        text.Append(result.Runs == 1
            ? "With this change the program prints exactly the output you gave."
            : $"With this change the program prints exactly the output you gave for all {result.Runs} runs - {result.Runs - result.Passed} of which were wrong before.");

        text.Append(result.FromPattern
            ? " The change corrects a mistake FixFinder recognised in the code, and running it confirmed it was the one."
            : $" It is the first of {result.Tried} small changes tried that does - changes to the lines the wrong runs went through, most suspicious first. " +
              "Another change could also produce that output, so check it is the one you meant; giving more runs narrows it down.");

        return text.ToString();
    }
}
