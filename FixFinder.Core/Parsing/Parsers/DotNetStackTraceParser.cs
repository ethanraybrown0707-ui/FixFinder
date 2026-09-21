using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>Reads .NET crash output, including the <c>---&gt;</c> inner-exception chain.</summary>
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

    private const string PreviousLocationMarker = "--- End of stack trace from previous location ---";

    public int Detect(IReadOnlyList<string> lines)
    {
        var score = 0;

        foreach (var line in lines)
        {
            if (line.StartsWith("Unhandled exception.", StringComparison.Ordinal)) score += 60;
            if (line.Contains(EndOfInnerMarker, StringComparison.Ordinal)) score += 25;
            if (line.Contains(PreviousLocationMarker, StringComparison.Ordinal)) score += 25;

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
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var text = lines[i].Text;
            if (text.StartsWith("Unhandled exception.", StringComparison.Ordinal)) return i;
        }

        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (!HeaderPattern().IsMatch(lines[i].Text)) continue;

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
