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

    public TimeSpan Budget { get; init; } = TimeSpan.FromSeconds(120);

    public int MostChanges { get; init; } = 400;

    public int MostLines { get; init; } = 40;

    public int AtOnce { get; init; } = LocalFixEngine.ChecksAtOnce;

    public async Task<LogicRepairResult?> RunAsync(
        string chosenFile, ExpectedBehaviour expected, TimeSpan runTimeout, CancellationToken cancellationToken)
    {
        if (expected.IsEmpty || SourceFile.Read(chosenFile) is not { } source) return null;

        var started = DateTime.UtcNow;
        var notes = new List<string>();

        Progress?.Invoke("Checking every run against the output you expected...");

        var root = ProgramRoot(chosenFile);
        var baseline = await EvaluateAsync(chosenFile, expected, runTimeout, stopAtFirstWrong: false, cancellationToken);
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

        var candidates = new List<(LocalFix Fix, bool FromPattern)>();

        foreach (var finding in LogicPatterns.Scan(source, Log))
        {
            if (finding is { Fix: { } fix, Kind: Checking.FindingKind.Logic } && finding.Severity != Checking.Severity.Suggestion) candidates.Add((fix, true));
        }

        // Suspiciousness decides the order changes are tried in, never which lines are tried at all: where a run's lines
        // were recorded imperfectly, the line holding the mistake can be missing from the ranking, and a change that is
        // never tried can never be the answer. Unranked lines simply come last.
        var ranked = coverageUsed ? suspicious.Take(MostLines).ToDictionary(s => s.Line, s => s.Ochiai) : [];
        var scores = Enumerable.Range(1, source.Count).ToDictionary(line => line, line => ranked.GetValueOrDefault(line, -1.0));

        var edits = scores.Keys
            .SelectMany(line => Mutations.For(source, line))
            .OrderByDescending(m => Math.Round(scores[m.Line], 6))
            .ThenBy(m => m.Cost)
            .ThenBy(m => m.Line)
            .ToList();

        foreach (var mutation in edits)
        {
            candidates.Add((LocalFix.ReplaceLine(
                "logic-edit", $"Change {mutation.From} to {mutation.To} on line {mutation.Line}",
                $"Line {mutation.Line} looks like {mutation.Kind}.", source.Path, mutation.Line, mutation.NewText), false));
        }

        foreach (var reordered in BranchOrder.For(source, scores.Keys.ToHashSet()))
            candidates.Add((reordered, false));

        var repeated = edits
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

        if (candidates.Count > MostChanges)
        {
            notes.Add($"There were {candidates.Count} small changes to try; the {MostChanges} on the most suspicious lines were tried.");
            candidates = candidates.Take(MostChanges).ToList();
        }

        Log?.Invoke($"Trying {candidates.Count} change(s), {AtOnce} at a time");

        var workers = Enumerable.Range(0, Math.Max(1, AtOnce)).Select(_ => new Workspace(root, chosenFile)).ToList();
        var tried = 0;

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

                if (DateTime.UtcNow - started > Budget)
                {
                    notes.Add($"Stopped after {tried} changes: the search took longer than {Budget.TotalSeconds:0} seconds.");
                    break;
                }

                var slice = candidates.Skip(batch).Take(workers.Count).ToList();
                Progress?.Invoke($"Trying changes {batch + 1}-{batch + slice.Count} of {candidates.Count}...");

                var verdicts = await Task.WhenAll(slice.Select((c, i) => TryAsync(workers[i], source, c.Fix, expected, perRun, wrong.Select(w => w.Index).ToList(), cancellationToken)));
                tried += slice.Count;

                for (var i = 0; i < slice.Count; i++)
                {
                    if (!verdicts[i]) continue;

                    Log?.Invoke($"Accepted: {slice[i].Fix.Title} - every run now prints what was expected");
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

    private sealed record RunVerdict(OutputMismatch? Mismatch, TimeSpan Duration, RunOutcome Outcome);

    private sealed record Evaluation(TargetSpec? Spec, IReadOnlyList<RunVerdict> Results);

    private async Task<Evaluation?> EvaluateAsync(
        string file, ExpectedBehaviour expected, TimeSpan timeout, bool stopAtFirstWrong, CancellationToken cancellationToken,
        IReadOnlyList<int>? order = null)
    {
        var plan = TargetFactory.FromFile(file, timeout);
        if (!plan.Ok || plan.Spec is null) return null;

        var runner = new TargetRunner();

        if (plan.Compile is { } compile)
        {
            var built = await runner.RunAsync(compile, cancellationToken);
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

            var mismatch = result.Outcome is RunOutcome.ExitedClean or RunOutcome.ExitedNonZero
                ? OutputComparison.Compare(OutputComparison.Printed(result), run.ExpectedOutput)
                : new OutputMismatch(1, null, $"({result.Explanation})", 0, 0);

            results[i] = new RunVerdict(mismatch, result.Duration, result.Outcome);
            if (mismatch is not null && stopAtFirstWrong) return new Evaluation(plan.Spec, results.Where(r => r is not null).ToList()!);
        }

        return new Evaluation(plan.Spec, results!);
    }

    private async Task<bool> TryAsync(
        Workspace workspace, SourceFile source, LocalFix fix, ExpectedBehaviour expected, TimeSpan perRun, IReadOnlyList<int> wrongFirst,
        CancellationToken cancellationToken)
    {
        if (fix.ApplyTo(source) is not { } lines) return false;

        try
        {
            await File.WriteAllBytesAsync(workspace.File, source.Render(lines), cancellationToken);

            var evaluation = await EvaluateAsync(workspace.File, expected, perRun, stopAtFirstWrong: true, cancellationToken, wrongFirst);

            return evaluation is { Results.Count: > 0 } && evaluation.Results.Count == expected.Runs.Count && evaluation.Results.All(r => r.Mismatch is null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string ProgramRoot(string file)
    {
        var extension = Path.GetExtension(file).ToLowerInvariant();

        return extension switch
        {
            ".java" => ProgramLayout.JavaSourceRoot(file),
            ".cs" when ProgramLayout.CSharpProject(file) is { } project => Path.GetDirectoryName(project)!,
            ".go" when ProgramLayout.GoPackageOf(file).Module is { } module => module,
            ".py" when ProgramLayout.PythonModule(file) is { } module => module.Folder,
            _ => Path.GetDirectoryName(file)!,
        };
    }

    /// <summary>A private copy of the program, where one change at a time is made, built and run.</summary>
    private sealed class Workspace(string root, string chosen) : IDisposable
    {
        private static readonly HashSet<string> NotCopied = new(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "obj", ".git", ".vs", ".idea", "node_modules", "__pycache__", ".venv", "venv", "target", "build", "out",
        };

        private const int MostFiles = 300;
        private const long MostBytes = 50 * 1024 * 1024;

        public string Folder { get; } = Path.Combine(Path.GetTempPath(), "FixFinder-logic", Guid.NewGuid().ToString("N")[..12]);

        public string File => Path.Combine(Folder, Path.GetRelativePath(root, chosen));

        public bool Create()
        {
            var files = new List<string>();
            var pending = new Stack<string>([root]);
            long bytes = 0;

            while (pending.Count > 0)
            {
                var directory = pending.Pop();

                foreach (var sub in Directory.EnumerateDirectories(directory))
                    if (!NotCopied.Contains(Path.GetFileName(sub))) pending.Push(sub);

                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    files.Add(file);
                    bytes += new FileInfo(file).Length;
                    if (files.Count > MostFiles || bytes > MostBytes) return false;
                }
            }

            foreach (var file in files)
            {
                var target = Path.Combine(Folder, Path.GetRelativePath(root, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                System.IO.File.Copy(file, target, overwrite: true);
            }

            return true;
        }

        public void Dispose()
        {
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
