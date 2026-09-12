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
            if (!diagnostic.Success) continue;

            score += diagnostic.Groups["sev"].Value == "error" ? 40 : 5;
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
    /// </remarks>
    public IReadOnlyList<ParsedError> ParseAll(IReadOnlyList<CapturedLine> lines)
    {
        var errors = new List<ParsedError>();

        for (var i = 0; i < lines.Count; i++)
        {
            var diagnostic = DiagnosticPattern().Match(lines[i].Text);
            if (!diagnostic.Success) continue;
            if (diagnostic.Groups["sev"].Value != "error") continue;

            errors.Add(new ParsedError
            {
                LanguageId = LanguageId,
                Confidence = 90,
                RawText = lines[i].Text,
                FirstLineSequence = lines[i].Sequence,
                ExceptionType = "compile error",
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
                        RawLine = lines[i].Text,
                    },
                ],
            });
        }

        return errors;
    }
}
