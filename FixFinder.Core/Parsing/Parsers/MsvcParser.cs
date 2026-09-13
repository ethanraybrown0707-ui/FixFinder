using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>
/// Reads MSBuild / MSVC / Roslyn compiler diagnostics: <c>Program.cs(12,17): error CS0103: ...</c>
/// </summary>
/// <remarks>
/// The <b>error code</b> is why this parser matters. <c>CS0103</c> or <c>C2065</c> is a globally
/// unique, stable identifier that people quote verbatim in issue titles and question headlines.
/// An exact-code search finds the right answer where message tokens return noise, so the code is
/// lifted into <see cref="ParsedError.ErrorCode"/> and given its own high weight in both the
/// query builder and the ranker - rather than being left buried in the message text.
/// </remarks>
public sealed partial class MsvcParser : IStackTraceParser, IMultiErrorParser
{
    public string LanguageId => "msvc";
    public string DisplayName => "MSVC / MSBuild";

    [GeneratedRegex(@"^\s*(?<file>[A-Za-z]:[^(]*?|[^(\s][^(]*?)\((?<line>\d+)(?:,(?<col>\d+))?\)\s*:\s*(?:fatal\s+)?(?<sev>error|warning)\s+(?<code>[A-Za-z]+\d+)\s*:\s*(?<msg>.*?)(?:\s*\[[^\]]*\])?\s*$")]
    private static partial Regex DiagnosticPattern();

    public int Detect(IReadOnlyList<string> lines)
    {
        var score = 0;

        foreach (var line in lines)
        {
            var diagnostic = DiagnosticPattern().Match(line);

            if (diagnostic.Success) score += diagnostic.Groups["sev"].Value == "error" ? 40 : 5;
            else if (LinkerPattern().IsMatch(line)) score += 40;
        }

        return Math.Min(score, 100);
    }

    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines) => ParseAll(lines).FirstOrDefault();

    /// <summary>
    /// Every error the build reported, in source order.
    /// </summary>
    /// <remarks>
    /// The first is still the one to start with - a build reports in source order and the first
    /// error is usually the cause of the ones after it - but the rest are real, independent and
    /// present in the same output, which is what lets a diagnostic nobody can fix be stepped past
    /// rather than ending the run. Warnings stay out: they did not stop the build.
    /// <para>
    /// <b>Linker errors are in, and were not before.</b> A misspelt function in C is not a compile
    /// error at all - the compiler only warns <c>C4013 'prinft' undefined</c> - so the one error is
    /// the linker's <c>LNK2019: unresolved external symbol prinft</c>. Without it, the only line
    /// anything read was the summary after it, <c>LNK1120: 1 unresolved externals</c>, which names
    /// nothing. That summary is now kept only when nothing more specific was printed.
    /// </para>
    /// </remarks>
    public IReadOnlyList<ParsedError> ParseAll(IReadOnlyList<CapturedLine> lines)
    {
        var errors = new List<ParsedError>();
        var summaries = new List<ParsedError>();

        foreach (var line in lines)
        {
            if (DiagnosticPattern().Match(line.Text) is { Success: true } diagnostic)
            {
                if (diagnostic.Groups["sev"].Value == "error") errors.Add(FromDiagnostic(line, diagnostic, "compile error"));
                continue;
            }

            if (LinkerPattern().Match(line.Text) is not { Success: true } linker) continue;

            var error = new ParsedError
            {
                LanguageId = "msvc",
                Confidence = 85,
                RawText = line.Text,
                FirstLineSequence = line.Sequence,
                ExceptionType = "link error",
                ErrorCode = linker.Groups["code"].Value,
                Message = linker.Groups["msg"].Value.Trim(),
                Frames = [],
            };

            (linker.Groups["code"].Value == "LNK1120" ? summaries : errors).Add(error);
        }

        return errors.Any(e => e.ExceptionType == "link error") ? errors : [.. errors, .. summaries];
    }

    /// <summary>
    /// The warnings a build printed. They never stopped it, and are sometimes the whole explanation
    /// for what happened when it ran.
    /// </summary>
    public static IReadOnlyList<ParsedError> ParseWarnings(IReadOnlyList<CapturedLine> lines)
    {
        var warnings = new List<ParsedError>();

        foreach (var line in lines)
        {
            if (DiagnosticPattern().Match(line.Text) is { Success: true } diagnostic &&
                diagnostic.Groups["sev"].Value == "warning")
                warnings.Add(FromDiagnostic(line, diagnostic, "compile warning"));
        }

        return warnings;
    }

    private static ParsedError FromDiagnostic(CapturedLine line, Match diagnostic, string type) => new()
    {
        LanguageId = "msvc",
        Confidence = 90,
        RawText = line.Text,
        FirstLineSequence = line.Sequence,
        ExceptionType = type,
        ErrorCode = diagnostic.Groups["code"].Value,
        Message = diagnostic.Groups["msg"].Value.Trim(),
        Frames =
        [
            new ErrorFrame
            {
                Order = 0,
                File = ParserHelpers.CleanFilePath(diagnostic.Groups["file"].Value),
                Line = int.Parse(diagnostic.Groups["line"].Value),
                Column = diagnostic.Groups["col"].Success ? int.Parse(diagnostic.Groups["col"].Value) : null,
                RawLine = line.Text,
            },
        ],
    };

    /// <summary>A linker error: <c>app.obj : error LNK2019: unresolved external symbol prinft ...</c></summary>
    [GeneratedRegex(@"^\s*(?<obj>(?:[A-Za-z]:)?[^\s(:][^(:]*?)\s*:\s*(?:fatal\s+)?error\s+(?<code>LNK\d+)\s*:\s*(?<msg>.*?)\s*$")]
    private static partial Regex LinkerPattern();
}
