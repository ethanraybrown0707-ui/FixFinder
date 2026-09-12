using FixFinder.Core.Engine;
using FixFinder.Core.Execution;
using FixFinder.Core.Fingerprinting;
using FixFinder.Core.Parsing;
using FixFinder.Core.Patching;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>
/// The loop over a program that really does go wrong twice, with real files on a real disk.
/// </summary>
/// <remarks>
/// Only the search is stood in for. Everything the loop depends on being true - that applying a
/// patch changes the file, that re-running then reports a <i>different</i> error, that the second
/// patch still finds its context in a file the first one has already edited, and that the third
/// run comes back clean - happens for real, because each of those is a place where the feature
/// could look right in a unit test and fail on a program.
/// <para>
/// A patch is fetched from the internet in normal use, so the two here are written out as diffs
/// rather than as edits: what is being tested includes the mapping and the exact-context match,
/// not just the ordering.
/// </para>
/// </remarks>
public class FixLoopEndToEndTests : IDisposable
{
    private readonly TempFolder _temp = new();

    private static readonly string? Python =
        TargetFactory.FindOnPath("python") ??
        TargetFactory.FindOnPath("py") ??
        TargetFactory.FindOnPath("python3");

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Two bugs, one after the other. The second is unreachable until the first is gone.</summary>
    private const string TwoBugs = """
        data = {}
        print("starting")
        print(data["user_id"])
        print(1 / 0)
        """;

    private const string FirstFix = """
        --- a/two_bugs.py
        +++ b/two_bugs.py
        @@ -1,4 +1,4 @@
         data = {}
         print("starting")
        -print(data["user_id"])
        +print(data.get("user_id"))
         print(1 / 0)
        """;

    private const string SecondFix = """
        --- a/two_bugs.py
        +++ b/two_bugs.py
        @@ -1,4 +1,4 @@
         data = {}
         print("starting")
         print(data.get("user_id"))
        -print(1 / 0)
        +print(1 / 1)
        """;

    private const string Fixed = """
        data = {}
        print("starting")
        print(data.get("user_id"))
        print(1 / 1)
        """;

    /// <summary>
    /// Stands in for the search only: it runs the program for real, then answers with a
    /// prepared patch for whatever came back.
    /// </summary>
    private sealed class PatchesInOrder(string sourceRoot, params string[] diffs) : IFixSession
    {
        private readonly Queue<string> _diffs = new(diffs);

        public async Task<SessionOutcome> RunAsync(LaunchPlan launch, SearchBudget? budget, CancellationToken ct)
        {
            var run = await new TargetRunner().RunAsync(launch.Spec!, ct);

            return Answer(run, launch.Spec!);
        }

        public Task<SessionOutcome> SearchForOtherAsync(
            SessionOutcome from, ParsedError error, SearchBudget? budget, CancellationToken ct) =>
            Task.FromResult(Answer(from.Run!, from.Spec!));

        public Task<SessionOutcome> ContinueFromAsync(
            TargetRunResult run, TargetSpec spec, SearchBudget? budget, string? sourceFolder,
            bool failedToCompile, CancellationToken ct) =>
            Task.FromResult(Answer(run, spec));

        private SessionOutcome Answer(TargetRunResult run, TargetSpec spec)
        {
            if (run.Error is null || _diffs.Count == 0)
            {
                return new SessionOutcome
                {
                    Result = run.Error is null ? SessionResult.RanFine : SessionResult.NothingFound,
                    Headline = run.Error is null ? "It ran without a problem." : "Nothing found.",
                    Detail = "",
                    Spec = spec,
                    Run = run,
                };
            }

            var stackFiles = run.Error.Frames
                .Where(f => f.File is { Length: > 0 })
                .Select(f => f.File!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var patch = UnifiedDiffParser.Parse(_diffs.Dequeue());

            var plan = new PatchApplier().Plan(
                patch, new SourcePathMapper(sourceRoot, stackFiles), stackFiles);

            return new SessionOutcome
            {
                Result = SessionResult.FoundFix,
                Headline = $"It crashed: {run.Error.Summary}",
                Detail = "",
                Spec = spec,
                Run = run,
                Error = run.Error,
                Fingerprint = FingerprintBuilder.Build(run.Error),
                Best = new FixCandidate
                {
                    Id = $"gh#{diffs.Length - _diffs.Count}",
                    SourceName = "GitHub",
                    Title = $"Fix for {run.Error.ExceptionType}",
                    Url = "https://example.invalid/issue",
                    Tier = FixTier.AutoAppliable,
                },
                Plan = plan,
                SourceRoot = sourceRoot,
                StackTraceFiles = stackFiles,
            };
        }
    }

    [Fact]
    public async Task Two_bugs_are_fixed_one_after_the_other()
    {
        if (Python is null) return;

        var script = Path.Combine(_temp.Path, "two_bugs.py");
        File.WriteAllText(script, TwoBugs.ReplaceLineEndings("\n"));

        var spec = new TargetSpec
        {
            ExecutablePath = Python,
            Arguments = $"\"{script}\"",
            WorkingDirectory = _temp.Path,
            Timeout = TimeSpan.FromSeconds(30),
        };

        var rounds = new List<string>();

        var loop = new FixLoop(
            new PatchesInOrder(_temp.Path, FirstFix, SecondFix),
            new FixStep(new BackupStore(Path.Combine(_temp.Path, "backups"))))
        {
            Ask = (outcome, _, _) =>
            {
                rounds.Add(outcome.Error!.ExceptionType!);
                return Task.FromResult(new RoundDecision(RoundChoice.Apply));
            },
        };

        var result = await loop.RunAsync(new LaunchPlan(spec, null, "python"));

        // The two errors are seen in the order the program hits them, which is only possible
        // because the first patch was really written before the program was run again.
        Assert.Equal(["KeyError", "ZeroDivisionError"], rounds);

        Assert.Equal(LoopEnd.Fixed, result.End);
        Assert.Equal(2, result.Rounds.Count);
        Assert.Equal(2, result.AppliedCount);
        Assert.True(result.Looped);

        Assert.Equal(Fixed.ReplaceLineEndings("\n"), File.ReadAllText(script).ReplaceLineEndings("\n"));
        Assert.Contains("2 changes", result.Detail);
    }

    /// <summary>
    /// "Apply all" reaches the same place without being asked a second time.
    /// </summary>
    [Fact]
    public async Task Apply_everything_works_through_both_without_asking_again()
    {
        if (Python is null) return;

        var script = Path.Combine(_temp.Path, "two_bugs.py");
        File.WriteAllText(script, TwoBugs.ReplaceLineEndings("\n"));

        var spec = new TargetSpec
        {
            ExecutablePath = Python,
            Arguments = $"\"{script}\"",
            WorkingDirectory = _temp.Path,
            Timeout = TimeSpan.FromSeconds(30),
        };

        var asked = 0;

        var loop = new FixLoop(
            new PatchesInOrder(_temp.Path, FirstFix, SecondFix),
            new FixStep(new BackupStore(Path.Combine(_temp.Path, "backups"))))
        {
            Ask = (_, _, _) =>
            {
                asked++;
                return Task.FromResult(new RoundDecision(RoundChoice.ApplyEverything));
            },
        };

        var result = await loop.RunAsync(new LaunchPlan(spec, null, "python"));

        Assert.Equal(1, asked);
        Assert.Equal(LoopEnd.Fixed, result.End);
        Assert.Equal(Fixed.ReplaceLineEndings("\n"), File.ReadAllText(script).ReplaceLineEndings("\n"));
    }

    /// <summary>
    /// Every round backs its own file up, so any one of them can be undone on its own.
    /// </summary>
    [Fact]
    public async Task Each_round_leaves_its_own_backup()
    {
        if (Python is null) return;

        var script = Path.Combine(_temp.Path, "two_bugs.py");
        File.WriteAllText(script, TwoBugs.ReplaceLineEndings("\n"));

        var root = Path.Combine(_temp.Path, "backups");

        var spec = new TargetSpec
        {
            ExecutablePath = Python,
            Arguments = $"\"{script}\"",
            WorkingDirectory = _temp.Path,
            Timeout = TimeSpan.FromSeconds(30),
        };

        var loop = new FixLoop(
            new PatchesInOrder(_temp.Path, FirstFix, SecondFix),
            new FixStep(new BackupStore(root)))
        {
            Ask = (_, _, _) => Task.FromResult(new RoundDecision(RoundChoice.ApplyEverything)),
        };

        var result = await loop.RunAsync(new LaunchPlan(spec, null, "python"));

        var folders = result.Rounds
            .Select(r => r.Step!.Apply.BackupFolder)
            .ToList();

        Assert.Equal(2, folders.Count);
        Assert.All(folders, folder => Assert.True(Directory.Exists(folder)));
        Assert.Equal(2, folders.Distinct().Count());

        // Undoing the last change alone puts back what the first one produced, not the original.
        var restored = new BackupStore(root).Restore(folders[1]!);

        Assert.True(restored.Ok);
        Assert.Contains("data.get(\"user_id\")", File.ReadAllText(script));
        Assert.Contains("1 / 0", File.ReadAllText(script));
    }
}
