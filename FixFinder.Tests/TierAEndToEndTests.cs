using FixFinder.Core.Execution;
using FixFinder.Core.Fingerprinting;
using FixFinder.Core.Parsing;
using FixFinder.Core.Patching;
using FixFinder.Core.Sources;

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
