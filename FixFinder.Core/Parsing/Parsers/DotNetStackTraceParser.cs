using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>
/// Reads .NET crash output, including the <c>---&gt;</c> inner-exception chain.
/// </summary>
/// <remarks>
/// The chain is the part worth care. .NET prints every nested exception header first, then the
/// innermost exception's frames, then <c>--- End of inner exception stack trace ---</c>, then
/// the next level's frames, and so on outwards. So the parser reads headers into a stack, then
/// attributes frames to the deepest exception, popping one level at each end-of-inner marker.
/// Attributing every frame to the outer exception - the obvious mistake - would put the culprit
/// in the wrapper rather than in the code that actually threw.
/// </remarks>
public sealed partial class DotNetStackTraceParser : IStackTraceParser
{
    public string LanguageId => "csharp";
    public string DisplayName => ".NET";

    [GeneratedRegex(@"^(?:Unhandled exception\.\s*)?(?<type>[\w.`+\[\],<>]*(?:Exception|Error))(?:\s*:\s*(?<msg>.*))?$")]
    private static partial Regex HeaderPattern();

    [GeneratedRegex(@"^\s*--->\s*(?:\(Inner Exception #\d+\)\s*)?(?<type>[\w.`+\[\],<>]*(?:Exception|Error))(?:\s*:\s*(?<msg>.*))?$")]
    private static partial Regex InnerHeaderPattern();

    [GeneratedRegex(@"^\s+at\s+(?<sym>.+?)(?:\s+in\s+(?<file>.+?):line\s+(?<line>\d+))?\s*$")]
    private static partial Regex FramePattern();

    private const string EndOfInnerMarker = "--- End of inner exception stack trace ---";

    /// <summary>
    /// The async resume boundary. Frames after it belong to whoever awaited the failing call.
    /// </summary>
    /// <remarks>
    /// The line itself is skipped, but the frames after it are treated as ordinary frames
    /// rather than being marked as runtime internals. They are usually the user's own awaiting
    /// method, and demoting them would push culprit selection away from exactly the code a
    /// person would want to look at first. <see cref="FrameClassifier"/> decides origin from
    /// the path instead, which is a far more reliable signal than position in the trace.
    /// </remarks>
    private const string PreviousLocationMarker = "--- End of stack trace from previous location ---";

    public int Detect(IReadOnlyList<string> lines)
    {
        var score = 0;

        foreach (var line in lines)
        {
            if (line.StartsWith("Unhandled exception.", StringComparison.Ordinal)) score += 60;
            if (line.Contains(EndOfInnerMarker, StringComparison.Ordinal)) score += 25;
            if (line.Contains(PreviousLocationMarker, StringComparison.Ordinal)) score += 25;

            // "at Namespace.Type.Method() in C:\path\File.cs:line 42" is unmistakably .NET.
            var frame = FramePattern().Match(line);
            if (frame.Success)
                score += frame.Groups["file"].Success ? 20 : 8;

            if (line.Contains("System.", StringComparison.Ordinal) &&
                line.Contains("Exception", StringComparison.Ordinal)) score += 10;
        }

        return Math.Min(score, 100);
    }

    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines)
    {
        var start = FindHeaderIndex(lines);
        if (start < 0) return null;

        // Each entry is one level of the ---> chain, outermost first.
        var levels = new List<Level>();
        var index = start;

        var first = HeaderPattern().Match(lines[index].Text);
        if (!first.Success) return null;
        levels.Add(new Level(first.Groups["type"].Value, TrimMessage(first.Groups["msg"].Value)));
        index++;

        while (index < lines.Count)
        {
            var inner = InnerHeaderPattern().Match(lines[index].Text);
            if (!inner.Success) break;
            levels.Add(new Level(inner.Groups["type"].Value, TrimMessage(inner.Groups["msg"].Value)));
            index++;
        }

        var current = levels.Count - 1;
        var end = index;

        for (; index < lines.Count; index++)
        {
            var text = lines[index].Text;

            if (text.Contains(EndOfInnerMarker, StringComparison.Ordinal))
            {
                if (current > 0) current--;
                end = index + 1;
                continue;
            }

            if (text.Contains(PreviousLocationMarker, StringComparison.Ordinal))
            {
                end = index + 1;
                continue;
            }

            // A second ---> after frames have started: an AggregateException sibling. Treat it
            // as another level rather than ending the trace.
            var sibling = InnerHeaderPattern().Match(text);
            if (sibling.Success)
            {
                levels.Add(new Level(sibling.Groups["type"].Value, TrimMessage(sibling.Groups["msg"].Value)));
                current = levels.Count - 1;
                end = index + 1;
                continue;
            }

            var frame = FramePattern().Match(text);
            if (!frame.Success)
            {
                // Blank lines inside a trace are common; anything else ends it.
                if (text.Trim().Length == 0) continue;
                break;
            }

            levels[current].Frames.Add(new ErrorFrame
            {
                Order = levels[current].Frames.Count,
                Symbol = frame.Groups["sym"].Value.Trim(),
                File = ParserHelpers.CleanFilePath(frame.Groups["file"].Success ? frame.Groups["file"].Value : null),
                Line = frame.Groups["line"].Success ? int.Parse(frame.Groups["line"].Value) : null,
                RawLine = text,
            });
            end = index + 1;
        }

        var raw = ParserHelpers.RawTextOf(lines, start, end);

        // Built inside out so each level carries the one below it as its cause.
        ParsedError? built = null;
        for (var level = levels.Count - 1; level >= 0; level--)
        {
            built = new ParsedError
            {
                LanguageId = LanguageId,
                Confidence = ConfidenceFor(levels, start, lines),
                RawText = raw,
                FirstLineSequence = lines[start].Sequence,
                ExceptionType = levels[level].Type,
                Message = levels[level].Message,
                Frames = levels[level].Frames,
                Causes = built is null ? [] : [built],
            };
        }

        return built;
    }

    private static int FindHeaderIndex(IReadOnlyList<CapturedLine> lines)
    {
        // Search backwards: when a program prints several errors, the fatal one is last, and
        // that is the one worth searching for.
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var text = lines[i].Text;
            if (text.StartsWith("Unhandled exception.", StringComparison.Ordinal)) return i;
        }

        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (!HeaderPattern().IsMatch(lines[i].Text)) continue;

            // A bare "SomeException: message" only counts if a .NET-shaped frame follows it;
            // otherwise it is just as likely to be a log line quoting an exception name.
            for (var j = i + 1; j < Math.Min(i + 4, lines.Count); j++)
                if (FramePattern().IsMatch(lines[j].Text) || InnerHeaderPattern().IsMatch(lines[j].Text))
                    return i;
        }

        return -1;
    }

    private static int ConfidenceFor(List<Level> levels, int start, IReadOnlyList<CapturedLine> lines)
    {
        var score = 55;
        if (lines[start].Text.StartsWith("Unhandled exception.", StringComparison.Ordinal)) score += 25;
        if (levels.Any(l => l.Frames.Count > 0)) score += 15;
        if (levels.Count > 1) score += 5;
        return Math.Min(score, 100);
    }

    private static string? TrimMessage(string message) =>
        message.Trim() is { Length: > 0 } trimmed ? trimmed : null;

    private sealed record Level(string Type, string? Message)
    {
        public List<ErrorFrame> Frames { get; } = [];
    }
}
