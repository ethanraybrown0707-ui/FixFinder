using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.Checking.Guides;

/// <summary>What a kind of mistake means, why it matters, how to fix it, and what the fixed code looks like.</summary>
public sealed record MistakeGuide(string Explanation, string WhyItMatters, string SuggestedFix, string Example)
{
    /// <summary>A short name for the mistake, for findings that have no error message to use as one.</summary>
    public string? Title { get; init; }
}

/// <summary>A guide and the errors it describes. Every condition given must hold.</summary>
public sealed class GuideEntry
{
    public required MistakeGuide Guide { get; init; }
    public IReadOnlyList<string> RuleIds { get; init; } = [];
    public IReadOnlyList<string> Codes { get; init; } = [];
    public IReadOnlyList<string> ExceptionTypes { get; init; } = [];
    public Regex? Message { get; init; }

    public bool Describes(ParsedError error)
    {
        if (Codes.Count == 0 && ExceptionTypes.Count == 0 && Message is null) return false;

        if (Codes.Count > 0 && !Codes.Contains(error.ErrorCode ?? "", StringComparer.OrdinalIgnoreCase)) return false;

        if (ExceptionTypes.Count > 0 &&
            !ExceptionTypes.Contains(error.ShortExceptionType ?? "", StringComparer.Ordinal) &&
            !ExceptionTypes.Contains(error.ExceptionType ?? "", StringComparer.Ordinal))
            return false;

        return Message is null || Message.IsMatch(error.Message ?? "");
    }
}
