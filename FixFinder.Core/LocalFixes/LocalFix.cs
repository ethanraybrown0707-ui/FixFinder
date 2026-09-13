namespace FixFinder.Core.LocalFixes;

/// <summary>One change to one file, worked out from an error and the code the error names.</summary>
/// <remarks>
/// Expressed as a run of lines replaced by another run, because that is the one shape every rule
/// here needs - a line corrected, a line inserted, a brace appended - and the one shape a unified
/// diff can say without ambiguity. A rule that needed two separate places changed would be making
/// a bigger claim than any of these do, and is not something this is for.
/// </remarks>
public sealed record LocalFix
{
    /// <summary>Which rule produced it, for the log and the candidate's id.</summary>
    public required string RuleId { get; init; }

    /// <summary>What the change is, short and imperative: <c>Add import java.util.List</c>.</summary>
    public required string Title { get; init; }

    /// <summary>Why this is the change the error asks for, in terms of what the error said.</summary>
    public required string Explanation { get; init; }

    /// <summary>The file being changed, as a full path.</summary>
    public required string File { get; init; }

    /// <summary>The first line replaced, 1-based. For an insertion, the new lines go in front of it.</summary>
    public required int StartLine { get; init; }

    /// <summary>How many existing lines the new ones replace. Zero for a pure insertion.</summary>
    public required int RemoveCount { get; init; }

    public required IReadOnlyList<string> NewLines { get; init; }

    /// <summary>
    /// Text in the compiler's output that the change must make go away, when the problem is a
    /// warning rather than an error.
    /// </summary>
    /// <remarks>
    /// Null for everything reported as an error, which is checked by comparing the errors
    /// themselves. A warning never stops a build, so there is no error to compare - but it can be
    /// the whole explanation for a crash, as <c>'malloc' undefined</c> is on 64-bit Windows.
    /// </remarks>
    public string? ResolvesWarning { get; init; }

    /// <summary>A replacement of a single line.</summary>
    public static LocalFix ReplaceLine(
        string ruleId, string title, string explanation, string file, int line, string newText) => new()
    {
        RuleId = ruleId, Title = title, Explanation = explanation, File = file,
        StartLine = line, RemoveCount = 1, NewLines = [newText],
    };

    /// <summary>New lines placed in front of <paramref name="beforeLine"/>.</summary>
    public static LocalFix Insert(
        string ruleId, string title, string explanation, string file, int beforeLine,
        IReadOnlyList<string> lines) => new()
    {
        RuleId = ruleId, Title = title, Explanation = explanation, File = file,
        StartLine = beforeLine, RemoveCount = 0, NewLines = lines,
    };

    /// <summary>The whole file with this change made, or null when the change does not fit it.</summary>
    public IReadOnlyList<string>? ApplyTo(SourceFile source)
    {
        if (StartLine < 1 || RemoveCount < 0 || StartLine - 1 + RemoveCount > source.Count) return null;

        return
        [
            .. source.Lines.Take(StartLine - 1),
            .. NewLines,
            .. source.Lines.Skip(StartLine - 1 + RemoveCount),
        ];
    }
}
