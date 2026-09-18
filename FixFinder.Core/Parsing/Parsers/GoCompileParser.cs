using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>Reads the Go compiler's errors: <c>.\app.go:6:2: declared and not used: count</c>.</summary>
public sealed partial class GoCompileParser : IStackTraceParser, IMultiErrorParser
{
    public string LanguageId => "go";
    public string DisplayName => "Go compiler";

    [GeneratedRegex(@"^(?<file>(?:[A-Za-z]:)?[^\s:][^:]*?\.go):(?<line>\d+):(?<col>\d+):\s+(?<msg>.+)$")]
    private static partial Regex DiagnosticPattern();

    [GeneratedRegex(@"^#\s+\S+")]
    private static partial Regex PackageHeader();

    [GeneratedRegex(@"(?<msg>function main is undeclared in the main package)\s*$")]
    private static partial Regex NoMain();

    [GeneratedRegex(@"^package (?<name>\S+) is not a main package\s*$")]
    private static partial Regex NotMain();

    public int Detect(IReadOnlyList<string> lines)
    {
        var score = 0;

        foreach (var line in lines)
        {
            if (PackageHeader().IsMatch(line)) score += 25;
            if (DiagnosticPattern().IsMatch(line)) score += 35;
            if (NoMain().IsMatch(line) || NotMain().IsMatch(line)) score += 60;
        }

        return Math.Min(score, 100);
    }

    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines) => ParseAll(lines).FirstOrDefault();

    public IReadOnlyList<ParsedError> ParseAll(IReadOnlyList<CapturedLine> lines)
    {
        var errors = new List<ParsedError>();

        for (var i = 0; i < lines.Count; i++)
        {
            var text = lines[i].Text;

            if (NoMain().Match(text) is { Success: true } noMain)
            {
                errors.Add(Error(lines, i, i + 1, "link error", noMain.Groups["msg"].Value, []));
                continue;
            }

            if (NotMain().Match(text) is { Success: true } notMain)
            {
                errors.Add(Error(lines, i, i + 1, "compile error", notMain.Value.Trim(), []));
                continue;
            }

            if (DiagnosticPattern().Match(text) is not { Success: true } diagnostic) continue;

            var end = i + 1;
            while (end < lines.Count && lines[end].Text.StartsWith('\t')) end++;

            errors.Add(Error(lines, i, end, "compile error", diagnostic.Groups["msg"].Value.Trim(),
            [
                new ErrorFrame
                {
                    Order = 0,
                    File = ParserHelpers.CleanFilePath(diagnostic.Groups["file"].Value),
                    Line = int.Parse(diagnostic.Groups["line"].Value),
                    Column = int.Parse(diagnostic.Groups["col"].Value),
                    RawLine = text,
                },
            ]));

            i = end - 1;
        }

        return errors;
    }

    private ParsedError Error(IReadOnlyList<CapturedLine> lines, int start, int end, string type, string message, List<ErrorFrame> frames) => new()
    {
        LanguageId = LanguageId,
        Confidence = 85,
        RawText = ParserHelpers.RawTextOf(lines, start, end),
        FirstLineSequence = lines[start].Sequence,
        ExceptionType = type,
        Message = message,
        Frames = frames,
    };
}
