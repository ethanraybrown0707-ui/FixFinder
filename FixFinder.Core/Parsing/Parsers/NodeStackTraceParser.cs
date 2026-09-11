using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>Reads Node.js / V8 stack traces.</summary>
/// <remarks>
/// Two details do real work here. Frames from <c>node:internal/*</c> are marked
/// <see cref="FrameOrigin.Runtime"/> at parse time rather than left to the path-based
/// classifier, because those are pseudo-paths that exist nowhere on disk and would otherwise
/// look like unrecognised first-party files. And ESM prints locations as
/// <c>file:///C:/src/app.js</c>, which <see cref="ParserHelpers.CleanFilePath"/> converts back
/// to a real path - without that, nothing under the source root ever matches.
/// </remarks>
public sealed partial class NodeStackTraceParser : IStackTraceParser
{
    public string LanguageId => "node";
    public string DisplayName => "Node.js";

    [GeneratedRegex(@"^\s+at\s+(?:async\s+)?(?:(?<sym>.+?)\s+\()?(?<file>(?:[A-Za-z]:[\\/]|file:\/\/|\/|node:|[\w.\-]+[\\/]).*?):(?<line>\d+):(?<col>\d+)\)?\s*$")]
    private static partial Regex FramePattern();

    [GeneratedRegex(@"^(?<type>[A-Z]\w*(?:Error|Exception))(?:\s*:\s*(?<msg>.*))?$")]
    private static partial Regex HeaderPattern();

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
                // file:line:col with a column is the V8 signature - Python and .NET never
                // print a column on a frame.
                score += 18;
                if (frame.Groups["sym"].Success) score += 4;
            }

            if (HeaderPattern().IsMatch(line)) score += 12;
        }

        return Math.Min(score, 100);
    }

    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines)
    {
        // Work backwards to the last header that is actually followed by V8 frames: a crashing
        // program often logs earlier, non-fatal errors too.
        var headerIndex = -1;
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (!HeaderPattern().IsMatch(lines[i].Text)) continue;
            for (var j = i + 1; j < Math.Min(i + 3, lines.Count); j++)
            {
                if (!FramePattern().IsMatch(lines[j].Text)) continue;
                headerIndex = i;
                break;
            }
            if (headerIndex >= 0) break;
        }

        if (headerIndex < 0) return null;

        var header = HeaderPattern().Match(lines[headerIndex].Text);
        var frames = new List<ErrorFrame>();
        var end = headerIndex + 1;

        for (var i = headerIndex + 1; i < lines.Count; i++)
        {
            var text = lines[i].Text;
            var frame = FramePattern().Match(text);
            if (!frame.Success)
            {
                if (text.Trim().Length == 0) continue;
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

        return new ParsedError
        {
            LanguageId = LanguageId,
            Confidence = 88,
            RawText = ParserHelpers.RawTextOf(lines, headerIndex, end),
            FirstLineSequence = lines[headerIndex].Sequence,
            ExceptionType = header.Groups["type"].Value,
            Message = header.Groups["msg"].Success && header.Groups["msg"].Value.Trim().Length > 0
                ? header.Groups["msg"].Value.Trim()
                : null,
            Frames = frames,
        };
    }
}
