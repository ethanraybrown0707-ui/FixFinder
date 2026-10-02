using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>Reads Node.js / V8 stack traces.</summary>
public sealed partial class NodeStackTraceParser : IStackTraceParser
{
    public string LanguageId => "node";
    public string DisplayName => "Node.js";

    // A frame can end with the { that opens the properties Node prints after an error's frames.
    [GeneratedRegex(@"^\s+at\s+(?:async\s+)?(?:(?<sym>.+?)\s+\()?(?<file>(?:[A-Za-z]:[\\/]|file:\/\/|\/|node:|[\w.\-]+[\\/]).*?):(?<line>\d+):(?<col>\d+)\)?(?:\s*\{)?\s*$")]
    private static partial Regex FramePattern();

    // The code in brackets is Node's own - Error [ERR_MODULE_NOT_FOUND] - or the system's: AggregateError [ECONNREFUSED].
    [GeneratedRegex(@"^(?<type>(?:[A-Z]\w*)?(?:Error|Exception))(?:\s*\[(?<code>[A-Z][A-Z0-9_]*)\])?(?:\s*:\s*(?<msg>.*))?$")]
    private static partial Regex HeaderPattern();

    /// <summary>An error printed as an object, with what caused it inside: [TypeError: fetch failed] {, as fetch fails.</summary>
    [GeneratedRegex(@"^\[(?<type>(?:[A-Z]\w*)?(?:Error|Exception))(?:\s*\[(?<code>[A-Z][A-Z0-9_]*)\])?(?::\s*(?<msg>.*?))?\]\s*\{\s*$")]
    private static partial Regex PrintedObjectHeaderPattern();

    private static Match Header(string text) => HeaderPattern().Match(text) is { Success: true } header ? header : PrintedObjectHeaderPattern().Match(text);

    [GeneratedRegex(@"^(?<file>(?:[A-Za-z]:[\\/]|file:\/\/|\/).+?\.[mc]?js):(?<line>\d+)\s*$")]
    private static partial Regex LocationPattern();

    [GeneratedRegex(@"^\s*\^+\s*$")]
    private static partial Regex CaretPattern();

    public int Detect(IReadOnlyList<string> lines)
    {
        var score = 0;

        foreach (var line in lines)
        {
            if (line.Contains("node:internal/", StringComparison.Ordinal)) score += 35;
            if (line.Contains("node_modules", StringComparison.Ordinal)) score += 10;

            var frame = FramePattern().Match(line);
            if (frame.Success)
            {
                score += 18;
                if (frame.Groups["sym"].Success) score += 4;
            }

            if (Header(line).Success) score += 12;
        }

        return Math.Min(score, 100);
    }

    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines)
    {
        var headerIndex = -1;
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (!Header(lines[i].Text).Success) continue;

            for (var j = i + 1; j < Math.Min(i + 12, lines.Count); j++)
            {
                if (FramePattern().IsMatch(lines[j].Text))
                {
                    headerIndex = i;
                    break;
                }

                if (!IsRequireStack(lines[j].Text) && !IsCause(lines[j].Text) && j >= i + 2) break;
            }
            if (headerIndex >= 0) break;
        }

        if (headerIndex < 0) return null;

        var header = Header(lines[headerIndex].Text);
        var frames = new List<ErrorFrame>();
        var end = headerIndex + 1;

        for (var i = headerIndex + 1; i < lines.Count; i++)
        {
            var text = lines[i].Text;
            var frame = FramePattern().Match(text);
            if (!frame.Success)
            {
                if (text.Trim().Length == 0 || (frames.Count == 0 && (IsRequireStack(text) || IsCause(text))) ||
                    (text.Length > 0 && char.IsWhiteSpace(text[0]) && text.TrimStart().StartsWith("at ", StringComparison.Ordinal))) continue;
                break;
            }

            var file = ParserHelpers.CleanFilePath(frame.Groups["file"].Value);
            frames.Add(new ErrorFrame
            {
                Order = frames.Count,
                Symbol = frame.Groups["sym"].Success ? frame.Groups["sym"].Value.Trim() : null,
                File = file,
                Line = int.Parse(frame.Groups["line"].Value),
                Column = int.Parse(frame.Groups["col"].Value),
                Origin = file is not null && file.StartsWith("node:", StringComparison.Ordinal)
                    ? FrameOrigin.Runtime
                    : FrameOrigin.Unknown,
                RawLine = text,
            });
            end = i + 1;
        }

        if (frames.Count == 0) return null;

        end = EndOfWhatNodePrinted(lines, end);

        if (Location(lines, headerIndex) is { } location &&
            !(string.Equals(frames[0].File, location.File, StringComparison.OrdinalIgnoreCase) && frames[0].Line == location.Line))
        {
            frames =
            [
                location,
                .. frames.Select((frame, i) => new ErrorFrame
                {
                    Order = i + 1, Symbol = frame.Symbol, File = frame.File, Line = frame.Line, Column = frame.Column,
                    Module = frame.Module, Origin = frame.Origin, RawLine = frame.RawLine,
                }),
            ];
        }

        return new ParsedError
        {
            LanguageId = LanguageId,
            Confidence = 88,
            RawText = ParserHelpers.RawTextOf(lines, headerIndex, end),
            FirstLineSequence = lines[headerIndex].Sequence,
            ExceptionType = header.Groups["type"].Value,
            ErrorCode = header.Groups["code"].Success ? header.Groups["code"].Value : null,
            Message = header.Groups["msg"].Success && header.Groups["msg"].Value.Trim().Length > 0
                ? header.Groups["msg"].Value.Trim()
                : null,
            Frames = frames,
        };
    }

    private static bool IsRequireStack(string text) =>
        text.StartsWith("Require stack:", StringComparison.Ordinal) || text.StartsWith("- ", StringComparison.Ordinal);

    /// <summary>The line that says, inside an error printed as an object, what caused it: [cause]: AggregateError [ECONNREFUSED]:.</summary>
    private static bool IsCause(string text) => text.TrimStart().StartsWith("[cause]:", StringComparison.Ordinal);

    /// <summary>
    /// Where what Node printed of the error ends, after the frames read: where an 'error' event was emitted from, with its
    /// frames, and the properties Node gives the error between { and } - its code and, for a connection, each address and
    /// port it tried. They are part of the error's text, not frames of its own.
    /// </summary>
    private static int EndOfWhatNodePrinted(IReadOnlyList<CapturedLine> lines, int end)
    {
        while (end < lines.Count &&
               (lines[end].Text.StartsWith("Emitted 'error' event", StringComparison.Ordinal) || FramePattern().IsMatch(lines[end].Text)))
        {
            end++;
        }

        if (end == 0 || !lines[end - 1].Text.TrimEnd().EndsWith('{')) return end;

        // The properties close with a } of their own on a line of its own.
        for (var i = end; i < lines.Count; i++)
        {
            if (lines[i].Text.Trim() == "}") return i + 1;
        }

        return end;
    }

    private static ErrorFrame? Location(IReadOnlyList<CapturedLine> lines, int headerIndex)
    {
        for (var k = headerIndex - 1; k >= Math.Max(0, headerIndex - 6); k--)
        {
            if (LocationPattern().Match(lines[k].Text) is not { Success: true } location) continue;

            int? column = k + 2 < headerIndex && CaretPattern().IsMatch(lines[k + 2].Text) ? lines[k + 2].Text.IndexOf('^') + 1 : null;

            return new ErrorFrame
            {
                Order = 0,
                File = ParserHelpers.CleanFilePath(location.Groups["file"].Value),
                Line = int.Parse(location.Groups["line"].Value),
                Column = column,
                RawLine = lines[k].Text,
            };
        }

        return null;
    }
}
