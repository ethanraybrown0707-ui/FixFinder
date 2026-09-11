namespace FixFinder.Core.Parsing;

/// <summary>Whose code a frame belongs to, which decides whether searching the web is worth it.</summary>
public enum FrameOrigin
{
    Unknown,

    /// <summary>Inside the source root the user chose - their own code.</summary>
    FirstParty,

    /// <summary>A library or package: node_modules, site-packages, the NuGet cache, a crate registry.</summary>
    ThirdParty,

    /// <summary>The language runtime or standard library itself.</summary>
    Runtime,
}

/// <summary>One frame of a parsed stack trace.</summary>
public sealed class ErrorFrame
{
    /// <summary>
    /// Position in the stack, where <b>0 is always the innermost frame - where it threw</b>.
    /// </summary>
    /// <remarks>
    /// Worth stating loudly because the languages disagree: Python prints frames
    /// outermost-first and everyone else prints innermost-first, so
    /// <c>PythonTracebackParser</c> reverses them before building this list. Getting it
    /// backwards does not throw or fail a build - it just makes every culprit-frame choice
    /// wrong, quietly, in a way that is very hard to spot from the outside.
    /// </remarks>
    public required int Order { get; init; }

    /// <summary>Function, method or symbol name, as printed.</summary>
    public string? Symbol { get; init; }

    /// <summary>Source file, when the trace carries one. Often absent from a release build.</summary>
    public string? File { get; init; }

    public int? Line { get; init; }

    public int? Column { get; init; }

    /// <summary>Assembly, package, jar, crate or module the frame came from, when printed.</summary>
    public string? Module { get; init; }

    /// <summary>Set by <see cref="FrameClassifier"/> after parsing, not by the parser itself.</summary>
    public FrameOrigin Origin { get; set; } = FrameOrigin.Unknown;

    /// <summary>The line exactly as the program printed it, kept for display and for debugging a parser.</summary>
    public required string RawLine { get; init; }

    /// <summary>Short "file:line" form for the UI, falling back to the symbol when there is no file.</summary>
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
