using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>
/// Reads gcc/clang diagnostics, AddressSanitizer reports, and bare runtime death messages.
/// </summary>
/// <remarks>
/// Three unrelated shapes share this parser because they share a toolchain, and they are tried
/// in descending order of how much information they carry: an ASan report has real frames, a
/// compiler diagnostic has a file and line, and <c>Segmentation fault (core dumped)</c> has
/// nothing but itself. That last one is still worth recognising - it is a real crash, and
/// reporting "a segfault with no detail" is far more useful than reporting nothing at all.
/// </remarks>
public sealed partial class GccClangParser : IStackTraceParser
{
    public string LanguageId => "gcc";
    public string DisplayName => "gcc / clang";

    [GeneratedRegex(@"^(?<file>[^\s:][^:]*?):(?<line>\d+):(?<col>\d+):\s*(?<sev>fatal error|error|warning|note):\s*(?<msg>.*)$")]
    private static partial Regex DiagnosticPattern();

    [GeneratedRegex(@"^==\d+==\s*ERROR:\s*(?<tool>\w+Sanitizer):\s*(?<type>[\w \-]+?)(?:\s+on\s+.*)?$")]
    private static partial Regex SanitizerPattern();

    [GeneratedRegex(@"^\s+#(?<n>\d+)\s+0x[0-9a-fA-F]+\s+in\s+(?<sym>.+?)\s+(?<file>[^\s]+?):(?<line>\d+)(?::(?<col>\d+))?\s*$")]
    private static partial Regex SanitizerFramePattern();

    /// <summary>Runtime deaths that print one line and nothing else.</summary>
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
                // Only count severities that mean a failure. A wall of warnings from a build
                // that succeeded is not what FixFinder is being asked about.
                var severity = diagnostic.Groups["sev"].Value;
                score += severity is "error" or "fatal error" ? 30 : 4;
            }

            foreach (var fatal in FatalRuntimeMessages)
                if (line.Contains(fatal, StringComparison.OrdinalIgnoreCase)) score += 45;
        }

        return Math.Min(score, 100);
    }

    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines)
    {
        return ParseSanitizer(lines) ?? ParseDiagnostic(lines) ?? ParseFatalRuntime(lines);
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
                // Frames come in blocks separated by prose; stop at the first blank line
                // after at least one frame has been read.
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

        return new ParsedError
        {
            LanguageId = LanguageId,
            Confidence = 92,
            RawText = ParserHelpers.RawTextOf(lines, index, end),
            FirstLineSequence = lines[index].Sequence,
            ExceptionType = header.Groups["type"].Value.Trim(),
            Message = lines[index].Text,
            Frames = frames,
        };
    }

    private ParsedError? ParseDiagnostic(IReadOnlyList<CapturedLine> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var diagnostic = DiagnosticPattern().Match(lines[i].Text);
            if (!diagnostic.Success) continue;
            if (diagnostic.Groups["sev"].Value is not ("error" or "fatal error")) continue;

            return new ParsedError
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
                // Low on purpose: it is definitely a crash, but there is nothing here to locate
                // it with, and the confidence number is what tells the user how much to trust
                // whatever the search turns up.
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
