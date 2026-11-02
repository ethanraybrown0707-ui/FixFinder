using System.Text.RegularExpressions;
using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>What looking for the change that lets a program's own failing assert hold found.</summary>
/// <param name="Fix">The change after which the program runs to the end, its assert holding, when one was found.</param>
/// <param name="Tried">How many changes were tried.</param>
/// <param name="AssertLine">The line of the assert the program stopped at.</param>
/// <param name="Compiled">Whether the program had to be built to run it, as Java's does.</param>
public sealed record AssertRepairResult(LocalFix? Fix, int Tried, int AssertLine, bool Compiled, IReadOnlyList<string> Notes);

/// <summary>
/// Finds the change that lets a program run past the assert it stopped at - an assert the program wrote to check itself,
/// which is a test of the code it checks: Java's assert, run with -ea, a Python assert in a program that also does other
/// work, node:assert's checks, C's assert. Small changes are tried in a private copy of the program, run as it was run,
/// each judged by whether the program then runs to the end without stopping with an error; a change to an assert, or to a
/// comment, is never tried, as it would change the check rather than what it checks.
/// </summary>
/// <remarks>
/// The functions the assert calls are where its answer was worked out, so their lines are tried first. With nothing more
/// than the assert to go by, a change can let it hold for the wrong reason, so the finding says to check the change is
/// the one meant - and that more asserts narrow it down.
/// </remarks>
public sealed partial class AssertRepair
{
    public event Action<string>? Log;
    public event Action<string>? Progress;

    /// <summary>How long the search goes on for once the changes it always tries have been - see <see cref="LogicRepair.OutOfTime"/>.</summary>
    public TimeSpan Budget { get; init; } = TimeSpan.FromSeconds(120);

    public int MostChanges { get; init; } = 400;

    public int AtOnce { get; init; } = LocalFixEngine.ChecksAtOnce;

    private const int MostTriedAgainForTime = 3;

    private enum AssertVerdict { RunsToTheEnd, StopsWithAnError, RanOutOfTime }

    /// <summary>A Python assert, or a C or C++ one.</summary>
    [GeneratedRegex(@"^\s*assert\b")]
    private static partial Regex PythonAssert();

    /// <summary>A Java assert statement: assert total >= 0; or assert total == 210 : "the total".</summary>
    [GeneratedRegex(@"(?:^|[;{}])\s*assert\b")]
    private static partial Regex JavaAssert();

    /// <summary>A check of node:assert's - assert(...), assert.strictEqual(...) - or C's assert(...).</summary>
    [GeneratedRegex(@"(?<![\w$.])assert(?:\s*\.\s*[A-Za-z]\w*)?\s*\(")]
    private static partial Regex CallToAssert();

    /// <summary>Whether a line - as code, outside its comments and text - begins or holds an assert of the file's language.</summary>
    public static bool IsAssertLine(string file, string maskedLine) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".py" or ".pyw" => PythonAssert().IsMatch(maskedLine),
        ".java" => JavaAssert().IsMatch(maskedLine),
        _ => CallToAssert().IsMatch(maskedLine),
    };

    private static Syntax SyntaxOf(string file) => Path.GetExtension(file).ToLowerInvariant() is ".py" or ".pyw" ? Syntax.Python : Syntax.CLike;

    /// <param name="spec">How the program was run: with what was typed in and its arguments, which each change is run with too.</param>
    /// <param name="ranFor">How long the run that stopped at the assert took.</param>
    public async Task<AssertRepairResult?> RunAsync(string chosenFile, TargetSpec spec, int assertLine, TimeSpan ranFor, CancellationToken cancellationToken)
    {
        if (SourceFile.Read(chosenFile) is not { } source || assertLine < 1 || assertLine > source.Count) return null;

        var masked = CodeText.MaskAll(source.Lines, SyntaxOf(chosenFile));
        if (!IsAssertLine(chosenFile, masked[assertLine - 1])) return null;

        var root = ProgramCopy.RootOf(chosenFile);
        var notes = new List<string>();
        var checks = LinesThatAreChecks(source, masked);

        var candidates = LogicRepair.CandidatesFor(source, Suspicion(source, masked, assertLine), new HashSet<int>(), Log)
            .Select(candidate => candidate.Fix)
            .Where(change => !ChangesACheck(source, change, checks))
            .ToList();

        if (candidates.Count > MostChanges)
        {
            notes.Add($"There were {candidates.Count} small changes to try; the {MostChanges} in the functions the assert on line {assertLine} calls first were tried.");
            candidates = candidates.Take(MostChanges).ToList();
        }

        if (candidates.Count == 0) return null;

        var name = Path.GetFileName(chosenFile);
        var perRun = TimeSpan.FromSeconds(Math.Clamp(ranFor.TotalSeconds * 3 + 5, 10, Math.Max(10, spec.Timeout.TotalSeconds)));
        var workers = Enumerable.Range(0, Math.Max(1, AtOnce)).Select(_ => new LogicRepair.Workspace(root, chosenFile)).ToList();
        var started = DateTime.UtcNow;
        var tried = 0;
        var triedAgainForTime = 0;
        var compiled = false;

        Log?.Invoke($"Trying {candidates.Count} change(s) to {name} against the assert on line {assertLine}, {AtOnce} at a time");

        try
        {
            foreach (var workspace in workers)
            {
                if (workspace.Create()) continue;

                notes.Add($"No changes were tried: {root} holds more than a program's worth of files to copy, so FixFinder does not copy it " +
                          "to run the program with changes. Moving the program into a folder of its own lets it.");
                return new AssertRepairResult(null, 0, assertLine, false, notes);
            }

            for (var batch = 0; batch < candidates.Count; batch += workers.Count)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (LogicRepair.OutOfTime(DateTime.UtcNow - started, Budget, tried))
                {
                    notes.Add($"Stopped after {tried} changes to {name}: running the program with each took longer than " +
                              $"{(tried < LogicRepair.AlwaysTried ? LogicRepair.MostTime : Budget).TotalSeconds:0} seconds in all.");
                    break;
                }

                var slice = candidates.Skip(batch).Take(workers.Count).ToList();
                Progress?.Invoke($"Running the program with changes {tried + 1}-{tried + slice.Count}...");

                var timed = await Task.WhenAll(slice.Select((candidate, i) =>
                    LogicRepair.TimedAsync(() => TryAsync(workers[i], source, root, candidate, spec, perRun, cancellationToken))));
                tried += slice.Count;

                for (var i = 0; i < slice.Count; i++)
                {
                    var (verdict, builtIt) = timed[i].Verdict;
                    compiled |= builtIt;

                    // Stopped for time beside other runs that were slow too shows nothing either way, so it is tried again at once.
                    if (verdict == AssertVerdict.RanOutOfTime && triedAgainForTime < MostTriedAgainForTime &&
                        LogicRepair.WorthTryingAgainForTime(timed.Where((_, other) => other != i).Select(each => each.Took).ToList(), perRun))
                    {
                        triedAgainForTime++;
                        (verdict, _) = await TryAsync(workers[0], source, root, slice[i], spec, perRun * 3, cancellationToken);
                    }

                    if (verdict != AssertVerdict.RunsToTheEnd) continue;

                    Log?.Invoke($"Accepted: {slice[i].Title} - the program now runs to the end");
                    return new AssertRepairResult(slice[i] with { Title = $"In {name}: {slice[i].Title}" }, tried, assertLine, compiled, notes);
                }
            }

            notes.Add($"FixFinder tried {tried} small change{(tried == 1 ? "" : "s")} to {name} - first in the code the assert on line {assertLine} calls - " +
                      "and none let the program run to the end, so the mistake probably needs more than a one-word change.");
            return new AssertRepairResult(null, tried, assertLine, compiled, notes);
        }
        finally
        {
            foreach (var workspace in workers) workspace.Dispose();
        }
    }

    /// <summary>
    /// Runs the program in a copy with one change, as it was run before - what was typed in, its arguments - building it
    /// first when it has to be built. Only a file inside the copy is ever written to.
    /// </summary>
    private static async Task<(AssertVerdict Verdict, bool Compiled)> TryAsync(
        LogicRepair.Workspace workspace, SourceFile source, string root, LocalFix fix, TargetSpec original, TimeSpan perRun, CancellationToken cancellationToken)
    {
        if (fix.ApplyTo(source) is not { } lines) return (AssertVerdict.StopsWithAnError, false);

        try
        {
            var copy = Path.GetFullPath(ProgramCopy.InCopy(root, source.Path, workspace.Folder));
            if (!copy.StartsWith(Path.GetFullPath(workspace.Folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return (AssertVerdict.StopsWithAnError, false);
            await File.WriteAllBytesAsync(copy, source.Render(lines), cancellationToken);

            var plan = TargetFactory.FromFile(copy, perRun);
            if (!plan.Ok || plan.Spec is null) return (AssertVerdict.StopsWithAnError, false);

            var runner = new TargetRunner();
            var compiled = plan.Compile is not null;

            if (plan.Compile is { } compile)
            {
                var built = await runner.RunAsync(compile.WithTimeout(perRun * 3), cancellationToken);
                if (built.Outcome == RunOutcome.TimedOut) return (AssertVerdict.RanOutOfTime, true);
                if (built.ExitCode != 0 || built.Outcome == RunOutcome.LaunchFailed) return (AssertVerdict.StopsWithAnError, true);
            }

            var run = await runner.RunAsync(plan.Spec.WithArguments(original.ProgramArguments).WithInput(original.StandardInput).WithTimeout(perRun), cancellationToken);

            return run.Outcome switch
            {
                RunOutcome.TimedOut => (AssertVerdict.RanOutOfTime, compiled),
                RunOutcome.ExitedClean when run.Error is null => (AssertVerdict.RunsToTheEnd, compiled),
                _ => (AssertVerdict.StopsWithAnError, compiled),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (AssertVerdict.StopsWithAnError, false);
        }
    }

    /// <summary>
    /// How likely each line is to hold the mistake: a line of a function the assert calls is where its answer was worked out;
    /// any other line could still hold it, less likely.
    /// </summary>
    private static Dictionary<int, double> Suspicion(SourceFile source, IReadOnlyList<string> masked, int assertLine)
    {
        var called = new HashSet<string>(StringComparer.Ordinal);
        var openBrackets = 0;

        for (var index = assertLine - 1; index < source.Count; index++)
        {
            foreach (Match call in TestRepair.CallOf().Matches(masked[index]))
                if (!TestRepair.NotNames.Contains(call.Groups["name"].Value)) called.Add(call.Groups["name"].Value);

            openBrackets = Math.Max(0, openBrackets + masked[index].Count(c => c is '(' or '[' or '{') - masked[index].Count(c => c is ')' or ']' or '}'));
            if (openBrackets == 0) break;
        }

        var functionOf = TestRepair.FunctionOfEachLine(source);
        return Enumerable.Range(1, source.Count).ToDictionary(line => line, line => functionOf[line - 1] is { } function && called.Contains(function) ? 1.0 : 0.1);
    }

    /// <summary>The lines that are the program's checks, or only text and comments: every line of every assert, and every line that masks to nothing.</summary>
    private static HashSet<int> LinesThatAreChecks(SourceFile source, IReadOnlyList<string> masked)
    {
        var checks = Enumerable.Range(1, source.Count).Where(line => source.Lines[line - 1].Trim().Length > 0 && masked[line - 1].Trim().Length == 0).ToHashSet();
        var openBrackets = 0;
        var inCheck = false;

        for (var index = 0; index < source.Count; index++)
        {
            if (openBrackets == 0) inCheck = IsAssertLine(source.Path, masked[index]);
            openBrackets = Math.Max(0, openBrackets + masked[index].Count(c => c is '(' or '[' or '{') - masked[index].Count(c => c is ')' or ']' or '}'));
            if (inCheck) checks.Add(index + 1);
        }

        return checks;
    }

    /// <summary>Whether a change alters a line of the program's checks - one it replaces with something else, takes out, or puts others among.</summary>
    private static bool ChangesACheck(SourceFile source, LocalFix change, IReadOnlySet<int> checks)
    {
        var replaced = Enumerable.Range(change.StartLine, Math.Max(0, change.RemoveCount)).ToList();
        if (change.NewLines.Count != change.RemoveCount) return replaced.Any(checks.Contains);

        return replaced.Where((line, offset) => source.Lines[line - 1] != change.NewLines[offset]).Any(checks.Contains);
    }

    /// <summary>What a change found by the program's own assert is said to have been checked by.</summary>
    public static string Describe(AssertRepairResult result, string chosenFile) =>
        $"With this change the program runs to the end, the assert on line {result.AssertLine} holding - it stopped there before. " +
        (result.Tried == 1 ? "It is the one small change tried" : $"It is the first of {result.Tried} small changes tried") +
        $" in {Path.GetFileName(chosenFile)}, first in the code the assert calls, that does. The assert is the only check of it, so another " +
        "change could also let it hold: check it is the one you meant; more asserts narrow it down.";
}
