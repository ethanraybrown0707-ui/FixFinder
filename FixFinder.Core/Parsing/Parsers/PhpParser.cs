using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>Reads PHP fatal errors, parse errors and warnings.</summary>
public sealed partial class PhpParser : IStackTraceParser
{
    public string LanguageId => "php";
    public string DisplayName => "PHP";

    [GeneratedRegex(
        @"^(?:PHP\s+)?(?<severity>Fatal error|Parse error|Recoverable fatal error|Warning|Notice|Deprecated)" +
        @":\s+(?:Uncaught\s+(?<type>[A-Za-z_\\][\w\\]*)\s*:\s*)?(?<msg>.*?)" +
        @"\s+in\s+(?<file>.+?)(?::(?<line>\d+)|\s+on line\s+(?<line>\d+))\s*$")]
    private static partial Regex HeaderPattern();

    [GeneratedRegex(@"^#(?<order>\d+)\s+(?:(?<file>.+?)\((?<line>\d+)\):\s*(?<sym>.+)|(?<main>\{main\}))\s*$")]
    private static partial Regex FramePattern();

    [GeneratedRegex(@"^\s*thrown in\s+(?<file>.+?)\s+on line\s+(?<line>\d+)\s*$")]
    private static partial Regex ThrownPattern();

    public int Detect(IReadOnlyList<string> lines)
    {
        var score = 0;

        foreach (var line in lines)
        {
            if (HeaderPattern().IsMatch(line)) score += 65;
            if (FramePattern().IsMatch(line)) score += 15;
            if (ThrownPattern().IsMatch(line)) score += 15;
            if (line.StartsWith("Stack trace:", StringComparison.Ordinal)) score += 20;
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
        var severity = header.Groups["severity"].Value;

        var frames = new List<ErrorFrame>
        {
            new()
            {
                Order = 0,
                Symbol = null,
                File = ParserHelpers.CleanFilePath(header.Groups["file"].Value),
                Line = int.Parse(header.Groups["line"].Value),
                RawLine = lines[headerIndex].Text,
            },
        };

        var end = headerIndex + 1;

        for (var i = headerIndex + 1; i < lines.Count; i++)
        {
            var text = lines[i].Text;

            if (text.StartsWith("Stack trace:", StringComparison.Ordinal))
            {
                end = i + 1;
                continue;
            }

            if (ThrownPattern().IsMatch(text))
            {
                end = i + 1;
                break;
            }

            var frame = FramePattern().Match(text);
            if (!frame.Success) break;

            end = i + 1;

            if (frame.Groups["main"].Success) break;

            frames.Add(new ErrorFrame
            {
                Order = frames.Count,
                Symbol = frame.Groups["sym"].Value.Trim(),
                File = ParserHelpers.CleanFilePath(frame.Groups["file"].Value),
                Line = int.Parse(frame.Groups["line"].Value),
                RawLine = text,
            });
        }

        var type = header.Groups["type"].Success
            ? header.Groups["type"].Value
            : severity.Contains("Parse", StringComparison.Ordinal) ? "ParseError" : null;

        return new ParsedError
        {
            LanguageId = LanguageId,
            Confidence = severity.Contains("error", StringComparison.OrdinalIgnoreCase) ? 90 : 70,
            RawText = ParserHelpers.RawTextOf(lines, headerIndex, end),
            FirstLineSequence = lines[headerIndex].Sequence,
            ExceptionType = type,
            Message = header.Groups["msg"].Value.Trim() is { Length: > 0 } message ? message : null,
            Frames = frames,
        };
    }
}
