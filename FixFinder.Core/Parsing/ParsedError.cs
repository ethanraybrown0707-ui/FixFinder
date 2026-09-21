namespace FixFinder.Core.Parsing;

/// <summary>An error recognised in a program's output, in the shape every language reduces to.</summary>
public sealed class ParsedError
{
    public required string LanguageId { get; init; }

    public required int Confidence { get; init; }

    public required string RawText { get; init; }

    public required int FirstLineSequence { get; init; }

    public string? ExceptionType { get; init; }

    public string? ErrorCode { get; init; }

    public string? Message { get; init; }

    public required IReadOnlyList<ErrorFrame> Frames { get; init; }

    public ErrorFrame? CulpritFrame { get; set; }

    public IReadOnlyList<ParsedError> Causes { get; init; } = [];

    public string? ShortExceptionType
    {
        get
        {
            if (ExceptionType is null) return null;
            var cut = ExceptionType.LastIndexOfAny(['.', ':', '\\', '/']);
            return cut >= 0 && cut < ExceptionType.Length - 1 ? ExceptionType[(cut + 1)..] : ExceptionType;
        }
    }

    public ParsedError RootCause => Causes.Count > 0 ? Causes[^1].RootCause : this;

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
