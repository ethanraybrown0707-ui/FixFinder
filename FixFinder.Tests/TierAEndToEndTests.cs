using FixFinder.Core.Execution;
using FixFinder.Core.Fingerprinting;
using FixFinder.Core.Parsing;
using FixFinder.Core.Patching;
using FixFinder.Core.Sources;
using FixFinder.Core.Verification;

namespace FixFinder.Tests;

/// <summary>
/// The whole loop, once, against a real program: crash, fingerprint, harvest, map, plan,
/// apply, rebuild, re-run, verdict.
/// </summary>
/// <remarks>
/// Every other test in the suite checks one stage. This one checks that the stages fit together,
/// which is a different thing and the place integration problems actually live - a fingerprint
/// the verifier compares differently from how the parser built it, a mapped path that is right
/// but arrives too late to gate on, a rollback that restores a file the applier never backed up.
/// <para>
/// The candidate is constructed rather than searched for, because the point here is the
/// machinery, not the search. Making this run against a live GitHub issue would make it depend
/// on a stranger's repository still existing and still carrying a diff that happens to apply.
/// </para>
/// </remarks>
public class TierAEndToEndTests : IDisposable
{
    private readonly TempFolder _temp = new();

    private static readonly string? Python = FindPython();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Found the same way the tool finds it, rather than by a hard-coded path.
    /// </summary>
    /// <remarks>
    /// Tests that need it return early when it is absent, so the suite reports on the code
    /// rather than on whichever interpreters a given machine happens to have.
    /// </remarks>
    private static string? FindPython() =>
        TargetFactory.FindOnPath("python") ??
        TargetFactory.FindOnPath("py") ??
        TargetFactory.FindOnPath("python3");

    /// <summary>The program under test: reads a key that is not there.</summary>
    private const string Broken = """
        import json

        def read_user(payload):
            return payload["user_id"]

        print("starting")
        print(read_user(json.loads("{}")))
        """;

    /// <summary>
    /// An issue comment carrying a real unified diff, exactly as GitHub would return it.
    /// </summary>
    /// <remarks>
    /// Markdown with a fenced diff, which is the shape the extractor has to get right: the fence
    /// tag says "diff", but only the parser accepting it makes the candidate appliable.
    /// </remarks>
    private const string IssueBody = """
        We hit this whenever the upstream response omits the field.

        The fix is to stop assuming the key is present:

        ```diff
        --- a/reader.py
        +++ b/reader.py
        @@ -1,4 +1,4 @@
         import json

         def read_user(payload):
        -    return payload["user_id"]
        +    return payload.get("user_id")
        ```

        That returns None instead of raising.
        """;

    private static FixCandidate Candidate(string body) => new()
    {
        SourceName = "GitHub",
        Id = "owner/repo#41",
        Title = "KeyError: 'user_id' when the payload omits the field",
        Url = "https://github.com/owner/repo/issues/41",
        BodyText = body,
        RawBody = body,
        RawBodyIsHtml = false,
        IsClosed = true,
        ClosedReason = "completed",
        Tags = ["python", "bug"],
        CreatedAt = DateTimeOffset.UtcNow.AddMonths(-6),
        LastActivityAt = DateTimeOffset.UtcNow.AddMonths(-5),
    };

    private TargetSpec SpecFor(string script) => new()
    {
        ExecutablePath = Python!,
        Arguments = $"\"{script}\"",
        WorkingDirectory = _temp.Path,
        Timeout = TimeSpan.FromSeconds(30),
    };

    /// <summary>
    /// Crash, find the patch, apply it, re-run, and get Fixed - with a backup that still works.
    /// </summary>
    [Fact]
    public async Task ACandidateCarryingADiffIsAppliedAndVerifiedAsFixed()
    {
        if (Python is null) return;

        var script = Path.Combine(_temp.Path, "reader.py");
        File.WriteAllText(script, Broken.ReplaceLineEndings("\n"));
        var original = File.ReadAllBytes(script);

        var spec = SpecFor(script);

        // ---------- 1. run it and read the crash
        var run = await new TargetRunner().RunAsync(spec, CancellationToken.None);

        Assert.Equal(RunOutcome.Crashed, run.Outcome);
        Assert.NotNull(run.Error);
        Assert.Equal("python", run.Error!.LanguageId);

        var fingerprint = FingerprintBuilder.Build(run.Error);
        Assert.Equal("KeyError", fingerprint.ShortExceptionType);

        // ---------- 2. harvest the patch out of the candidate
        var candidate = Candidate(IssueBody);

        var harvest = await new PatchHarvester(new Core.Http.FixFinderHttpClient(
            new Core.Http.HttpCache(Path.Combine(_temp.Path, "cache"))))
            .HarvestAsync(candidate);

        Assert.True(harvest.HasAppliablePatch, "the fenced diff should have parsed");
        Assert.Equal(FixTier.AutoAppliable, candidate.Tier);

        // ---------- 3. map it onto this tree and plan every hunk
        var stackFiles = run.Error.Frames.Where(f => f.File is not null).Select(f => f.File!).ToArray();
        var mapper = new SourcePathMapper(_temp.Path, stackFiles);

        var applier = new PatchApplier();
        var plan = applier.Plan(harvest.Patches[0], mapper, stackFiles);

        Assert.True(plan.CanApply, plan.Explanation);
        Assert.Equal(script, Assert.Single(plan.TargetPaths));

        // ---------- 4. a dry run must still write nothing
        var backups = new BackupStore(Path.Combine(_temp.Path, "backups"));

        var dryRun = applier.Apply(plan, backups, _temp.Path);
        Assert.True(dryRun.WasDryRun);
        Assert.Equal(original, File.ReadAllBytes(script));

        // ---------- 5. apply for real
        var applied = applier.Apply(plan, backups, _temp.Path, dryRun: false,
            candidate.Id, candidate.Title, candidate.Url);

        Assert.True(applied.Ok, applied.Failure);
        Assert.NotEqual(original, File.ReadAllBytes(script));

        // ---------- 6. re-run and reach a verdict
        var verification = await new FixVerifier().VerifyAsync(
            spec, fingerprint, backups, applied.BackupFolder);

        Assert.Equal(FixVerdict.Fixed, verification.Verdict);
        Assert.False(verification.RolledBack);

        // ---------- 7. the backup still puts it back exactly
        var restore = backups.Restore(applied.BackupFolder!);

        Assert.True(restore.Ok, restore.Summary);
        Assert.Equal(original, File.ReadAllBytes(script));
    }

    /// <summary>
    /// The same loop, with a patch that changes the code without fixing the bug.
    /// </summary>
    /// <remarks>
    /// The failure path matters more than the success one. A patch applying cleanly says nothing
    /// about whether it worked, and this is the check that catches a confident-looking change
    /// that did not help - and undoes it without being asked.
    /// </remarks>
    [Fact]
    public async Task APatchThatAppliesButDoesNotHelpIsRolledBackAutomatically()
    {
        if (Python is null) return;

        var script = Path.Combine(_temp.Path, "reader.py");
        File.WriteAllText(script, Broken.ReplaceLineEndings("\n"));
        var original = File.ReadAllBytes(script);

        var spec = SpecFor(script);
        var run = await new TargetRunner().RunAsync(spec, CancellationToken.None);
        var fingerprint = FingerprintBuilder.Build(run.Error!);

        // Applies perfectly, changes the source, and leaves the bug exactly where it was. It
        // has to leave the program still *running* - a patch that breaks it differently would
        // be a DifferentError, which is a separate verdict with separate handling.
        var uselessDiff = string.Join("\n",
            "Tidying up the log message while we are in here.",
            "",
            "```diff",
            "--- a/reader.py",
            "+++ b/reader.py",
            "@@ -6,2 +6,2 @@",
            "-print(\"starting\")",
            "+print(\"starting up\")",
            " print(read_user(json.loads(\"{}\")))",
            "```");

        var candidate = Candidate(uselessDiff);

        var harvest = await new PatchHarvester(new Core.Http.FixFinderHttpClient(
            new Core.Http.HttpCache(Path.Combine(_temp.Path, "cache"))))
            .HarvestAsync(candidate);

        Assert.True(harvest.HasAppliablePatch);

        var stackFiles = run.Error!.Frames.Where(f => f.File is not null).Select(f => f.File!).ToArray();
        var applier = new PatchApplier();
        var plan = applier.Plan(harvest.Patches[0], new SourcePathMapper(_temp.Path, stackFiles), stackFiles);

        Assert.True(plan.CanApply, plan.Explanation);

        var backups = new BackupStore(Path.Combine(_temp.Path, "backups"));
        var applied = applier.Apply(plan, backups, _temp.Path, dryRun: false, candidate.Id);

        Assert.True(applied.Ok, applied.Failure);

        var verification = await new FixVerifier().VerifyAsync(
            spec, fingerprint, backups, applied.BackupFolder);

        Assert.Equal(FixVerdict.SameErrorPersists, verification.Verdict);
        Assert.True(verification.RolledBack);
        Assert.Equal(original, File.ReadAllBytes(script));
    }

    /// <summary>
    /// The fixture shipped in TestTargets still applies to the file it was written for.
    /// </summary>
    /// <remarks>
    /// Guards a demo that would otherwise rot in silence. <c>fix.patch</c> matches
    /// <c>reader.py</c> byte for byte, so reformatting the script, re-wrapping its docstring or
    /// letting an editor trim the lone space on its blank context line all break it - and
    /// nothing else in the suite would notice until someone tried to demonstrate the tool.
    /// </remarks>
    [Fact]
    public void TheShippedFixablePythonFixtureStillApplies()
    {
        var folder = FindTestTarget("FixablePython");
        if (folder is null) return;   // not present in this checkout

        var script = Path.Combine(folder, "reader.py");
        var patchText = File.ReadAllText(Path.Combine(folder, "fix.patch"));

        var patch = UnifiedDiffParser.Parse(patchText);
        Assert.True(patch.Ok, patch.Rejection);

        // Copied to a temp tree so the test never edits the repository.
        var working = Path.Combine(_temp.Path, "reader.py");
        File.Copy(script, working, overwrite: true);

        var plan = new PatchApplier().Plan(
            patch, new SourcePathMapper(_temp.Path, [working]), [working]);

        Assert.True(plan.CanApply,
            $"fix.patch no longer matches reader.py - {plan.Explanation}");

        var hunk = plan.Files[0].Hunks[0];
        Assert.Contains("matched exactly", hunk.Explanation, StringComparison.Ordinal);
    }

    private static string? FindTestTarget(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && directory.Name != "FixFinder") directory = directory.Parent;
        if (directory is null) return null;

        var folder = Path.Combine(directory.FullName, "TestTargets", name);

        return Directory.Exists(folder) ? folder : null;
    }

    /// <summary>
    /// A Stack Overflow answer goes through the same pipeline and stays advisory throughout.
    /// </summary>
    [Fact]
    public async Task AStackOverflowAnswerYieldsCodeToReadAndNothingToApply()
    {
        var answer =
            "<p>Use <code>dict.get</code> instead:</p>\n" +
            "<pre><code>return payload.get(\"user_id\")\n</code></pre>";

        var candidate = new FixCandidate
        {
            SourceName = "Stack Overflow",
            Id = "SO 12345",
            Title = "KeyError when a key is missing",
            Url = "https://stackoverflow.com/questions/12345",
            RawBody = answer,
            RawBodyIsHtml = true,
            Attribution = "Answer by someone on Stack Overflow, CC BY-SA — https://stackoverflow.com/a/12346",
        };

        using var temp = new TempFolder();

        var harvest = await new PatchHarvester(new Core.Http.FixFinderHttpClient(
            new Core.Http.HttpCache(Path.Combine(temp.Path, "cache"))))
            .HarvestAsync(candidate);

        Assert.False(harvest.HasAppliablePatch);
        Assert.Equal(FixTier.Advisory, candidate.Tier);

        var snippet = Assert.Single(harvest.Snippets);
        Assert.Contains("payload.get", snippet.Code, StringComparison.Ordinal);
        Assert.Contains("code block(s) to read", harvest.Summary, StringComparison.Ordinal);
    }
}
