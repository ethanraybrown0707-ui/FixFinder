using System.Text.RegularExpressions;
using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>What looking for the change that makes a file's failing tests pass found.</summary>
/// <param name="CodeUnderTest">The files of the program the failing tests call, which is where changes were tried.</param>
/// <param name="Fix">The change that makes every test pass, when one was found - one or more lines of one file.</param>
/// <param name="Steps">How many one-line changes the fix is made of: more than one when the code had more than one mistake.</param>
/// <param name="Tried">How many changes were tried.</param>
/// <param name="Tests">How many of the file's tests ran.</param>
/// <param name="FailedBefore">How many of those failed before any change.</param>
/// <param name="UnfinishedBefore">How many never finished before any change, the run of them stopped for time.</param>
/// <param name="Compiled">Whether the tests had to be compiled to run them, as Java's do.</param>
public sealed record TestRepairResult(
    IReadOnlyList<string> CodeUnderTest, LocalFix? Fix, int Steps, int Tried, int Tests, int FailedBefore, int UnfinishedBefore, bool Compiled,
    IReadOnlyList<string> Notes);

/// <summary>
/// Finds the change to the code a file of tests calls that makes every one of its tests pass - by trying small changes
/// there and running the tests with each, not by guessing. The tests are the judge, as the expected output is for
/// <see cref="LogicRepair"/>: a change counts only when every test that ran before runs again and passes, so one that
/// passes the failing tests by breaking others, or by stopping the tests being found at all, does not.
/// </summary>
/// <remarks>
/// <para>
/// Which lines are tried first comes from the tests themselves: a function the failing tests call, and the passing ones
/// do not, is the likeliest place for the mistake - the same Ochiai formula <see cref="LogicRepair"/> gives the lines the
/// wrong runs went through, given the functions each test calls instead. The lines a failure was raised through come
/// first among lines as likely as each other.
/// </para>
/// <para>
/// Code with two mistakes has no one change that makes every test pass, so a fix is built a step at a time: a one-line
/// change that makes some of the failing tests pass, and no passing test fail, is built on, and the search goes on for
/// the rest from there - up to <see cref="MostSteps"/> changes, all in one file - giving it up for the next such change
/// when it leads nowhere. Only the whole fix is ever reported, and only once every test passes with it.
/// </para>
/// </remarks>
public sealed partial class TestRepair
{
    public event Action<string>? Log;
    public event Action<string>? Progress;

    /// <summary>How long the search goes on for once the changes it always tries have been - see <see cref="LogicRepair.OutOfTime"/>.</summary>
    public TimeSpan Budget { get; init; } = TimeSpan.FromSeconds(120);

    public int MostChanges { get; init; } = 400;

    public int AtOnce { get; init; } = LocalFixEngine.ChecksAtOnce;

    /// <summary>How many files of the program are changed: the ones the failures went through most, or the tests import.</summary>
    private const int MostFilesChanged = 2;

    /// <summary>How many one-line changes a fix built a step at a time can be made of.</summary>
    public const int MostSteps = 3;

    /// <summary>How many of the changes that made more tests pass at one step are each built on, in turn, before giving up there.</summary>
    private const int MostBuiltOnEachStep = 3;

    /// <summary>How many changes whose tests were stopped for time are tried again, on their own and with longer to run.</summary>
    private const int MostTriedAgainForTime = 3;

    private enum TestVerdict { EveryTestPasses, MoreTestsPass, NotBetter, RanOutOfTime }

    /// <summary>What running the tests with a change showed: what each test did, when they ran.</summary>
    private sealed record TestRun(TestVerdict Verdict, bool Compiled, IReadOnlyList<TestReport.TestResult> Results, int Fixed);

    /// <param name="testsFound">How many tests the framework said it found, when it said - more than finished when the run was stopped.</param>
    /// <param name="runStoppedForTime">Whether the run of the tests was stopped for time, a test never finishing.</param>
    public async Task<TestRepairResult?> RunAsync(
        string testFile, IReadOnlyList<TestReport.TestResult> before, int? testsFound, bool runStoppedForTime, TimeSpan ranFor, TimeSpan runTimeout,
        CancellationToken cancellationToken)
    {
        var failing = before.Where(test => test.Status == "FAILED").ToList();
        var ran = before.Where(test => test.Status is "SUCCESSFUL" or "FAILED").Select(IdentityOf).ToHashSet(StringComparer.Ordinal);

        // Every test found has to pass: one that never finished, its run stopped for time, has not.
        var allTests = Math.Max(testsFound ?? 0, ran.Count);
        var unfinished = runStoppedForTime ? allTests - before.Count(test => test.Status is "SUCCESSFUL" or "FAILED" or "SKIPPED" or "ABORTED") : 0;

        // A failure setting up for the tests, or cleaning up after them, is not one a change to the code they call would show.
        if (failing.Count == 0 && unfinished <= 0 || before.Any(test => test.Status is "SETUP-FAILED" or "TEARDOWN-FAILED")) return null;
        if (SourceFile.Read(testFile) is not { } tests) return null;
        var root = ProgramCopy.RootOf(testFile);
        var framework = PythonTests.FrameworkOf(testFile);
        var consoleAsserts = NodeTests.ChecksWithConsoleAssert(testFile);
        var testNoun = consoleAsserts ? "check" : NounFor(framework);
        var targets = CodeUnderTest(tests, failing, root, TestsAreInTheCode(framework) || consoleAsserts);

        if (targets.Count == 0)
        {
            Log?.Invoke("No file of the program that the failing tests call was found to try changes in.");
            return null;
        }

        var names = string.Join(" and ", targets.Select(target => Path.GetFileName(target.Path)));
        var notes = new List<string>();
        var started = DateTime.UtcNow;

        // A test run is given several times as long as the one the check made, and never less than ten seconds: starting
        // Python or the JVM again is most of a short run's time, and on a busy computer that alone varies a lot. A run the
        // check stopped for time says nothing of how long the tests take, so each is given the ten seconds - and a change
        // that does not finish in them is not tried again with longer, as never finishing is what the tests did before.
        var perRun = runStoppedForTime ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(Math.Clamp(ranFor.TotalSeconds * 3 + 5, 10, Math.Max(10, runTimeout.TotalSeconds)));

        var workers = Enumerable.Range(0, Math.Max(1, AtOnce)).Select(_ => new LogicRepair.Workspace(root, testFile)).ToList();
        var tried = 0;
        var stepsTriedOn = 0;
        var stoppedForTime = 0;
        var triedAgainForTime = 0;
        var compiled = false;
        var outOfTime = false;

        try
        {
            foreach (var workspace in workers)
            {
                if (workspace.Create()) continue;

                notes.Add($"No changes were tried: {root} holds more than a program's worth of files to copy, so FixFinder does not copy it " +
                          "to run the tests with changes. Moving the program and its tests into a folder of their own lets it.");
                return Result(null, 0);
            }

            if (await SearchAsync([], before) is { } found) return Result(found.Fix, found.Steps);

            notes.Add($"FixFinder tried {Count(tried, "small change")} to {names}, the code the failing {testNoun}s call" +
                      (stepsTriedOn > 0 ? $" - building on {Count(stepsTriedOn, "change")} that made more of them pass and none fail, in turn -" : ",") +
                      $" and none made every {testNoun} pass - so the mistake probably needs more than a one-word change." +
                      (stoppedForTime > 0 ? $" The {testNoun}s were stopped for time with {Count(stoppedForTime, "change")}, which showed nothing either way." : ""));
            return Result(null, 0);
        }
        finally
        {
            foreach (var workspace in workers) workspace.Dispose();
        }

        // Every change on top of those kept, until one makes every test pass. Failing that, each change that made more tests
        // pass and none fail is built on in turn - the one that made most pass first - until a whole fix is found, the fix
        // would be more than MostSteps changes, or the time is up: a change can make a test pass for the wrong reason, as
        // adding 1 to every item pushed onto a stack made a test of what pop gives back pass, and lead nowhere.
        async Task<(LocalFix Fix, int Steps)?> SearchAsync(IReadOnlyList<LocalFix> kept, IReadOnlyList<TestReport.TestResult> latest)
        {
            var candidates = CandidatesNow(targets, tests, latest, kept, framework, consoleAsserts);

            if (candidates.Count > MostChanges)
            {
                if (kept.Count == 0) notes.Add($"There were {candidates.Count} small changes to try in {names}; the {MostChanges} in the functions the failing tests call most were tried.");
                candidates = candidates.Take(MostChanges).ToList();
            }

            Log?.Invoke(kept.Count == 0
                ? $"Trying {candidates.Count} change(s) to {names} against {Count(allTests, "test")}, {AtOnce} at a time"
                : $"Trying {candidates.Count} more change(s) to {Path.GetFileName(kept[0].File)} with {string.Join("; ", kept.Select(change => change.Title))}");

            var better = new List<(LocalFix Fix, TestRun Run)>();

            for (var batch = 0; batch < candidates.Count; batch += workers.Count)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (LogicRepair.OutOfTime(DateTime.UtcNow - started, Budget, tried))
                {
                    if (!outOfTime) notes.Add($"Stopped after {tried} changes to {names}: running the tests with each took longer than " +
                                              $"{(tried < LogicRepair.AlwaysTried ? LogicRepair.MostTime : Budget).TotalSeconds:0} seconds in all.");
                    outOfTime = true;
                    return null;
                }

                var slice = candidates.Skip(batch).Take(workers.Count).ToList();
                Progress?.Invoke($"Running the tests with changes {tried + 1}-{tried + slice.Count}...");

                var batchStarted = DateTime.UtcNow;
                var timed = await Task.WhenAll(slice.Select((candidate, i) =>
                    LogicRepair.TimedAsync(() => TryAsync(workers[i], root, targets, kept, candidate, ran, allTests, latest, perRun, runTimeout, cancellationToken))));
                var runs = timed.Select(each => each.Verdict).ToArray();
                tried += slice.Count;
                Log?.Invoke($"Ran the tests with {Count(slice.Count, "change")} in {(DateTime.UtcNow - batchStarted).TotalSeconds:0.0} s: " +
                            string.Join(", ", runs.Select(run => run.Verdict switch
                            {
                                TestVerdict.EveryTestPasses => "every test passes",
                                TestVerdict.MoreTestsPass => $"{Count(run.Fixed, "more")} pass",
                                TestVerdict.RanOutOfTime => "stopped for time",
                                _ => "no better",
                            })));

                for (var i = 0; i < slice.Count; i++)
                {
                    compiled |= runs[i].Compiled;

                    // Stopped for time beside other runs shows nothing either way, so the change is tried again at once, on its
                    // own and with three times as long - as the search against an expected output does - unless the runs beside
                    // it finished quickly, when the change itself ran on.
                    if (runs[i].Verdict == TestVerdict.RanOutOfTime && !runStoppedForTime && triedAgainForTime < MostTriedAgainForTime &&
                        LogicRepair.WorthTryingAgainForTime(timed.Where((_, other) => other != i).Select(each => each.Took).ToList(), perRun))
                    {
                        triedAgainForTime++;
                        runs[i] = await TryAsync(workers[0], root, targets, kept, slice[i], ran, allTests, latest, perRun * 3, runTimeout, cancellationToken);
                    }

                    if (runs[i].Verdict == TestVerdict.RanOutOfTime) stoppedForTime++;

                    if (runs[i].Verdict == TestVerdict.EveryTestPasses)
                    {
                        var whole = Together(targets, [.. kept, slice[i]]);
                        Log?.Invoke($"Accepted: {whole.Title} - every test now passes");
                        return (whole, kept.Count + 1);
                    }

                    if (runs[i].Verdict == TestVerdict.MoreTestsPass && IsOneLine(slice[i])) better.Add((slice[i], runs[i]));
                }
            }

            if (kept.Count + 1 >= MostSteps) return null;

            // OrderByDescending keeps the order the changes were tried in among those that made as many pass.
            foreach (var step in better.OrderByDescending(step => step.Run.Fixed).Take(MostBuiltOnEachStep))
            {
                stepsTriedOn++;
                Log?.Invoke($"Building on: {step.Fix.Title} - {Count(step.Run.Fixed, "more test")} pass{(step.Run.Fixed == 1 ? "es" : "")} and none fails that passed");

                if (await SearchAsync([.. kept, step.Fix], step.Run.Results) is { } whole) return whole;
                if (outOfTime) return null;
            }

            return null;
        }

        TestRepairResult Result(LocalFix? fix, int steps) =>
            new(targets.Select(target => target.Path).ToList(), fix, steps, tried, allTests,
                failing.Select(IdentityOf).Distinct(StringComparer.Ordinal).Count(), Math.Max(0, unfinished), compiled, notes);
    }

    /// <summary>
    /// The changes to try next. With none kept yet, every change to every file the tests call, most suspicious first; after
    /// one is kept, only one-line changes to other lines of its file, ranked again by the tests that still fail. A file of
    /// doctests is the code its examples call as well as its tests, and a change to a line of its docstrings - or of its
    /// comments - would change the tests, not the code, so none is tried.
    /// </summary>
    private List<LocalFix> CandidatesNow(
        IReadOnlyList<SourceFile> targets, SourceFile tests, IReadOnlyList<TestReport.TestResult> latest, IReadOnlyList<LocalFix> kept, PythonTests.Framework framework,
        bool consoleAsserts)
    {
        var candidates = new List<LocalFix>();
        var stillFailing = latest.Where(test => test.Status == "FAILED").ToList();

        foreach (var target in kept.Count == 0 ? targets : targets.Where(target => SamePath(target.Path, kept[0].File)))
        {
            // The innermost line of this file each failure was raised through: where it went wrong, not every call on the way -
            // a recursion that never stops goes through both of binary search's calls of itself, and only one is the mistake.
            var raisedThrough = stillFailing
                .Select(test => test.Frames.FirstOrDefault(frame => frame is { File: { } file, Line: > 0 } && SamePath(file, target.Path)))
                .Where(frame => frame.File is not null)
                .Select(frame => frame.Line)
                .ToHashSet();

            var changes = LogicRepair.CandidatesFor(target, Suspicion(target, tests, latest, framework, consoleAsserts), raisedThrough, Log).Select(candidate => candidate.Fix);
            if ((TestsAreInTheCode(framework) || consoleAsserts) && SamePath(target.Path, tests.Path))
            {
                var testLines = LinesThatAreTests(target);
                changes = changes.Where(change => !ChangesItsTests(target, change, testLines)).ToList();
            }
            candidates.AddRange(kept.Count == 0 ? changes : changes.Where(change => IsOneLine(change) && kept.All(done => done.StartLine != change.StartLine)));
        }

        return candidates;
    }

    /// <summary>Whether a change replaces one line with one line, so others can be made beside it with every line where it was.</summary>
    private static bool IsOneLine(LocalFix fix) => fix is { RemoveCount: 1, NewLines.Count: 1 };

    /// <summary>
    /// Changes made together as one fix: one change as it is, with its file named; several one-line changes to one file as
    /// one change from the first of their lines to the last, with every line between as it was.
    /// </summary>
    private static LocalFix Together(IReadOnlyList<SourceFile> targets, IReadOnlyList<LocalFix> changes)
    {
        var file = Path.GetFileName(changes[0].File);
        if (changes.Count == 1) return changes[0] with { Title = $"In {file}: {changes[0].Title}" };

        // Changes are only made together when each is one line, so the file's lines with all of them made are always there.
        var source = targets.First(target => SamePath(target.Path, changes[0].File));
        var lines = Combined(source, changes)!;
        var (first, last) = (changes.Min(change => change.StartLine), changes.Max(change => change.StartLine));

        return new LocalFix
        {
            RuleId = "logic-edit",
            Title = $"In {file}: {UpperFirst(string.Join("; ", changes.OrderBy(change => change.StartLine).Select(change => LowerFirst(change.Title))))}",
            Explanation = string.Join(" ", changes.OrderBy(change => change.StartLine).Select(change => change.Explanation)),
            File = changes[0].File,
            StartLine = first,
            RemoveCount = last - first + 1,
            NewLines = lines.Skip(first - 1).Take(last - first + 1).ToList(),
        };
    }

    private static string LowerFirst(string text) => text.Length > 0 && char.IsUpper(text[0]) ? char.ToLowerInvariant(text[0]) + text[1..] : text;

    private static string UpperFirst(string text) => text.Length > 0 && char.IsLower(text[0]) ? char.ToUpperInvariant(text[0]) + text[1..] : text;

    /// <summary>A file's lines with changes made: each kept one-line change, then the last change tried, which may be any change.</summary>
    private static IReadOnlyList<string>? Combined(SourceFile source, IReadOnlyList<LocalFix> changes)
    {
        var lines = source.Lines.ToList();

        foreach (var change in changes.Take(changes.Count - 1))
            lines[change.StartLine - 1] = change.NewLines[0];

        var last = changes[^1];
        if (changes.Count == 1) return last.ApplyTo(source);
        if (!IsOneLine(last)) return null;

        lines[last.StartLine - 1] = last.NewLines[0];
        return lines;
    }

    /// <summary>A test as the same test from one run to the next: its class, its method and its own name.</summary>
    private static string IdentityOf(TestReport.TestResult test) => $"{test.ClassName}.{test.Method}.{test.Name}";

    private static string Count(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";

    private static bool SamePath(string first, string second)
    {
        try
        {
            return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>
    /// Runs the tests in a copy with the changes kept so far and one more: the file they change is written with them, and
    /// every other file a change may be tried in is put back as it was, so each run has these changes and no others.
    /// </summary>
    /// <param name="latest">What each test did with the changes kept so far, which a change has to do better than.</param>
    private static async Task<TestRun> TryAsync(
        LogicRepair.Workspace workspace, string root, IReadOnlyList<SourceFile> targets, IReadOnlyList<LocalFix> kept, LocalFix fix,
        IReadOnlySet<string> ran, int allTests, IReadOnlyList<TestReport.TestResult> latest, TimeSpan perRun, TimeSpan buildTimeout, CancellationToken cancellationToken)
    {
        var notBetter = new TestRun(TestVerdict.NotBetter, false, [], 0);

        if (targets.FirstOrDefault(target => SamePath(target.Path, fix.File)) is not { } changed || Combined(changed, [.. kept, fix]) is not { } lines)
            return notBetter;

        try
        {
            foreach (var target in targets)
            {
                // Only ever a file inside the copy: the program's own files are never written to while changes are tried.
                var copy = Path.GetFullPath(ProgramCopy.InCopy(root, target.Path, workspace.Folder));
                if (!copy.StartsWith(Path.GetFullPath(workspace.Folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return notBetter;

                if (ReferenceEquals(target, changed)) await File.WriteAllBytesAsync(copy, changed.Render(lines), cancellationToken);
                else File.Copy(target.Path, copy, overwrite: true);
            }

            var plan = TargetFactory.FromFile(workspace.File, perRun);
            if (!plan.Ok || plan.Spec is null) return notBetter;

            var runner = new TargetRunner();
            var compiled = plan.Compile is not null;

            if (plan.Compile is { } compile)
            {
                var built = await runner.RunAsync(compile.WithTimeout(buildTimeout), cancellationToken);
                if (built.Outcome == RunOutcome.TimedOut) return notBetter with { Verdict = TestVerdict.RanOutOfTime, Compiled = true };
                if (built.ExitCode != 0 || built.Outcome == RunOutcome.LaunchFailed) return notBetter with { Compiled = true };
            }

            var run = await runner.RunAsync(plan.Spec.WithTimeout(perRun), cancellationToken);
            var after = TestReport.ResultsIn(run.Lines.Select(line => line.Text));

            var passing = after.Where(test => test.Status == "SUCCESSFUL").Select(IdentityOf).ToHashSet(StringComparer.Ordinal);
            var passedBefore = latest.Where(test => test.Status == "SUCCESSFUL").Select(IdentityOf).ToList();
            var failedBefore = latest.Where(test => test.Status == "FAILED").Select(IdentityOf).Distinct(StringComparer.Ordinal).ToList();
            var anyFailed = after.Any(test => test.Status is "FAILED" or "SETUP-FAILED" or "TEARDOWN-FAILED");

            if (!anyFailed && ran.All(passing.Contains) && passing.Count >= allTests)
            {
                // Every test passed, and the run was still stopped for time - on a busy computer, before it could end: that
                // shows nothing about whether it finishes, so it is a run to try again with longer, not a step to build on.
                return run.Outcome == RunOutcome.TimedOut
                    ? new TestRun(TestVerdict.RanOutOfTime, compiled, after, 0)
                    : new TestRun(TestVerdict.EveryTestPasses, compiled, after, allTests - passedBefore.Count);
            }

            // Better: none that passed fails, and more pass - ones that failed, or never finished before.
            var nowFixed = passing.Count - passedBefore.Count;
            if (passedBefore.All(passing.Contains) && nowFixed > 0 && !after.Any(test => test.Status is "SETUP-FAILED" or "TEARDOWN-FAILED"))
                return new TestRun(TestVerdict.MoreTestsPass, compiled, after, nowFixed);

            return new TestRun(run.Outcome == RunOutcome.TimedOut ? TestVerdict.RanOutOfTime : TestVerdict.NotBetter, compiled, after, 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return notBetter;
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // The code the tests call, and where in it the mistake most likely is.

    [GeneratedRegex(@"^\s*from\s+(?<module>[A-Za-z_][\w.]*)\s+import\b")]
    private static partial Regex PythonFromImport();

    [GeneratedRegex(@"^\s*import\s+(?<modules>[A-Za-z_][\w.]*(?:\s*,\s*[A-Za-z_][\w.]*)*)\s*$")]
    private static partial Regex PythonImport();

    [GeneratedRegex(@"(?<![\w$.])(?<type>[A-Z][A-Za-z0-9_$]*)\b")]
    private static partial Regex JavaTypeName();

    [GeneratedRegex(@"^\s*(?:async\s+)?def\s+(?<name>[A-Za-z_]\w*)\s*\(")]
    private static partial Regex PythonFunction();

    /// <summary>A Java method's head: what it gives back, its name and its parameters, ending the line or opening its body.</summary>
    [GeneratedRegex(@"^\s*(?:@\w+(?:\([^)]*\))?\s+)*(?:(?:public|private|protected|static|final|synchronized|abstract|native|default)\s+)*(?:<[^>]+>\s+)?(?<type>[\w.$]+(?:<[^;{}]*>)?(?:\s*\[\s*\])*)\s+(?<name>[A-Za-z_$][\w$]*)\s*\([^;{}]*\)\s*(?:throws\s+[\w.$,\s]+)?\{?\s*$")]
    private static partial Regex JavaMethod();

    [GeneratedRegex(@"(?<![\w$])(?<name>[A-Za-z_$][\w$]*)\s*\(")]
    internal static partial Regex CallOf();

    internal static readonly HashSet<string> NotNames = new(StringComparer.Ordinal)
    {
        "if", "for", "while", "switch", "catch", "return", "new", "else", "throw", "case", "do", "try", "synchronized",
    };

    private static bool IsPython(string file) => Path.GetExtension(file).ToLowerInvariant() is ".py" or ".pyw";

    private static bool IsJavaScript(string file) => Path.GetExtension(file).ToLowerInvariant() is ".js" or ".mjs" or ".cjs" or ".ts" or ".mts" or ".cts";

    /// <summary>Whether two files are of one language: the same extension, or both JavaScript or TypeScript, which import each other.</summary>
    private static bool SameLanguage(string first, string second) =>
        Path.GetExtension(first).Equals(Path.GetExtension(second), StringComparison.OrdinalIgnoreCase) || IsJavaScript(first) && IsJavaScript(second);

    /// <summary>A JavaScript or TypeScript function's head: function grade(, const grade = (mark) =>, or a method's, pop() {.</summary>
    [GeneratedRegex(@"^\s*(?:export\s+(?:default\s+)?)?(?:async\s+)?function\s*\*?\s*(?<name>[A-Za-z_$][\w$]*)\s*[(<]|^\s*(?:export\s+)?(?:const|let|var)\s+(?<name>[A-Za-z_$][\w$]*)\s*(?::[^=]+)?=\s*(?:async\s+)?(?:function\b|\([^)]*\)\s*(?::[^=]+)?=>|[A-Za-z_$][\w$]*\s*=>)|^\s*(?:(?:public|private|protected|static|async|get|set)\s+)*(?<name>[A-Za-z_$][\w$]*)\s*\([^)]*\)\s*(?::[^{]+)?\{\s*$")]
    private static partial Regex JavaScriptFunction();

    /// <summary>The head of a node:test test, or one in a describe block: test( or it( - the name it is given is text, which masking leaves out.</summary>
    [GeneratedRegex(@"\b(?:test|it)(?:\.(?:only|skip|todo))?\s*\(")]
    private static partial Regex NodeTestHead();

    /// <summary>
    /// The files of the program the failing tests call: the ones their failures were raised through, most first, or else
    /// the program's own files the test file imports - never a file of tests, and only files of the test file's language.
    /// </summary>
    private static List<SourceFile> CodeUnderTest(SourceFile tests, IReadOnlyList<TestReport.TestResult> failing, string root, bool testsAreInTheCode)
    {
        bool Usable(string file) =>
            File.Exists(file) && !SamePath(file, tests.Path) && SameLanguage(file, tests.Path) &&
            !Path.GetRelativePath(root, file).StartsWith("..", StringComparison.Ordinal) && !IsTestFile(file);

        var raisedThrough = failing.SelectMany(test => test.Frames)
            .Select(frame => frame.File)
            .OfType<string>()
            .Where(Usable)
            .GroupBy(file => Path.GetFullPath(file), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key);

        var chosen = raisedThrough.Take(MostFilesChanged).ToList();

        // A module beside the tests and one under the program's folder are often the same file, found both ways.
        if (chosen.Count == 0) chosen = Imported(tests, root).Where(Usable).Distinct(StringComparer.OrdinalIgnoreCase).Take(MostFilesChanged).ToList();

        // A file's doctests, and a program's own asserts, test the code beside them in it, so that is where changes are tried
        // first - then a module of the program's it imports, where a check that failed, rather than raised, leaves no trace.
        if (testsAreInTheCode) chosen = [Path.GetFullPath(tests.Path), .. chosen.Take(MostFilesChanged - 1)];

        return chosen.Select(SourceFile.Read).OfType<SourceFile>().ToList();
    }

    /// <summary>Whether a file is one of tests: test_x.py or x_test.py, one of node:test's tests, or a Java class with an @Test method.</summary>
    private static bool IsTestFile(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        if (IsPython(file)) return name.StartsWith("test_", StringComparison.OrdinalIgnoreCase) || name.EndsWith("_test", StringComparison.OrdinalIgnoreCase);
        if (IsJavaScript(file)) return NodeTests.AreItsTests(file);

        try
        {
            return File.ReadAllText(file).Contains("@Test", StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>
    /// The program's own files a test file imports: in Python, a module beside it, under the program's folder or in a
    /// folder above it that Python, or pytest's settings, tell it to look in - src beside the tests folder - by its import
    /// lines; in JavaScript and TypeScript, a file it brings in by a path of its own; in Java, a class it names whose .java
    /// file is beside it or under the program's folder.
    /// </summary>
    private static IEnumerable<string> Imported(SourceFile tests, string root)
    {
        if (IsJavaScript(tests.Path))
        {
            foreach (var imported in ProgramLayout.JavaScriptImports(tests.Path)) yield return imported;
            yield break;
        }

        var folder = Path.GetDirectoryName(tests.Path)!;
        var masked = CodeText.MaskAll(tests.Lines, IsPython(tests.Path) ? Syntax.Python : Syntax.CLike);

        if (IsPython(tests.Path))
        {
            var modules = new List<string>();
            foreach (var line in masked)
            {
                if (PythonFromImport().Match(line) is { Success: true } from) modules.Add(from.Groups["module"].Value);
                else if (PythonImport().Match(line) is { Success: true } plain) modules.AddRange(plain.Groups["modules"].Value.Split(',').Select(module => module.Trim()));
            }

            var lookedIn = new[] { folder, root }
                .Concat(ProgramLayout.PythonImportFoldersOfTests(tests.Path))
                .Concat(ProgramLayout.PytestPythonPath(tests.Path))
                .ToList();

            foreach (var module in modules.Distinct(StringComparer.Ordinal))
            {
                var relative = module.Replace('.', Path.DirectorySeparatorChar) + ".py";
                foreach (var under in lookedIn)
                    if (File.Exists(Path.Combine(under, relative))) yield return Path.GetFullPath(Path.Combine(under, relative));
            }

            yield break;
        }

        var named = masked.SelectMany(line => JavaTypeName().Matches(line).Select(match => match.Groups["type"].Value))
            .GroupBy(name => name, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key);

        foreach (var type in named)
        {
            var beside = Path.Combine(folder, type + ".java");
            if (File.Exists(beside))
            {
                yield return Path.GetFullPath(beside);
                continue;
            }

            var underRoot = Directory.EnumerateFiles(root, type + ".java", SearchOption.AllDirectories).FirstOrDefault();
            if (underRoot is not null) yield return Path.GetFullPath(underRoot);
        }
    }

    /// <summary>
    /// How suspicious each line of a file the tests call is: the Ochiai score of the function it is in, counting the
    /// failing tests that call that function and the passing ones that call it. Every line has a score: 0 for one in a
    /// function no failing test calls by name, and -1 everywhere when no test's own code could be found to read.
    /// </summary>
    private static Dictionary<int, double> Suspicion(
        SourceFile target, SourceFile tests, IReadOnlyList<TestReport.TestResult> before, PythonTests.Framework framework, bool consoleAsserts)
    {
        var functionOf = FunctionOfEachLine(target);
        var functions = functionOf.OfType<string>().ToHashSet(StringComparer.Ordinal);

        var called = before.Where(test => test.Status is "SUCCESSFUL" or "FAILED")
            .Select(test => (Failed: test.Status == "FAILED",
                Calls: consoleAsserts ? CallsInAssert(tests, test.Method) : framework switch
                {
                    PythonTests.Framework.Doctest => CallsInExamples(tests, test.Method),
                    PythonTests.Framework.Asserts => CallsInAssert(tests, test.Method),
                    _ => IsJavaScript(tests.Path) ? CallsInNamedTest(tests, test.Method) : CallsIn(tests, test.Method),
                }))
            .Where(test => test.Calls is not null)
            .Select(test => (test.Failed, Calls: test.Calls!.Where(functions.Contains).ToHashSet(StringComparer.Ordinal)))
            .ToList();

        var totalFailed = called.Count(test => test.Failed);
        var scores = functions.ToDictionary(
            function => function,
            function => Suspiciousness.Ochiai(called.Count(test => test.Failed && test.Calls.Contains(function)), called.Count(test => !test.Failed && test.Calls.Contains(function)), totalFailed),
            StringComparer.Ordinal);

        return Enumerable.Range(1, target.Count).ToDictionary(
            line => line,
            line => totalFailed == 0 ? -1.0 : functionOf[line - 1] is { } function ? scores[function] : 0.0);
    }

    /// <summary>
    /// The function or method each line of a file is in: the nearest head above it that is indented less - def in
    /// Python, a function's or a method's head in JavaScript, a method's in Java - or null outside every one, such as a
    /// class's fields or the module's own code.
    /// </summary>
    internal static string?[] FunctionOfEachLine(SourceFile source)
    {
        var python = IsPython(source.Path);
        var masked = CodeText.MaskAll(source.Lines, python ? Syntax.Python : Syntax.CLike);
        var functionOf = new string?[source.Count];
        var open = new Stack<(int Indent, string Name)>();

        for (var index = 0; index < source.Count; index++)
        {
            var line = masked[index];
            if (line.Trim().Length == 0)
            {
                functionOf[index] = open.Count > 0 ? open.Peek().Name : null;
                continue;
            }

            var indent = line.Length - line.TrimStart().Length;
            while (open.Count > 0 && open.Peek().Indent >= indent) open.Pop();

            if (HeadOf(line, source.Path) is { } name) open.Push((indent, name));
            functionOf[index] = open.Count > 0 ? open.Peek().Name : null;
        }

        return functionOf;
    }

    private static string? HeadOf(string maskedLine, string file)
    {
        if (IsPython(file)) return PythonFunction().Match(maskedLine) is { Success: true } def ? def.Groups["name"].Value : null;

        if (IsJavaScript(file))
        {
            return JavaScriptFunction().Match(maskedLine) is { Success: true } function && !NotNames.Contains(function.Groups["name"].Value)
                ? function.Groups["name"].Value
                : null;
        }

        return JavaMethod().Match(maskedLine) is { Success: true } method && !NotNames.Contains(method.Groups["name"].Value) && !NotNames.Contains(method.Groups["type"].Value)
            ? method.Groups["name"].Value
            : null;
    }

    /// <summary>An example of a docstring, or a line it goes on to: >>> grade(70), or ... and more of it.</summary>
    [GeneratedRegex(@"^\s*(?:>>>|\.\.\.)(?: (?<code>.*))?$")]
    private static partial Regex DoctestExampleLine();

    /// <summary>
    /// The names the examples in a function's docstring call - its doctests, found by the function's name in the file - or
    /// null when there is no function of that name: a class's docstring, or the module's own.
    /// </summary>
    private static HashSet<string>? CallsInExamples(SourceFile tests, string function)
    {
        var masked = CodeText.MaskAll(tests.Lines, Syntax.Python);

        for (var index = 0; index < masked.Count; index++)
        {
            if (HeadOf(masked[index], tests.Path) != function) continue;

            var indent = masked[index].Length - masked[index].TrimStart().Length;
            var calls = new HashSet<string>(StringComparer.Ordinal);

            for (var inside = index + 1; inside < masked.Count; inside++)
            {
                var line = tests.Lines[inside];
                if (line.Trim().Length == 0) continue;
                if (masked[inside].Trim().Length > 0 && masked[inside].Length - masked[inside].TrimStart().Length <= indent) break;

                if (DoctestExampleLine().Match(line) is { Success: true } example)
                {
                    foreach (Match call in CallOf().Matches(example.Groups["code"].Value))
                        if (!NotNames.Contains(call.Groups["name"].Value)) calls.Add(call.Groups["name"].Value);
                }
            }

            return calls;
        }

        return null;
    }

    /// <summary>Whether a file's tests are in the code they call: its doctests, or the asserts a program checks itself with.</summary>
    private static bool TestsAreInTheCode(PythonTests.Framework framework) => framework is PythonTests.Framework.Doctest or PythonTests.Framework.Asserts;

    /// <summary>What a framework's tests are called: a docstring's examples are its doctests, a program's own checks its asserts.</summary>
    private static string NounFor(PythonTests.Framework framework) => framework switch
    {
        PythonTests.Framework.Doctest => "doctest",
        PythonTests.Framework.Asserts => "assert",
        _ => "test",
    };

    [GeneratedRegex(@"^if\s+__name__\s*==\s*(?:'__main__'|""__main__"")\s*:")]
    private static partial Regex MainBlockHead();

    [GeneratedRegex(@"^\s*assert\b")]
    private static partial Regex AssertHead();

    /// <summary>
    /// The lines of a file that are its tests rather than the code they call: every line only of text or a comment - a
    /// docstring's, where its doctests are - and every line of an assert at its top or in its __main__ block, or, in
    /// JavaScript and TypeScript, of a console.assert.
    /// </summary>
    private static HashSet<int> LinesThatAreTests(SourceFile source)
    {
        var javaScript = IsJavaScript(source.Path);
        var masked = CodeText.MaskAll(source.Lines, javaScript ? Syntax.CLike : Syntax.Python);
        var testLines = Enumerable.Range(1, source.Count).Where(line => source.Lines[line - 1].Trim().Length > 0 && masked[line - 1].Trim().Length == 0).ToHashSet();

        var inMainBlock = false;
        var inAssert = false;
        var openBrackets = 0;

        for (var index = 0; index < source.Count; index++)
        {
            var line = masked[index];
            if (line.Trim().Length == 0) continue;

            if (openBrackets == 0)
            {
                var indent = line.Length - line.TrimStart().Length;
                if (indent == 0 && !javaScript) inMainBlock = MainBlockHead().IsMatch(source.Lines[index].TrimEnd());
                inAssert = javaScript ? ConsoleAssertHead().IsMatch(line) : (indent == 0 || inMainBlock) && AssertHead().IsMatch(line);
            }

            openBrackets = Math.Max(0, openBrackets + line.Count(c => c is '(' or '[' or '{') - line.Count(c => c is ')' or ']' or '}'));
            if (inAssert) testLines.Add(index + 1);
        }

        return testLines;
    }

    /// <summary>
    /// Whether a change alters a line that is one of the file's tests - one it replaces with something else, or takes out, or
    /// puts others among - so that it would change the tests rather than the code.
    /// </summary>
    private static bool ChangesItsTests(SourceFile source, LocalFix change, IReadOnlySet<int> testLines)
    {
        var replaced = Enumerable.Range(change.StartLine, Math.Max(0, change.RemoveCount)).ToList();
        if (change.NewLines.Count != change.RemoveCount) return replaced.Any(testLines.Contains);

        return replaced.Where((line, offset) => source.Lines[line - 1] != change.NewLines[offset]).Any(testLines.Contains);
    }

    [GeneratedRegex(@"^\s*console\s*\.\s*assert\s*\(")]
    private static partial Regex ConsoleAssertHead();

    [GeneratedRegex(@"line (?<line>\d+)$")]
    private static partial Regex AssertLine();

    /// <summary>
    /// The names a program's own check calls - the assert on line 17, the console.assert on line 9 - on its line and those it
    /// goes on over; null when there is none there.
    /// </summary>
    private static HashSet<string>? CallsInAssert(SourceFile tests, string name)
    {
        if (AssertLine().Match(name) is not { Success: true } named || !int.TryParse(named.Groups["line"].Value, out var line) || line < 1 || line > tests.Count) return null;

        var masked = CodeText.MaskAll(tests.Lines, IsJavaScript(tests.Path) ? Syntax.CLike : Syntax.Python);
        var calls = new HashSet<string>(StringComparer.Ordinal);
        var openBrackets = 0;

        for (var index = line - 1; index < tests.Count; index++)
        {
            foreach (Match call in CallOf().Matches(masked[index]))
                if (!NotNames.Contains(call.Groups["name"].Value)) calls.Add(call.Groups["name"].Value);

            openBrackets = Math.Max(0, openBrackets + masked[index].Count(c => c is '(' or '[' or '{') - masked[index].Count(c => c is ')' or ']' or '}'));
            if (openBrackets == 0) break;
        }

        return calls;
    }

    /// <summary>The names a test calls in its own code, found by its method's name in the file of tests; null when it is not there.</summary>
    private static HashSet<string>? CallsIn(SourceFile tests, string method)
    {
        // pytest names a test made for each of several values with them after it - test_add[1-2] - and that is not its function's name.
        var name = method.Split('[')[0].Trim();
        if (name.Length == 0) return null;

        var python = IsPython(tests.Path);
        var masked = CodeText.MaskAll(tests.Lines, python ? Syntax.Python : Syntax.CLike);

        for (var index = 0; index < masked.Count; index++)
        {
            if (HeadOf(masked[index], tests.Path) != name) continue;

            var indent = masked[index].Length - masked[index].TrimStart().Length;
            var calls = new HashSet<string>(StringComparer.Ordinal);

            for (var inside = index + 1; inside < masked.Count; inside++)
            {
                var line = masked[inside];
                if (line.Trim().Length == 0) continue;
                if (line.Length - line.TrimStart().Length <= indent) break;

                foreach (Match call in CallOf().Matches(line))
                    if (!NotNames.Contains(call.Groups["name"].Value)) calls.Add(call.Groups["name"].Value);
            }

            return calls;
        }

        return null;
    }

    /// <summary>
    /// The names a node:test test calls in its own code - test("pops the last pushed", () => { ... }) - found by the name
    /// it is given, in the body below its head; null when no test of that name is in the file.
    /// </summary>
    private static HashSet<string>? CallsInNamedTest(SourceFile tests, string name)
    {
        var masked = CodeText.MaskAll(tests.Lines, Syntax.CLike);
        string[] named = [$"\"{name}\"", $"'{name}'", $"`{name}`"];

        for (var index = 0; index < masked.Count; index++)
        {
            if (!NodeTestHead().IsMatch(masked[index]) || !named.Any(quoted => tests.Lines[index].Contains(quoted, StringComparison.Ordinal))) continue;

            var indent = masked[index].Length - masked[index].TrimStart().Length;
            var calls = new HashSet<string>(StringComparer.Ordinal);

            for (var inside = index + 1; inside < masked.Count; inside++)
            {
                var line = masked[inside];
                if (line.Trim().Length == 0) continue;
                if (line.Length - line.TrimStart().Length <= indent) break;

                foreach (Match call in CallOf().Matches(line))
                    if (!NotNames.Contains(call.Groups["name"].Value)) calls.Add(call.Groups["name"].Value);
            }

            return calls;
        }

        return null;
    }

    /// <summary>What a change found by the tests is said to have been checked by.</summary>
    public static string Describe(TestRepairResult result, string testFile)
    {
        var tests = Path.GetFileName(testFile);

        // doctest's tests are the examples in each docstring, which are called its doctests.
        var testNoun = NodeTests.ChecksWithConsoleAssert(testFile) ? "check" : NounFor(PythonTests.FrameworkOf(testFile));
        var all = result.Tests == 1 ? $"the {testNoun} in {tests} passes" : $"every one of the {result.Tests} {testNoun}s in {tests} passes";
        var before = (result.FailedBefore, result.UnfinishedBefore) switch
        {
            (var failed, 0) when failed == result.Tests => result.Tests == 1 ? "" : " - all of which failed before",
            (var failed, 0) => $" - {failed} of which failed before",
            (0, var unfinished) => $" - {unfinished} of which never finished before, the {testNoun}s stopped for time",
            var (failed, unfinished) => $" - {failed} of which failed before, and {unfinished} never finished, the {testNoun}s stopped for time",
        };
        var code = string.Join(" and ", result.CodeUnderTest.Select(Path.GetFileName));

        var how = result.Steps > 1
            ? $" No one change made every {testNoun} pass, so it was built a change at a time: each of its {result.Steps} changes was kept only when it made " +
              $"more of the failing {testNoun}s pass and none fail that passed, of {Count(result.Tried, "small change")} tried in {code}, the code the failing {testNoun}s call."
            : result.Tried == 1 ? $" It is the one small change tried in {code}, the code the failing {testNoun}s call." : $" It is the first of {result.Tried} small changes tried in {code}, the code the failing {testNoun}s call, that does.";

        return $"With this change {all}{before}.{how} Another change could also make the {testNoun}s pass, so check it is the one you meant; more {testNoun}s narrow it down.";
    }
}
