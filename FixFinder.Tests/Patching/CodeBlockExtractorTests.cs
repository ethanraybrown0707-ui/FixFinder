using FixFinder.Core.Patching;

namespace FixFinder.Tests;

/// <summary>Covers pulling code out of the prose a candidate is made of.</summary>
public class CodeBlockExtractorTests
{
    private static string Lines(params string[] lines) => string.Join("\n", lines);

    [Fact]
    public void AFencedDiffBecomesAnAppliablePatch()
    {
        var body = Lines(
            "This fixes it:",
            "",
            "```diff",
            "--- a/app.py",
            "+++ b/app.py",
            "@@ -1 +1 @@",
            "-uid = payload[\"user_id\"]",
            "+uid = payload.get(\"user_id\")",
            "```");

        var blocks = CodeBlockExtractor.Extract(body, isHtml: false);

        var block = Assert.Single(blocks);
        Assert.True(block.IsAppliablePatch);
        Assert.Equal("app.py", block.Patch!.Files[0].TargetPath);
    }

    [Fact]
    public void AFenceTaggedDiffThatIsNotOneIsReportedRatherThanAccepted()
    {
        var body = Lines(
            "```diff",
            "--- a/app.py",
            "+++ b/app.py",
            "@@ -1,9 +1,9 @@",
            "-only one line here",
            "```");

        var block = Assert.Single(CodeBlockExtractor.Extract(body, isHtml: false));

        Assert.False(block.IsAppliablePatch);
        Assert.True(block.IsFailedPatch);
        Assert.Contains("does not add up", block.Rejection!, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUntaggedFenceHoldingADiffIsStillRecognised()
    {
        var body = Lines(
            "```",
            "diff --git a/app.py b/app.py",
            "--- a/app.py",
            "+++ b/app.py",
            "@@ -1 +1 @@",
            "-a",
            "+b",
            "```");

        Assert.True(Assert.Single(CodeBlockExtractor.Extract(body, isHtml: false)).IsAppliablePatch);
    }

    [Fact]
    public void OrdinaryCodeIsKeptAsAReadableSnippet()
    {
        var body = Lines(
            "Use get instead:",
            "",
            "```python",
            "uid = payload.get(\"user_id\")",
            "```");

        var block = Assert.Single(CodeBlockExtractor.Extract(body, isHtml: false));

        Assert.False(block.IsAppliablePatch);
        Assert.False(block.IsFailedPatch);
        Assert.Equal("python", block.Language);
        Assert.Contains("payload.get", block.Code, StringComparison.Ordinal);
    }

    [Fact]
    public void SeveralBlocksInOneBodyAreAllReturned()
    {
        var body = Lines(
            "```python",
            "before()",
            "```",
            "and then",
            "```python",
            "after()",
            "```");

        var blocks = CodeBlockExtractor.Extract(body, isHtml: false);

        Assert.Equal(2, blocks.Count);
        Assert.Contains("before()", blocks[0].Code, StringComparison.Ordinal);
        Assert.Contains("after()", blocks[1].Code, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnpairedFenceIsIgnored()
    {
        var body = Lines(
            "```python",
            "this block was never closed",
            "",
            "and this is ordinary prose that follows it");

        Assert.Empty(CodeBlockExtractor.Extract(body, isHtml: false));
    }

    [Fact]
    public void ADiffPastedWithNoFenceIsStillFound()
    {
        var body = Lines(
            "Here is the change I made.",
            "",
            "diff --git a/app.py b/app.py",
            "--- a/app.py",
            "+++ b/app.py",
            "@@ -1 +1 @@",
            "-uid = payload[\"user_id\"]",
            "+uid = payload.get(\"user_id\")");

        var blocks = CodeBlockExtractor.Extract(body, isHtml: false);

        Assert.Single(blocks);
        Assert.True(blocks[0].IsAppliablePatch);
    }

    [Fact]
    public void ProseThatMerelyStartsALineWithADashIsNotReportedAsAFailedPatch()
    {
        var body = Lines(
            "A few notes:",
            "--- this is a separator, not a diff ---",
            "and nothing else here is a patch either");

        Assert.Empty(CodeBlockExtractor.Extract(body, isHtml: false));
    }

    [Fact]
    public void StackOverflowMarkupYieldsItsCodeBlocks()
    {
        var body =
            "<p>Use <code>dict.get</code>:</p>\n" +
            "<pre><code>uid = payload.get(\"user_id\")\n</code></pre>\n" +
            "<p>Or supply a default.</p>";

        var block = Assert.Single(CodeBlockExtractor.Extract(body, isHtml: true));

        Assert.Equal("uid = payload.get(\"user_id\")", block.Code);
        Assert.False(block.IsAppliablePatch);
    }

    [Fact]
    public void HtmlEntitiesAreDecodedBeforeTheDiffIsParsed()
    {
        var body =
            "<pre><code>--- a/app.py\n" +
            "+++ b/app.py\n" +
            "@@ -1 +1 @@\n" +
            "-if x &lt; 1 &amp;&amp; y:\n" +
            "+if x &lt;= 1 &amp;&amp; y:\n" +
            "</code></pre>";

        var block = Assert.Single(CodeBlockExtractor.Extract(body, isHtml: true));

        Assert.True(block.IsAppliablePatch, block.Rejection);
        Assert.Contains("if x < 1 && y:", block.Code, StringComparison.Ordinal);
    }

    [Fact]
    public void SyntaxHighlightingMarkupInsideABlockIsStripped()
    {
        var body =
            "<pre class=\"lang-py prettyprint\"><code>" +
            "<span class=\"pln\">uid </span><span class=\"pun\">=</span> payload" +
            "</code></pre>";

        var block = Assert.Single(CodeBlockExtractor.Extract(body, isHtml: true));

        Assert.Equal("uid = payload", block.Code);
        Assert.Contains("lang-py", block.Language!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Just some prose with no code in it whatsoever.")]
    public void ABodyWithNoCodeYieldsNothing(string? body) =>
        Assert.Empty(CodeBlockExtractor.Extract(body, isHtml: false));
}
