using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.Fingerprinting;

/// <summary>A search query, with the terms it was built from and why.</summary>
public sealed record SearchQuery(string Text, IReadOnlyList<string> Terms, string Explanation);

/// <summary>A parsed error reduced to the facts worth searching for, plus the two queries built from them.</summary>
public sealed record ErrorFingerprint
{
    public required string LanguageId { get; init; }
    public string? ExceptionType { get; init; }
    public string? ShortExceptionType { get; init; }
    public string? ErrorCode { get; init; }
    public required string NormalizedMessage { get; init; }
    public required IReadOnlyList<string> Tokens { get; init; }
    public IReadOnlyList<string> QuotedLiterals { get; init; } = [];

    public string? CulpritFile { get; init; }

    public string? NearestThirdPartyModule { get; init; }

    public required bool CulpritIsFirstParty { get; init; }

    public required string Hash { get; init; }

    public required SearchQuery Tight { get; init; }
    public required SearchQuery Relaxed { get; init; }

    public IReadOnlyList<AppliedNormalization> Trace { get; init; } = [];
}

/// <summary>Builds an <c>ErrorFingerprint</c> from a <c>ParsedError</c>.</summary>
public static partial class FingerprintBuilder
{
    private const int TightTokenCount = 12;

    private const int RelaxedTokenCount = 4;

    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_.:$]*")]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"'([^']{2,})'|""([^""]{2,})""")]
    private static partial Regex QuotedPattern();

    [GeneratedRegex(@"<(?:path|guid|addr|time|num|val)>")]
    private static partial Regex PlaceholderPattern();

    public static ErrorFingerprint Build(ParsedError error)
    {
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

        if (parts.Count == 0 && error.ExceptionType is { Length: > 0 }) parts.Add(error.ExceptionType);

        return string.Join(" ", parts);
    }

    private static string[] Tokenize(string text) =>
        TokenPattern()
            .Matches(PlaceholderPattern().Replace(text, " "))
            .Select(m => m.Value.Trim('.', ':', '$', '_').ToLowerInvariant())
            .Where(t => Stoplists.Weight(t) > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(Stoplists.Weight)
            .ToArray();

    private static SearchQuery BuildTight(ParsedError error, IReadOnlyList<string> tokens, IReadOnlyList<string> literals)
    {
        var terms = new List<string>();
        var parts = new List<string>();

        if (error.ErrorCode is { Length: > 0 })
        {
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
