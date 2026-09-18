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

    /// <summary>True when the example is this file's own lines with the fix made, rather than a general illustration.</summary>
    public bool ExampleIsFromYourCode { get; init; }

    /// <summary>How the fix was proved, or null when it was not.</summary>
    public string? FixCheckedBy { get; init; }

    public string RuleId { get; init; } = "";

    /// <summary>The error the compiler or the run reported, kept so it can be looked up online.</summary>
    public ParsedError? Error { get; init; }

    public LocalFix? Fix { get; init; }

    /// <summary>The logic pattern that finds the same mistake, so a compiler warning and a pattern on one line count once.</summary>
    public string? Family { get; init; }

    public string Location => Line is { } line ? $"{Path.GetFileName(File)}, line {line}" : Path.GetFileName(File);
}
