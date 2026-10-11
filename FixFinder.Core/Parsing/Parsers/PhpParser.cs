using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>
/// Reads what PHP says about a program: a parse error from php -l, a fatal error that stops it, and the warnings it prints
/// while it carries on - as PHP 8.4 writes them.
/// </summary>
/// <remarks>
/// <code>
/// Parse error: syntax error, unexpected token "echo" in p_semi.php on line 3
/// Fatal error: Uncaught DivisionByZeroError: Division by zero in C:\...\p_divzero.php:3
/// Stack trace:
/// #0 {main}
///   thrown in C:\...\p_divzero.php on line 3
/// Warning: Undefined variable $totl in C:\...\p_undefvar.php on line 3
/// </code>
/// <para>
/// A message can itself hold " in ... on line 5" - "Too few arguments to function area(), 1 passed in p_args.php on line 5
/// and exactly 2 expected in p_args.php:2" - so the file and line are the last ones the line names. A fatal error's type
/// is the exception's, after Uncaught; a parse error's is ParseError; a warning's, a notice's or a deprecation's is that
/// word, as PHP writes it.
/// </para>
/// </remarks>
public sealed partial class PhpParser : IStackTraceParser
{
    public string LanguageId => "php";
    public string DisplayName => "PHP";

    [GeneratedRegex(
        @"^(?:PHP\s+)?(?<severity>Fatal error|Parse error|Recoverable fatal error|Warning|Notice|Deprecated)" +
        @":\s+(?:Uncaught\s+(?<type>[A-Za-z_\\][\w\\]*)\s*:\s*)?(?<msg>.*)" +
        @"\s+in\s+(?<file>.+?)(?::(?<line>\d+)|\s+on line\s+(?<line>\d+))\s*$")]
    private static partial Regex HeaderPattern();

    [GeneratedRegex(@"^#(?<order>\d+)\s+(?:(?<file>.+?)\((?<line>\d+)\):\s*(?<sym>.+)|(?<main>\{main\}))\s*$")]
    private static partial Regex FramePattern();

    [GeneratedRegex(@"^\s*thrown in\s+(?<file>.+?)\s+on line\s+(?<line>\d+)\s*$")]
    private static partial Regex ThrownPattern();

    /// <summary>The kinds of message PHP prints while the program carries on.</summary>
    private static readonly HashSet<string> CarriesOn = new(StringComparer.Ordinal) { "Warning", "Notice", "Deprecated" };

    /// <summary>
    /// What PHP printed, in words, when it was one PHP carried on past - "a warning", "a notice", "a deprecation notice" -
    /// or null for an error that stopped the program, or a message that is not PHP's.
    /// </summary>
    public static string? CarriedOnPast(ParsedError error) => error.LanguageId != "php" ? null : error.ExceptionType switch
    {
        "Warning" => "a warning",
        "Notice" => "a notice",
        "Deprecated" => "a deprecation notice",
        _ => null,
    };

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

    /// <summary>What stopped the program - its fatal or parse error - or, when nothing did, the last thing it warned of.</summary>
    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines)
    {
        var headers = Enumerable.Range(0, lines.Count)
            .Select(index => (Index: index, Match: HeaderPattern().Match(lines[index].Text)))
            .Where(header => header.Match.Success)
            .ToList();
        if (headers.Count == 0) return null;

        var stopping = headers.LastOrDefault(header => !CarriesOn.Contains(header.Match.Groups["severity"].Value));
        var chosen = stopping.Match is not null ? stopping : headers[^1];
        return Read(lines, chosen.Index, chosen.Match);
    }

    /// <summary>
    /// Every warning, notice and deprecation PHP printed while the program ran - each a message of its own, once: PHP set up both
    /// to show its messages and to log them prints each twice, and a line run again in a loop warns again of the same thing.
    /// </summary>
    public static IReadOnlyList<ParsedError> RunWarnings(IReadOnlyList<CapturedLine> lines) => Enumerable.Range(0, lines.Count)
        .Select(index => (Index: index, Match: HeaderPattern().Match(lines[index].Text)))
        .Where(header => header.Match.Success && CarriesOn.Contains(header.Match.Groups["severity"].Value))
        .Select(header => Read(lines, header.Index, header.Match))
        .DistinctBy(warning => (warning.ExceptionType, warning.Message, warning.Frames[0].File, warning.Frames[0].Line))
        .ToList();

    private static ParsedError Read(IReadOnlyList<CapturedLine> lines, int headerIndex, Match header)
    {
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

        for (var i = headerIndex + 1; i < lines.Count && !CarriesOn.Contains(severity); i++)
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

            if (frame.Groups["main"].Success) continue;

            frames.Add(new ErrorFrame
            {
                Order = frames.Count,
                Symbol = frame.Groups["sym"].Value.Trim(),
                File = ParserHelpers.CleanFilePath(frame.Groups["file"].Value),
                Line = int.Parse(frame.Groups["line"].Value),
                RawLine = text,
            });
        }

        var type = header.Groups["type"].Success ? header.Groups["type"].Value
            : severity.Contains("Parse", StringComparison.Ordinal) ? "ParseError"
            : severity;

        return new ParsedError
        {
            LanguageId = "php",
            Confidence = severity.Contains("error", StringComparison.OrdinalIgnoreCase) ? 90 : 70,
            RawText = ParserHelpers.RawTextOf(lines, headerIndex, end),
            FirstLineSequence = lines[headerIndex].Sequence,
            ExceptionType = type,
            Message = header.Groups["msg"].Value.Trim() is { Length: > 0 } message ? message : null,
            Frames = frames,
        };
    }
}
