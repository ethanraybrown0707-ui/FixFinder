using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>Reads PowerShell error records.</summary>
/// <remarks>
/// A PowerShell error is not a stack trace, it is a record printed across several lines, and the
/// most useful field in it is the one no other language has: <c>FullyQualifiedErrorId</c>. That is
/// a stable identifier for the failure - <c>PathNotFound</c>,
/// <c>CommandNotFoundException</c> - which makes it an exact search term where the message is
/// full of the user's own paths. It is carried as <see cref="ParsedError.ErrorCode"/> for the same
/// reason MSVC's <c>C2065</c> is.
/// <para>
/// Anchored on the record's own markers rather than on its first line. The first line is
/// <c>Get-Item : Cannot find path...</c>, which is a cmdlet name, a colon and some prose -
/// a shape so ordinary that detecting on it would claim half the log output ever written.
/// </para>
/// <para>
/// Windows PowerShell 5.1 and PowerShell 7 differ: 5.1 always prints the <c>At file:line char:col</c>
/// block, 7's default view often does not. Either marker alone is enough.
/// </para>
/// </remarks>
public sealed partial class PowerShellParser : IStackTraceParser
{
    public string LanguageId => "powershell";
    public string DisplayName => "PowerShell";

    /// <summary>Where it happened. Absent from PowerShell 7's concise view.</summary>
    [GeneratedRegex(@"^At\s+(?<file>.+?):(?<line>\d+)\s+char:(?<col>\d+)\s*$")]
    private static partial Regex AtPattern();

    /// <summary>The trailing type on the CategoryInfo line - the .NET exception behind the record.</summary>
    [GeneratedRegex(@"^\s*\+\s*CategoryInfo\s*:\s*(?<category>[^:]*):.*?,\s*(?<type>[A-Za-z_][\w.]*)\s*$")]
    private static partial Regex CategoryPattern();

    /// <summary>The stable identifier for this failure, and the best search term in the record.</summary>
    [GeneratedRegex(@"^\s*\+\s*FullyQualifiedErrorId\s*:\s*(?<id>[^,\r\n]+)")]
    private static partial Regex ErrorIdPattern();

    /// <summary>The first line: a command name, then the message.</summary>
    [GeneratedRegex(@"^(?<sym>[A-Za-z][\w-]*)\s*:\s*(?<msg>.+?)\s*$")]
    private static partial Regex HeaderPattern();

    public int Detect(IReadOnlyList<string> lines)
    {
        var score = 0;

        foreach (var line in lines)
        {
            if (AtPattern().IsMatch(line)) score += 55;
            if (ErrorIdPattern().IsMatch(line)) score += 40;
            if (CategoryPattern().IsMatch(line)) score += 25;
        }

        return Math.Min(score, 100);
    }

    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines)
    {
        // Anchored on whichever marker this host printed, taking the last record in the output.
        var anchor = -1;
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (!AtPattern().IsMatch(lines[i].Text) && !ErrorIdPattern().IsMatch(lines[i].Text)) continue;
            anchor = i;
            break;
        }

        if (anchor < 0) return null;

        // The record runs from its message down through the "+ ..." detail lines. Walk up over the
        // record's own body and stop on the first line that is not part of it - that line is the
        // message. Walking further would take whatever the script printed before it crashed.
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

            if (AtPattern().Match(text) is { Success: true } at)
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

    /// <summary>
    /// True for the parts of a record that are not its message: the location line, the offending
    /// source echoed back with carets under it, and the trailing <c>+ CategoryInfo</c> detail.
    /// </summary>
    /// <remarks>
    /// Used to walk up to the message and no further. Getting this wrong in the other direction
    /// is what makes a parser report whatever the script last printed as the error - which is
    /// worse than reporting nothing, because it looks like an answer.
    /// </remarks>
    private static bool IsRecordBody(string text) =>
        text.TrimStart().StartsWith('+') ||
        AtPattern().IsMatch(text) ||
        text.Trim().Length == 0;
}
