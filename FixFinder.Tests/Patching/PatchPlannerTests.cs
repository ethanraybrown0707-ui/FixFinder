using System.Text;
using FixFinder.Core.Patching;

namespace FixFinder.Tests;

/// <summary>Path mapping, containment, and where a patch would land in a real tree on disk.</summary>
public class PatchPlannerTests : IDisposable
{
    private readonly TempFolder _temp = new();

    private string Root => Path.Combine(_temp.Path, "tree");

    public PatchPlannerTests() => Directory.CreateDirectory(Root);

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private static string Lines(params string[] lines) => string.Join("\n", lines);

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

        var plan = new PatchPlanner().Plan(patch, Mapper());

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

    [Fact]
    public void AFolderWhoseNameMerelySharesAPrefixWithTheRootIsOutside()
    {
        var sibling = Path.Combine(_temp.Path, "tree-secrets");
        Directory.CreateDirectory(sibling);
        File.WriteAllText(Path.Combine(sibling, "config.env"), "SECRET=1");

        Assert.False(Mapper().IsInsideRoot(Path.Combine(sibling, "config.env")));
    }

    [Fact]
    public void APathThatMatchesExactlyIsMapped()
    {
        var expected = Write("app/reader.py", ReaderSource);
        var mapped = Mapper().Map("app/reader.py");

        Assert.Equal(MapOutcome.Mapped, mapped.Outcome);
        Assert.Equal(expected, mapped.FullPath);
    }

    [Fact]
    public void AShorterSuffixIsMatchedWhenTheFullPathDoesNot()
    {
        var expected = Write("app/cart/basket.py", "x = 1\n");
        var mapped = Mapper().Map("src/cart/basket.py");

        Assert.Equal(MapOutcome.Mapped, mapped.Outcome);
        Assert.Equal(expected, mapped.FullPath);
        Assert.Contains("cart/basket.py", mapped.Explanation, StringComparison.Ordinal);
    }

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

    [Fact]
    public void VendorAndBuildFoldersAreNotIndexed()
    {
        Write("node_modules/express/basket.py", "vendored = True\n");
        Write("obj/Debug/basket.py", "generated = True\n");

        Assert.Equal(MapOutcome.NotFound, Mapper().Map("anything/basket.py").Outcome);
    }

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

        var plan = new PatchPlanner().Plan(patch, Mapper(existing), [existing]);

        Assert.False(plan.CanApply);
        Assert.Equal(ApplyOutcome.RejectedUnrelatedToCrash, plan.Outcome);
        Assert.False(File.Exists(Path.Combine(Root, "notes", "solution.md")));
    }

    [Fact]
    public void AnErrorThatNamesNoFileCannotAuthoriseAnyPatch()
    {
        var existing = Write("app/reader.py", ReaderSource);

        var plan = new PatchPlanner().Plan(Patch(SimpleDiff()), Mapper(existing), stackTraceFiles: []);

        Assert.False(plan.CanApply);
        Assert.Equal(ApplyOutcome.RejectedUnrelatedToCrash, plan.Outcome);
        Assert.Contains("names no source file", plan.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void AHunkWhoseContextIsNotInTheFileIsRefused()
    {
        var path = Write("app/reader.py", "something completely different\n");

        var applier = new PatchPlanner();
        var plan = applier.Plan(Patch(SimpleDiff()), Mapper(path), [path]);

        Assert.False(plan.CanApply);
        Assert.Equal(ApplyOutcome.RejectedContextMismatch, plan.Outcome);
        Assert.Contains("are not in the file", plan.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void AContextLineDifferingOnlyInIndentationDoesNotMatch()
    {
        var path = Write("app/reader.py", """
            import json

              def read(payload):
                return payload["user_id"]

            print(read({}))
            """);

        var applier = new PatchPlanner();
        var plan = applier.Plan(Patch(SimpleDiff()), Mapper(path), [path]);

        Assert.False(plan.CanApply);
        Assert.Equal(ApplyOutcome.RejectedContextMismatch, plan.Outcome);
    }

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

        var plan = new PatchPlanner().Plan(patch, Mapper(path), [path]);

        Assert.False(plan.CanApply);
        Assert.Equal(ApplyOutcome.RejectedAmbiguousLocation, plan.Outcome);
        Assert.Contains("no way to know which was meant", plan.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void APatchTouchingNoFileFromTheCrashIsRefused()
    {
        var unrelated = Write("app/reader.py", ReaderSource);
        var crashed = Write("app/other.py", "print('this is where it crashed')\n");

        var plan = new PatchPlanner().Plan(Patch(SimpleDiff()), Mapper(crashed), [crashed]);

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
            var plan = new PatchPlanner().Plan(Patch(SimpleDiff()), Mapper(path), [path]);

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

        var plan = new PatchPlanner().Plan(Patch(SimpleDiff()), Mapper(path), [path]);

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

        var plan = new PatchPlanner().Plan(patch, Mapper(path), [path]);

        Assert.False(plan.CanApply);
        Assert.Contains("will not do on your behalf", plan.Explanation, StringComparison.Ordinal);
        Assert.True(File.Exists(path));
    }
}
