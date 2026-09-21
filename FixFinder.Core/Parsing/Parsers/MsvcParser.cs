using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>Reads MSBuild / MSVC / Roslyn compiler diagnostics: <c>Program.cs(12,17): error CS0103: ...</c></summary>
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

    [GeneratedRegex(@"^\s*(?<obj>(?:[A-Za-z]:)?[^\s(:][^(:]*?)\s*:\s*(?:fatal\s+)?error\s+(?<code>LNK\d+)\s*:\s*(?<msg>.*?)\s*$")]
    private static partial Regex LinkerPattern();
}
