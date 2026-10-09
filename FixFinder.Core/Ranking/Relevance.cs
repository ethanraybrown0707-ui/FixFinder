using FixFinder.Core.Fingerprinting;
using FixFinder.Core.Sources;

namespace FixFinder.Core.Ranking;

/// <summary>
/// Whether a search result is about the problem the program has, and why it is or is not - in the words of the score it
/// was given, so the reason shown is the reason it was kept.
/// </summary>
/// <param name="IsAboutThisProblem">Whether it is shown at all.</param>
/// <param name="Because">Why: what in it matches this error, or what shows it is about another one.</param>
public sealed record Relevance(bool IsAboutThisProblem, IReadOnlyList<string> Because)
{
    /// <summary>The reasons as one line, to show beside the result.</summary>
    public string Said => string.Join("; ", Because);
}

/// <summary>
/// Decides which search results are about this error. A result that only shares a few words with it - a different
/// error, another language - is a fix for somebody else's problem, and shown as a suggestion it makes the real one harder
/// to understand; so only those that name the same error, are not about another language and share enough of its own
/// words are shown. FixFinder's own fixes, worked out from the program itself, are always about it.
/// </summary>
public static class RelevanceCheck
{
    /// <summary>
    /// The least a result has to share of the error's own distinctive words, by their weight: a quarter. Below that it
    /// shares the error's name and little else - usually somebody else's problem that happens to raise the same error.
    /// </summary>
    public const double LeastSharedWords = 0.25;

    /// <summary>
    /// How strongly a result has to name the error's type or code: in its title or its body. The ranker gives naming only
    /// the error's family - some other Error or Exception - a quarter, and that is not this error.
    /// </summary>
    public const double LeastTypeMatch = 0.4;

    /// <summary>
    /// The least a result has to share of the error's words when the error has no type or code of its own to be named by -
    /// a compiler message such as "expected ';' before 'return'" - so its words are all there is to go on: half.
    /// </summary>
    public const double LeastSharedWordsWithoutAType = 0.5;

    /// <summary>The ranker's weight for a result tagged only with other languages.</summary>
    private const double TaggedAnotherLanguage = 0.2;

    /// <summary>Whether a scored result is about this error, and why. The result must have been scored by the ranker first.</summary>
    public static Relevance Of(FixCandidate candidate, ErrorFingerprint fingerprint)
    {
        if (IsFixFindersOwn(candidate))
            return new Relevance(true, ["worked out by FixFinder from this program itself, not taken from somebody else's"]);

        var type = Component(candidate, "Type match");
        var words = Component(candidate, "Message similarity");
        var language = Component(candidate, "Language match");

        var notThisProblem = new List<string>();

        if (HasANameOfItsOwn(fingerprint))
        {
            if (type is null || type.Value < LeastTypeMatch)
                notThisProblem.Add($"{type?.Explanation ?? "it does not name this error"} - so it is about a different error");
        }
        else if (fingerprint.Tokens.Count > 0 && (words is null || words.Value < LeastSharedWordsWithoutAType))
        {
            notThisProblem.Add($"{words?.Explanation ?? "it shares none of the error's words"} - too few to be the same error");
        }

        if (language is { Value: TaggedAnotherLanguage })
            notThisProblem.Add($"{language.Explanation} - so it is about another language");

        if (HasANameOfItsOwn(fingerprint) && fingerprint.Tokens.Count > 0 && words is not null && words.Value < LeastSharedWords)
            notThisProblem.Add($"{words.Explanation} - so it is somebody else's problem that raises the same error");

        if (notThisProblem.Count > 0) return new Relevance(false, notThisProblem);

        var matches = new[] { type, words, language, Component(candidate, "Resolution") }
            .Where(component => component is { Value: > 0 })
            .Select(component => component!.Explanation)
            .ToList();

        return new Relevance(true, matches);
    }

    /// <summary>Whether a candidate is one of FixFinder's own: worked out from the program, or an install for what it is missing.</summary>
    public static bool IsFixFindersOwn(FixCandidate candidate) => candidate.LocalFix is not null || candidate.Tier == FixTier.Dependency;

    /// <summary>
    /// Whether the error has a name a result can be held to - an exception type or a compiler's code - rather than only a
    /// compiler message, which a question about it would quote rather than name.
    /// </summary>
    private static bool HasANameOfItsOwn(ErrorFingerprint fingerprint) =>
        fingerprint.ErrorCode is { Length: > 0 } ||
        fingerprint.ShortExceptionType is { Length: > 0 } type && type is not ("compile error" or "compile warning" or "link error" or "launcher error");

    private static ScoreComponent? Component(FixCandidate candidate, string name) =>
        candidate.ScoreComponents.FirstOrDefault(component => component.Name == name);
}
