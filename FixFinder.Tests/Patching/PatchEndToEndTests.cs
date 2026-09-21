using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;
using FixFinder.Core.Patching;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>A real crash, fingerprinted and matched to a patch that fits the program, and an answer that is only advice.</summary>
public class PatchEndToEndTests : IDisposable
{
    private readonly TempFolder _temp = new();

    private static readonly string? Python = FindPython();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private static string? FindPython() =>
        TargetFactory.FindOnPath("python") ??
        TargetFactory.FindOnPath("py") ??
        TargetFactory.FindOnPath("python3");

    private const string Broken = """
        import json

        def read_user(payload):
            return payload["user_id"]

        print("starting")
        print(read_user(json.loads("{}")))
        """;

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

    [Fact]
    public void TheShippedFixablePythonFixtureStillApplies()
    {
        var folder = FindTestTarget("FixablePython");
        if (folder is null) return;

        var script = Path.Combine(folder, "reader.py");
        var patchText = File.ReadAllText(Path.Combine(folder, "fix.patch"));

        var patch = UnifiedDiffParser.Parse(patchText);
        Assert.True(patch.Ok, patch.Rejection);

        var working = Path.Combine(_temp.Path, "reader.py");
        File.Copy(script, working, overwrite: true);

        var plan = new PatchPlanner().Plan(
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
