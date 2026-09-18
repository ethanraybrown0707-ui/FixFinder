using FixFinder.Core.Engine;
using FixFinder.Core.Fingerprinting;
using FixFinder.Core.Http;
using FixFinder.Core.Patching;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>The whole point of the dependency route, checked at the level the window reads.</summary>
public class DependencyPatchTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly FixFinderHttpClient _http = new();

    public void Dispose()
    {
        _http.Dispose();
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string UpstreamFix = """
        --- a/requests/adapters.py
        +++ b/requests/adapters.py
        @@ -1,3 +1,3 @@
         import os
        -raise InvalidSchema(msg)
        +raise InvalidSchema(msg) from None
         import sys
        """;

    private (SessionOutcome Outcome, string Library) Scenario(string diff)
    {
        var project = Path.Combine(_temp.Path, "project");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, "main.py"), "import requests\n");

        var package = Path.Combine(_temp.Path, "site-packages", "requests");
        Directory.CreateDirectory(package);

        var adapters = Path.Combine(package, "adapters.py");
        File.WriteAllText(adapters, "import os\nraise InvalidSchema(msg)\nimport sys\n");

        var stackFiles = new[] { adapters, Path.Combine(project, "main.py") };

        var harvest = HarvestOf(diff);

        var outcome = new SessionOutcome
        {
            Result = SessionResult.FoundFix,
            Headline = "It crashed.",
            Detail = "",
            SourceRoot = project,
            StackTraceFiles = stackFiles,
            Dependency = InstalledPackages.From(stackFiles),
            Harvest = harvest,

            Plan = new PatchPlanner().Plan(
                UnifiedDiffParser.Parse(diff), new SourcePathMapper(project, stackFiles), stackFiles),

            Best = Candidate(),
            Candidates = [Candidate()],
        };

        return (outcome, adapters);
    }

    private static FixCandidate Candidate() => new()
    {
        SourceName = "GitHub",
        Id = "gh#1",
        Title = "InvalidSchema should not chain",
        Url = "https://example.invalid/1",
        Tier = FixTier.AutoAppliable,
        Score = 80,
    };

    private static HarvestResult HarvestOf(string diff) =>
        new([], [new FetchedPatch("https://example.invalid/1.patch", UnifiedDiffParser.Parse(diff), false)], 1);

    [Fact]
    public void TheSessionOnItsOwnStillRefusesALibraryPatch()
    {
        var (outcome, _) = Scenario(UpstreamFix);

        Assert.False(outcome.CanApply);
        Assert.Equal(ApplyOutcome.RejectedPathNotFound, outcome.Plan!.Outcome);
    }

    [Fact]
    public async Task TheFirstResultOffersAPatchThatBelongsToTheLibrary()
    {
        var (outcome, _) = Scenario(UpstreamFix);

        var browser = new CandidateBrowser(_http, outcome, CacheMode.CacheOnly);
        var examined = await browser.CurrentAsync();

        Assert.True(examined.CanApply, examined.WhyNotAppliable);
        Assert.NotNull(examined.Into);
        Assert.Equal("requests", examined.Into!.Name);
    }

    [Fact]
    public async Task APatchForTheProjectIsNotDivertedIntoTheLibrary()
    {
        var (outcome, _) = Scenario(UpstreamFix);

        var project = outcome.SourceRoot!;
        File.WriteAllText(Path.Combine(project, "cart.py"), "import os\nold\nimport sys\n");

        var ours = HarvestOf("""
            --- a/cart.py
            +++ b/cart.py
            @@ -1,3 +1,3 @@
             import os
            -old
            +new
             import sys
            """);

        var files = outcome.StackTraceFiles.Append(Path.Combine(project, "cart.py")).ToList();

        var browser = new CandidateBrowser(
            _http,
            outcome with
            {
                Harvest = ours,
                StackTraceFiles = files,
                Plan = new PatchPlanner().Plan(
                    ours.Patches[0], new SourcePathMapper(project, files), files),
            },
            CacheMode.CacheOnly);

        var examined = await browser.CurrentAsync();

        Assert.True(examined.CanApply);
        Assert.Null(examined.Into);
    }

    [Fact]
    public async Task AFirstPartyCrashOffersNoDependencyAtAll()
    {
        var (outcome, _) = Scenario(UpstreamFix);

        var browser = new CandidateBrowser(
            _http, outcome with { Dependency = null }, CacheMode.CacheOnly);

        var examined = await browser.CurrentAsync();

        Assert.False(examined.CanApply);
        Assert.Null(examined.Into);
    }
}
