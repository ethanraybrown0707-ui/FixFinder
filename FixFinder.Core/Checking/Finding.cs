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

    /// <summary>Work the program does more times than it needs to. Never an error: a slow program that is right is right.</summary>
    Performance,
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

    /// <summary>
    /// How far the fix was actually taken - compiled, run, matched against what the program should print - and what
    /// each of those showed. A fix nobody tested carries no claim rather than an empty one.
    /// </summary>
    public Verification Verified { get; init; } = Verification.NotTested;

    public string RuleId { get; init; } = "";

    /// <summary>
    /// What this finding is, as one string: where it is and what was found there. Two reports of the same mistake in
    /// the same place are the same finding, which is what lets one be named as the cause of another.
    /// </summary>
    public string Id => $"{Path.GetFileName(File)}:{Line?.ToString() ?? "?"}:{RuleId}:{Title}";

    /// <summary>
    /// The finding this one follows from, where there is real evidence that fixing that one removes this one. Null
    /// whenever the evidence is not that specific, because a wrong grouping hides a real problem inside another.
    /// </summary>
    public Relation? CausedBy { get; init; }

    public ParsedError? Error { get; init; }

    public LocalFix? Fix { get; init; }

    /// <summary>
    /// Where the fix came from - one of FixFinder's own rules, or the page it was taken from - so the reader can
    /// check it rather than take it on trust. Null where a fix arrived with no source to name.
    /// </summary>
    public FixOrigin? CameFrom { get; init; }

    /// <summary>
    /// Where to read more about what this is, on the documentation for the language it is written in. A search
    /// rather than a page, and nothing at all for a language whose documentation search has not been checked.
    /// </summary>
    public FurtherReading? FurtherReading => Documentation.For(this);

    /// <summary>The CWE entry this is an instance of - MITRE's independent description of the weakness - or null when none fits exactly.</summary>
    public Weakness? Weakness => Weaknesses.For(RuleId, File);

    public string? Family { get; init; }

    /// <summary>The technique that found it, when it was one of the analyses rather than a compiler, a run or a pattern.</summary>
    public string? FoundBy { get; init; }

    /// <summary>Inputs that make the line fail, found by following the program path by path.</summary>
    public string? Witness { get; init; }

    /// <summary>The lines that decide the value that goes wrong, in order.</summary>
    public IReadOnlyList<int>? Slice { get; init; }

    /// <summary>What running the code with the inputs that break it showed.</summary>
    public string? Confirmation { get; init; }

    /// <summary>
    /// What the variables held each time the failing line ran, taken from that same run. Null wherever the program
    /// could not be run, because a trace of values nobody observed would be a guess dressed as evidence.
    /// </summary>
    public StateTrace? State { get; init; }

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
