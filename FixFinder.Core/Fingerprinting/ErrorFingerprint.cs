using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.Fingerprinting;

/// <summary>A search query, with the terms it was built from and why.</summary>
public sealed record SearchQuery(string Text, IReadOnlyList<string> Terms, string Explanation);

/// <summary>
/// A parsed error reduced to the facts worth searching for, plus the two queries built from them.
/// </summary>
/// <remarks>
/// A record so the window can produce an edited copy with <c>with</c> when the user rewrites
/// a query by hand - which is a supported and encouraged thing to do, not an edge case.
/// </remarks>
public sealed record ErrorFingerprint
{
    public required string LanguageId { get; init; }
    public string? ExceptionType { get; init; }
    public string? ShortExceptionType { get; init; }
    public string? ErrorCode { get; init; }
    public required string NormalizedMessage { get; init; }
    public required IReadOnlyList<string> Tokens { get; init; }
    public IReadOnlyList<string> QuotedLiterals { get; init; } = [];

    /// <summary>File name only - a full path would never match anyone else's repository.</summary>
    public string? CulpritFile { get; init; }

    public string? NearestThirdPartyModule { get; init; }

    /// <summary>
    /// True when the crash is in the user's own code, where searching is unlikely to help.
    /// </summary>
    public required bool CulpritIsFirstParty { get; init; }

    /// <summary>
    /// Stable identity for this error: SHA-256 of type and normalised message, first 16 hex chars.
    /// </summary>
    /// <remarks>
    /// Does three jobs. It is the cache key for search results, the dedup key across runs, and -
    /// most importantly - the before/after comparison that tells <c>FixVerifier</c> whether a
    /// patch actually fixed the crash or merely moved it. That last use is why line numbers,
    /// paths and timestamps must be normalised out first: a patch changes line numbers, so a
    /// hash that included them would report every applied patch as "different error".
    /// </remarks>
    public required string Hash { get; init; }

    public required SearchQuery Tight { get; init; }
    public required SearchQuery Relaxed { get; init; }

    /// <summary>What normalisation removed, in order, for the window's trace panel.</summary>
    public IReadOnlyList<AppliedNormalization> Trace { get; init; } = [];
}

/// <summary>Builds an <see cref="ErrorFingerprint"/> from a <see cref="ParsedError"/>.</summary>
public static partial class FingerprintBuilder
{
    /// <summary>Tokens kept in the tight query. Beyond roughly a dozen, precision stops helping.</summary>
    private const int TightTokenCount = 12;

    private const int RelaxedTokenCount = 4;

    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_.:$]*")]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"'([^']{2,})'|""([^""]{2,})""")]
    private static partial Regex QuotedPattern();

    /// <summary>
    /// The markers normalisation leaves behind, which must never become search terms.
    /// </summary>
    /// <remarks>
    /// Easy to miss and quietly damaging. Normalising <c>KeyError: 'user_id'</c> for the relaxed
    /// query produces <c>KeyError: &lt;val&gt;</c>, and a tokenizer that does not know what those
    /// angle brackets mean happily emits "val" as a distinctive word - so the query sent to two
    /// search engines becomes "KeyError val", where "val" matches nothing anyone ever wrote.
    /// The placeholders exist to mark removed text, so removed is how they must be treated.
    /// </remarks>
    [GeneratedRegex(@"<(?:path|guid|addr|time|num|val)>")]
    private static partial Regex PlaceholderPattern();

    public static ErrorFingerprint Build(ParsedError error)
    {
        // The root cause, not the wrapper. "Could not load the basket from the store" is text
        // this program alone has ever printed; "NullReferenceException" is what to search for.
        var target = error.RootCause;

        var rawMessage = BuildRawMessage(target);
        var (tightMessage, trace) = ErrorNormalizer.Normalize(rawMessage, relaxed: false);
        var (relaxedMessage, _) = ErrorNormalizer.Normalize(rawMessage, relaxed: true);

        var literals = QuotedPattern()
            .Matches(rawMessage)
            .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
            .Where(v => v.Length is > 1 and < 60)
            .Distinct(StringComparer.Ordinal)
            .Take(3)
            .ToArray();

        var tokens = Tokenize(tightMessage);
        var relaxedTokens = Tokenize(relaxedMessage);

        var culpritFile = target.CulpritFrame?.File is { } file ? Path.GetFileName(file) : null;

        return new ErrorFingerprint
        {
            LanguageId = target.LanguageId,
            ExceptionType = target.ExceptionType,
            ShortExceptionType = target.ShortExceptionType,
            ErrorCode = target.ErrorCode,
            NormalizedMessage = tightMessage,
            Tokens = tokens,
            QuotedLiterals = literals,
            CulpritFile = culpritFile,
            NearestThirdPartyModule = CulpritFrameSelector.NearestThirdPartyModule(error),
            CulpritIsFirstParty = CulpritFrameSelector.CulpritIsFirstParty(error),
            Hash = ComputeHash(target.ShortExceptionType, tightMessage),
            Tight = BuildTight(target, tokens, literals),
            Relaxed = BuildRelaxed(target, relaxedTokens),
            Trace = trace,
        };
    }

    private static string BuildRawMessage(ParsedError error)
    {
        var parts = new List<string>();
        if (error.Message is { Length: > 0 }) parts.Add(error.Message);

        // With no message at all, the type is all there is - better than an empty query.
        if (parts.Count == 0 && error.ExceptionType is { Length: > 0 }) parts.Add(error.ExceptionType);

        return string.Join(" ", parts);
    }

    private static string[] Tokenize(string text) =>
        TokenPattern()
            .Matches(PlaceholderPattern().Replace(text, " "))
            // The token pattern allows dots and colons so that "System.NullReferenceException"
            // and "sqlite3::Error" survive as one term - which means it also swallows the
            // punctuation that ends a sentence. Trimming it here keeps "object." from becoming
            // a search term distinct from "object".
            .Select(m => m.Value.Trim('.', ':', '$', '_').ToLowerInvariant())
            .Where(t => Stoplists.Weight(t) > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(Stoplists.Weight)
            .ToArray();

    /// <summary>
    /// The precise query: exact type, a phrase from the message, the quoted literals, the code.
    /// </summary>
    private static SearchQuery BuildTight(ParsedError error, IReadOnlyList<string> tokens, IReadOnlyList<string> literals)
    {
        var terms = new List<string>();
        var parts = new List<string>();

        if (error.ErrorCode is { Length: > 0 })
        {
            // Highest-value term available: globally unique and quoted verbatim by everyone
            // who has ever hit it.
            parts.Add(error.ErrorCode);
            terms.Add(error.ErrorCode);
        }

        if (error.ExceptionType is { Length: > 0 })
        {
            parts.Add($"\"{error.ExceptionType}\"");
            terms.Add(error.ExceptionType);
        }

        foreach (var literal in literals)
        {
            parts.Add($"\"{literal}\"");
            terms.Add(literal);
        }

        // A quoted literal is already in the query as an exact phrase; repeating it as a bare
        // word adds nothing and makes the query the user is shown look careless.
        var covered = literals
            .SelectMany(l => TokenPattern().Matches(l).Select(m => m.Value.ToLowerInvariant()))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var token in tokens.Where(t => !covered.Contains(t)).Take(TightTokenCount))
        {
            parts.Add(token);
            terms.Add(token);
        }

        return new SearchQuery(
            string.Join(" ", parts),
            terms,
            "Exact type and error code, the quoted values from the message, and its most " +
            "distinctive words. Quoted values are kept here because they are often the error " +
            "itself - 'user_id' in a KeyError, or the name of a package that would not load.");
    }

    /// <summary>The fallback: short type plus a few strong words, run only when the tight query finds nothing.</summary>
    private static SearchQuery BuildRelaxed(ParsedError error, IReadOnlyList<string> tokens)
    {
        var terms = new List<string>();

        if (error.ShortExceptionType is { Length: > 0 }) terms.Add(error.ShortExceptionType);
        if (error.ErrorCode is { Length: > 0 }) terms.Add(error.ErrorCode);
        terms.AddRange(tokens.Take(RelaxedTokenCount));

        return new SearchQuery(
            string.Join(" ", terms),
            terms,
            "Short type and the four strongest words, with no phrase quoting and no literal " +
            "values - so that data unique to your run cannot make the query unmatchable.");
    }

    private static string ComputeHash(string? shortType, string normalizedMessage)
    {
        var material = $"{shortType}|{normalizedMessage}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }
}
