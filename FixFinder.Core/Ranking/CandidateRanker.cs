using System.Text.RegularExpressions;
using FixFinder.Core.Fingerprinting;
using FixFinder.Core.Sources;

namespace FixFinder.Core.Ranking;

/// <summary>Orders search results by how likely each one is to be the fix for this crash.</summary>
public static partial class CandidateRanker
{
    private const double TypeMatchWeight = 0.30;
    private const double MessageSimilarityWeight = 0.22;
    private const double TitleContainmentWeight = 0.10;
    private const double ResolutionWeight = 0.10;
    private const double AuthorityWeight = 0.08;
    private const double RecencyWeight = 0.06;
    private const double LanguageWeight = 0.06;
    private const double PatchAvailableWeight = 0.08;

    private const double DriftPenalty = 25;

    private const double NoAnswersPenalty = 15;
    private const double DuplicatePenalty = 10;
    private const double StalePenalty = 20;

    private const double RejectedPenalty = 20;

    public const double AutoAppliableFloor = 55;

    private const int BodyWindow = 1500;

    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex WordPattern();

    public static IReadOnlyList<FixCandidate> Rank(
        IEnumerable<FixCandidate> candidates, ErrorFingerprint fingerprint)
    {
        var ranked = candidates.ToList();

        foreach (var candidate in ranked) Score(candidate, fingerprint);

        return
        [
            .. ranked
                .OrderByDescending(c => c.Tier == FixTier.AutoAppliable && c.Score >= AutoAppliableFloor)
                .ThenByDescending(c => c.Score)
                .ThenByDescending(c => c.LastActivityAt ?? DateTimeOffset.MinValue)
        ];
    }

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

        if (candidate.IsClosed && candidate.Closure == ClosureMeaning.Rejected)
        {
            reason = candidate.ClosedReason is { Length: > 0 } why
                ? $"closed by the site as \"{why}\" - turned down rather than answered"
                : "closed by the site - turned down rather than answered";

            return 0.1;
        }

        if (candidate.IsClosed)
        {
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

    private readonly record struct Penalty(string Name, double Amount, string Reason);

    private static IEnumerable<Penalty> Penalties(
        FixCandidate candidate, ErrorFingerprint fingerprint, string haystack)
    {
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
        else if (candidate.IsClosed &&
                 candidate.Closure == ClosureMeaning.Rejected &&
                 !candidate.HasAcceptedAnswer)
        {
            yield return new Penalty("Penalty: turned down", RejectedPenalty,
                candidate.ClosedReason is { Length: > 0 } why
                    ? $"the site closed this question as \"{why}\", and no answer on it was accepted"
                    : "the site closed this question rather than answering it");
        }

        if (IsStale(candidate, fingerprint, out var breakReason))
        {
            yield return new Penalty("Penalty: predates a breaking change", StalePenalty, breakReason);
        }
    }

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

    private static string Haystack(FixCandidate candidate) =>
        $"{candidate.Title}\n{Window(candidate.BodyText)}";

    private static string Window(string body) =>
        body.Length <= BodyWindow ? body : body[..BodyWindow];
}
