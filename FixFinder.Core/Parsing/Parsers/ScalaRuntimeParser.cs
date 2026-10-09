using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>
/// Reads a Scala program's crash. A Scala program runs on the JVM, so its crash is Java's stack trace - but with frames in
/// the program's own .scala and .sc files: <c>at Marks$.average(Marks.scala:2)</c>. It is read as Java's is, and called
/// Scala's, so the report, the search online and the fixes treat it as a crash of a Scala program.
/// </summary>
public sealed partial class ScalaRuntimeParser : IStackTraceParser
{
    private readonly JavaStackTraceParser _jvm = new();

    public string LanguageId => "scala";

    public string DisplayName => "Scala (JVM stack trace)";

    /// <summary>A frame of a stack trace in a Scala file: <c>at Marks$.average(Marks.scala:2)</c>.</summary>
    [GeneratedRegex(@"^\s+at\s+\S+\([^():]+\.(?:scala|sc):\d+\)")]
    private static partial Regex ScalaFrame();

    public int Detect(IReadOnlyList<string> lines) =>
        lines.Any(line => ScalaFrame().IsMatch(line)) ? Math.Min(100, _jvm.Detect(lines) + 10) : 0;

    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines) => _jvm.Parse(lines) is { } jvm ? AsScala(jvm) : null;

    private static ParsedError AsScala(ParsedError jvm) => new()
    {
        LanguageId = "scala",
        Confidence = jvm.Confidence,
        RawText = jvm.RawText,
        FirstLineSequence = jvm.FirstLineSequence,
        ExceptionType = jvm.ExceptionType,
        ErrorCode = jvm.ErrorCode,
        Message = jvm.Message,
        Frames = jvm.Frames,
        CulpritFrame = jvm.CulpritFrame,
        Causes = jvm.Causes.Select(AsScala).ToList(),
    };
}
