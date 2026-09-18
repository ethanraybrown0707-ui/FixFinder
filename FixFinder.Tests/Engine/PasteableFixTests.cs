using FixFinder.Core.Engine;
using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;
using FixFinder.Core.Patching;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>What lands on the clipboard.</summary>
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

    [Fact]
    public void ACommandIsCopiedAsACommand()
    {
        var fix = PasteableFix.For(Examined(Candidate(@"""C:\py\python.exe"" -m pip install pyyaml")));

        Assert.Equal(@"""C:\py\python.exe"" -m pip install pyyaml", fix!.Text);
        Assert.Contains("install", fix.Description, StringComparison.Ordinal);
    }

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

        Assert.Contains("avarage", File.ReadAllText(script), StringComparison.Ordinal);
    }
}
