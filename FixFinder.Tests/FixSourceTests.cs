using FixFinder.Core.Fingerprinting;
using FixFinder.Core.Http;
using FixFinder.Core.Parsing;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>
/// Covers both fix sources against recorded responses, never the network.
/// </summary>
/// <remarks>
/// Every request goes through <see cref="PlaybackHttpMessageHandler"/>, which throws on a URL it
/// has no recording for. That is what makes these assertions worth anything: a change to how a
/// query is built shows up here as a loud failure naming both URLs, rather than as a live search
/// that quietly returns different results and passes anyway.
/// </remarks>
public class FixSourceTests
{
    /// <summary>Fingerprints a captured crash the same way a real run would.</summary>
    private static ErrorFingerprint FingerprintOf(string fixture)
    {
        var parsed = new ParserRegistry().Parse(Fixtures.LoadStackTrace(fixture), []);
        Assert.NotNull(parsed);

        return FingerprintBuilder.Build(parsed!);
    }

    /// <summary>
    /// A fingerprint with a made-up type, for exercising a wrapper field rather than a crash.
    /// </summary>
    private static ErrorFingerprint Probe(string exceptionType) =>
        FingerprintBuilder.Build(new ParsedError
        {
            LanguageId = "csharp",
            Confidence = 90,
            RawText = exceptionType,
            FirstLineSequence = 1,
            ExceptionType = exceptionType,
            Frames = [],
        });

    // ================================================================== Stack Overflow

    [Fact]
    public async Task StackOverflowReturnsOneCandidatePerQuestion()
    {
        using var folder = new TempFolder();
        var handler = new PlaybackHttpMessageHandler("stackoverflow");
        using var http = handler.CreateClient(folder.Path);

        var result = await new StackOverflowFixSource(http)
            .SearchAsync(FingerprintOf("python/keyerror.txt"), SearchBudget.Default, default);

        Assert.True(result.Ok);
        Assert.Equal(3, result.Candidates.Count);
        Assert.All(result.Candidates, c => Assert.Equal("Stack Overflow", c.SourceName));
    }

    /// <summary>
    /// One search and one batched answers call, however many questions came back.
    /// </summary>
    /// <remarks>
    /// Worth asserting as a number rather than trusting the code to stay batched. The unkeyed
    /// allowance is three hundred requests a day; fetching answers per question would turn every
    /// search into eleven requests, and nothing about the results would look wrong.
    /// </remarks>
    [Fact]
    public async Task StackOverflowSpendsExactlyTwoRequestsForThreeQuestions()
    {
        using var folder = new TempFolder();
        var handler = new PlaybackHttpMessageHandler("stackoverflow");
        using var http = handler.CreateClient(folder.Path);

        var result = await new StackOverflowFixSource(http)
            .SearchAsync(FingerprintOf("python/keyerror.txt"), SearchBudget.Default, default);

        Assert.Equal(2, handler.RequestCount);
        Assert.Equal(2, result.RequestsMade);
    }

    /// <summary>Repeating a search must cost nothing, which is what CacheOnly relies on.</summary>
    [Fact]
    public async Task RepeatingTheSameSearchMakesNoFurtherRequests()
    {
        using var folder = new TempFolder();
        var handler = new PlaybackHttpMessageHandler("stackoverflow");
        using var http = handler.CreateClient(folder.Path);
        var source = new StackOverflowFixSource(http);
        var fingerprint = FingerprintOf("python/keyerror.txt");

        await source.SearchAsync(fingerprint, SearchBudget.Default, default);
        var second = await source.SearchAsync(fingerprint, SearchBudget.Default, default);

        Assert.Equal(2, handler.RequestCount);
        Assert.Equal(0, second.RequestsMade);
        Assert.Equal(3, second.Candidates.Count);
    }

    /// <summary>
    /// The accepted answer wins even when another answer has four times the votes.
    /// </summary>
    /// <remarks>
    /// The opposite of what raw popularity suggests, and right: the person who actually had this
    /// problem said this was the answer that fixed it.
    /// </remarks>
    [Fact]
    public async Task TheAcceptedAnswerIsPreferredOverAHigherVotedOne()
    {
        using var folder = new TempFolder();
        var handler = new PlaybackHttpMessageHandler("stackoverflow");
        using var http = handler.CreateClient(folder.Path);

        var result = await new StackOverflowFixSource(http)
            .SearchAsync(FingerprintOf("python/keyerror.txt"), SearchBudget.Default, default);

        var first = result.Candidates[0];

        Assert.Equal(120, first.Votes);
        Assert.Contains("Alex Martelli", first.Attribution!, StringComparison.Ordinal);
        Assert.Contains("dict.get", first.BodyText, StringComparison.Ordinal);
    }

    /// <summary>Required by the API terms wherever an answer body is rendered.</summary>
    [Fact]
    public async Task EveryStackOverflowCandidateCarriesItsCcBySaAttribution()
    {
        using var folder = new TempFolder();
        var handler = new PlaybackHttpMessageHandler("stackoverflow");
        using var http = handler.CreateClient(folder.Path);

        var result = await new StackOverflowFixSource(http)
            .SearchAsync(FingerprintOf("python/keyerror.txt"), SearchBudget.Default, default);

        Assert.All(result.Candidates, candidate =>
        {
            Assert.NotNull(candidate.Attribution);
            Assert.Contains("CC BY-SA", candidate.Attribution!, StringComparison.Ordinal);
            Assert.Contains("stackoverflow.com", candidate.Attribution!, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task StackOverflowIsNeverAutoAppliable()
    {
        using var folder = new TempFolder();
        var handler = new PlaybackHttpMessageHandler("stackoverflow");
        using var http = handler.CreateClient(folder.Path);

        var result = await new StackOverflowFixSource(http)
            .SearchAsync(FingerprintOf("python/keyerror.txt"), SearchBudget.Default, default);

        Assert.All(result.Candidates, c => Assert.Equal(FixTier.Advisory, c.Tier));
    }

    [Fact]
    public async Task HtmlEntitiesInATitleAreDecoded()
    {
        using var folder = new TempFolder();
        var handler = new PlaybackHttpMessageHandler("stackoverflow");
        using var http = handler.CreateClient(folder.Path);

        var result = await new StackOverflowFixSource(http)
            .SearchAsync(FingerprintOf("python/keyerror.txt"), SearchBudget.Default, default);

        Assert.Contains("'KeyError: 'user_id''", result.Candidates[0].Title, StringComparison.Ordinal);
        Assert.DoesNotContain("&#39;", result.Candidates[0].Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADuplicateQuestionIsMarkedAsOne()
    {
        using var folder = new TempFolder();
        var handler = new PlaybackHttpMessageHandler("stackoverflow");
        using var http = handler.CreateClient(folder.Path);

        var result = await new StackOverflowFixSource(http)
            .SearchAsync(FingerprintOf("python/keyerror.txt"), SearchBudget.Default, default);

        var duplicate = result.Candidates.Single(c => c.Id == "SO 4410999");

        Assert.NotNull(duplicate.DuplicateOfUrl);
        Assert.Equal("duplicate", duplicate.StateLabel);
    }

    /// <summary>
    /// The relaxed query exists for exactly this: a tight query that matches nothing.
    /// </summary>
    [Fact]
    public async Task TheRelaxedQueryIsTriedWhenTheTightOneFindsNothing()
    {
        using var folder = new TempFolder();
        var handler = new PlaybackHttpMessageHandler("stackoverflow");
        using var http = handler.CreateClient(folder.Path);

        var result = await new StackOverflowFixSource(http)
            .SearchAsync(FingerprintOf("csharp/inner-exception.txt"), SearchBudget.Default, default);

        Assert.Equal(2, result.QueriesTried.Count);
        Assert.StartsWith("\"System.NullReferenceException\"", result.QueriesTried[0], StringComparison.Ordinal);
        Assert.StartsWith("NullReferenceException", result.QueriesTried[1], StringComparison.Ordinal);
        Assert.Single(result.Candidates);
        Assert.Contains("NullReferenceException", result.Candidates[0].Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASearchBudgetOfOneCallSuppressesTheRelaxedRetry()
    {
        using var folder = new TempFolder();
        var handler = new PlaybackHttpMessageHandler("stackoverflow");
        using var http = handler.CreateClient(folder.Path);

        var result = await new StackOverflowFixSource(http).SearchAsync(
            FingerprintOf("csharp/inner-exception.txt"),
            SearchBudget.Default with { MaxSearchCalls = 1 },
            default);

        Assert.Single(result.QueriesTried);
        Assert.Empty(result.Candidates);
    }

    /// <summary>
    /// The backoff arrives in the body, so only the source can see it and hand it to the limiter.
    /// </summary>
    /// <remarks>
    /// Ignoring it does not produce an error - it produces a temporary ban some minutes later,
    /// which is why this is worth a test of its own.
    /// </remarks>
    [Fact]
    public async Task ABackoffInTheResponseBodyReachesTheRateLimiter()
    {
        using var folder = new TempFolder();
        var handler = new PlaybackHttpMessageHandler("stackoverflow");
        using var http = handler.CreateClient(folder.Path);

        Assert.Equal(TimeSpan.Zero, http.Limiter.PeekWait(StackOverflowFixSource.Bucket));

        await new StackOverflowFixSource(http).SearchAsync(
            Probe("BackoffProbe"), SearchBudget.Default with { MaxSearchCalls = 1 }, default);

        var pending = http.Limiter.PeekWait(StackOverflowFixSource.Bucket);

        Assert.True(pending > TimeSpan.FromSeconds(8), $"expected roughly 10s of backoff, got {pending}");
    }

    [Fact]
    public async Task TheApiErrorMessageIsReportedRatherThanTheHttpStatus()
    {
        using var folder = new TempFolder();
        var handler = new PlaybackHttpMessageHandler("stackoverflow");
        using var http = handler.CreateClient(folder.Path);

        var result = await new StackOverflowFixSource(http)
            .SearchAsync(Probe("ThrottleProbe"), SearchBudget.Default, default);

        Assert.False(result.Ok);
        Assert.Contains("too many requests", result.Failure!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("throttle_violation", result.Failure!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDailyQuotaIsReadFromTheResponseWrapper()
    {
        using var folder = new TempFolder();
        var handler = new PlaybackHttpMessageHandler("stackoverflow");
        using var http = handler.CreateClient(folder.Path);
        var source = new StackOverflowFixSource(http);

        await source.SearchAsync(FingerprintOf("python/keyerror.txt"), SearchBudget.Default, default);

        Assert.NotNull(source.Quota);
        Assert.Equal(300, source.Quota!.Limit);
        Assert.Equal(296, source.Quota.Remaining);
    }

    // ================================================================== GitHub

    /// <summary>
    /// The module in the stack trace narrows the search to that library's own tracker.
    /// </summary>
    /// <remarks>
    /// The single highest-value thing GitHub search can do here. Unconfined, "TypeError cannot
    /// read properties of undefined" returns thousands of issues from every JavaScript project
    /// on the site; confined to express, it returns the people who hit this bug in this code.
    /// </remarks>
    [Fact]
    public async Task TheSearchIsConfinedToTheRepositoryOwningTheCrashingModule()
    {
        using var folder = new TempFolder();
        var handler = new PlaybackHttpMessageHandler("github");
        using var http = handler.CreateClient(folder.Path);

        var result = await new GitHubFixSource(http)
            .SearchAsync(FingerprintOf("node/typeerror.txt"), SearchBudget.Default, default);

        Assert.True(result.Ok);
        Assert.Contains("repo:expressjs/express", result.QueriesTried[0], StringComparison.Ordinal);
        Assert.Equal(2, result.Candidates.Count);
        Assert.Equal("expressjs/express#4671", result.Candidates[0].Id);
    }

    /// <summary>GitHub made advanced search the default in September 2025; we pin it explicitly.</summary>
    [Fact]
    public async Task TheSearchRequestAsksForAdvancedSearchExplicitly()
    {
        using var folder = new TempFolder();
        var handler = new PlaybackHttpMessageHandler("github");
        using var http = handler.CreateClient(folder.Path);

        await new GitHubFixSource(http)
            .SearchAsync(FingerprintOf("node/typeerror.txt"), SearchBudget.Default, default);

        Assert.Contains(handler.Requested, url => url.Contains("advanced_search=true", StringComparison.Ordinal));
    }

    /// <summary>
    /// The timeline is the only place a closing commit is recorded, and it is where Tier A comes from.
    /// </summary>
    [Fact]
    public async Task LinkedCommitsAndPullRequestsAreHarvestedFromTheTimeline()
    {
        using var folder = new TempFolder();
        var handler = new PlaybackHttpMessageHandler("github");
        using var http = handler.CreateClient(folder.Path);

        var result = await new GitHubFixSource(http)
            .SearchAsync(FingerprintOf("node/typeerror.txt"), SearchBudget.Default, default);

        var links = result.Candidates[0].LinkedPatchUrls;

        Assert.Contains(
            "https://github.com/expressjs/express/commit/9f1b1c2d3e4f5a6b7c8d9e0f1a2b3c4d5e6f7a8b.patch",
            links);

        Assert.Contains("https://github.com/expressjs/express/pull/4680.diff", links);

        // The cross-referenced plain issue is not a pull request, so it carries no diff.
        Assert.DoesNotContain(links, url => url.Contains("/issues/4690", StringComparison.Ordinal));
    }

    /// <summary>
    /// An issue's comment count must not be described as answers.
    /// </summary>
    /// <remarks>
    /// Found on the first live search: a tracking issue with 998 comments rendered as
    /// "998 answers", which reads like a thread full of solutions rather than a busy backlog.
    /// </remarks>
    [Fact]
    public async Task AGitHubIssueCountsCommentsAndAStackOverflowQuestionCountsAnswers()
    {
        using var githubFolder = new TempFolder();
        var githubHandler = new PlaybackHttpMessageHandler("github");
        using var githubHttp = githubHandler.CreateClient(githubFolder.Path);

        var issues = await new GitHubFixSource(githubHttp)
            .SearchAsync(FingerprintOf("node/typeerror.txt"), SearchBudget.Default, default);

        Assert.Equal("2 comments", issues.Candidates[1].StateLabel);

        using var soFolder = new TempFolder();
        var soHandler = new PlaybackHttpMessageHandler("stackoverflow");
        using var soHttp = soHandler.CreateClient(soFolder.Path);

        var questions = await new StackOverflowFixSource(soHttp)
            .SearchAsync(FingerprintOf("python/keyerror.txt"), SearchBudget.Default, default);

        Assert.Equal("1 answers", questions.Candidates.Single(c => c.Id == "SO 4410101").StateLabel);
    }

    [Fact]
    public async Task AnIssueWithNothingLinkedKeepsAnEmptyPatchList()
    {
        using var folder = new TempFolder();
        var handler = new PlaybackHttpMessageHandler("github");
        using var http = handler.CreateClient(folder.Path);

        var result = await new GitHubFixSource(http)
            .SearchAsync(FingerprintOf("node/typeerror.txt"), SearchBudget.Default, default);

        Assert.Empty(result.Candidates[1].LinkedPatchUrls);
    }

    /// <summary>
    /// Following a timeline costs a request from the hourly pool, so the budget must cap it.
    /// </summary>
    [Fact]
    public async Task TheDetailBudgetLimitsHowManyTimelinesAreFollowed()
    {
        using var folder = new TempFolder();
        var handler = new PlaybackHttpMessageHandler("github");
        using var http = handler.CreateClient(folder.Path);

        var result = await new GitHubFixSource(http).SearchAsync(
            FingerprintOf("node/typeerror.txt"),
            SearchBudget.Default with { MaxDetailCalls = 1 },
            default);

        // One search plus exactly one timeline.
        Assert.Equal(2, handler.RequestCount);
        Assert.NotEmpty(result.Candidates[0].LinkedPatchUrls);
        Assert.Empty(result.Candidates[1].LinkedPatchUrls);
    }

    [Fact]
    public async Task TheSearchAllowanceIsReadFromTheRateLimitHeaders()
    {
        using var folder = new TempFolder();
        var handler = new PlaybackHttpMessageHandler("github");
        using var http = handler.CreateClient(folder.Path);
        var source = new GitHubFixSource(http);

        await source.SearchAsync(FingerprintOf("node/typeerror.txt"), SearchBudget.Default, default);

        Assert.NotNull(source.Quota);
        Assert.Equal(7, source.Quota!.Remaining);
        Assert.Equal(10, source.Quota.Limit);
    }

    [Fact]
    public async Task AnUnknownModuleSearchesWithoutARepositoryFilter()
    {
        using var folder = new TempFolder();
        var handler = new PlaybackHttpMessageHandler("github");
        using var http = handler.CreateClient(folder.Path);

        var result = await new GitHubFixSource(http)
            .SearchAsync(FingerprintOf("python/keyerror.txt"), SearchBudget.Default, default);

        Assert.True(result.Ok);
        Assert.DoesNotContain("repo:", result.QueriesTried[0], StringComparison.Ordinal);
        Assert.Empty(result.Candidates);
    }

    // ================================================================== registry

    /// <summary>
    /// One source failing must not discard what the other found.
    /// </summary>
    /// <remarks>
    /// The realistic case: GitHub's sixty-an-hour pool runs out mid-session while Stack Overflow
    /// still has two hundred and ninety requests left. Losing a good set of answers to that
    /// would be an entirely self-inflicted failure.
    /// </remarks>
    [Fact]
    public async Task AFailingSourceDoesNotTakeTheOtherOneDownWithIt()
    {
        using var folder = new TempFolder();
        var handler = new PlaybackHttpMessageHandler("stackoverflow");
        using var http = handler.CreateClient(folder.Path);

        var registry = new FixSourceRegistry();
        registry.Add(new StackOverflowFixSource(http));
        registry.Add(new ThrowingSource());

        var result = await registry.SearchAsync(FingerprintOf("python/keyerror.txt"), SearchBudget.Default);

        Assert.Equal(3, result.Candidates.Count);
        Assert.Single(result.Failures);
        Assert.Contains("Broken", result.Failures[0], StringComparison.Ordinal);
        Assert.Contains("3 candidates", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEmptyResultSaysSoPlainlyRatherThanLookingLikeAnError()
    {
        var registry = new FixSourceRegistry();
        var result = await registry.SearchAsync(Probe("Whatever"), SearchBudget.Default);

        Assert.Empty(result.Candidates);
        Assert.Empty(result.Failures);
        Assert.Contains("No candidates", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnlyTheTickedSourcesAreSearched()
    {
        using var folder = new TempFolder();
        var handler = new PlaybackHttpMessageHandler("stackoverflow");
        using var http = handler.CreateClient(folder.Path);

        var registry = new FixSourceRegistry();
        registry.Add(new StackOverflowFixSource(http));
        registry.Add(new ThrowingSource());

        var result = await registry.SearchAsync(
            FingerprintOf("python/keyerror.txt"), SearchBudget.Default, enabled: ["Stack Overflow"]);

        Assert.Single(result.PerSource);
        Assert.Empty(result.Failures);
    }

    private sealed class ThrowingSource : IFixSource
    {
        public string Name => "Broken";
        public bool RequiresNetwork => false;
        public bool IsConfigured => true;
        public string QuotaBucket => "broken";
        public QuotaStatus? Quota => null;

        public Task<FixSearchResult> SearchAsync(ErrorFingerprint f, SearchBudget b, CancellationToken ct) =>
            throw new InvalidOperationException("this source is deliberately broken");
    }

    // ================================================================== repository map

    [Theory]
    [InlineData("express", "expressjs/express")]
    [InlineData("Newtonsoft.Json", "JamesNK/Newtonsoft.Json")]
    [InlineData("org.springframework.beans.factory", "spring-projects/spring-framework")]
    [InlineData("com.fasterxml.jackson.databind", "FasterXML/jackson-databind")]
    [InlineData("github.com/gin-gonic/gin/binding", "gin-gonic/gin")]
    [InlineData("requests", "psf/requests")]
    public void KnownModulesResolveToTheirRepository(string module, string expected) =>
        Assert.Equal(expected, KnownRepoMap.Resolve(module));

    [Theory]
    [InlineData("EthansPrivateHelper")]
    [InlineData("")]
    [InlineData(null)]
    public void UnknownModulesResolveToNothingRatherThanAGuess(string? module) =>
        Assert.Null(KnownRepoMap.Resolve(module));
}
