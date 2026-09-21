using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>Reads gcc/clang diagnostics, AddressSanitizer reports, and bare runtime death messages.</summary>
public sealed partial class GccClangParser : IStackTraceParser, IMultiErrorParser
{
    public string LanguageId => "gcc";
    public string DisplayName => "gcc / clang";

    [GeneratedRegex(@"^(?<file>(?:[A-Za-z]:)?[^\s:][^:]*?):(?<line>\d+):(?<col>\d+):\s*(?<sev>fatal error|error|warning|note):\s*(?<msg>.*)$")]
    private static partial Regex DiagnosticPattern();

    [GeneratedRegex(@"^(?<file>(?:[A-Za-z]:)?[^:\r\n]+?):(?:(?<line>\d+):)?\([^)]*\):\s*undefined reference to [`'‘](?<symbol>[^'`’]+)['’]\s*$")]
    private static partial Regex LinkerPattern();

    [GeneratedRegex(@"undefined reference to [`'‘](?<symbol>[^'`’]+)['’]\s*$")]
    private static partial Regex LibraryLinkerPattern();

    [GeneratedRegex(@"^==\d+==\s*ERROR:\s*(?<tool>\w+Sanitizer):\s*(?<type>[\w \-]+?)(?:\s+on\s+.*)?$")]
    private static partial Regex SanitizerPattern();

    [GeneratedRegex(@"^==\d+==\s*ERROR:\s*\w+Sanitizer:\s*(?<body>.+?)(?:\s+\(pc\s.*|\s+at pc\s.*)?$")]
    private static partial Regex SanitizerHeader();

    [GeneratedRegex(@"^\s+#(?<n>\d+)\s+0x[0-9a-fA-F]+\s+in\s+(?<sym>.+?)\s+(?<file>[^\s]+?):(?<line>\d+)(?::(?<col>\d+))?\s*$")]
    private static partial Regex SanitizerFramePattern();

    [GeneratedRegex(@"^terminate called (?:after throwing an instance of '(?<type>[^']+)'|without an active exception)\s*$")]
    private static partial Regex TerminatePattern();

    [GeneratedRegex(@"^\s*what\(\):\s*(?<what>.*?)\s*$")]
    private static partial Regex WhatPattern();

    private static readonly string[] FatalRuntimeMessages =
    [
        "Segmentation fault",
        "stack smashing detected",
        "double free or corruption",
        "free(): invalid pointer",
        "munmap_chunk(): invalid pointer",
        "Aborted (core dumped)",
        "Bus error",
    ];

    public int Detect(IReadOnlyList<string> lines)
    {
        var score = 0;

        foreach (var line in lines)
        {
            if (SanitizerPattern().IsMatch(line)) score += 70;
            if (SanitizerFramePattern().IsMatch(line)) score += 15;

            var diagnostic = DiagnosticPattern().Match(line);
            if (diagnostic.Success)
            {
                var severity = diagnostic.Groups["sev"].Value;
                score += severity is "error" or "fatal error" ? 30 : 4;
            }

            if (LinkerPattern().IsMatch(line) || LibraryLinkerPattern().IsMatch(line)) score += 30;
            if (TerminatePattern().IsMatch(line)) score += 60;

            foreach (var fatal in FatalRuntimeMessages)
                if (line.Contains(fatal, StringComparison.OrdinalIgnoreCase)) score += 45;
        }

        return Math.Min(score, 100);
    }

    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines)
    {
        return ParseSanitizer(lines) ?? ParseDiagnostic(lines) ?? ParseUncaught(lines) ?? ParseFatalRuntime(lines);
    }

    private ParsedError? ParseSanitizer(IReadOnlyList<CapturedLine> lines)
    {
        var index = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (!SanitizerPattern().IsMatch(lines[i].Text)) continue;
            index = i;
            break;
        }

        if (index < 0) return null;

        var header = SanitizerPattern().Match(lines[index].Text);
        var frames = new List<ErrorFrame>();
        var end = index + 1;

        for (var i = index + 1; i < lines.Count; i++)
        {
            var frame = SanitizerFramePattern().Match(lines[i].Text);
            if (!frame.Success)
            {
                if (frames.Count > 0 && lines[i].Text.Trim().Length == 0) break;
                continue;
            }

            frames.Add(new ErrorFrame
            {
                Order = frames.Count,
                Symbol = frame.Groups["sym"].Value.Trim(),
                File = ParserHelpers.CleanFilePath(frame.Groups["file"].Value),
                Line = int.Parse(frame.Groups["line"].Value),
                Column = frame.Groups["col"].Success ? int.Parse(frame.Groups["col"].Value) : null,
                RawLine = lines[i].Text,
            });
            end = i + 1;
        }

        var message = SanitizerHeader().Match(lines[index].Text) is { Success: true } body
            ? body.Groups["body"].Value.Trim()
            : lines[index].Text.Trim();

        for (var i = index + 1; i < end; i++)
        {
            if (!lines[i].Text.Contains("address points to the zero page", StringComparison.Ordinal)) continue;

            message += " (a null pointer)";
            break;
        }

        return new ParsedError
        {
            LanguageId = LanguageId,
            Confidence = 92,
            RawText = ParserHelpers.RawTextOf(lines, index, end),
            FirstLineSequence = lines[index].Sequence,
            ExceptionType = header.Groups["type"].Value.Trim(),
            Message = message,
            Frames = frames,
        };
    }

    public IReadOnlyList<ParsedError> ParseAll(IReadOnlyList<CapturedLine> lines)
    {
        if (ParseSanitizer(lines) is { } sanitizer) return [sanitizer];

        var diagnostics = ParseDiagnostics(lines);
        if (diagnostics.Count > 0) return diagnostics;

        if (ParseUncaught(lines) is { } uncaught) return [uncaught];

        return ParseFatalRuntime(lines) is { } fatal ? [fatal] : [];
    }

    private ParsedError? ParseDiagnostic(IReadOnlyList<CapturedLine> lines) =>
        ParseDiagnostics(lines).FirstOrDefault();

    private List<ParsedError> ParseDiagnostics(IReadOnlyList<CapturedLine> lines)
    {
        var errors = new List<ParsedError>();

        for (var i = 0; i < lines.Count; i++)
        {
            if (LinkerPattern().Match(lines[i].Text) is not { Success: true } && LibraryLinkerPattern().Match(lines[i].Text) is { Success: true } library)
            {
                errors.Add(new ParsedError
                {
                    LanguageId = LanguageId,
                    Confidence = 70,
                    RawText = lines[i].Text,
                    FirstLineSequence = lines[i].Sequence,
                    ExceptionType = "link error",
                    Message = $"undefined reference to '{library.Groups["symbol"].Value}'",
                    Frames = [],
                });

                continue;
            }

            if (LinkerPattern().Match(lines[i].Text) is { Success: true } linker)
            {
                errors.Add(new ParsedError
                {
                    LanguageId = LanguageId,
                    Confidence = 80,
                    RawText = lines[i].Text,
                    FirstLineSequence = lines[i].Sequence,
                    ExceptionType = "link error",
                    Message = $"undefined reference to '{linker.Groups["symbol"].Value}'",
                    Frames =
                    [
                        new ErrorFrame
                        {
                            Order = 0,
                            File = ParserHelpers.CleanFilePath(linker.Groups["file"].Value),
                            Line = linker.Groups["line"].Success ? int.Parse(linker.Groups["line"].Value) : null,
                            RawLine = lines[i].Text,
                        },
                    ],
                });

                continue;
            }

            var diagnostic = DiagnosticPattern().Match(lines[i].Text);
            if (!diagnostic.Success) continue;
            if (diagnostic.Groups["sev"].Value is not ("error" or "fatal error")) continue;

            errors.Add(new ParsedError
            {
                LanguageId = LanguageId,
                Confidence = 80,
                RawText = lines[i].Text,
                FirstLineSequence = lines[i].Sequence,
                ExceptionType = "compile error",
                Message = diagnostic.Groups["msg"].Value.Trim(),
                Frames =
                [
                    new ErrorFrame
                    {
                        Order = 0,
                        File = ParserHelpers.CleanFilePath(diagnostic.Groups["file"].Value),
                        Line = int.Parse(diagnostic.Groups["line"].Value),
                        Column = int.Parse(diagnostic.Groups["col"].Value),
                        RawLine = lines[i].Text,
                    },
                ],
            });
        }

        return errors;
    }

    public static IReadOnlyList<ParsedError> ParseWarnings(IReadOnlyList<CapturedLine> lines)
    {
        var warnings = new List<ParsedError>();

        foreach (var line in lines)
        {
            if (DiagnosticPattern().Match(line.Text) is not { Success: true } diagnostic) continue;
            if (diagnostic.Groups["sev"].Value != "warning") continue;

            warnings.Add(new ParsedError
            {
                LanguageId = "gcc",
                Confidence = 80,
                RawText = line.Text,
                FirstLineSequence = line.Sequence,
                ExceptionType = "compile warning",
                Message = diagnostic.Groups["msg"].Value.Trim(),
                Frames =
                [
                    new ErrorFrame
                    {
                        Order = 0,
                        File = ParserHelpers.CleanFilePath(diagnostic.Groups["file"].Value),
                        Line = int.Parse(diagnostic.Groups["line"].Value),
                        Column = int.Parse(diagnostic.Groups["col"].Value),
                        RawLine = line.Text,
                    },
                ],
            });
        }

        return warnings;
    }

    private ParsedError? ParseUncaught(IReadOnlyList<CapturedLine> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (TerminatePattern().Match(lines[i].Text) is not { Success: true } terminate) continue;

            var thrown = terminate.Groups["type"].Success;
            var what = thrown && i + 1 < lines.Count && WhatPattern().Match(lines[i + 1].Text) is { Success: true } w ? w.Groups["what"].Value : null;

            return new ParsedError
            {
                LanguageId = LanguageId,
                Confidence = 75,
                RawText = ParserHelpers.RawTextOf(lines, i, what is null ? i + 1 : i + 2),
                FirstLineSequence = lines[i].Sequence,
                ExceptionType = thrown ? terminate.Groups["type"].Value : "std::terminate",
                Message = what ?? (thrown ? $"uncaught {terminate.Groups["type"].Value}" : "terminate called without an active exception"),
                Frames = [],
            };
        }

        return null;
    }

    private ParsedError? ParseFatalRuntime(IReadOnlyList<CapturedLine> lines)
    {
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var match = FatalRuntimeMessages.FirstOrDefault(
                m => lines[i].Text.Contains(m, StringComparison.OrdinalIgnoreCase));
            if (match is null) continue;

            return new ParsedError
            {
                LanguageId = LanguageId,
                Confidence = 45,
                RawText = lines[i].Text,
                FirstLineSequence = lines[i].Sequence,
                ExceptionType = match,
                Message = lines[i].Text.Trim(),
                Frames = [],
            };
        }

        return null;
    }
}
