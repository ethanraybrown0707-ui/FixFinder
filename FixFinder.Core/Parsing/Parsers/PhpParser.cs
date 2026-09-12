using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>Reads PHP fatal errors, parse errors and warnings.</summary>
/// <remarks>
/// PHP prints the same failure two different ways depending on how it was configured. Run from the
/// command line with <c>log_errors</c> on it prefixes every line with <c>PHP </c>; run through a
/// web server with <c>display_errors</c> on it does not. Both are accepted, because which one a
/// user sees is a setting they probably did not choose.
/// <para>
/// <b>The location is written two ways too</b>, and the difference is not cosmetic. An uncaught
/// exception ends <c>in /app/thing.php:12</c>; a parse error or a warning ends <c>in
/// /app/thing.php on line 12</c>. A parser that knows only the first reads a parse error's file
/// path as <c>/app/thing.php on line 12</c> and finds no source file at all.
/// </para>
/// <para>
/// The capital F in <c>Fatal error</c> is load-bearing for detection: Go and gcc both print a
/// lower-case <c>fatal error:</c>, and matching case-insensitively here would have this parser
/// bidding for their output.
/// </para>
/// </remarks>
public sealed partial class PhpParser : IStackTraceParser
{
    public string LanguageId => "php";
    public string DisplayName => "PHP";

    /// <summary>
    /// The failure line, with either location form.
    /// </summary>
    /// <remarks>
    /// <c>line</c> appears twice by design - .NET takes whichever alternative actually matched,
    /// which keeps the two location forms in one pattern instead of two near-identical ones.
    /// </remarks>
    [GeneratedRegex(
        @"^(?:PHP\s+)?(?<severity>Fatal error|Parse error|Recoverable fatal error|Warning|Notice|Deprecated)" +
        @":\s+(?:Uncaught\s+(?<type>[A-Za-z_\\][\w\\]*)\s*:\s*)?(?<msg>.*?)" +
        @"\s+in\s+(?<file>.+?)(?::(?<line>\d+)|\s+on line\s+(?<line>\d+))\s*$")]
    private static partial Regex HeaderPattern();

    /// <summary>A stack-trace entry, or the <c>{main}</c> sentinel that ends every PHP trace.</summary>
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
        // Searched backwards: a script can print several warnings before the one that kills it,
        // and the fatal one is the last.
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

            // "#1 {main}" is the bottom of every PHP trace and names no location, so it is a
            // terminator rather than a frame.
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

        // A parse error names no exception class, but "ParseError" is what PHP 7+ calls the
        // throwable for one and is what an answer about it will be written against.
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
