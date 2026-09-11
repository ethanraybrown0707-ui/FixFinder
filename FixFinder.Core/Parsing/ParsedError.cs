namespace FixFinder.Core.Parsing;

/// <summary>An error recognised in a program's output, in the shape every language reduces to.</summary>
public sealed class ParsedError
{
    /// <summary>Which parser produced this - "csharp", "python", "go", "generic" and so on.</summary>
    public required string LanguageId { get; init; }

    /// <summary>
    /// 0-100 confidence that this really is an error and was read correctly.
    /// </summary>
    /// <remarks>
    /// Carried all the way to the UI rather than collapsed into a bool. A low-confidence read
    /// from the generic fallback and a textbook Python traceback should not look alike to
    /// someone deciding whether to trust the search results built from them.
    /// </remarks>
    public required int Confidence { get; init; }

    /// <summary>The exact slice of output the parser claimed, joined with newlines.</summary>
    public required string RawText { get; init; }

    /// <summary>Sequence number of the first captured line this error covers.</summary>
    public required int FirstLineSequence { get; init; }

    /// <summary>Exception or error type as printed - "System.NullReferenceException", "KeyError".</summary>
    public string? ExceptionType { get; init; }

    /// <summary>
    /// Compiler or tool error code: CS1234, C2065, E0308.
    /// </summary>
    /// <remarks>
    /// Kept separate from the message because it is the single best search term available.
    /// An exact code match finds the right answer where message tokens return noise, so the
    /// query builder and the ranker both weight it heavily.
    /// </remarks>
    public string? ErrorCode { get; init; }

    public string? Message { get; init; }

    /// <summary>Frames, innermost first. May be empty - plenty of real errors print no stack at all.</summary>
    public required IReadOnlyList<ErrorFrame> Frames { get; init; }

    /// <summary>
    /// The frame the fix most likely belongs in, chosen by <see cref="CulpritFrameSelector"/>.
    /// </summary>
    public ErrorFrame? CulpritFrame { get; set; }

    /// <summary>
    /// Inner exceptions, "Caused by:" chains and Python's <c>__cause__</c>, outermost first.
    /// </summary>
    public IReadOnlyList<ParsedError> Causes { get; init; } = [];

    /// <summary>Last dotted or scoped segment of the type - "NullReferenceException", "KeyError".</summary>
    public string? ShortExceptionType
    {
        get
        {
            if (ExceptionType is null) return null;
            var cut = ExceptionType.LastIndexOfAny(['.', ':', '\\', '/']);
            return cut >= 0 && cut < ExceptionType.Length - 1 ? ExceptionType[(cut + 1)..] : ExceptionType;
        }
    }

    /// <summary>
    /// The innermost error in the chain - the one that actually went wrong.
    /// </summary>
    /// <remarks>
    /// A wrapped failure prints the outer exception first ("Could not load the basket") but the
    /// searchable fact is the inner one ("NullReferenceException"). Searching the wrapper's
    /// message finds nothing, because that text is unique to this program.
    /// </remarks>
    public ParsedError RootCause => Causes.Count > 0 ? Causes[^1].RootCause : this;

    /// <summary>One-line summary for the window's detected-error label.</summary>
    /// <remarks>
    /// A wrapped failure names both ends of the chain, because showing only one is misleading
    /// either way: the outer type with the inner location reads as though the wrapper threw at
    /// that line, and the inner type alone hides the message the program actually printed.
    /// </remarks>
    public string Summary
    {
        get
        {
            var root = RootCause;
            var type = ShortExceptionType ?? ErrorCode ?? "error";

            if (!ReferenceEquals(root, this) && root.ShortExceptionType is { Length: > 0 } innerType)
                type = $"{type} -> {innerType}";

            var where = CulpritFrame is not null ? $" at {CulpritFrame.Location}" : "";
            var message = Message is { Length: > 0 } ? $": {Truncate(Message, 90)}" : "";

            return $"{type}{message}{where}";
        }
    }

    private static string Truncate(string text, int max)
    {
        var oneLine = text.ReplaceLineEndings(" ").Trim();
        return oneLine.Length <= max ? oneLine : oneLine[..(max - 1)] + "…";
    }
}
