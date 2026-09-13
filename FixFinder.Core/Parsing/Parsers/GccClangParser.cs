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
public sealed partial class GccClangParser : IStackTraceParser, IMultiErrorParser
{
    public string LanguageId => "gcc";
    public string DisplayName => "gcc / clang";

    /// <summary>A compiler diagnostic: <c>app.c:3:18: error: ...</c></summary>
    /// <remarks>
    /// The optional drive letter is the difference between reading gcc on Windows and not reading
    /// it at all. MinGW prints the path it was given, <c>C:\src\app.c:3:18:</c>, and a file pattern
    /// that stops at the first colon read <c>C</c> as the file and matched nothing - so every gcc
    /// error on Windows fell to the generic parser, which kept the whole line, path and all, as
    /// the message.
    /// </remarks>
    [GeneratedRegex(@"^(?<file>(?:[A-Za-z]:)?[^\s:][^:]*?):(?<line>\d+):(?<col>\d+):\s*(?<sev>fatal error|error|warning|note):\s*(?<msg>.*)$")]
    private static partial Regex DiagnosticPattern();

    /// <summary>
    /// The linker's own error: <c>app.c:3:(.text+0x18): undefined reference to `prinft'</c>.
    /// </summary>
    /// <remarks>
    /// A misspelt function in C is not a compile error - the compiler warns and carries on - so this
    /// is the only error there is. Without it, the line read was <c>collect2.exe: error: ld returned
    /// 1 exit status</c>, which names nothing.
    /// </remarks>
    [GeneratedRegex(@"^(?<file>(?:[A-Za-z]:)?[^:\r\n]+?):(?:(?<line>\d+):)?\([^)]*\):\s*undefined reference to [`'‘](?<symbol>[^'`’]+)['’]\s*$")]
    private static partial Regex LinkerPattern();

    [GeneratedRegex(@"^==\d+==\s*ERROR:\s*(?<tool>\w+Sanitizer):\s*(?<type>[\w \-]+?)(?:\s+on\s+.*)?$")]
    private static partial Regex SanitizerPattern();

    /// <summary>What went wrong, without the process id in front or the registers behind.</summary>
    [GeneratedRegex(@"^==\d+==\s*ERROR:\s*\w+Sanitizer:\s*(?<body>.+?)(?:\s+\(pc\s.*|\s+at pc\s.*)?$")]
    private static partial Regex SanitizerHeader();

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

            if (LinkerPattern().IsMatch(line)) score += 30;

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

        // The header as printed is "==43860==ERROR: AddressSanitizer: access-violation on unknown
        // address 0x0 (pc 0x7ff7... bp 0x0 sp 0x00fd... T0)". The process id and the registers
        // change every run, and as a headline they bury the four words that matter.
        var message = SanitizerHeader().Match(lines[index].Text) is { Success: true } body
            ? body.Groups["body"].Value.Trim()
            : lines[index].Text.Trim();

        // AddressSanitizer says so itself when the address is in the zero page. That is a null
        // pointer, which is the most useful thing anyone can be told about an access violation.
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

    /// <summary>
    /// Every diagnostic the compiler reported, in source order.
    /// </summary>
    /// <remarks>
    /// A sanitizer report or a fatal runtime message is a single failure and stays single: the
    /// process died once. Only the compile path can hold several, because a compiler keeps going
    /// after the first error and says so.
    /// </remarks>
    public IReadOnlyList<ParsedError> ParseAll(IReadOnlyList<CapturedLine> lines)
    {
        if (ParseSanitizer(lines) is { } sanitizer) return [sanitizer];

        var diagnostics = ParseDiagnostics(lines);
        if (diagnostics.Count > 0) return diagnostics;

        return ParseFatalRuntime(lines) is { } fatal ? [fatal] : [];
    }

    private ParsedError? ParseDiagnostic(IReadOnlyList<CapturedLine> lines) =>
        ParseDiagnostics(lines).FirstOrDefault();

    private List<ParsedError> ParseDiagnostics(IReadOnlyList<CapturedLine> lines)
    {
        var errors = new List<ParsedError>();

        for (var i = 0; i < lines.Count; i++)
        {
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
