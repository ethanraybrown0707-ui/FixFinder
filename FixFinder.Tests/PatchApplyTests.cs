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

    // ================================================================== backups
}
