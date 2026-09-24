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

    /// <summary>Inputs that make the line fail, found by following the program path by path.</summary>
    public string? Witness { get; init; }

    /// <summary>The lines that decide the value that goes wrong, in order.</summary>
    public IReadOnlyList<int>? Slice { get; init; }

    /// <summary>What running the code with the inputs that break it showed.</summary>
    public string? Confirmation { get; init; }

    /// <summary>What the proposed fix changes in what the program does, found by comparing it with the original path by path.</summary>
    public IReadOnlyList<string>? FixChanges { get; init; }

    /// <summary>
    /// The user's own lines beside the lines the fix would leave them as, built from the fix rather than described.
    /// Null whenever there is no fix, so nothing is ever shown as a change that FixFinder did not actually work out.
    /// </summary>
    public CodeChange? Change { get; init; }

    private readonly Explained? _explanations;

    /// <summary>
    /// The explanation at each depth a reader might want it, carried on the finding so changing depth is a matter of
    /// reading a different string rather than checking the program again. <see cref="Explanation"/> is the middle one.
    /// </summary>
    /// <remarks>
    /// Where nothing sets this, every depth gives the one explanation that was written. That is the honest default: a
    /// finding whose wording nobody has written three ways should repeat itself rather than have two of them invented.
    /// </remarks>
    public Explained Explanations
    {
        get => _explanations ?? Explained.Of(Explanation);
        init => _explanations = value;
    }

    public string Location => Line is { } line ? $"{Path.GetFileName(File)}, line {line}" : Path.GetFileName(File);
}
