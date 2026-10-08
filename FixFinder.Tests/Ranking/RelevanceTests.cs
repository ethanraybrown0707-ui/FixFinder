using FixFinder.Core.Engine;
using FixFinder.Core.Fingerprinting;
using FixFinder.Core.Parsing;
using FixFinder.Core.Patching;
using FixFinder.Core.Ranking;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>
/// Which search results are shown: only those about this very error. Somebody else's fix for another problem, offered as
/// this one's, makes the real problem harder to understand - so a result about another error, another language, or one
/// that shares little of the error's own words is left out, and every result shown says why it is.
/// </summary>
public class RelevanceTests
{
    private static ErrorFingerprint PythonKeyError
    {
        get
        {
            var parsed = new ParserRegistry().Parse(Fixtures.LoadStackTrace("python/keyerror.txt"), []);
            Assert.NotNull(parsed);
            return FingerprintBuilder.Build(parsed!);
        }
    }

    private static FixCandidate Scored(string title, string body, string[] tags, ErrorFingerprint fingerprint)
    {
        var candidate = new FixCandidate
        {
            SourceName = "test",
            Id = title,
            Title = title,
            Url = "https://example.test/question",
            BodyText = body,
            Tags = tags,
            AnswerCount = 2,
            HasAcceptedAnswer = true,
            CreatedAt = DateTimeOffset.UtcNow.AddYears(-1),
        };

        CandidateRanker.Score(candidate, fingerprint);
        return candidate;
    }

    [Fact]
    public void AResultAboutTheSameErrorInTheSameLanguageIsShownWithWhy()
    {
        var fingerprint = PythonKeyError;
        var words = string.Join(" ", fingerprint.Tokens);
        var candidate = Scored($"KeyError: {words}", $"A KeyError raised when reading {words} from a dict", ["python"], fingerprint);

        var relevance = RelevanceCheck.Of(candidate, fingerprint);

        Assert.True(relevance.IsAboutThisProblem, relevance.Said);
        Assert.Contains(relevance.Because, reason => reason.Contains("KeyError", StringComparison.Ordinal));
        Assert.Contains(relevance.Because, reason => reason.Contains("tagged python", StringComparison.Ordinal));
    }

    [Fact]
    public void AResultThatNeverNamesTheErrorIsLeftOut()
    {
        var fingerprint = PythonKeyError;
        var candidate = Scored("How do I sort a dictionary by value?", "Use sorted with a key function.", ["python"], fingerprint);

        var relevance = RelevanceCheck.Of(candidate, fingerprint);

        Assert.False(relevance.IsAboutThisProblem);
        Assert.Contains(relevance.Because, reason => reason.EndsWith("so it is about a different error", StringComparison.Ordinal));
    }

    [Fact]
    public void AResultAboutAnotherLanguageIsLeftOutEvenWhenItNamesTheSameError()
    {
        var fingerprint = PythonKeyError;
        var words = string.Join(" ", fingerprint.Tokens);
        var candidate = Scored($"KeyError: {words}", $"KeyError {words}", ["java", "spring"], fingerprint);

        var relevance = RelevanceCheck.Of(candidate, fingerprint);

        Assert.False(relevance.IsAboutThisProblem);
        Assert.Contains(relevance.Because, reason => reason.EndsWith("so it is about another language", StringComparison.Ordinal));
    }

    [Fact]
    public void AResultThatSharesOnlyTheErrorsNameIsLeftOut()
    {
        var fingerprint = PythonKeyError;
        Assert.NotEmpty(fingerprint.Tokens);

        var candidate = Scored("KeyError when parsing my config file", "KeyError raised by configparser for a missing section", ["python"], fingerprint);

        var relevance = RelevanceCheck.Of(candidate, fingerprint);

        Assert.False(relevance.IsAboutThisProblem, relevance.Said);
        Assert.Contains(relevance.Because, reason => reason.EndsWith("somebody else's problem that raises the same error", StringComparison.Ordinal));
    }

    [Fact]
    public void FixFindersOwnFixIsAlwaysAboutThisProgram()
    {
        var install = new FixCandidate
        {
            SourceName = "Your Python environment",
            Id = "install",
            Title = "Install requests",
            Url = "",
            Tier = FixTier.Dependency,
            Command = "python -m pip install requests",
        };

        var relevance = RelevanceCheck.Of(install, PythonKeyError);

        Assert.True(relevance.IsAboutThisProblem);
        Assert.True(RelevanceCheck.IsFixFindersOwn(install));
    }

    [Fact]
    public void CodeFromSomebodyElsesAnswerIsNeverOfferedAsThisProgramsFix()
    {
        var fingerprint = PythonKeyError;
        var answer = Scored("KeyError when reading a dict", "Use dict.get", ["python"], fingerprint);
        var examined = new ExaminedCandidate(answer, 1, 1, new HarvestResult([new CodeBlock("python", "value = data.get('key')\n")], [], 0), null);

        Assert.NotNull(PasteableFix.For(examined));
        Assert.False(PasteableFix.FitsThisProgram(examined));
    }

    [Fact]
    public void ACommandThatInstallsWhatIsMissingFitsThisProgram()
    {
        var install = new FixCandidate
        {
            SourceName = "Your Python environment",
            Id = "install",
            Title = "Install requests",
            Url = "",
            Tier = FixTier.Dependency,
            Command = "python -m pip install requests",
        };

        Assert.True(PasteableFix.FitsThisProgram(new ExaminedCandidate(install, 1, 1, null, null)));
    }
}
