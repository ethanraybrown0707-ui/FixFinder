using FixFinder.Core.Patching;

namespace FixFinder.Tests;

/// <summary>Covers the diff parser, which is the last thing standing between a stranger's issue comment and your source tree.</summary>
public class UnifiedDiffParserTests
{
    private static string Lines(params string[] lines) => string.Join("\n", lines);

    [Fact]
    public void ParsesASingleFileGitDiff()
    {
        var patch = UnifiedDiffParser.Parse(Lines(
            "diff --git a/src/cart.py b/src/cart.py",
            "index 83db48f..bf269f4 100644",
            "--- a/src/cart.py",
            "+++ b/src/cart.py",
            "@@ -10,4 +10,4 @@ def total(items):",
            "     subtotal = 0",
            "     for item in items:",
            "-        subtotal += item[\"price\"]",
            "+        subtotal += item.get(\"price\", 0)",
            "     return subtotal"));

        Assert.True(patch.Ok, patch.Rejection);
        var file = Assert.Single(patch.Files);

        Assert.Equal("src/cart.py", file.NewPath);
        Assert.Equal("src/cart.py", file.OldPath);
        Assert.False(file.IsRename);

        var hunk = Assert.Single(file.Hunks);
        Assert.Equal(10, hunk.OldStart);
        Assert.Equal(4, hunk.OldCount);
        Assert.Equal("def total(items):", hunk.Heading);
        Assert.Equal(1, hunk.AddedCount);
        Assert.Equal(1, hunk.RemovedCount);
    }

    [Fact]
    public void ParsesAMultiFileGitPatch()
    {
        var patch = UnifiedDiffParser.Parse(Lines(
            "From 9f1b1c2 Mon Sep 17 00:00:00 2001",
            "Subject: [PATCH] guard against a missing key",
            "",
            "diff --git a/app/reader.py b/app/reader.py",
            "--- a/app/reader.py",
            "+++ b/app/reader.py",
            "@@ -1,3 +1,3 @@",
            " import json",
            "-uid = payload[\"user_id\"]",
            "+uid = payload.get(\"user_id\")",
            " print(uid)",
            "diff --git a/tests/test_reader.py b/tests/test_reader.py",
            "--- a/tests/test_reader.py",
            "+++ b/tests/test_reader.py",
            "@@ -5,2 +5,3 @@",
            " def test_missing():",
            "+    assert read({}) is None",
            "     pass"));

        Assert.True(patch.Ok, patch.Rejection);
        Assert.Equal(2, patch.Files.Count);
        Assert.Equal("app/reader.py", patch.Files[0].NewPath);
        Assert.Equal("tests/test_reader.py", patch.Files[1].NewPath);
        Assert.Equal(2, patch.TotalHunks);
    }

    [Fact]
    public void AnOmittedHunkCountMeansExactlyOneLine()
    {
        var patch = UnifiedDiffParser.Parse(Lines(
            "--- a/version.txt",
            "+++ b/version.txt",
            "@@ -1 +1 @@",
            "-1.0.0",
            "+1.0.1"));

        Assert.True(patch.Ok, patch.Rejection);

        var hunk = patch.Files[0].Hunks[0];
        Assert.Equal(1, hunk.OldCount);
        Assert.Equal(1, hunk.NewCount);
    }

    [Fact]
    public void ParsesANewFileAgainstDevNull()
    {
        var patch = UnifiedDiffParser.Parse(Lines(
            "diff --git a/app/guard.py b/app/guard.py",
            "new file mode 100644",
            "--- /dev/null",
            "+++ b/app/guard.py",
            "@@ -0,0 +1,2 @@",
            "+def guard(payload):",
            "+    return payload.get(\"user_id\")"));

        Assert.True(patch.Ok, patch.Rejection);

        var file = patch.Files[0];
        Assert.True(file.IsNewFile);
        Assert.Null(file.OldPath);
        Assert.Equal("app/guard.py", file.NewPath);
        Assert.Equal("app/guard.py", file.TargetPath);
    }

    [Fact]
    public void ParsesADeletion()
    {
        var patch = UnifiedDiffParser.Parse(Lines(
            "diff --git a/old.py b/old.py",
            "deleted file mode 100644",
            "--- a/old.py",
            "+++ /dev/null",
            "@@ -1,2 +0,0 @@",
            "-print('gone')",
            "-print('also gone')"));

        Assert.True(patch.Ok, patch.Rejection);
        Assert.True(patch.Files[0].IsDeletion);
        Assert.Equal("old.py", patch.Files[0].OldPath);
    }

    [Fact]
    public void ParsesARename()
    {
        var patch = UnifiedDiffParser.Parse(Lines(
            "diff --git a/src/old_name.py b/src/new_name.py",
            "similarity index 96%",
            "rename from src/old_name.py",
            "rename to src/new_name.py",
            "--- a/src/old_name.py",
            "+++ b/src/new_name.py",
            "@@ -1,2 +1,2 @@",
            " import os",
            "-x = 1",
            "+x = 2"));

        Assert.True(patch.Ok, patch.Rejection);

        var file = patch.Files[0];
        Assert.True(file.IsRename);
        Assert.Equal("src/old_name.py", file.OldPath);
        Assert.Equal("src/new_name.py", file.NewPath);
    }

    [Fact]
    public void NoNewlineAtEndOfFileAttachesToThePrecedingLine()
    {
        var patch = UnifiedDiffParser.Parse(Lines(
            "--- a/trailing.txt",
            "+++ b/trailing.txt",
            "@@ -1,2 +1,2 @@",
            " first",
            "-second",
            @"\ No newline at end of file",
            "+second line",
            @"\ No newline at end of file"));

        Assert.True(patch.Ok, patch.Rejection);

        var lines = patch.Files[0].Hunks[0].Lines;

        Assert.False(lines[0].NoNewlineAtEnd);
        Assert.True(lines[1].NoNewlineAtEnd);
        Assert.Equal(DiffLineKind.Removed, lines[1].Kind);
        Assert.True(lines[2].NoNewlineAtEnd);
        Assert.Equal(DiffLineKind.Added, lines[2].Kind);
    }

    [Fact]
    public void EmptyContextLinesStrippedByMarkdownAreStillContext()
    {
        var mangled = Lines(
            "--- a/app.py",
            "+++ b/app.py",
            "@@ -1,5 +1,5 @@",
            " import os",
            "",
            "-x = 1",
            "+x = 2",
            "",
            " print(x)");

        var patch = UnifiedDiffParser.Parse(mangled);

        Assert.True(patch.Ok, patch.Rejection);

        var hunk = patch.Files[0].Hunks[0];
        Assert.Equal(5, hunk.OldCount);
        Assert.Equal(5, hunk.OldSide.Count);
        Assert.Equal(5, hunk.NewSide.Count);
        Assert.Equal(2, hunk.Lines.Count(l => l.Kind == DiffLineKind.Context && l.Text.Length == 0));
    }

    [Fact]
    public void CarriageReturnsAreNormalisedAway()
    {
        var patch = UnifiedDiffParser.Parse(
            "--- a/x.txt\r\n+++ b/x.txt\r\n@@ -1,1 +1,1 @@\r\n-old\r\n+new\r\n");

        Assert.True(patch.Ok, patch.Rejection);

        var lines = patch.Files[0].Hunks[0].Lines;
        Assert.Equal("old", lines[0].Text);
        Assert.Equal("new", lines[1].Text);
    }

    [Fact]
    public void ATimestampAfterAPathIsIgnored()
    {
        var patch = UnifiedDiffParser.Parse(Lines(
            "--- a/cart.py\t2026-01-04 10:11:12.000000000 +0000",
            "+++ b/cart.py\t2026-01-05 09:00:00.000000000 +0000",
            "@@ -1 +1 @@",
            "-a",
            "+b"));

        Assert.True(patch.Ok, patch.Rejection);
        Assert.Equal("cart.py", patch.Files[0].NewPath);
    }

    [Fact]
    public void AHunkWhoseCountsDoNotAddUpIsRefused()
    {
        var patch = UnifiedDiffParser.Parse(Lines(
            "--- a/cart.py",
            "+++ b/cart.py",
            "@@ -1,6 +1,6 @@",
            " one",
            "-two",
            "+TWO"));

        Assert.False(patch.Ok);
        Assert.Contains("does not add up", patch.Rejection!, StringComparison.Ordinal);
    }

    [Fact]
    public void ABinaryPatchIsRefused()
    {
        var patch = UnifiedDiffParser.Parse(Lines(
            "diff --git a/logo.png b/logo.png",
            "index 1234567..89abcde 100644",
            "GIT binary patch",
            "delta 42",
            "zcmZo@V0d;1"));

        Assert.False(patch.Ok);
        Assert.Contains("binary", patch.Rejection!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ABinaryFilesDifferLineIsAlsoRefused()
    {
        var patch = UnifiedDiffParser.Parse(Lines(
            "diff --git a/logo.png b/logo.png",
            "Binary files a/logo.png and b/logo.png differ"));

        Assert.False(patch.Ok);
        Assert.Contains("binary", patch.Rejection!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("This is just prose about a bug, with no diff in it at all.")]
    [InlineData("```python\nprint('hello')\n```")]
    public void TextThatIsNotADiffIsRefused(string text) =>
        Assert.False(UnifiedDiffParser.Parse(text).Ok);

    [Fact]
    public void AFileHeaderWithNoHunksIsRefused()
    {
        var patch = UnifiedDiffParser.Parse(Lines(
            "diff --git a/cart.py b/cart.py",
            "index 83db48f..bf269f4 100644"));

        Assert.False(patch.Ok);
        Assert.Contains("changes nothing", patch.Rejection!, StringComparison.Ordinal);
    }

    [Fact]
    public void ATraversalPathSurvivesParsingUnchangedForTheContainmentCheck()
    {
        var patch = UnifiedDiffParser.Parse(Lines(
            "--- a/../../../../Windows/System32/drivers/etc/hosts",
            "+++ b/../../../../Windows/System32/drivers/etc/hosts",
            "@@ -1 +1 @@",
            "-127.0.0.1 localhost",
            "+127.0.0.1 evil.test"));

        Assert.True(patch.Ok, "the parser reads it; the containment check is what refuses it");
        Assert.Equal("../../../../Windows/System32/drivers/etc/hosts", patch.Files[0].TargetPath);
        Assert.DoesNotContain(":", patch.Files[0].TargetPath!, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAbsurdlyLargeDiffIsRefusedRatherThanRead()
    {
        var huge = Lines("--- a/x", "+++ b/x", "@@ -1,60000 +1,60000 @@") +
                   "\n" + string.Join("\n", Enumerable.Repeat(" line", 60_000));

        var patch = UnifiedDiffParser.Parse(huge);

        Assert.False(patch.Ok);
        Assert.Contains("far past", patch.Rejection!, StringComparison.Ordinal);
    }

    [Fact]
    public void TheOldSideIsContextAndRemovalsInOrder()
    {
        var patch = UnifiedDiffParser.Parse(Lines(
            "--- a/x.py",
            "+++ b/x.py",
            "@@ -1,3 +1,3 @@",
            " keep",
            "-drop",
            "+add",
            " tail"));

        var hunk = patch.Files[0].Hunks[0];

        Assert.Equal(["keep", "drop", "tail"], hunk.OldSide.Select(l => l.Text));
        Assert.Equal(["keep", "add", "tail"], hunk.NewSide.Select(l => l.Text));
    }
}
