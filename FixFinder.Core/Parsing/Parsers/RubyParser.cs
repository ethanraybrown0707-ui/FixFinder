using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>Reads Ruby exception output.</summary>
public sealed partial class RubyParser : IStackTraceParser
{
    public string LanguageId => "ruby";
    public string DisplayName => "Ruby";

    [GeneratedRegex(@"^(?<file>.+?):(?<line>\d+):in\s+[`'](?<sym>[^']+)':\s*(?<msg>.*?)\s*\((?<type>[\w:]+)\)\s*$")]
    private static partial Regex HeaderPattern();

    [GeneratedRegex(@"^\s+from\s+(?<file>.+?):(?<line>\d+):in\s+[`'](?<sym>[^']+)'\s*$")]
    private static partial Regex FramePattern();

    public int Detect(IReadOnlyList<string> lines)
    {
        var score = 0;

        foreach (var line in lines)
        {
            if (HeaderPattern().IsMatch(line)) score += 65;
            if (FramePattern().IsMatch(line)) score += 25;
            if (line.Contains(".rb:", StringComparison.Ordinal)) score += 5;
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
        var frames = new List<ErrorFrame>
        {
            new()
            {
                Order = 0,
                Symbol = header.Groups["sym"].Value,
                File = ParserHelpers.CleanFilePath(header.Groups["file"].Value),
                Line = int.Parse(header.Groups["line"].Value),
                RawLine = lines[headerIndex].Text,
            },
        };

        var end = headerIndex + 1;
        for (var i = headerIndex + 1; i < lines.Count; i++)
        {
            var frame = FramePattern().Match(lines[i].Text);
            if (!frame.Success) break;

            frames.Add(new ErrorFrame
            {
                Order = frames.Count,
                Symbol = frame.Groups["sym"].Value,
                File = ParserHelpers.CleanFilePath(frame.Groups["file"].Value),
                Line = int.Parse(frame.Groups["line"].Value),
                RawLine = lines[i].Text,
            });
            end = i + 1;
        }

        return new ParsedError
        {
            LanguageId = LanguageId,
            Confidence = 88,
            RawText = ParserHelpers.RawTextOf(lines, headerIndex, end),
            FirstLineSequence = lines[headerIndex].Sequence,
            ExceptionType = header.Groups["type"].Value,
            Message = header.Groups["msg"].Value.Trim() is { Length: > 0 } m ? m : null,
            Frames = frames,
        };
    }
}
