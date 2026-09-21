using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>Reads JVM stack traces, including <c>Caused by:</c> chains.</summary>
public sealed partial class JavaStackTraceParser : IStackTraceParser, IMultiErrorParser
{
    public string LanguageId => "java";
    public string DisplayName => "Java / JVM";

    [GeneratedRegex(@"^(?:Exception in thread\s+""(?<thread>[^""]*)""\s+)?(?<type>(?:[\w$]+\.)+[\w$]*(?:Exception|Error|Throwable))(?:\s*:\s*(?<msg>.*))?$")]
    private static partial Regex HeaderPattern();

    [GeneratedRegex(@"^Caused by:\s*(?<type>(?:[\w$]+\.)+[\w$]*(?:Exception|Error|Throwable))(?:\s*:\s*(?<msg>.*))?$")]
    private static partial Regex CausedByPattern();

    [GeneratedRegex(@"^\s+at\s+(?<sym>[\w.$<>/]+)\((?<file>[^:)]+?)(?::(?<line>\d+))?\)(?:\s*~?\[(?<module>[^\]]*)\])?\s*$")]
    private static partial Regex FramePattern();

    [GeneratedRegex(@"^\s+\.\.\.\s+(?<count>\d+)\s+more\s*$")]
    private static partial Regex ElidedPattern();

    [GeneratedRegex(@"^(?<file>(?:[A-Za-z]:)?[^:\r\n]*?\.java):(?<line>\d+):\s*(?<severity>error|warning):\s*(?<msg>.+)$")]
    private static partial Regex JavacPattern();

    [GeneratedRegex(@"^\s+(?<key>symbol|location):\s*(?<value>.+?)\s*$")]
    private static partial Regex JavacDetailPattern();

    public int Detect(IReadOnlyList<string> lines)
    {
        var score = 0;

        foreach (var line in lines)
        {
            if (line.StartsWith("Exception in thread", StringComparison.Ordinal)) score += 55;
            if (CausedByPattern().IsMatch(line)) score += 30;
            if (ElidedPattern().IsMatch(line)) score += 20;
            if (FramePattern().IsMatch(line)) score += 18;
            if (line.Contains("java.lang.", StringComparison.Ordinal)) score += 12;

            if (JavacPattern().IsMatch(line)) score += 60;
        }

        return Math.Min(score, 100);
    }

    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines)
    {
        if (ParseJavacDiagnostic(lines) is { } diagnostic) return diagnostic;

        return ParseStackTrace(lines);
    }

    public IReadOnlyList<ParsedError> ParseAll(IReadOnlyList<CapturedLine> lines)
    {
        var diagnostics = ParseJavacDiagnostics(lines);
        if (diagnostics.Count > 0) return diagnostics;

        return ParseStackTrace(lines) is { } thrown ? [thrown] : [];
    }

    private static ParsedError? ParseJavacDiagnostic(IReadOnlyList<CapturedLine> lines) =>
        ParseJavacDiagnostics(lines).FirstOrDefault();

    public static IReadOnlyList<ParsedError> ParseWarnings(IReadOnlyList<CapturedLine> lines)
    {
        var warnings = new List<ParsedError>();

        foreach (var line in lines)
        {
            if (JavacPattern().Match(line.Text) is not { Success: true } match || match.Groups["severity"].Value != "warning") continue;

            var frame = new ErrorFrame { Order = 0, File = match.Groups["file"].Value, Line = int.Parse(match.Groups["line"].Value), RawLine = line.Text };

            warnings.Add(new ParsedError
            {
                LanguageId = "java",
                Confidence = 80,
                RawText = line.Text,
                FirstLineSequence = line.Sequence,
                ExceptionType = "compile warning",
                Message = match.Groups["msg"].Value.Trim(),
                Frames = [frame],
                CulpritFrame = frame,
            });
        }

        return warnings;
    }

    private static List<ParsedError> ParseJavacDiagnostics(IReadOnlyList<CapturedLine> lines)
    {
        var errors = new List<ParsedError>();

        for (var i = 0; i < lines.Count; i++)
        {
            var match = JavacPattern().Match(lines[i].Text);
            if (!match.Success || match.Groups["severity"].Value != "error") continue;

            var message = match.Groups["msg"].Value.Trim();
            var details = new List<string>();

            for (var k = i + 1; k < Math.Min(i + 5, lines.Count); k++)
            {
                var detail = JavacDetailPattern().Match(lines[k].Text);
                if (detail.Success) details.Add($"{detail.Groups["key"].Value}: {detail.Groups["value"].Value}");
            }

            var file = match.Groups["file"].Value;

            var frame = new ErrorFrame
            {
                Order = 0,
                File = file,
                Line = int.Parse(match.Groups["line"].Value),
                RawLine = lines[i].Text,
            };

            errors.Add(new ParsedError
            {
                LanguageId = "java",
                Confidence = 88,
                RawText = string.Join("\n", lines.Skip(i).Take(1 + details.Count).Select(l => l.Text)),
                FirstLineSequence = lines[i].Sequence,
                ExceptionType = "compile error",
                Message = details.Count > 0 ? $"{message} ({string.Join(", ", details)})" : message,
                Frames = [frame],
                CulpritFrame = frame,
            });
        }

        return errors;
    }

    private ParsedError? ParseStackTrace(IReadOnlyList<CapturedLine> lines)
    {
        var headerIndex = -1;
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (!HeaderPattern().IsMatch(lines[i].Text)) continue;
            if (i + 1 < lines.Count && FramePattern().IsMatch(lines[i + 1].Text)) { headerIndex = i; break; }
        }

        if (headerIndex < 0) return null;

        var levels = new List<Level>();
        var header = HeaderPattern().Match(lines[headerIndex].Text);
        levels.Add(new Level(header.Groups["type"].Value, Trim(header.Groups["msg"])));

        var end = headerIndex + 1;

        for (var i = headerIndex + 1; i < lines.Count; i++)
        {
            var text = lines[i].Text;

            var causedBy = CausedByPattern().Match(text);
            if (causedBy.Success)
            {
                levels.Add(new Level(causedBy.Groups["type"].Value, Trim(causedBy.Groups["msg"])));
                end = i + 1;
                continue;
            }

            var elided = ElidedPattern().Match(text);
            if (elided.Success)
            {
                levels[^1].Frames.Add(new ErrorFrame
                {
                    Order = levels[^1].Frames.Count,
                    Symbol = $"... {elided.Groups["count"].Value} frames identical to the enclosing trace",
                    RawLine = text,
                    Origin = FrameOrigin.Unknown,
                });
                end = i + 1;
                continue;
            }

            var frame = FramePattern().Match(text);
            if (!frame.Success)
            {
                if (text.Trim().Length == 0) continue;
                break;
            }

            var fileName = frame.Groups["file"].Value.Trim();
            levels[^1].Frames.Add(new ErrorFrame
            {
                Order = levels[^1].Frames.Count,
                Symbol = frame.Groups["sym"].Value.Trim(),
                File = fileName is "Native Method" or "Unknown Source" ? null : fileName,
                Line = frame.Groups["line"].Success ? int.Parse(frame.Groups["line"].Value) : null,
                Module = frame.Groups["module"].Success ? frame.Groups["module"].Value : null,
                RawLine = text,
            });
            end = i + 1;
        }

        var raw = ParserHelpers.RawTextOf(lines, headerIndex, end);

        ParsedError? built = null;
        for (var level = levels.Count - 1; level >= 0; level--)
        {
            built = new ParsedError
            {
                LanguageId = LanguageId,
                Confidence = 88,
                RawText = raw,
                FirstLineSequence = lines[headerIndex].Sequence,
                ExceptionType = levels[level].Type,
                Message = levels[level].Message,
                Frames = levels[level].Frames,
                Causes = built is null ? [] : [built],
            };
        }

        return built;
    }

    private static string? Trim(Group group) =>
        group.Success && group.Value.Trim().Length > 0 ? group.Value.Trim() : null;

    private sealed record Level(string Type, string? Message)
    {
        public List<ErrorFrame> Frames { get; } = [];
    }
}
