using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>What changing the code a file's tests call showed of the tests.</summary>
/// <param name="CodeUnderTest">The files of the program the tests call, which is where the changes were made.</param>
/// <param name="Tests">How many tests ran, every one passing before any change.</param>
/// <param name="Tried">How many small changes were made, each on its own in a copy, with the tests run against it.</param>
/// <param name="Caught">How many of those made at least one test fail.</param>
/// <param name="Missed">Each change every test still passed with.</param>
/// <param name="NotJudged">How many showed nothing either way: a change that did not build, or a run stopped for time.</param>
public sealed record TestStrengthResult(
    IReadOnlyList<string> CodeUnderTest, int Tests, int Tried, int Caught, IReadOnlyList<MissedChange> Missed, int NotJudged, IReadOnlyList<string> Notes);

/// <summary>A change to the code the tests call that every test still passed with: a mistake there they would not catch.</summary>
/// <param name="Was">The line as the program has it.</param>
/// <param name="Became">The line as it was changed.</param>
/// <param name="From">What on the line was changed: &lt;.</param>
/// <param name="To">What it was changed to: &lt;=.</param>
/// <param name="Kind">The kind of mistake the change makes: a bound off by one.</param>
public sealed record MissedChange(string File, int Line, string Was, string Became, string From, string To, string Kind);

public sealed partial class TestRepair
{
    /// <summary>How many small changes are made to measure the tests: enough to cover a short program's lines, few enough to run in a minute.</summary>
    private const int MostChangesToMeasure = 24;

    /// <summary>How long measuring the tests goes on for before it stops with what it has.</summary>
    private static readonly TimeSpan MeasuringBudget = TimeSpan.FromSeconds(60);

    private enum ChangeVerdict { Caught, Missed, NotJudged }

    /// <summary>
    /// How well a file's tests check the code they call, when every one of them passes: small changes of the kinds a mistake
    /// makes - a bound off by one, a comparison the wrong way round, the wrong operator - are made to that code one at a time,
    /// in a copy, and the tests run against each. A change some test fails with is one the tests catch; a change every test
    /// still passes with is a mistake there that would go unnoticed. Nothing in the program itself is ever changed.
    /// </summary>
    /// <remarks>
    /// Only for tests in a file of their own - unittest, pytest, JUnit, node:test - not doctests or asserts written in the
    /// code they test, which a change could reach. The changes are spread over the lines of the code under test, one to a
    /// line, the likeliest kind of mistake first, at most <see cref="MostChangesToMeasure"/>; a change that does not build,
    /// or whose run is stopped for time, shows nothing either way and is counted neither caught nor missed.
    /// </remarks>
    public async Task<TestStrengthResult?> WeakSpotsAsync(
        string testFile, IReadOnlyList<TestReport.TestResult> before, int? testsFound, TimeSpan ranFor, TimeSpan runTimeout, CancellationToken cancellationToken)
    {
        var passed = before.Where(test => test.Status == "SUCCESSFUL").ToList();
        if (passed.Count == 0 || before.Any(test => test.Status is not ("SUCCESSFUL" or "SKIPPED"))) return null;
        if (testsFound is { } found && found > before.Count) return null;
        if (SourceFile.Read(testFile) is not { } tests) return null;

        var framework = PythonTests.FrameworkOf(testFile);
        if (TestsAreInTheCode(framework) || NodeTests.ChecksWithConsoleAssert(testFile)) return null;

        var root = ProgramCopy.RootOf(testFile);
        var targets = CodeUnderTest(tests, [], root, testsAreInTheCode: false);
        if (targets.Count == 0) return null;

        var changes = ChangesToMeasureWith(targets);
        if (changes.Count == 0) return null;

        var ran = passed.Select(IdentityOf).ToHashSet(StringComparer.Ordinal);
        var allTests = Math.Max(testsFound ?? 0, ran.Count);
        var perRun = TimeSpan.FromSeconds(Math.Clamp(ranFor.TotalSeconds * 3 + 5, 10, Math.Max(10, runTimeout.TotalSeconds)));
        var workers = Enumerable.Range(0, Math.Max(1, AtOnce)).Select(_ => new LogicRepair.Workspace(root, testFile)).ToList();
        var notes = new List<string>();
        var started = DateTime.UtcNow;
        var (tried, caught, notJudged) = (0, 0, 0);
        var missed = new List<MissedChange>();

        try
        {
            foreach (var workspace in workers)
                if (!workspace.Create()) return null;

            for (var batch = 0; batch < changes.Count; batch += workers.Count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (DateTime.UtcNow - started > MeasuringBudget)
                {
                    notes.Add($"Measuring the tests stopped after {Count(tried, "change")} of {changes.Count}: running them with each took more than " +
                              $"{MeasuringBudget.TotalSeconds:0} seconds in all.");
                    break;
                }

                var slice = changes.Skip(batch).Take(workers.Count).ToList();
                Progress?.Invoke($"Running the tests with changes {tried + 1}-{tried + slice.Count} to see what they catch...");
                var verdicts = await Task.WhenAll(slice.Select((change, index) =>
                    VerdictAsync(workers[index], root, targets, change.Fix, ran, allTests, perRun, runTimeout, cancellationToken)));
                tried += slice.Count;

                for (var index = 0; index < slice.Count; index++)
                {
                    switch (verdicts[index])
                    {
                        case ChangeVerdict.Caught:
                            caught++;
                            break;
                        case ChangeVerdict.Missed:
                            var (mutation, source) = (slice[index].Mutation, slice[index].Source);
                            missed.Add(new MissedChange(source.Path, mutation.Line, source.Line(mutation.Line)!, mutation.NewText, mutation.From, mutation.To, mutation.Kind));
                            break;
                        default:
                            notJudged++;
                            break;
                    }
                }
            }
        }
        finally
        {
            foreach (var workspace in workers) workspace.Dispose();
        }

        Log?.Invoke($"Measured the tests with {Count(tried, "change")}: {caught} caught, {missed.Count} missed, {notJudged} showed nothing either way.");
        return new TestStrengthResult(targets.Select(target => target.Path).ToList(), ran.Count, tried, caught, missed, notJudged, notes);
    }

    /// <summary>
    /// The changes the tests are measured with: for each line of the code under test that one can be made to, the change of
    /// the likeliest kind of mistake there - the cheapest - replacing the line; spread evenly over the lines when there are
    /// more than <see cref="MostChangesToMeasure"/>.
    /// </summary>
    private static List<(Mutation Mutation, SourceFile Source, LocalFix Fix)> ChangesToMeasureWith(IReadOnlyList<SourceFile> targets)
    {
        var everyLine = new List<(Mutation Mutation, SourceFile Source, LocalFix Fix)>();
        foreach (var source in targets)
        {
            for (var number = 1; number <= source.Lines.Count; number++)
            {
                if (Mutations.For(source, number).Where(mutation => mutation.Placement is null).OrderBy(mutation => mutation.Cost).FirstOrDefault() is not { } cheapest)
                    continue;
                everyLine.Add((cheapest, source, LocalFix.ReplaceLine("logic-edit", $"Change {cheapest.From} to {cheapest.To} on line {number}",
                    $"Line {number} made into {cheapest.Kind}.", source.Path, number, cheapest.NewText)));
            }
        }

        if (everyLine.Count <= MostChangesToMeasure) return everyLine;
        return Enumerable.Range(0, MostChangesToMeasure).Select(index => everyLine[index * everyLine.Count / MostChangesToMeasure]).ToList();
    }

    /// <summary>
    /// The tests run in a copy with one change made: caught when some test fails, missed when every test that passed before
    /// passes again, and not judged when the change did not build, the run was stopped for time, or fewer tests ran.
    /// </summary>
    private static async Task<ChangeVerdict> VerdictAsync(
        LogicRepair.Workspace workspace, string root, IReadOnlyList<SourceFile> targets, LocalFix change, IReadOnlySet<string> ran, int allTests,
        TimeSpan perRun, TimeSpan buildTimeout, CancellationToken cancellationToken)
    {
        if (targets.FirstOrDefault(target => SamePath(target.Path, change.File)) is not { } changed || Combined(changed, [change]) is not { } lines)
            return ChangeVerdict.NotJudged;

        try
        {
            foreach (var target in targets)
            {
                // Only ever a file inside the copy: the program's own files are never written to while the tests are measured.
                var copy = Path.GetFullPath(ProgramCopy.InCopy(root, target.Path, workspace.Folder));
                if (!copy.StartsWith(Path.GetFullPath(workspace.Folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return ChangeVerdict.NotJudged;

                if (ReferenceEquals(target, changed)) await File.WriteAllBytesAsync(copy, changed.Render(lines), cancellationToken);
                else File.Copy(target.Path, copy, overwrite: true);
            }

            var plan = TargetFactory.FromFile(workspace.File, perRun);
            if (!plan.Ok || plan.Spec is null) return ChangeVerdict.NotJudged;

            var runner = new TargetRunner();
            if (plan.Compile is { } compile)
            {
                var built = await runner.RunAsync(compile.WithTimeout(buildTimeout), cancellationToken);
                if (built.Outcome is RunOutcome.TimedOut or RunOutcome.LaunchFailed || built.ExitCode != 0) return ChangeVerdict.NotJudged;
            }

            var run = await runner.RunAsync(plan.Spec.WithTimeout(perRun), cancellationToken);
            if (run.Outcome is RunOutcome.TimedOut or RunOutcome.LaunchFailed) return ChangeVerdict.NotJudged;

            var after = TestReport.ResultsIn(run.Lines.Select(line => line.Text));
            if (after.Any(test => test.Status is "FAILED" or "SETUP-FAILED" or "TEARDOWN-FAILED")) return ChangeVerdict.Caught;

            var passing = after.Where(test => test.Status == "SUCCESSFUL").Select(IdentityOf).ToHashSet(StringComparer.Ordinal);
            return ran.All(passing.Contains) && passing.Count >= allTests ? ChangeVerdict.Missed : ChangeVerdict.NotJudged;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ChangeVerdict.NotJudged;
        }
    }
}
