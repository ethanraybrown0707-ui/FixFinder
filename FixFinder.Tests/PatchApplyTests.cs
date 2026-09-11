using System.Text;
using FixFinder.Core.Patching;

namespace FixFinder.Tests;

/// <summary>
/// Covers path mapping, containment, and applying a patch to a real tree on disk.
/// </summary>
/// <remarks>
/// The most consequential tests in the project. Everything up to this point reads; this is the
/// only code that writes, and it writes to source files. The refusal tests are therefore the
/// point of the class - a patch that is applied wrongly is worse than one that is never applied
/// at all, because the tree then looks changed on purpose.
/// </remarks>
public class PatchApplyTests : IDisposable
{
    private readonly TempFolder _temp = new();

    private string Root => Path.Combine(_temp.Path, "tree");

    public PatchApplyTests() => Directory.CreateDirectory(Root);

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private static string Lines(params string[] lines) => string.Join("\n", lines);

    /// <summary>Writes a file into the tree, creating folders as needed.</summary>
    private string Write(string relative, string content, string ending = "\n", bool bom = false)
    {
        var path = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var text = content.ReplaceLineEndings(ending);
        var bytes = new UTF8Encoding(false).GetBytes(text);

        if (bom) bytes = [0xEF, 0xBB, 0xBF, .. bytes];

        File.WriteAllBytes(path, bytes);
        return path;
    }

    private SourcePathMapper Mapper(params string[] stackTraceFiles) => new(Root, stackTraceFiles);

    private static ParsedPatch Patch(string diff)
    {
        var parsed = UnifiedDiffParser.Parse(diff);
        Assert.True(parsed.Ok, parsed.Rejection);
        return parsed;
    }

    /// <summary>The simple one-line fix used by most of these tests.</summary>
    private static string SimpleDiff(string path = "app/reader.py") => Lines(
        $"--- a/{path}",
        $"+++ b/{path}",
        "@@ -2,3 +2,3 @@",
        " def read(payload):",
        "-    return payload[\"user_id\"]",
        "+    return payload.get(\"user_id\")",
        "");

    private const string ReaderSource = """
        import json

        def read(payload):
            return payload["user_id"]

        print(read({}))
        """;

    // ================================================================== containment

    /// <summary>
    /// The traversal case. Nothing is written, and the refusal is structural.
    /// </summary>
    /// <remarks>
    /// This is the single test the whole patching design exists to pass. A diff is
    /// attacker-controlled text fetched from a public issue tracker, and
    /// <c>a/../../../../Windows/System32/drivers/etc/hosts</c> is a perfectly well-formed one.
    /// </remarks>
    [Fact]
    public void APatchThatEscapesTheSourceRootIsRefusedAndWritesNothing()
    {
        Write("app/reader.py", ReaderSource);

        var patch = Patch(Lines(
            "--- a/../../../../Windows/System32/drivers/etc/hosts",
            "+++ b/../../../../Windows/System32/drivers/etc/hosts",
            "@@ -1 +1 @@",
            "-127.0.0.1 localhost",
            "+127.0.0.1 evil.test"));

        var plan = new PatchApplier().Plan(patch, Mapper());

        Assert.False(plan.CanApply);
        Assert.Equal(ApplyOutcome.RejectedOutsideRoot, plan.Outcome);
        Assert.Empty(plan.TargetPaths);
    }

    [Fact]
    public void AnAbsolutePathInAPatchIsRefused()
    {
        var mapped = Mapper().Map(@"C:\Windows\System32\drivers\etc\hosts");

        Assert.Equal(MapOutcome.OutsideRoot, mapped.Outcome);
        Assert.Null(mapped.FullPath);
    }

    /// <summary>
    /// A sibling folder whose name merely starts the same way is still outside.
    /// </summary>
    /// <remarks>
    /// The reason containment compares against the root plus a separator. Without it, a root of
    /// <c>...\tree</c> would happily accept <c>...\tree-secrets\config.env</c>.
    /// </remarks>
    [Fact]
    public void AFolderWhoseNameMerelySharesAPrefixWithTheRootIsOutside()
    {
        var sibling = Path.Combine(_temp.Path, "tree-secrets");
        Directory.CreateDirectory(sibling);
        File.WriteAllText(Path.Combine(sibling, "config.env"), "SECRET=1");

        Assert.False(Mapper().IsInsideRoot(Path.Combine(sibling, "config.env")));
    }

    // ================================================================== path mapping

    [Fact]
    public void APathThatMatchesExactlyIsMapped()
    {
        var expected = Write("app/reader.py", ReaderSource);
        var mapped = Mapper().Map("app/reader.py");

        Assert.Equal(MapOutcome.Mapped, mapped.Outcome);
        Assert.Equal(expected, mapped.FullPath);
    }

    /// <summary>A diff written against a differently-laid-out checkout still resolves.</summary>
    [Fact]
    public void AShorterSuffixIsMatchedWhenTheFullPathDoesNot()
    {
        var expected = Write("app/cart/basket.py", "x = 1\n");
        var mapped = Mapper().Map("src/cart/basket.py");

        Assert.Equal(MapOutcome.Mapped, mapped.Outcome);
        Assert.Equal(expected, mapped.FullPath);
        Assert.Contains("cart/basket.py", mapped.Explanation, StringComparison.Ordinal);
    }

    /// <summary>
    /// Two files of the same name and nothing to choose between them: refuse, never guess.
    /// </summary>
    [Fact]
    public void TwoEquallyGoodMatchesAreReportedAsAmbiguous()
    {
        Write("one/basket.py", "x = 1\n");
        Write("two/basket.py", "x = 2\n");

        var mapped = Mapper().Map("somewhere/basket.py");

        Assert.Equal(MapOutcome.Ambiguous, mapped.Outcome);
        Assert.Null(mapped.FullPath);
        Assert.Contains("nothing distinguishes them", mapped.Explanation, StringComparison.Ordinal);
    }

    /// <summary>The stack trace is the only evidence allowed to break a tie.</summary>
    [Fact]
    public void TheStackTraceBreaksATieBetweenTwoFilesOfTheSameName()
    {
        Write("one/basket.py", "x = 1\n");
        var crashed = Write("two/basket.py", "x = 2\n");

        var mapped = Mapper(crashed).Map("somewhere/basket.py");

        Assert.Equal(MapOutcome.Mapped, mapped.Outcome);
        Assert.Equal(crashed, mapped.FullPath);
        Assert.Contains("stack trace", mapped.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileThatIsNotInTheTreeIsReportedAsNotFound()
    {
        Write("app/reader.py", ReaderSource);

        var mapped = Mapper().Map("app/nothing_like_it.py");

        Assert.Equal(MapOutcome.NotFound, mapped.Outcome);
        Assert.Contains("nothing_like_it.py", mapped.Explanation, StringComparison.Ordinal);
    }

    /// <summary>Build output and dependencies are not searched, so they cannot be patched.</summary>
    [Fact]
    public void VendorAndBuildFoldersAreNotIndexed()
    {
        Write("node_modules/express/basket.py", "vendored = True\n");
        Write("obj/Debug/basket.py", "generated = True\n");

        Assert.Equal(MapOutcome.NotFound, Mapper().Map("anything/basket.py").Outcome);
    }

    // ================================================================== applying

    [Fact]
    public void APatchThatMatchesIsAppliedAndTheFileChanges()
    {
        var path = Write("app/reader.py", ReaderSource);

        var applier = new PatchApplier();
        var plan = applier.Plan(Patch(SimpleDiff()), Mapper(path), [path]);

        Assert.True(plan.CanApply, plan.Explanation);

        var result = applier.Apply(plan, new BackupStore(Path.Combine(_temp.Path, "backups")), Root, dryRun: false);

        Assert.True(result.Ok, result.Failure);
        Assert.Single(result.Written);

        var after = File.ReadAllText(path);
        Assert.Contains("payload.get(\"user_id\")", after, StringComparison.Ordinal);
        Assert.DoesNotContain("payload[\"user_id\"]", after, StringComparison.Ordinal);
    }

    /// <summary>Dry run is the default, and it must genuinely write nothing.</summary>
    [Fact]
    public void ADryRunReportsTheChangeAndLeavesTheFileAlone()
    {
        var path = Write("app/reader.py", ReaderSource);
        var before = File.ReadAllBytes(path);

        var applier = new PatchApplier();
        var plan = applier.Plan(Patch(SimpleDiff()), Mapper(path), [path]);

        var result = applier.Apply(plan, new BackupStore(Path.Combine(_temp.Path, "backups")), Root);

        Assert.True(result.Ok);
        Assert.True(result.WasDryRun);
        Assert.Empty(result.Written);
        Assert.Null(result.BackupFolder);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    /// <summary>
    /// A file's own line endings survive being patched.
    /// </summary>
    /// <remarks>
    /// Diffs are normalised to newlines when parsed. Writing that back would rewrite every line
    /// of a CRLF file, turning a one-line fix into a whole-file diff for whoever reviews it next.
    /// </remarks>
    [Fact]
    public void CrLfLineEndingsArePreserved()
    {
        var path = Write("app/reader.py", ReaderSource, ending: "\r\n");

        var applier = new PatchApplier();
        var plan = applier.Plan(Patch(SimpleDiff()), Mapper(path), [path]);
        applier.Apply(plan, new BackupStore(Path.Combine(_temp.Path, "backups")), Root, dryRun: false);

        var text = File.ReadAllText(path);

        Assert.Contains("\r\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain(text.Replace("\r\n", ""), "\n", StringComparison.Ordinal);
        Assert.Contains("payload.get", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AByteOrderMarkIsPreserved()
    {
        var path = Write("app/reader.py", ReaderSource, bom: true);

        var applier = new PatchApplier();
        var plan = applier.Plan(Patch(SimpleDiff()), Mapper(path), [path]);
        applier.Apply(plan, new BackupStore(Path.Combine(_temp.Path, "backups")), Root, dryRun: false);

        var bytes = File.ReadAllBytes(path);

        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);
    }

    /// <summary>
    /// A patch may create a file, as long as it also changes one the crash named.
    /// </summary>
    /// <remarks>
    /// Creating files is a real thing a fix does - a guard clause moved into its own module, say.
    /// What is refused is a patch that <i>only</i> adds files, because that applies by definition
    /// and therefore proves nothing by fitting.
    /// </remarks>
    [Fact]
    public void ANewFileIsCreatedAlongsideAChangeToAnExistingOne()
    {
        var existing = Write("app/reader.py", ReaderSource);

        var patch = Patch(Lines(
            "diff --git a/app/guard.py b/app/guard.py",
            "new file mode 100644",
            "--- /dev/null",
            "+++ b/app/guard.py",
            "@@ -0,0 +1,2 @@",
            "+def guard(payload):",
            "+    return payload.get(\"user_id\")",
            "") + SimpleDiff());

        var applier = new PatchApplier();
        var plan = applier.Plan(patch, Mapper(existing), [existing]);

        Assert.True(plan.CanApply, plan.Explanation);

        applier.Apply(plan, new BackupStore(Path.Combine(_temp.Path, "backups")), Root, dryRun: false);

        var created = Path.Combine(Root, "app", "guard.py");
        Assert.True(File.Exists(created));
        Assert.Contains("def guard(payload):", File.ReadAllText(created), StringComparison.Ordinal);
        Assert.Contains("payload.get", File.ReadAllText(existing), StringComparison.Ordinal);
    }

    /// <summary>
    /// A patch that only adds files is refused, however cleanly it fits.
    /// </summary>
    /// <remarks>
    /// Found by running the sample programs. A bug-bounty issue scoring 30 out of 100 carried a
    /// patch adding <c>bounty_71_solution.md</c>, and FixFinder offered to write it into the
    /// source folder - because a new file has no context to match, so it "applies" by
    /// definition. Fitting is evidence only when there was something it could have failed to fit.
    /// </remarks>
    [Fact]
    public void APatchThatOnlyAddsFilesIsRefused()
    {
        var existing = Write("app/reader.py", ReaderSource);

        var patch = Patch(Lines(
            "diff --git a/notes/solution.md b/notes/solution.md",
            "new file mode 100644",
            "--- /dev/null",
            "+++ b/notes/solution.md",
            "@@ -0,0 +1,2 @@",
            "+# How I fixed it",
            "+Some prose that is not a change to any code."));

        var plan = new PatchApplier().Plan(patch, Mapper(existing), [existing]);

        Assert.False(plan.CanApply);
        // Two rules independently refuse this - the file is not one the crash named, and the
        // patch adds without changing anything - so the outcome is asserted rather than the
        // wording of whichever fired first.
        Assert.Equal(ApplyOutcome.RejectedUnrelatedToCrash, plan.Outcome);
        Assert.False(File.Exists(Path.Combine(Root, "notes", "solution.md")));
    }

    /// <summary>
    /// With no file named by the error, relevance cannot be established - so nothing is applied.
    /// </summary>
    /// <remarks>
    /// The hole the bounty patch came through. The relevance gate used to run only when the
    /// crash named at least one file; an error with no file references skipped it entirely,
    /// which turned "nothing to compare against" into "nothing to worry about".
    /// </remarks>
    [Fact]
    public void AnErrorThatNamesNoFileCannotAuthoriseAnyPatch()
    {
        var existing = Write("app/reader.py", ReaderSource);

        var plan = new PatchApplier().Plan(Patch(SimpleDiff()), Mapper(existing), stackTraceFiles: []);

        Assert.False(plan.CanApply);
        Assert.Equal(ApplyOutcome.RejectedUnrelatedToCrash, plan.Outcome);
        Assert.Contains("names no source file", plan.Explanation, StringComparison.Ordinal);
    }

    // ================================================================== refusals

    /// <summary>
    /// Zero fuzz. If the lines are not there, the patch does not apply.
    /// </summary>
    [Fact]
    public void AHunkWhoseContextIsNotInTheFileIsRefused()
    {
        var path = Write("app/reader.py", "something completely different\n");

        var applier = new PatchApplier();
        var plan = applier.Plan(Patch(SimpleDiff()), Mapper(path), [path]);

        Assert.False(plan.CanApply);
        Assert.Equal(ApplyOutcome.RejectedContextMismatch, plan.Outcome);
        Assert.Contains("are not in the file", plan.Explanation, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whitespace is part of the context, not decoration.
    /// </summary>
    /// <remarks>
    /// A whitespace-insensitive mode would make this apply, and in a language where indentation
    /// is the block structure it would apply to the wrong block.
    /// </remarks>
    [Fact]
    public void AContextLineDifferingOnlyInIndentationDoesNotMatch()
    {
        var path = Write("app/reader.py", """
            import json

              def read(payload):
                return payload["user_id"]

            print(read({}))
            """);

        var applier = new PatchApplier();
        var plan = applier.Plan(Patch(SimpleDiff()), Mapper(path), [path]);

        Assert.False(plan.CanApply);
        Assert.Equal(ApplyOutcome.RejectedContextMismatch, plan.Outcome);
    }

    /// <summary>
    /// Two equally good positions have no safe answer, so neither is chosen.
    /// </summary>
    [Fact]
    public void AHunkThatFitsInTwoPlacesIsRefusedAsAmbiguous()
    {
        var repeated = string.Join("\n", Enumerable.Repeat(Lines(
            "def read(payload):",
            "    return payload[\"user_id\"]",
            "", ""), 2)).TrimEnd();

        var path = Write("app/reader.py", repeated + "\n");

        var patch = Patch(Lines(
            "--- a/app/reader.py",
            "+++ b/app/reader.py",
            "@@ -1,2 +1,2 @@",
            " def read(payload):",
            "-    return payload[\"user_id\"]",
            "+    return payload.get(\"user_id\")"));

        var plan = new PatchApplier().Plan(patch, Mapper(path), [path]);

        Assert.False(plan.CanApply);
        Assert.Equal(ApplyOutcome.RejectedAmbiguousLocation, plan.Outcome);
        Assert.Contains("no way to know which was meant", plan.Explanation, StringComparison.Ordinal);
    }

    /// <summary>
    /// A clean patch for a different problem is still refused.
    /// </summary>
    /// <remarks>
    /// Context matching cannot tell a relevant patch from an irrelevant one that happens to fit.
    /// Requiring a touched file to appear in the crash is what supplies that judgement.
    /// </remarks>
    [Fact]
    public void APatchTouchingNoFileFromTheCrashIsRefused()
    {
        var unrelated = Write("app/reader.py", ReaderSource);
        var crashed = Write("app/other.py", "print('this is where it crashed')\n");

        var plan = new PatchApplier().Plan(Patch(SimpleDiff()), Mapper(crashed), [crashed]);

        Assert.False(plan.CanApply);
        Assert.Equal(ApplyOutcome.RejectedUnrelatedToCrash, plan.Outcome);
        Assert.Contains("no connection to the error", plan.Explanation, StringComparison.Ordinal);
        Assert.Contains(ReaderSource.ReplaceLineEndings("\n"), File.ReadAllText(unrelated), StringComparison.Ordinal);
    }

    [Fact]
    public void AReadOnlyFileIsRefused()
    {
        var path = Write("app/reader.py", ReaderSource);
        new FileInfo(path).IsReadOnly = true;

        try
        {
            var plan = new PatchApplier().Plan(Patch(SimpleDiff()), Mapper(path), [path]);

            Assert.False(plan.CanApply);
            Assert.Equal(ApplyOutcome.RejectedUnsafeFile, plan.Outcome);
            Assert.Contains("read-only", plan.Explanation, StringComparison.Ordinal);
        }
        finally
        {
            new FileInfo(path).IsReadOnly = false;
        }
    }

    [Fact]
    public void AFileWithNulBytesIsRefusedAsNotText()
    {
        var path = Path.Combine(Root, "app", "reader.py");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0x70, 0x79, 0x00, 0x01, 0x02, 0x03]);

        var plan = new PatchApplier().Plan(Patch(SimpleDiff()), Mapper(path), [path]);

        Assert.False(plan.CanApply);
        Assert.Equal(ApplyOutcome.RejectedUnsafeFile, plan.Outcome);
        Assert.Contains("NUL", plan.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void APatchThatDeletesAFileIsRefused()
    {
        var path = Write("app/reader.py", ReaderSource);

        var patch = Patch(Lines(
            "diff --git a/app/reader.py b/app/reader.py",
            "deleted file mode 100644",
            "--- a/app/reader.py",
            "+++ /dev/null",
            "@@ -1,1 +0,0 @@",
            "-import json"));

        var plan = new PatchApplier().Plan(patch, Mapper(path), [path]);

        Assert.False(plan.CanApply);
        Assert.Contains("will not do on your behalf", plan.Explanation, StringComparison.Ordinal);
        Assert.True(File.Exists(path));
    }

    /// <summary>
    /// Atomic at the patch level: one bad file means none of them is written.
    /// </summary>
    /// <remarks>
    /// A half-applied cross-file patch compiles about as often as an unpatched tree and leaves
    /// a state matching neither the original nor the fix.
    /// </remarks>
    [Fact]
    public void OneUnmatchableFileStopsTheWholePatch()
    {
        var good = Write("app/reader.py", ReaderSource);
        var bad = Write("app/other.py", "nothing the patch expects\n");
        var goodBefore = File.ReadAllText(good);

        var patch = Patch(SimpleDiff() + Lines(
            "--- a/app/other.py",
            "+++ b/app/other.py",
            "@@ -1,1 +1,1 @@",
            "-this line is not in the file",
            "+replacement"));

        var applier = new PatchApplier();
        var plan = applier.Plan(patch, Mapper(good, bad), [good]);

        Assert.False(plan.CanApply);

        var result = applier.Apply(plan, new BackupStore(Path.Combine(_temp.Path, "backups")), Root, dryRun: false);

        Assert.False(result.Ok);
        Assert.Empty(result.Written);
        Assert.Equal(goodBefore, File.ReadAllText(good));
    }

    // ================================================================== backups

    /// <summary>A backup taken before a patch restores the file byte for byte.</summary>
    [Fact]
    public void ABackupRestoresTheOriginalExactly()
    {
        var path = Write("app/reader.py", ReaderSource, ending: "\r\n", bom: true);
        var original = File.ReadAllBytes(path);

        var backups = new BackupStore(Path.Combine(_temp.Path, "backups"));
        var applier = new PatchApplier();

        var plan = applier.Plan(Patch(SimpleDiff()), Mapper(path), [path]);
        var result = applier.Apply(plan, backups, Root, dryRun: false, candidateId: "gh#1");

        Assert.True(result.Ok, result.Failure);
        Assert.NotEqual(original, File.ReadAllBytes(path));

        var restore = backups.Restore(result.BackupFolder!);

        Assert.True(restore.Ok, restore.Summary);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    /// <summary>Undoing a patch that created a file means removing it, not restoring it.</summary>
    [Fact]
    public void RollingBackAPatchThatCreatedAFileDeletesIt()
    {
        var existing = Write("app/reader.py", ReaderSource);

        var patch = Patch(Lines(
            "diff --git a/app/guard.py b/app/guard.py",
            "new file mode 100644",
            "--- /dev/null",
            "+++ b/app/guard.py",
            "@@ -0,0 +1,1 @@",
            "+created = True",
            "") + SimpleDiff());

        var backups = new BackupStore(Path.Combine(_temp.Path, "backups"));
        var applier = new PatchApplier();

        var plan = applier.Plan(patch, Mapper(existing), [existing]);
        var result = applier.Apply(plan, backups, Root, dryRun: false);

        var created = Path.Combine(Root, "app", "guard.py");
        Assert.True(File.Exists(created));

        var restore = backups.Restore(result.BackupFolder!);

        Assert.True(restore.Ok, restore.Summary);
        Assert.False(File.Exists(created), "a file the patch created must be removed, not left behind");
    }

    /// <summary>
    /// A corrupted backup is refused rather than written over working code.
    /// </summary>
    /// <remarks>
    /// Restore is what you reach for when something has already gone wrong - the worst moment to
    /// discover the safety net was itself damaged.
    /// </remarks>
    [Fact]
    public void ABackupThatFailsItsChecksumIsNotRestored()
    {
        var path = Write("app/reader.py", ReaderSource);

        var backups = new BackupStore(Path.Combine(_temp.Path, "backups"));
        var applier = new PatchApplier();

        var plan = applier.Plan(Patch(SimpleDiff()), Mapper(path), [path]);
        var result = applier.Apply(plan, backups, Root, dryRun: false);

        var patched = File.ReadAllText(path);

        // Tamper with the stored copy.
        var copy = Path.Combine(result.BackupFolder!, "app", "reader.py");
        File.WriteAllText(copy, "this is not what was backed up");

        var restore = backups.Restore(result.BackupFolder!);

        Assert.False(restore.Ok);
        Assert.Contains("checksum", restore.Summary, StringComparison.Ordinal);
        Assert.Equal(patched, File.ReadAllText(path));
    }

    [Fact]
    public void TheManifestRecordsWhichCandidateCausedTheChange()
    {
        var path = Write("app/reader.py", ReaderSource);

        var backups = new BackupStore(Path.Combine(_temp.Path, "backups"));
        var applier = new PatchApplier();

        var plan = applier.Plan(Patch(SimpleDiff()), Mapper(path), [path]);
        var result = applier.Apply(plan, backups, Root, dryRun: false,
            candidateId: "owner/repo#42", candidateTitle: "Guard the missing key",
            candidateUrl: "https://github.com/owner/repo/issues/42");

        var manifest = backups.ReadManifest(result.BackupFolder!);

        Assert.NotNull(manifest);
        Assert.Equal("owner/repo#42", manifest!.CandidateId);
        Assert.Equal("https://github.com/owner/repo/issues/42", manifest.CandidateUrl);
        Assert.Single(manifest.Files);
        Assert.True(manifest.Files[0].Existed);
    }
}
