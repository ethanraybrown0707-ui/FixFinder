using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>
/// Reads Dart's messages, as Dart 3.13 prints them: the analyzer's, what dart says of code it will not compile, and the
/// exceptions that stop a program.
/// </summary>
/// <remarks>
/// <para>
/// dart analyze lists every problem on a line of its own - <c>  error - marks.dart:3:9 - Undefined name 'totl'. Try correcting
/// the name to one that is defined, or defining the name. - undefined_identifier</c> - its kind, the file, line and column, the
/// message with the analyzer's own advice, and the diagnostic's name, kept as the code.
/// </para>
/// <para>
/// Running code dart will not compile gives <c>marks.dart:3:9: Error: Undefined name 'totl'.</c>, then the line and a ^ under the place.
/// </para>
/// <para>
/// An exception that stops a program follows <c>Unhandled exception:</c> - as <c>RangeError (length): Invalid value: ...</c>, or as
/// a message alone, <c>Null check operator used on a null value</c> or <c>IntegerDivisionByZeroException</c> - and its frames,
/// <c>#1      main (file:///C:/marks/main.dart:3:12)</c>. A FormatException shows the text it could not read, with a ^ under the
/// place, before its frames.
/// </para>
/// </remarks>
public sealed partial class DartParser : IStackTraceParser
{
    public string LanguageId => "dart";
    public string DisplayName => "Dart";

    [GeneratedRegex(@"^Unhandled exception:\s*$")]
    private static partial Regex HeaderPattern();

    [GeneratedRegex(@"^(?<type>[A-Za-z_]\w*(?:Error|Exception|Failure))(?:\s*\((?<detail>[^)]*)\))?(?::\s*(?<msg>.*))?$")]
    private static partial Regex ErrorPattern();

    [GeneratedRegex(@"^#(?<order>\d+)\s+(?<sym>.+?)\s+\((?<file>[^\s()]+?)(?::(?<line>\d+)(?::(?<col>\d+))?)?\)\s*$")]
    private static partial Regex FramePattern();

    [GeneratedRegex(@"^\s*(?<severity>error|warning|info) - (?<file>.+?\.dart):(?<line>\d+):(?<col>\d+) - (?<msg>.+) - (?<code>[a-z0-9_]+)\s*$")]
    private static partial Regex AnalyzerPattern();

    [GeneratedRegex(@"^(?<file>.+?\.dart):(?<line>\d+):(?<col>\d+): Error: (?<msg>.+?)\s*$")]
    private static partial Regex CompilerPattern();

    public int Detect(IReadOnlyList<string> lines)
    {
        var score = 0;

        foreach (var line in lines)
        {
            if (HeaderPattern().IsMatch(line)) score += 50;
            if (AnalyzerPattern().IsMatch(line) || CompilerPattern().IsMatch(line)) score += 60;

            if (!FramePattern().IsMatch(line)) continue;

            score += line.Contains("dart:", StringComparison.Ordinal) ||
                     line.Contains("package:", StringComparison.Ordinal) ||
                     line.Contains(".dart:", StringComparison.Ordinal)
                ? 30
                : 5;
        }

        return Math.Min(score, 100);
    }

    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines) =>
        AnalyzerErrors(lines).FirstOrDefault() ?? CompilerError(lines) ?? Exception(lines);

    /// <summary>Every error dart analyze listed, in its order.</summary>
    public static IReadOnlyList<ParsedError> AnalyzerErrors(IReadOnlyList<CapturedLine> lines) => Analyzed(lines, "error", "compile error");

    /// <summary>Every warning dart analyze listed - not its infos, which are matters of style a project's own lints choose.</summary>
    public static IReadOnlyList<ParsedError> AnalyzerWarnings(IReadOnlyList<CapturedLine> lines) => Analyzed(lines, "warning", "compile warning");

    private static IReadOnlyList<ParsedError> Analyzed(IReadOnlyList<CapturedLine> lines, string severity, string kind) => lines
        .Select(line => (Line: line, Match: AnalyzerPattern().Match(line.Text)))
        .Where(analyzed => analyzed.Match.Success && analyzed.Match.Groups["severity"].Value == severity)
        .Select(analyzed =>
        {
            var frame = new ErrorFrame
            {
                Order = 0,
                File = ParserHelpers.CleanFilePath(analyzed.Match.Groups["file"].Value),
                Line = int.Parse(analyzed.Match.Groups["line"].Value),
                Column = int.Parse(analyzed.Match.Groups["col"].Value),
                RawLine = analyzed.Line.Text,
            };

            return new ParsedError
            {
                LanguageId = "dart",
                Confidence = 95,
                RawText = analyzed.Line.Text.Trim(),
                FirstLineSequence = analyzed.Line.Sequence,
                ExceptionType = kind,
                ErrorCode = analyzed.Match.Groups["code"].Value,
                Message = analyzed.Match.Groups["msg"].Value.Trim(),
                Frames = [frame],
                CulpritFrame = frame,
            };
        })
        .ToList();

    /// <summary>The first error dart gave for code it would not compile, with the line it showed and the ^ under the place.</summary>
    private ParsedError? CompilerError(IReadOnlyList<CapturedLine> lines)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            if (CompilerPattern().Match(lines[index].Text) is not { Success: true } error) continue;

            var end = index + 1;
            while (end < lines.Count && end <= index + 2 && !CompilerPattern().IsMatch(lines[end].Text) && lines[end].Text.Trim().Length > 0) end++;

            var frame = new ErrorFrame
            {
                Order = 0,
                File = ParserHelpers.CleanFilePath(error.Groups["file"].Value),
                Line = int.Parse(error.Groups["line"].Value),
                Column = int.Parse(error.Groups["col"].Value),
                RawLine = lines[index].Text,
            };

            return new ParsedError
            {
                LanguageId = LanguageId,
                Confidence = 90,
                RawText = ParserHelpers.RawTextOf(lines, index, end),
                FirstLineSequence = lines[index].Sequence,
                ExceptionType = "compile error",
                Message = error.Groups["msg"].Value.Trim(),
                Frames = [frame],
                CulpritFrame = frame,
            };
        }

        return null;
    }

    private ParsedError? Exception(IReadOnlyList<CapturedLine> lines)
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

        var errorLine = lines[errorIndex].Text.Trim();
        var error = ErrorPattern().Match(errorLine);

        // After Unhandled exception: the line may be a message with no type of its own - "Null check operator used on a null value".
        if (!error.Success && (headerIndex < 0 || errorLine.Length == 0 || FramePattern().IsMatch(errorLine))) return null;

        var frames = new List<ErrorFrame>();
        var end = errorIndex + 1;

        // A FormatException shows the text it could not read, and a ^ under the place, before the frames.
        var firstFrame = Enumerable.Range(errorIndex + 1, Math.Max(0, Math.Min(lines.Count, errorIndex + 6) - errorIndex - 1))
            .FirstOrDefault(index => FramePattern().IsMatch(lines[index].Text), errorIndex + 1);

        for (var i = firstFrame; i < lines.Count; i++)
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

        var type = !error.Success ? null
            : error.Groups["detail"].Success && error.Groups["detail"].Value.Length > 0
                ? $"{error.Groups["type"].Value} ({error.Groups["detail"].Value})"
                : error.Groups["type"].Value;

        var message = !error.Success ? errorLine : error.Groups["msg"].Value.Trim();
        var start = headerIndex >= 0 ? headerIndex : errorIndex;

        // The first frames can be inside Dart itself - #0 int.~/ (dart:core-patch/integers.dart:27:7) - so the place is the
        // first frame in the program's own files.
        var own = frames.FirstOrDefault(frame => frame.Line is not null && frame.File is { } file &&
                                                 !file.StartsWith("dart:", StringComparison.Ordinal) && !file.StartsWith("package:", StringComparison.Ordinal));

        return new ParsedError
        {
            LanguageId = LanguageId,
            Confidence = headerIndex >= 0 ? 90 : 75,
            RawText = ParserHelpers.RawTextOf(lines, start, end),
            FirstLineSequence = lines[start].Sequence,
            ExceptionType = type,
            Message = message.Length > 0 ? message : null,
            Frames = frames,
            CulpritFrame = own,
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
