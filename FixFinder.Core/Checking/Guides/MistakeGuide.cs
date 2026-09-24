using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.Checking.Guides;

/// <summary>What a kind of mistake means, why it matters, how to fix it, and what the fixed code looks like.</summary>
public sealed record MistakeGuide(string Explanation, string WhyItMatters, string SuggestedFix, string Example)
{
    public string? Title { get; init; }

    /// <summary>
    /// The same explanation for someone new to programming, and for someone who works on this every day.
    /// </summary>
    /// <remarks>
    /// Both are optional, and where one is missing <see cref="Explanation"/> is used at that level too. Saying the same
    /// thing three times is better than saying a different thing at one of them: the level changes who is reading, not
    /// what is true of the program.
    /// </remarks>
    public string? ForBeginners { get; init; }

    /// <inheritdoc cref="ForBeginners"/>
    public string? ForTechnical { get; init; }

    /// <summary>The explanation at each depth, falling back to the one wording wherever another was not written.</summary>
    public Explained Explanations => Explained.Of(Explanation, ForBeginners, ForTechnical);
}

/// <summary>A guide and the errors it describes.</summary>
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
