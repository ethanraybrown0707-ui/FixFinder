using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>Reads Elixir and Erlang exception output.</summary>
public sealed partial class ElixirParser : IStackTraceParser
{
    public string LanguageId => "elixir";
    public string DisplayName => "Elixir";

    [GeneratedRegex(@"^\*\*\s+\((?<type>[A-Za-z_][\w.]*)\)\s*(?<msg>.*)$")]
    private static partial Regex HeaderPattern();

    [GeneratedRegex(
        @"^\s+(?:\((?<app>[^)]+)\)\s+)?(?<file>[^\s:()]+\.(?:ex|exs|erl)):(?<line>\d+):\s*(?<sym>.+?)\s*$")]
    private static partial Regex FramePattern();

    [GeneratedRegex(@"^\s+(?<sym>:[a-z_]\w*\.[^\s(]+)\(.*\)\s*$")]
    private static partial Regex BuiltInPattern();

    public int Detect(IReadOnlyList<string> lines)
    {
        var score = 0;

        foreach (var line in lines)
        {
            if (HeaderPattern().IsMatch(line)) score += 65;
            if (FramePattern().IsMatch(line)) score += 20;
            if (BuiltInPattern().IsMatch(line)) score += 15;
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

        if (headerIndex < 0) return null;

        var header = HeaderPattern().Match(lines[headerIndex].Text);
        var frames = new List<ErrorFrame>();
        var end = headerIndex + 1;

        for (var i = headerIndex + 1; i < lines.Count; i++)
        {
            var text = lines[i].Text;

            if (text.Trim().Length == 0) break;

            if (FramePattern().Match(text) is { Success: true } frame)
            {
                frames.Add(new ErrorFrame
                {
                    Order = frames.Count,
                    Symbol = frame.Groups["sym"].Value,
                    File = ParserHelpers.CleanFilePath(frame.Groups["file"].Value),
                    Line = int.Parse(frame.Groups["line"].Value),
                    Module = frame.Groups["app"].Success ? frame.Groups["app"].Value : null,
                    RawLine = text,
                });

                end = i + 1;
                continue;
            }

            if (BuiltInPattern().Match(text) is { Success: true } builtIn)
            {
                frames.Add(new ErrorFrame
                {
                    Order = frames.Count,
                    Symbol = builtIn.Groups["sym"].Value,
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
            Confidence = 88,
            RawText = ParserHelpers.RawTextOf(lines, headerIndex, end),
            FirstLineSequence = lines[headerIndex].Sequence,
            ExceptionType = header.Groups["type"].Value,
            Message = header.Groups["msg"].Value.Trim() is { Length: > 0 } message ? message : null,
            Frames = frames,
        };
    }
}
