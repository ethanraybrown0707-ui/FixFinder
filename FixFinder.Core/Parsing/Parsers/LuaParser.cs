using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>Reads Lua error output.</summary>
public sealed partial class LuaParser : IStackTraceParser
{
    public string LanguageId => "lua";
    public string DisplayName => "Lua";

    [GeneratedRegex(
        @"^(?:(?i:\S*lua(?:\d[\d.]*)?(?:\.exe)?:)\s+)?(?<file>(?:[A-Za-z]:)?[^\s:]+):(?<line>\d+):\s*(?<msg>.+?)\s*$")]
    private static partial Regex HeaderPattern();

    [GeneratedRegex(@"^stack traceback:\s*$")]
    private static partial Regex TracebackPattern();

    [GeneratedRegex(@"^\s+(?<file>(?:[A-Za-z]:)?[^\s:]+):(?<line>\d+):\s+in\s+(?<sym>.+?)\s*$")]
    private static partial Regex FramePattern();

    [GeneratedRegex(@"^\s+\[C\]:\s+in\s+(?<sym>.+?)\s*$")]
    private static partial Regex NativeFramePattern();

    public int Detect(IReadOnlyList<string> lines)
    {
        var score = 0;

        foreach (var line in lines)
        {
            if (TracebackPattern().IsMatch(line)) score += 60;
            if (NativeFramePattern().IsMatch(line)) score += 20;
            if (line.Contains("in main chunk", StringComparison.Ordinal)) score += 20;
            if (line.StartsWith("lua:", StringComparison.Ordinal)) score += 25;
        }

        return score == 0 ? 0 : Math.Min(score, 100);
    }

    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines)
    {
        var tracebackIndex = -1;
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (!TracebackPattern().IsMatch(lines[i].Text)) continue;
            tracebackIndex = i;
            break;
        }

        var headerIndex = tracebackIndex > 0 ? tracebackIndex - 1 : -1;
        if (headerIndex < 0 || !HeaderPattern().IsMatch(lines[headerIndex].Text)) return null;

        var header = HeaderPattern().Match(lines[headerIndex].Text);

        var frames = new List<ErrorFrame>
        {
            new()
            {
                Order = 0,
                File = ParserHelpers.CleanFilePath(header.Groups["file"].Value),
                Line = int.Parse(header.Groups["line"].Value),
                RawLine = lines[headerIndex].Text,
            },
        };

        var end = tracebackIndex + 1;

        for (var i = tracebackIndex + 1; i < lines.Count; i++)
        {
            var text = lines[i].Text;

            if (FramePattern().Match(text) is { Success: true } frame)
            {
                frames.Add(new ErrorFrame
                {
                    Order = frames.Count,
                    Symbol = frame.Groups["sym"].Value,
                    File = ParserHelpers.CleanFilePath(frame.Groups["file"].Value),
                    Line = int.Parse(frame.Groups["line"].Value),
                    RawLine = text,
                });

                end = i + 1;
                continue;
            }

            if (NativeFramePattern().Match(text) is { Success: true } native)
            {
                frames.Add(new ErrorFrame
                {
                    Order = frames.Count,
                    Symbol = $"[C] {native.Groups["sym"].Value}",
                    RawLine = text,
                });

                end = i + 1;
                continue;
            }

            break;
        }

        return new ParsedError
        {
            LanguageId = LanguageId,
            Confidence = 85,
            RawText = ParserHelpers.RawTextOf(lines, headerIndex, end),
            FirstLineSequence = lines[headerIndex].Sequence,
            Message = header.Groups["msg"].Value.Trim() is { Length: > 0 } message ? message : null,
            Frames = frames,
        };
    }
}
