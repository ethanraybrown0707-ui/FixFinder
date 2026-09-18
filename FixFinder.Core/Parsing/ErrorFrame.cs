namespace FixFinder.Core.Parsing;

/// <summary>Whose code a frame belongs to, which decides whether searching the web is worth it.</summary>
public enum FrameOrigin
{
    Unknown,

    FirstParty,

    ThirdParty,

    Runtime,
}

/// <summary>One frame of a parsed stack trace.</summary>
public sealed class ErrorFrame
{
    public required int Order { get; init; }

    public string? Symbol { get; init; }

    public string? File { get; init; }

    public int? Line { get; init; }

    public int? Column { get; init; }

    public string? Module { get; init; }

    public FrameOrigin Origin { get; set; } = FrameOrigin.Unknown;

    public required string RawLine { get; init; }

    public string Location
    {
        get
        {
            if (File is null) return Symbol ?? "(unknown)";
            var name = System.IO.Path.GetFileName(File);
            return Line.HasValue ? $"{name}:{Line}" : name;
        }
    }
}
