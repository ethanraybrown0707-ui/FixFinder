using System.Text.RegularExpressions;
using FixFinder.Core.Fingerprinting;
using FixFinder.Core.Sources;

namespace FixFinder.Core.Ranking;

/// <summary>
/// Orders search results by how likely each one is to be the fix for this crash.
/// </summary>
/// <remarks>
/// The whole tool turns on this class. Two services return thirty results apiece, ordered by
/// their own idea of relevance, which is keyword overlap across the entire site; what is wanted
/// is the handful describing <i>this</i> failure. Without that, FixFinder is a link dump with
/// extra steps.
/// <para>
/// Every number here is fixed, written down, and shown in the window. Nothing is learned, fitted
/// or tuned against a corpus, because a weight nobody can explain is exactly the kind of opacity
/// this tool exists to avoid - the promise is deterministic code you can read, so "why is this
/// one first" has to be answerable by pointing at a row in a table.
/// </para>
/// </remarks>
public static partial class CandidateRanker
{
    // -------------------------------------------------------------- weights

    /// <summary>
    /// What each signal contributes. These sum to 1.0 before penalties.
    /// </summary>
    /// <remarks>
    /// Type match is the largest share on purpose. The exception type is the one fact both sides
    /// state in the same words: everything else - the message, the stack, the wording of a title -
    /// varies with whose program it was, but <c>KeyError</c> is <c>KeyError</c> everywhere.
    /// </remarks>
    private const double TypeMatchWeight = 0.30;
    private const double MessageSimilarityWeight = 0.22;
    private const double TitleContainmentWeight = 0.10;
    private const double ResolutionWeight = 0.10;
    private const double AuthorityWeight = 0.08;
    private const double RecencyWeight = 0.06;
    private const double LanguageWeight = 0.06;
    private const double PatchAvailableWeight = 0.08;

    // -------------------------------------------------------------- penalties

    /// <summary>Body mentions nothing resembling the exception type.</summary>
    private const double DriftPenalty = 25;

    private const double NoAnswersPenalty = 15;
    private const double DuplicatePenalty = 10;
    private const double StalePenalty = 20;

    /// <summary>
    /// A patch must clear this before its tier is allowed to float it above better matches.
    /// </summary>
    /// <remarks>
    /// The floor is the point. A weakly-matched patch that happens to apply cleanly is more
    /// dangerous than an obviously irrelevant one - it produces a confident-looking change to
    /// the wrong code, and there is no model here to notice. Carrying a diff earns a place at
    /// the top only once the candidate has independently been judged relevant.
    /// </remarks>
    public const double AutoAppliableFloor = 55;

    /// <summary>Body text beyond this is ignored: relevance lives near the top of a post.</summary>
    private const int BodyWindow = 1500;

    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex WordPattern();

    /// <summary>
    /// Scores every candidate and returns them in the order they should be shown.
    /// </summary>
    public static IReadOnlyList<FixCandidate> Rank(
        IEnumerable<FixCandidate> candidates, ErrorFingerprint fingerprint)
    {
        var ranked = candidates.ToList();

        foreach (var candidate in ranked) Score(candidate, fingerprint);

        return
        [
            .. ranked
                // A patch that is also a good match leads, because it is the only kind of result
                // this tool can act on rather than merely show.
                .OrderByDescending(c => c.Tier == FixTier.AutoAppliable && c.Score >= AutoAppliableFloor)
                .ThenByDescending(c => c.Score)
                .ThenByDescending(c => c.LastActivityAt ?? DateTimeOffset.MinValue)
        ];
    }

    /// <summary>Scores one candidate in place, recording every component and penalty.</summary>
    public static void Score(FixCandidate candidate, ErrorFingerprint fingerprint)
    {
        var haystack = Haystack(candidate);
        var components = new List<ScoreComponent>();

        var type = TypeMatch(candidate, fingerprint, out var typeReason);
        components.Add(new ScoreComponent("Type match", TypeMatchWeight, type, typeReason));

        var similarity = MessageSimilarity(candidate, fingerprint, out var similarityReason);
        components.Add(new ScoreComponent("Message similarity", MessageSimilarityWeight, similarity, similarityReason));

        var containment = TitleContainment(candidate, fingerprint, out var containmentReason);
        components.Add(new ScoreComponent("Title containment", TitleContainmentWeight, containment, containmentReason));

        var resolution = Resolution(candidate, out var resolutionReason);
        components.Add(new ScoreComponent("Resolution", ResolutionWeight, resolution, resolutionReason));

        var authority = Authority(candidate, out var authorityReason);
        components.Add(new ScoreComponent("Authority", AuthorityWeight, authority, authorityReason));

        var recency = Recency(candidate, out var recencyReason);
        components.Add(new ScoreComponent("Recency", RecencyWeight, recency, recencyReason));

        var language = LanguageMatch(candidate, fingerprint, out var languageReason);
        components.Add(new ScoreComponent("Language match", LanguageWeight, language, languageReason));

        var patch = candidate.LinkedPatchUrls.Count > 0 ? 1.0 : 0.0;
        components.Add(new ScoreComponent("Patch available", PatchAvailableWeight, patch,
            patch > 0
                ? $"{candidate.LinkedPatchUrls.Count} linked commit or pull request to fetch a diff from"
                : "nothing linked that could be turned into a patch"));

        var raw = components.Sum(c => c.Contribution);
        var score = 100 * raw;

        foreach (var penalty in Penalties(candidate, fingerprint, haystack))
        {
            score -= penalty.Amount;
            components.Add(new ScoreComponent(penalty.Name, 0, 0, penalty.Reason));
        }

        candidate.Score = Math.Clamp(score, 0, 100);
        candidate.ScoreComponents = components;
    }

    // -------------------------------------------------------------- components

    /// <summary>
    /// How exactly the candidate names the thing that was thrown.
    /// </summary>
    /// <remarks>
    /// Graded rather than boolean because the three cases are genuinely different in worth. The
    /// fully-qualified type or an exact compiler code is near-proof. A bare short name is strong
    /// but ambiguous across ecosystems. Merely belonging to the same family - some other
    /// <c>*Error</c> - is close to no evidence at all, and scoring it as though it were would let
    /// any exception on the site look like a partial match.
    /// </remarks>
    /// <summary>
    /// How much a match buried in the body is worth, against the same match in the title.
    /// </summary>
    /// <remarks>
    /// Not a detail. A title is a claim about what a post is <i>about</i>; a body is whatever
    /// anyone pasted into it, and on a long tracking issue or a security audit that routinely
    /// includes a stack trace from something else entirely. Without this discount, one
    /// incidental mention of the type deep inside a large document earns exactly as much as a
    /// question titled with it, which is how an XSS report ends up at the top of the list for a
    /// KeyError.
    /// </remarks>
    private const double BodyOnlyDiscount = 0.6;

    private static double TypeMatch(FixCandidate candidate, ErrorFingerprint fingerprint, out string reason)
    {
        var title = candidate.Title;
        var body = candidate.BodyText;

        var (score, what) = Graded(fingerprint, title, body);

        if (score == 0)
        {
            reason = "does not mention this error type at all";
            return 0;
        }

        // The same-family case names nothing in particular, and is already scored low enough
        // that discounting it further would be noise.
        if (what.Length == 0)
        {
            reason = "mentions a different error of the same family only";
            return score;
        }

        if (title.Contains(what, StringComparison.OrdinalIgnoreCase))
        {
            reason = $"the title names {what}";
            return score;
        }

        reason = $"mentions {what}, but only in the body, not the title";
        return score * BodyOnlyDiscount;
    }

    /// <summary>The graded match itself, and the text that produced it.</summary>
    private static (double Score, string What) Graded(ErrorFingerprint fingerprint, string title, string body)
    {
        var text = $"{title}\n{body}";

        if (fingerprint.ErrorCode is { Length: > 0 } code &&
            text.Contains(code, StringComparison.OrdinalIgnoreCase))
        {
            return (1.0, code);
        }

        if (fingerprint.ExceptionType is { Length: > 0 } full &&
            text.Contains(full, StringComparison.OrdinalIgnoreCase))
        {
            return (1.0, full);
        }

        if (fingerprint.ShortExceptionType is { Length: > 0 } shortType &&
            text.Contains(shortType, StringComparison.OrdinalIgnoreCase))
        {
            return (0.7, shortType);
        }

        if (fingerprint.ShortExceptionType is { Length: > 0 } mine && SameFamily(mine, text))
        {
            return (0.25, "");
        }

        return (0, "");
    }

    private static bool SameFamily(string shortType, string text)
    {
        var suffix =
            shortType.EndsWith("Exception", StringComparison.OrdinalIgnoreCase) ? "Exception" :
            shortType.EndsWith("Error", StringComparison.OrdinalIgnoreCase) ? "Error" :
            null;

        return suffix is not null && text.Contains(suffix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Weighted overlap between the error's distinctive words and the candidate's text.
    /// </summary>
    /// <remarks>
    /// Weighted with the same token weights the query builder uses, so a shared
    /// <c>nullreferenceexception</c> counts for far more than a shared "the". Plain Jaccard
    /// treats every word alike, which on prose of this kind mostly measures how long the post is.
    /// </remarks>
    private static double MessageSimilarity(
        FixCandidate candidate, ErrorFingerprint fingerprint, out string reason)
    {
        if (fingerprint.Tokens.Count == 0)
        {
            reason = "the error carried no distinctive words to compare";
            return 0;
        }

        var inTitle = WordsIn(candidate.Title);
        var inBody = WordsIn(Window(candidate.BodyText));

        double total = 0, matched = 0;
        var titleHits = new List<string>();
        var bodyHits = new List<string>();

        foreach (var token in fingerprint.Tokens)
        {
            var weight = Math.Max(Stoplists.Weight(token), 0.5);
            total += weight;

            // Same discount as the type match, for the same reason: a word in the title is a
            // statement of subject, a word in the body may be anything someone pasted in.
            if (inTitle.Contains(token))
            {
                matched += weight;
                if (titleHits.Count < 4) titleHits.Add(token);
            }
            else if (inBody.Contains(token))
            {
                matched += weight * BodyOnlyDiscount;
                if (bodyHits.Count < 4) bodyHits.Add(token);
            }
        }

        if (total == 0)
        {
            reason = "nothing to compare";
            return 0;
        }

        reason = (titleHits.Count, bodyHits.Count) switch
        {
            (0, 0) => "shares none of the error's distinctive words",
            (> 0, 0) => $"the title shares {string.Join(", ", titleHits)}",
            (0, > 0) => $"shares {string.Join(", ", bodyHits)}, but only in the body",
            _ => $"the title shares {string.Join(", ", titleHits)}; the body adds {string.Join(", ", bodyHits)}",
        };

        return matched / total;
    }

    private static HashSet<string> WordsIn(string text) =>
        WordPattern()
            .Matches(text)
            .Select(m => m.Value.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// How much of the query appears in the title.
    /// </summary>
    /// <remarks>
    /// Separate from body similarity because a title is a claim about what a post is <i>about</i>,
    /// while a body may mention anything in passing - a stack trace pasted in a comment, an
    /// unrelated aside. Overlap in the title is much stronger evidence per word.
    /// </remarks>
    private static double TitleContainment(FixCandidate candidate, ErrorFingerprint fingerprint, out string reason)
    {
        var terms = fingerprint.Tight.Terms;

        if (terms.Count == 0)
        {
            reason = "no query terms to look for";
            return 0;
        }

        var found = terms.Count(term => candidate.Title.Contains(term, StringComparison.OrdinalIgnoreCase));

        reason = $"{found} of {terms.Count} query terms appear in the title";
        return (double)found / terms.Count;
    }

    /// <summary>How settled the discussion is - whether anyone concluded anything.</summary>
    private static double Resolution(FixCandidate candidate, out string reason)
    {
        if (candidate.HasAcceptedAnswer)
        {
            reason = "the person who asked marked an answer as the one that worked";
            return 1.0;
        }

        if (candidate.DuplicateOfUrl is not null)
        {
            reason = "closed as a duplicate";
            return 0.2;
        }

        if (candidate.IsClosed)
        {
            // "not_planned" means it was closed without being fixed, which is nearly the
            // opposite of the good news that a closed issue usually is.
            if (string.Equals(candidate.ClosedReason, "not_planned", StringComparison.OrdinalIgnoreCase))
            {
                reason = "closed without being fixed (not planned)";
                return 0.2;
            }

            if (candidate.LinkedPatchUrls.Count > 0)
            {
                reason = "closed, with a commit or pull request attached to it";
                return 1.0;
            }

            reason = "closed as completed";
            return 0.9;
        }

        if (candidate.LinkedPatchUrls.Count > 0)
        {
            reason = "still open, but a pull request references it";
            return 0.9;
        }

        if (candidate.AnswerCount > 0)
        {
            reason = $"open, with {candidate.AnswerCount} {candidate.AnswerNoun} and nothing settled";
            return 0.4;
        }

        reason = "open, with no replies at all";
        return 0.3;
    }

    /// <summary>
    /// How much the community endorsed it, on a saturating scale.
    /// </summary>
    /// <remarks>
    /// Logarithmic and capped so votes cannot dominate. A five-thousand-vote answer to a vaguely
    /// similar question must not outrank a twelve-vote answer describing precisely this failure,
    /// and on a linear scale it would every time.
    /// </remarks>
    private static double Authority(FixCandidate candidate, out string reason)
    {
        if (candidate.Votes <= 0)
        {
            reason = "no votes or reactions";
            return 0;
        }

        var score = Math.Min(1.0, Math.Log10(1 + candidate.Votes) / 2);

        reason = candidate.Votes == 1 ? "1 vote or reaction" : $"{candidate.Votes} votes or reactions";
        return score;
    }

    /// <summary>
    /// Decays with age, because an old answer is often actively wrong rather than merely dated.
    /// </summary>
    private static double Recency(FixCandidate candidate, out string reason)
    {
        var when = candidate.LastActivityAt ?? candidate.CreatedAt;

        if (when is null)
        {
            reason = "no date given";
            return 0.5;
        }

        var years = (DateTimeOffset.UtcNow - when.Value).TotalDays / 365.25;
        var score = Math.Exp(-Math.Max(years, 0) / 3);

        reason = years < 1
            ? "active within the last year"
            : $"last active about {years:0.#} years ago";

        return score;
    }

    /// <summary>Whether the candidate is even about the language that crashed.</summary>
    private static double LanguageMatch(FixCandidate candidate, ErrorFingerprint fingerprint, out string reason)
    {
        var expected = TagsFor(fingerprint.LanguageId);

        if (expected.Count == 0)
        {
            reason = "no tag is known for this language";
            return 0.5;
        }

        if (candidate.Tags.Count == 0)
        {
            // A GitHub issue's labels describe the project's own workflow ("bug", "triage"), not
            // the language. Absent evidence is not evidence against.
            reason = "carries no tags to check against";
            return 0.5;
        }

        var hit = candidate.Tags.FirstOrDefault(tag => expected.Contains(tag, StringComparer.OrdinalIgnoreCase));

        if (hit is not null)
        {
            reason = $"tagged {hit}";
            return 1.0;
        }

        reason = $"tagged {string.Join(", ", candidate.Tags.Take(3))}, none of which is {string.Join(" or ", expected)}";
        return 0.2;
    }

    /// <summary>Maps a parser's language id to the tags the sites actually use.</summary>
    private static IReadOnlyList<string> TagsFor(string languageId) => languageId switch
    {
        "csharp" => ["c#", ".net", "asp.net", "dotnet"],
        "python" => ["python", "python-3.x"],
        "node" => ["javascript", "node.js", "typescript"],
        "java" => ["java", "spring", "android"],
        "go" => ["go"],
        "rust" => ["rust"],
        "gcc" => ["c++", "c", "gcc", "clang"],
        "msvc" => ["c++", "c", "visual-studio", "c#"],
        "ruby" => ["ruby", "ruby-on-rails"],
        _ => [],
    };

    // -------------------------------------------------------------- penalties

    private readonly record struct Penalty(string Name, double Amount, string Reason);

    private static IEnumerable<Penalty> Penalties(
        FixCandidate candidate, ErrorFingerprint fingerprint, string haystack)
    {
        // The drift guard, and the most valuable rule here. Both search engines cheerfully
        // return adjacent topics when the query is unusual, and a long, busy, recently-active
        // issue can otherwise score respectably on every other signal while having nothing
        // whatsoever to do with the crash.
        if (fingerprint.ShortExceptionType is { Length: > 0 } shortType &&
            !haystack.Contains(shortType, StringComparison.OrdinalIgnoreCase) &&
            (fingerprint.ErrorCode is not { Length: > 0 } code ||
             !haystack.Contains(code, StringComparison.OrdinalIgnoreCase)))
        {
            yield return new Penalty("Penalty: off topic", DriftPenalty,
                $"neither the title nor the body mentions {shortType}");
        }

        if (candidate.AnswerCount == 0 && !candidate.IsClosed && candidate.LinkedPatchUrls.Count == 0)
        {
            yield return new Penalty("Penalty: unanswered", NoAnswersPenalty,
                "nobody has replied, so there is no fix here to read");
        }

        if (candidate.DuplicateOfUrl is not null)
        {
            yield return new Penalty("Penalty: duplicate", DuplicatePenalty,
                "closed as a duplicate - the question it points at is usually the better read");
        }

        if (IsStale(candidate, fingerprint, out var breakReason))
        {
            yield return new Penalty("Penalty: predates a breaking change", StalePenalty, breakReason);
        }
    }

    /// <summary>
    /// True when the candidate predates a change that makes its advice actively misleading.
    /// </summary>
    /// <remarks>
    /// Age alone is not the problem - plenty of decade-old answers are still exactly right. The
    /// problem is age across a break in the ecosystem, where the advice was correct when written
    /// and is wrong now, which is worse than no answer because it reads as authoritative.
    /// </remarks>
    private static bool IsStale(FixCandidate candidate, ErrorFingerprint fingerprint, out string reason)
    {
        reason = "";

        var when = candidate.LastActivityAt ?? candidate.CreatedAt;
        if (when is null) return false;

        var (year, description) = fingerprint.LanguageId switch
        {
            "csharp" => (2016, ".NET Framework gave way to .NET Core"),
            "python" => (2020, "Python 2 reached end of life"),
            "node" => (2020, "Node moved to ES modules"),
            _ => (0, ""),
        };

        if (year == 0) return false;
        if (when.Value.Year >= year) return false;
        if ((DateTimeOffset.UtcNow - when.Value).TotalDays / 365.25 < 8) return false;

        reason = $"written in {when.Value.Year}, before {description}";
        return true;
    }

    // -------------------------------------------------------------- helpers

    /// <summary>Title plus the first part of the body: the text the drift guard searches.</summary>
    private static string Haystack(FixCandidate candidate) =>
        $"{candidate.Title}\n{Window(candidate.BodyText)}";

    /// <summary>
    /// The part of a body worth reading.
    /// </summary>
    /// <remarks>
    /// Relevance lives near the top of a post. Reading further mostly adds the tail of a long
    /// thread - follow-ups, unrelated logs, a second problem someone appended later - which
    /// dilutes every measurement taken over it.
    /// </remarks>
    private static string Window(string body) =>
        body.Length <= BodyWindow ? body : body[..BodyWindow];
}
