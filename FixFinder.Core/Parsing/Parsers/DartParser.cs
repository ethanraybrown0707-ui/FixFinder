using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>Reads Dart and Flutter exception output.</summary>
/// <remarks>
/// Dart numbers its frames <c>#0</c>, <c>#1</c> the way PHP does, and the two are told apart by
/// which side of the line the location sits: PHP writes <c>#0 /app/x.php(5): doThing()</c> and
/// Dart writes <c>#0      doThing (file:///app/x.dart:5:13)</c>.
/// <para>
/// <b>Most Dart frames do not name a file on this disk.</b> A frame in <c>dart:core-patch/…</c>
/// is inside the SDK and one in <c>package:foo/…</c> is inside a dependency; only the
/// <c>file:///</c> ones can be opened. They are all kept, because the frame that matters for
/// choosing a culprit is usually a <c>file:///</c> one further down and dropping the others would
/// renumber the trace.
/// </para>
/// </remarks>
public sealed partial class DartParser : IStackTraceParser
{
    public string LanguageId => "dart";
    public string DisplayName => "Dart";

    [GeneratedRegex(@"^Unhandled exception:\s*$")]
    private static partial Regex HeaderPattern();

    /// <summary>
    /// The error line under the header - <c>RangeError (index): Invalid value: …</c>.
    /// </summary>
    /// <remarks>
    /// The parenthesised part is a subtype rather than a detail of the message, and it belongs in
    /// the type: <c>RangeError (index)</c> and <c>RangeError (length)</c> are asked about
    /// separately and answered separately.
    /// </remarks>
    [GeneratedRegex(@"^(?<type>[A-Za-z_]\w*(?:Error|Exception|Failure))(?:\s*\((?<detail>[^)]*)\))?:\s*(?<msg>.*)$")]
    private static partial Regex ErrorPattern();

    /// <summary>
    /// A numbered frame. The line and column are optional, because SDK frames do not carry them.
    /// </summary>
    /// <remarks>
    /// Requiring them looked safe and was not. A real trace opens with
    /// <c>#0      List.[] (dart:core-patch/growable_array.dart)</c> - no position at all - so the
    /// very first frame failed to match, the loop stopped there, and every frame below it was
    /// lost, including the only one naming a file on this disk. The failure was silent: the error
    /// still parsed, with its type and message intact and nowhere to go.
    /// </remarks>
    [GeneratedRegex(@"^#(?<order>\d+)\s+(?<sym>.+?)\s+\((?<file>[^\s()]+?)(?::(?<line>\d+)(?::(?<col>\d+))?)?\)\s*$")]
    private static partial Regex FramePattern();

    public int Detect(IReadOnlyList<string> lines)
    {
        var score = 0;

        foreach (var line in lines)
        {
            if (HeaderPattern().IsMatch(line)) score += 50;

            if (!FramePattern().IsMatch(line)) continue;

            // A numbered frame alone is ambiguous with PHP; a numbered frame pointing into a Dart
            // URI scheme is not.
            score += line.Contains("dart:", StringComparison.Ordinal) ||
                     line.Contains("package:", StringComparison.Ordinal) ||
                     line.Contains(".dart:", StringComparison.Ordinal)
                ? 30
                : 5;
        }

        return Math.Min(score, 100);
    }

    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines)
    {
        var headerIndex = -1;
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (!HeaderPattern().IsMatch(lines[i].Text)) continue;
            headerIndex = i;
            break;
        }

        // Flutter prints the frames without the "Unhandled exception:" banner often enough that
        // refusing to parse without it would lose the commonest real case.
        var errorIndex = headerIndex >= 0 ? headerIndex + 1 : FirstErrorLine(lines);
        if (errorIndex < 0 || errorIndex >= lines.Count) return null;

        var error = ErrorPattern().Match(lines[errorIndex].Text);
        if (!error.Success) return null;

        var frames = new List<ErrorFrame>();
        var end = errorIndex + 1;

        for (var i = errorIndex + 1; i < lines.Count; i++)
        {
            var frame = FramePattern().Match(lines[i].Text);
            if (!frame.Success) break;

            frames.Add(new ErrorFrame
            {
                Order = frames.Count,
                Symbol = frame.Groups["sym"].Value.Trim(),
                File = ParserHelpers.CleanFilePath(frame.Groups["file"].Value),
                Line = frame.Groups["line"].Success ? int.Parse(frame.Groups["line"].Value) : null,
                Column = frame.Groups["col"].Success ? int.Parse(frame.Groups["col"].Value) : null,
                RawLine = lines[i].Text,
            });

            end = i + 1;
        }

        // Nothing but a line that looks like "SomethingError: ..." is too weak to claim; plenty of
        // languages print that, and the generic parser exists for exactly this case.
        if (frames.Count == 0 && headerIndex < 0) return null;

        var type = error.Groups["detail"].Success && error.Groups["detail"].Value.Length > 0
            ? $"{error.Groups["type"].Value} ({error.Groups["detail"].Value})"
            : error.Groups["type"].Value;

        var start = headerIndex >= 0 ? headerIndex : errorIndex;

        return new ParsedError
        {
            LanguageId = LanguageId,
            Confidence = headerIndex >= 0 ? 90 : 75,
            RawText = ParserHelpers.RawTextOf(lines, start, end),
            FirstLineSequence = lines[start].Sequence,
            ExceptionType = type,
            Message = error.Groups["msg"].Value.Trim() is { Length: > 0 } message ? message : null,
            Frames = frames,
        };
    }

    /// <summary>The last error line that is followed by at least one Dart frame.</summary>
    private static int FirstErrorLine(IReadOnlyList<CapturedLine> lines)
    {
        for (var i = lines.Count - 2; i >= 0; i--)
            if (ErrorPattern().IsMatch(lines[i].Text) && FramePattern().IsMatch(lines[i + 1].Text))
                return i;

        return -1;
    }
}
