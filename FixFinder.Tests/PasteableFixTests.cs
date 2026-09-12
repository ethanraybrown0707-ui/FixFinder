using FixFinder.Core.Engine;
using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;
using FixFinder.Core.Patching;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>
/// What lands on the clipboard, which is now the whole product.
/// </summary>
/// <remarks>
/// The one thing every case here is really checking: <b>a diff must never reach the clipboard</b>.
/// Markers, hunk headers and removed lines are instructions to a machine, and pasting them into a
/// source file produces something that does not compile - so the fix has to arrive as the code it
/// should end up as.
/// </remarks>
public class PasteableFixTests
{
    private static readonly string? Python =
        TargetFactory.FindOnPath("python") ??
        TargetFactory.FindOnPath("py") ??
        TargetFactory.FindOnPath("python3");

    private static ExaminedCandidate Examined(FixCandidate candidate, HarvestResult? harvest = null) =>
        new(candidate, 1, 1, harvest, null);

    private static FixCandidate Candidate(string? command = null) => new()
    {
        SourceName = "test",
        Id = "x",
        Title = "t",
        Url = "",
        Command = command,
    };

    private static HarvestResult HarvestOf(string diff) =>
        new([], [new FetchedPatch("u", UnifiedDiffParser.Parse(diff), false)], 1);

    private static HarvestResult SnippetOf(string code) =>
        new([new CodeBlock("python", code)], [], 0);

    // ------------------------------------------------------------------ patches

    /// <summary>
    /// The new side of the hunk, with the markers gone and the removed line gone with them.
    /// </summary>
    [Fact]
    public void APatchBecomesTheCodeAsItShouldEndUp()
    {
        var harvest = HarvestOf("""
            --- a/cart.py
            +++ b/cart.py
            @@ -10,3 +10,3 @@
             def read_user(payload):
            -    return payload["user_id"]
            +    return payload.get("user_id")
             print("done")
            """);

        var fix = PasteableFix.For(Examined(Candidate(), harvest));

        Assert.NotNull(fix);
        Assert.Equal(
            "def read_user(payload):\n    return payload.get(\"user_id\")\nprint(\"done\")",
            fix!.Text);
    }

    /// <summary>Nothing a diff uses to mean something survives into the clipboard.</summary>
    [Fact]
    public void NoDiffMarkersReachTheClipboard()
    {
        var harvest = HarvestOf("""
            --- a/cart.py
            +++ b/cart.py
            @@ -10,3 +10,3 @@
             def read_user(payload):
            -    return payload["user_id"]
            +    return payload.get("user_id")
             print("done")
            """);

        var text = PasteableFix.For(Examined(Candidate(), harvest))!.Text;

        Assert.DoesNotContain("@@", text, StringComparison.Ordinal);
        Assert.DoesNotContain("--- a/", text, StringComparison.Ordinal);
        Assert.DoesNotContain("+++ b/", text, StringComparison.Ordinal);
        Assert.DoesNotContain("payload[\"user_id\"]", text, StringComparison.Ordinal);

        foreach (var line in text.Split('\n'))
            Assert.False(line.StartsWith('+') || line.StartsWith('-'), line);
    }

    /// <summary>Where to paste it, so the copied block replaces the right lines.</summary>
    [Fact]
    public void ASingleHunkSaysWhereItGoes()
    {
        var harvest = HarvestOf("""
            --- a/cart.py
            +++ b/cart.py
            @@ -10,3 +42,3 @@
             a
            -b
            +c
             d
            """);

        var fix = PasteableFix.For(Examined(Candidate(), harvest));

        Assert.Equal("cart.py, from line 42", fix!.Where);
    }

    /// <summary>
    /// Two hunks are not contiguous, so they are not run together.
    /// </summary>
    /// <remarks>
    /// Pasting them as one block would silently delete every line between them, which is a far
    /// worse outcome than an awkward-looking clipboard.
    /// </remarks>
    [Fact]
    public void SeparateHunksAreKeptApart()
    {
        var harvest = HarvestOf("""
            --- a/cart.py
            +++ b/cart.py
            @@ -1,2 +1,2 @@
            -one
            +ONE
             two
            @@ -40,2 +40,2 @@
            -forty
            +FORTY
             after
            """);

        var fix = PasteableFix.For(Examined(Candidate(), harvest));

        Assert.Contains("ONE", fix!.Text, StringComparison.Ordinal);
        Assert.Contains("FORTY", fix.Text, StringComparison.Ordinal);
        Assert.Contains("separate block", fix.Text, StringComparison.Ordinal);
        Assert.Contains("line 40", fix.Text, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ the other shapes

    /// <summary>A command is pasted into a terminal, so it is copied as written.</summary>
    [Fact]
    public void ACommandIsCopiedAsACommand()
    {
        var fix = PasteableFix.For(Examined(Candidate(@"""C:\py\python.exe"" -m pip install pyyaml")));

        Assert.Equal(@"""C:\py\python.exe"" -m pip install pyyaml", fix!.Text);
        Assert.Contains("install", fix.Description, StringComparison.Ordinal);
    }

    /// <summary>Prose from an answer is already what somebody wrote to be copied.</summary>
    [Fact]
    public void AnAnswersCodeBlockIsCopiedAsWritten()
    {
        var fix = PasteableFix.For(Examined(Candidate(), SnippetOf("import yaml\nyaml.safe_load(f)\n")));

        Assert.Equal("import yaml\nyaml.safe_load(f)", fix!.Text);
    }

    [Fact]
    public void NothingToCopyIsReportedAsNothing()
    {
        Assert.Null(PasteableFix.For(Examined(Candidate())));
        Assert.Null(PasteableFix.For(Examined(Candidate(), new HarvestResult([], [], 0))));
    }

    // ------------------------------------------------------------------ end to end

    /// <summary>
    /// The runtime's own correction, copied as a line you could paste over the broken one.
    /// </summary>
    /// <remarks>
    /// Driven off a real interpreter, because the claim is that Python worked the answer out -
    /// and then checked as text rather than applied, which is the whole point of the change.
    /// </remarks>
    [Fact]
    public async Task ARuntimeCorrectionIsCopiedAsTheCorrectedLine()
    {
        if (Python is null) return;

        using var temp = new TempFolder();

        var script = Path.Combine(temp.Path, "typo.py");
        File.WriteAllText(script, "average = 10\nprint(\"starting\")\nprint(avarage)\n");

        var spec = new TargetSpec
        {
            ExecutablePath = Python,
            Arguments = $"\"{script}\"",
            WorkingDirectory = temp.Path,
            Timeout = TimeSpan.FromSeconds(30),
        };

        var run = await new TargetRunner(new ParserRegistry()).RunAsync(spec, CancellationToken.None);
        var candidate = RuntimeSuggestion.For(run.Error!, temp.Path);

        Assert.NotNull(candidate);

        var harvest = await new PatchHarvester(new Core.Http.FixFinderHttpClient())
            .HarvestAsync(candidate!, Core.Http.CacheMode.CacheOnly);

        var fix = PasteableFix.For(Examined(candidate!, harvest));

        Assert.NotNull(fix);
        Assert.Contains("print(average)", fix!.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("avarage", fix.Text, StringComparison.Ordinal);

        // And the file is untouched, because nothing applies any more.
        Assert.Contains("avarage", File.ReadAllText(script), StringComparison.Ordinal);
    }
}
