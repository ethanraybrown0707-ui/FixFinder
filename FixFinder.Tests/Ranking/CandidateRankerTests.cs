using FixFinder.Core.Fingerprinting;
using FixFinder.Core.Parsing;
using FixFinder.Core.Ranking;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>Covers the ranker by asserting relative order , never absolute scores.</summary>
public class CandidateRankerTests
{
    private static ErrorFingerprint FingerprintOf(string fixture)
    {
        var parsed = new ParserRegistry().Parse(Fixtures.LoadStackTrace(fixture), []);
        Assert.NotNull(parsed);

        return FingerprintBuilder.Build(parsed!);
    }

    private static ErrorFingerprint PythonKeyError => FingerprintOf("python/keyerror.txt");
    private static ErrorFingerprint DotNetNullReference => FingerprintOf("csharp/inner-exception.txt");
    private static ErrorFingerprint PythonSyntaxError => FingerprintOf("python/syntax-error.txt");

    private static FixCandidate Candidate(
        string id,
        string title,
        string body = "",
        int votes = 0,
        int answers = 0,
        bool accepted = false,
        bool closed = false,
        string? closedReason = null,
        string? duplicateOf = null,
        string[]? tags = null,
        string[]? patches = null,
        int yearsOld = 1,
        FixTier tier = FixTier.Advisory,
        ClosureMeaning closure = ClosureMeaning.Resolved) =>
        new()
        {
            SourceName = "test",
            Id = id,
            Title = title,
            Url = $"https://example.test/{id}",
            BodyText = body,
            Votes = votes,
            AnswerCount = answers,
            AnswerNoun = "answers",
            HasAcceptedAnswer = accepted,
            IsClosed = closed,
            ClosedReason = closedReason,
            Closure = closure,
            DuplicateOfUrl = duplicateOf,
            Tags = tags ?? [],
            LinkedPatchUrls = patches ?? [],
            CreatedAt = DateTimeOffset.UtcNow.AddYears(-yearsOld),
            LastActivityAt = DateTimeOffset.UtcNow.AddYears(-yearsOld),
            Tier = tier,
        };

    private static IReadOnlyList<string> Order(IEnumerable<FixCandidate> ranked) =>
        ranked.Select(c => c.Id).ToArray();

    [Fact]
    public void AQuestionTheSiteTurnedDownLosesToOneItAnswered()
    {
        var fingerprint = PythonSyntaxError;

        var turnedDown = Candidate("rejected", "python SyntaxError: '(' was never closed",
            body: "SyntaxError somewhere in my crontab script",
            answers: 1, closed: true, closedReason: "Not suitable for this site",
            closure: ClosureMeaning.Rejected);

        var answered = Candidate("answered", "SyntaxError: '(' was never closed in my script",
            body: "SyntaxError raised when a bracket is left open",
            answers: 2, accepted: true, closure: ClosureMeaning.Rejected);

        var ranked = CandidateRanker.Rank([turnedDown, answered], fingerprint);

        Assert.Equal(["answered", "rejected"], Order(ranked));
        Assert.True(turnedDown.Score < CandidateRanker.AutoAppliableFloor,
            $"a turned-down question scored {turnedDown.Score:0}");
    }

    [Fact]
    public void AClosedGitHubIssueIsStillGoodNews()
    {
        var fingerprint = PythonKeyError;

        var closed = Candidate("gh-closed", "KeyError: user_id when loading a payload",
            body: "KeyError raised for user_id", closed: true, closedReason: "completed");

        var open = Candidate("gh-open", "KeyError: user_id when loading a payload",
            body: "KeyError raised for user_id", answers: 1);

        var ranked = CandidateRanker.Rank([open, closed], fingerprint);

        Assert.Equal(["gh-closed", "gh-open"], Order(ranked));
    }

    [Fact]
    public void AnAcceptedAnswerOnAClosedQuestionIsNotPenalised()
    {
        var fingerprint = PythonSyntaxError;

        var accepted = Candidate("accepted", "SyntaxError: '(' was never closed",
            body: "SyntaxError from an unclosed bracket", answers: 3, accepted: true,
            closed: true, closedReason: "Needs details or clarity",
            closure: ClosureMeaning.Rejected);

        CandidateRanker.Score(accepted, fingerprint);

        Assert.DoesNotContain(accepted.ScoreComponents, c => c.Name.Contains("turned down", StringComparison.Ordinal));
    }

    [Fact]
    public void NamingTheExactTypeBeatsNamingOnlyItsFamily()
    {
        var exact = Candidate("exact", "KeyError: 'user_id' when reading a dict", tags: ["python"]);
        var family = Candidate("family", "ValueError raised somewhere in my script", tags: ["python"]);

        var ranked = CandidateRanker.Rank([family, exact], PythonKeyError);

        Assert.Equal("exact", ranked[0].Id);
        Assert.True(exact.Score > family.Score);
    }

    [Fact]
    public void TheFullyQualifiedTypeScoresAboveTheShortNameAlone()
    {
        var full = Candidate("full", "System.NullReferenceException thrown from my repository layer",
            tags: ["c#"]);
        var brief = Candidate("brief", "NullReferenceException somewhere", tags: ["c#"]);

        CandidateRanker.Rank([brief, full], DotNetNullReference);

        Assert.True(full.Score > brief.Score);
    }

    [Fact]
    public void AnErrorNamedOnlyDeepInTheBodyScoresBelowOneNamedInTheTitle()
    {
        var titled = Candidate("titled",
            "KeyError: 'user_id' raised on a missing key",
            body: "Some discussion of the problem.",
            closed: true, closedReason: "completed", tags: ["python"]);

        var buried = Candidate("buried",
            "[P0] Authorization bypass and crashes across the share-grant path",
            body: "Audit notes. One of the attached logs ends in KeyError: 'user_id' " +
                  "but that is incidental to this issue.",
            closed: true, closedReason: "completed", tags: ["python"]);

        var ranked = CandidateRanker.Rank([buried, titled], PythonKeyError);

        Assert.Equal("titled", ranked[0].Id);
        Assert.True(titled.Score > buried.Score + 10,
            $"a title match should lead clearly; titled={titled.Score:0.#} buried={buried.Score:0.#}");

        Assert.Contains(buried.ScoreComponents,
            c => c.Name == "Type match" && c.Explanation.Contains("only in the body", StringComparison.Ordinal));
    }

    [Fact]
    public void AClosedIssueWithAPatchOutranksAnOpenOneWithout()
    {
        var closed = Candidate("closed", "KeyError: 'user_id' on missing key",
            closed: true, closedReason: "completed", tags: ["python"],
            patches: ["https://github.com/o/r/commit/abc.patch"]);

        var open = Candidate("open", "KeyError: 'user_id' on missing key",
            answers: 1, tags: ["python"]);

        Assert.Equal(["closed", "open"], Order(CandidateRanker.Rank([open, closed], PythonKeyError)));
    }

    [Fact]
    public void AnAcceptedAnswerOutranksAnEquallyRelevantUnacceptedOne()
    {
        var accepted = Candidate("accepted", "KeyError: 'user_id'", answers: 3, accepted: true, tags: ["python"]);
        var unaccepted = Candidate("unaccepted", "KeyError: 'user_id'", answers: 3, tags: ["python"]);

        Assert.Equal(["accepted", "unaccepted"],
            Order(CandidateRanker.Rank([unaccepted, accepted], PythonKeyError)));
    }

    [Fact]
    public void AnIssueClosedAsNotPlannedScoresBelowOneClosedAsCompleted()
    {
        var completed = Candidate("completed", "KeyError: 'user_id'", closed: true,
            closedReason: "completed", tags: ["python"]);

        var notPlanned = Candidate("notplanned", "KeyError: 'user_id'", closed: true,
            closedReason: "not_planned", tags: ["python"]);

        CandidateRanker.Rank([notPlanned, completed], PythonKeyError);

        Assert.True(completed.Score > notPlanned.Score);
    }

    [Fact]
    public void APatchOnAnOffTopicCandidateDoesNotFloatItAboveAGoodMatch()
    {
        var irrelevant = Candidate("irrelevant",
            "Rolling work queue for the release train",
            body: "Tracking issue for this quarter's scheduling work.",
            answers: 400, tags: ["meta"],
            patches: ["https://github.com/o/r/commit/abc.patch"],
            tier: FixTier.AutoAppliable);

        var relevant = Candidate("relevant",
            "KeyError: 'user_id' reading a payload that lacks the key",
            body: "KeyError is raised because the dict has no such key.",
            answers: 2, accepted: true, tags: ["python"]);

        var ranked = CandidateRanker.Rank([irrelevant, relevant], PythonKeyError);

        Assert.Equal("relevant", ranked[0].Id);
        Assert.True(irrelevant.Score < CandidateRanker.AutoAppliableFloor,
            $"an off-topic candidate must stay below the floor, scored {irrelevant.Score:0.#}");
    }

    [Fact]
    public void APatchOnAGoodMatchDoesLead()
    {
        var patched = Candidate("patched",
            "KeyError: 'user_id' raised for a missing key",
            body: "KeyError when user_id is absent from the payload.",
            closed: true, closedReason: "completed", tags: ["python"],
            patches: ["https://github.com/o/r/commit/abc.patch"],
            tier: FixTier.AutoAppliable);

        var alsoGood = Candidate("alsogood",
            "KeyError: 'user_id' raised for a missing key",
            body: "KeyError when user_id is absent from the payload.",
            answers: 5, accepted: true, votes: 900, tags: ["python"]);

        var ranked = CandidateRanker.Rank([alsoGood, patched], PythonKeyError);

        Assert.Equal("patched", ranked[0].Id);
        Assert.True(patched.Score >= CandidateRanker.AutoAppliableFloor);
    }

    [Fact]
    public void ACandidateThatNeverMentionsTheErrorTypeIsPenalised()
    {
        var onTopic = Candidate("ontopic", "Handling a KeyError cleanly", tags: ["python"], answers: 1);

        var offTopic = Candidate("offtopic",
            "Weekly infrastructure digest",
            body: "Notes from this week, entirely unrelated to anything that threw.",
            votes: 500, answers: 40, closed: true, closedReason: "completed", tags: ["python"]);

        CandidateRanker.Rank([offTopic, onTopic], PythonKeyError);

        Assert.Contains(offTopic.ScoreComponents, c => c.Name == "Penalty: off topic");
        Assert.DoesNotContain(onTopic.ScoreComponents, c => c.Name == "Penalty: off topic");
        Assert.True(onTopic.Score > offTopic.Score);
    }

    [Fact]
    public void AQuestionNobodyAnsweredIsPenalised()
    {
        var answered = Candidate("answered", "KeyError: 'user_id'", answers: 2, tags: ["python"]);
        var silent = Candidate("silent", "KeyError: 'user_id'", answers: 0, tags: ["python"]);

        CandidateRanker.Rank([silent, answered], PythonKeyError);

        Assert.Contains(silent.ScoreComponents, c => c.Name == "Penalty: unanswered");
        Assert.True(answered.Score > silent.Score);
    }

    [Fact]
    public void ADuplicateIsPenalisedButKeepsThePointerToTheRealAnswer()
    {
        var duplicate = Candidate("dupe", "KeyError: 'user_id'", answers: 1, tags: ["python"],
            duplicateOf: "https://stackoverflow.com/questions/999/the-canonical-one");

        var ordinary = Candidate("ordinary", "KeyError: 'user_id'", answers: 1, tags: ["python"]);

        CandidateRanker.Rank([duplicate, ordinary], PythonKeyError);

        Assert.Contains(duplicate.ScoreComponents, c => c.Name == "Penalty: duplicate");
        Assert.True(ordinary.Score > duplicate.Score);
        Assert.Equal("https://stackoverflow.com/questions/999/the-canonical-one", duplicate.DuplicateOfUrl);
    }

    [Fact]
    public void AnAnswerFromBeforeAnEcosystemBreakIsPenalised()
    {
        var ancient = Candidate("ancient",
            "System.NullReferenceException in System.Web handlers",
            accepted: true, votes: 300, tags: ["c#"], yearsOld: 14);

        var current = Candidate("current",
            "System.NullReferenceException in a repository",
            accepted: true, votes: 300, tags: ["c#"], yearsOld: 1);

        CandidateRanker.Rank([ancient, current], DotNetNullReference);

        Assert.Contains(ancient.ScoreComponents, c => c.Name.Contains("breaking change", StringComparison.Ordinal));
        Assert.True(current.Score > ancient.Score);
    }

    [Fact]
    public void MerelyBeingOldIsNotPenalisedWhenNothingBroke()
    {
        var old = Candidate("old", "KeyError: 'user_id'", accepted: true, tags: ["ruby"], yearsOld: 12);

        CandidateRanker.Rank([old], FingerprintOf("ruby/zerodivision.txt"));

        Assert.DoesNotContain(old.ScoreComponents, c => c.Name.Contains("breaking change", StringComparison.Ordinal));
    }

    [Fact]
    public void AFamousButVagueAnswerDoesNotOutrankAPreciseQuietOne()
    {
        var famous = Candidate("famous",
            "What is a KeyError in Python?",
            body: "A general explanation of dictionary lookups.",
            votes: 9000, answers: 30, accepted: true, tags: ["python"]);

        var precise = Candidate("precise",
            "KeyError: 'user_id' when the payload omits the key",
            body: "KeyError 'user_id' happens when the response has no user_id field.",
            votes: 12, answers: 2, accepted: true, tags: ["python"]);

        Assert.Equal("precise", Order(CandidateRanker.Rank([famous, precise], PythonKeyError))[0]);
    }

    [Fact]
    public void AWrongLanguageTagCostsButDoesNotEliminate()
    {
        var right = Candidate("right", "KeyError: 'user_id'", tags: ["python"], answers: 1);
        var wrong = Candidate("wrong", "KeyError: 'user_id'", tags: ["php"], answers: 1);

        CandidateRanker.Rank([wrong, right], PythonKeyError);

        Assert.True(right.Score > wrong.Score);
        Assert.True(wrong.Score > 0, "a wrong tag is evidence against, not a disqualification");
    }

    [Fact]
    public void CarryingNoTagsIsTreatedAsUnknownRatherThanWrong()
    {
        var untagged = Candidate("untagged", "KeyError: 'user_id'", answers: 1);
        var mistagged = Candidate("mistagged", "KeyError: 'user_id'", tags: ["php"], answers: 1);

        CandidateRanker.Rank([untagged, mistagged], PythonKeyError);

        Assert.True(untagged.Score > mistagged.Score);
    }

    [Fact]
    public void EveryCandidateCarriesAReadableBreakdownOfItsOwnScore()
    {
        var candidate = Candidate("explained", "KeyError: 'user_id'", answers: 2, accepted: true, tags: ["python"]);

        CandidateRanker.Rank([candidate], PythonKeyError);

        Assert.Equal(8, candidate.ScoreComponents.Count(c => c.Weight > 0));

        Assert.All(candidate.ScoreComponents, component =>
        {
            Assert.False(string.IsNullOrWhiteSpace(component.Name));
            Assert.False(string.IsNullOrWhiteSpace(component.Explanation));
        });

        Assert.Contains(candidate.ScoreComponents, c => c.Name == "Type match" && c.Value > 0);
    }

    [Fact]
    public void TheWeightedComponentsSumToOne()
    {
        var candidate = Candidate("weights", "KeyError: 'user_id'", tags: ["python"]);

        CandidateRanker.Rank([candidate], PythonKeyError);

        var total = candidate.ScoreComponents.Where(c => c.Weight > 0).Sum(c => c.Weight);

        Assert.Equal(1.0, total, precision: 6);
    }

    [Fact]
    public void ScoresStayInsideZeroToOneHundred()
    {
        var awful = Candidate("awful", "Completely unrelated tracking issue", yearsOld: 20);
        var excellent = Candidate("excellent",
            "KeyError: 'user_id' fixed",
            body: "KeyError user_id resolved.",
            votes: 50000, answers: 99, accepted: true, closed: true, closedReason: "completed",
            tags: ["python"], patches: ["https://github.com/o/r/commit/a.patch"], yearsOld: 0);

        CandidateRanker.Rank([awful, excellent], PythonKeyError);

        Assert.InRange(awful.Score, 0, 100);
        Assert.InRange(excellent.Score, 0, 100);
    }

    [Fact]
    public void RankingAnEmptyListReturnsAnEmptyList() =>
        Assert.Empty(CandidateRanker.Rank([], PythonKeyError));
}
