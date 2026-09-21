using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>Reads Dart and Flutter exception output.</summary>
public sealed partial class DartParser : IStackTraceParser
{
    public string LanguageId => "dart";
    public string DisplayName => "Dart";

    [GeneratedRegex(@"^Unhandled exception:\s*$")]
    private static partial Regex HeaderPattern();

    [GeneratedRegex(@"^(?<type>[A-Za-z_]\w*(?:Error|Exception|Failure))(?:\s*\((?<detail>[^)]*)\))?:\s*(?<msg>.*)$")]
    private static partial Regex ErrorPattern();

    [GeneratedRegex(@"^#(?<order>\d+)\s+(?<sym>.+?)\s+\((?<file>[^\s()]+?)(?::(?<line>\d+)(?::(?<col>\d+))?)?\)\s*$")]
    private static partial Regex FramePattern();

    public int Detect(IReadOnlyList<string> lines)
    {
        var score = 0;

        foreach (var line in lines)
        {
            if (HeaderPattern().IsMatch(line)) score += 50;

            if (!FramePattern().IsMatch(line)) continue;

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

    private static int FirstErrorLine(IReadOnlyList<CapturedLine> lines)
    {
        for (var i = lines.Count - 2; i >= 0; i--)
            if (ErrorPattern().IsMatch(lines[i].Text) && FramePattern().IsMatch(lines[i + 1].Text))
                return i;

        return -1;
    }
}
