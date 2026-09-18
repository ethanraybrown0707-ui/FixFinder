using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>Reads PowerShell error records.</summary>
public sealed partial class PowerShellParser : IStackTraceParser
{
    public string LanguageId => "powershell";
    public string DisplayName => "PowerShell";

    [GeneratedRegex(@"^At\s+(?<file>.+?):(?<line>\d+)\s+char:(?<col>\d+)\s*$")]
    private static partial Regex AtPattern();

    [GeneratedRegex(@"^\s*\+\s*CategoryInfo\s*:\s*(?<category>[^:]*):.*?,\s*(?<type>[A-Za-z_][\w.]*)\s*$")]
    private static partial Regex CategoryPattern();

    [GeneratedRegex(@"^\s*\+\s*FullyQualifiedErrorId\s*:\s*(?<id>[^,\r\n]+)")]
    private static partial Regex ErrorIdPattern();

    [GeneratedRegex(@"^(?<sym>[A-Za-z][\w-]*)\s*:\s*(?<msg>.+?)\s*$")]
    private static partial Regex HeaderPattern();

    [GeneratedRegex(@"char:\s*\d+\s*$")]
    private static partial Regex LocationTailPattern();

    private static Match? Location(IReadOnlyList<CapturedLine> lines, int index)
    {
        if (!lines[index].Text.StartsWith("At ", StringComparison.Ordinal)) return null;

        var joined = lines[index].Text;

        for (var extra = 0; extra <= 2; extra++)
        {
            if (AtPattern().Match(joined) is { Success: true } match) return match;

            var next = index + extra + 1;
            if (next >= lines.Count) return null;

            joined += lines[next].Text;
        }

        return null;
    }

    public int Detect(IReadOnlyList<string> lines)
    {
        var score = 0;

        foreach (var line in lines)
        {
            if (LocationTailPattern().IsMatch(line)) score += 55;
            if (ErrorIdPattern().IsMatch(line)) score += 40;
            if (CategoryPattern().IsMatch(line)) score += 25;
        }

        return Math.Min(score, 100);
    }

    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines)
    {
        var anchor = -1;
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (Location(lines, i) is null && !ErrorIdPattern().IsMatch(lines[i].Text)) continue;
            anchor = i;
            break;
        }

        if (anchor < 0) return null;

        var start = anchor;
        while (start > 0 && IsRecordBody(lines[start - 1].Text)) start--;
        if (start > 0) start--;

        var end = anchor + 1;
        while (end < lines.Count && lines[end].Text.TrimStart().StartsWith('+')) end++;

        string? file = null;
        var line = 0;
        var column = 0;
        string? type = null;
        string? errorId = null;

        for (var i = start; i < end; i++)
        {
            var text = lines[i].Text;

            if (Location(lines, i) is { } at)
            {
                file = ParserHelpers.CleanFilePath(at.Groups["file"].Value);
                line = int.Parse(at.Groups["line"].Value);
                column = int.Parse(at.Groups["col"].Value);
            }

            if (CategoryPattern().Match(text) is { Success: true } category)
                type = category.Groups["type"].Value;

            if (ErrorIdPattern().Match(text) is { Success: true } id)
                errorId = id.Groups["id"].Value.Trim();
        }

        var header = HeaderPattern().Match(lines[start].Text);
        var symbol = header.Success ? header.Groups["sym"].Value : null;
        var message = header.Success ? header.Groups["msg"].Value : lines[start].Text.Trim();

        var frames = file is null && symbol is null
            ? Array.Empty<ErrorFrame>()
            :
            [
                new ErrorFrame
                {
                    Order = 0,
                    Symbol = symbol,
                    File = file,
                    Line = line,
                    Column = column,
                    RawLine = lines[start].Text,
                },
            ];

        return new ParsedError
        {
            LanguageId = LanguageId,
            Confidence = 85,
            RawText = ParserHelpers.RawTextOf(lines, start, end),
            FirstLineSequence = lines[start].Sequence,
            ExceptionType = type,
            ErrorCode = errorId,
            Message = message is { Length: > 0 } ? message : null,
            Frames = frames,
        };
    }

    private static bool IsRecordBody(string text) =>
        text.TrimStart().StartsWith('+') ||
        text.StartsWith("At ", StringComparison.Ordinal) ||
        LocationTailPattern().IsMatch(text) ||
        text.Trim().Length == 0;
}
