using FixFinder.Core.LocalFixes;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.Checking;

public enum Severity
{
    Error,
    Warning,
    Suggestion,
}

public enum Confidence
{
    Certain,
    Likely,
    Possible,
}

public enum FindingKind
{
    Syntax,
    Runtime,
    Logic,
    Style,
}

public sealed record Finding
{
    public required FindingKind Kind { get; init; }
    public required Severity Severity { get; init; }
    public required Confidence Confidence { get; init; }

    public required string File { get; init; }
    public int? Line { get; init; }

    public required string Title { get; init; }
    public required string Explanation { get; init; }
    public required string WhyItMatters { get; init; }
    public required string SuggestedFix { get; init; }
    public required string CorrectedExample { get; init; }

    public bool ExampleIsFromYourCode { get; init; }

    public string? FixCheckedBy { get; init; }

    public string RuleId { get; init; } = "";

    public ParsedError? Error { get; init; }

    public LocalFix? Fix { get; init; }

    public string? Family { get; init; }

    /// <summary>The technique that found it, when it was one of the analyses rather than a compiler, a run or a pattern.</summary>
    public string? FoundBy { get; init; }

    public string Location => Line is { } line ? $"{Path.GetFileName(File)}, line {line}" : Path.GetFileName(File);
}
