using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>
/// Reads PowerShell's error records, as Windows PowerShell 5.1 writes them, and the parse errors FixFinder's check of a
/// script reports.
/// </summary>
/// <remarks>
/// <para>
/// A record is the message - after the command's name, <c>Get-Content : Cannot find path ...</c>, when a command gave it -
/// then <c>At C:\scripts\marks.ps1:2 char:1</c>, the line with ~ marks under the part it means, and its
/// <c>+ CategoryInfo</c> and <c>+ FullyQualifiedErrorId</c>, the error's id. A script with code PowerShell cannot read
/// writes its record the other way round: the place first, then the line, then the message - one for each mistake - and
/// ParserError as its category.
/// </para>
/// <para>
/// Written to a file or a pipe, each line is cut at 120 characters: a message at a space, which stays at the end of the
/// line, a long path wherever it reaches the edge. The cut lines are joined back as they were.
/// </para>
/// <para>
/// PowerShell carries on past most errors - a command not found, a method called on $null, a division by zero - so a run
/// can print several records, and finish with success.
/// </para>
/// <para>
/// The check FixFinder makes before a script runs asks PowerShell's own parser, which runs none of it, and writes each
/// mistake on a line: <c>marks.ps1:4:7: MissingStatementBlockAfterElse: Missing statement block after 'else' keyword.</c>
/// </para>
/// </remarks>
public sealed partial class PowerShellParser : IStackTraceParser
{
    /// <summary>The category of code PowerShell could not read - nothing of the script ran.</summary>
    public const string ParserError = "ParserError";

    /// <summary>How long a line PowerShell writes to a file or a pipe can be before it is cut.</summary>
    private const int CutLength = 100;

    public string LanguageId => "powershell";
    public string DisplayName => "PowerShell";

    [GeneratedRegex(@"^At\s+(?<file>.+?):(?<line>\d+)\s+char:(?<col>\d+)\s*$")]
    private static partial Regex AtPattern();

    [GeneratedRegex(@"^\s*\+\s*CategoryInfo\s*:\s*(?<category>[^:]*):.*?(?:,\s*|\]\s+)(?<type>[A-Za-z_][\w.]*)\s*$")]
    private static partial Regex CategoryPattern();

    [GeneratedRegex(@"^\s*\+\s*FullyQualifiedErrorId\s*:\s*(?<id>[^,\r\n]+)")]
    private static partial Regex ErrorIdPattern();

    /// <summary>The command a message came from, and the message: "Get-Content : Cannot find path ..." - or "= : The term '=' is not
    /// recognized", for the second = of ==, which PowerShell took for a command.</summary>
    [GeneratedRegex(@"^(?<sym>[A-Za-z][\w-]*|[=!<>]{1,2})\s*:\s*(?<msg>.+?)\s*$")]
    private static partial Regex HeaderPattern();

    [GeneratedRegex(@"char:\s*\d+\s*$")]
    private static partial Regex LocationTailPattern();

    [GeneratedRegex(@"^(?<file>.+?\.ps[md]?1):(?<line>\d+):(?<col>\d+): (?<id>\w+): (?<msg>.+?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex CheckPattern();

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
            if (CheckPattern().IsMatch(line)) score += 60;
        }

        return Math.Min(score, 100);
    }

    /// <summary>What a run printed last - or, from FixFinder's check, the first mistake PowerShell's parser found.</summary>
    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines) => CheckErrors(lines).FirstOrDefault() ?? RunErrors(lines).LastOrDefault();

    /// <summary>Every mistake FixFinder's check found with PowerShell's parser, in order.</summary>
    public static IReadOnlyList<ParsedError> CheckErrors(IReadOnlyList<CapturedLine> lines) => lines
        .Select(line => (Line: line, Match: CheckPattern().Match(line.Text)))
        .Where(checkedLine => checkedLine.Match.Success)
        .Select(checkedLine =>
        {
            var frame = new ErrorFrame
            {
                Order = 0,
                File = ParserHelpers.CleanFilePath(checkedLine.Match.Groups["file"].Value),
                Line = int.Parse(checkedLine.Match.Groups["line"].Value),
                Column = int.Parse(checkedLine.Match.Groups["col"].Value),
                RawLine = checkedLine.Line.Text,
            };

            return new ParsedError
            {
                LanguageId = "powershell",
                Confidence = 95,
                RawText = checkedLine.Line.Text.Trim(),
                FirstLineSequence = checkedLine.Line.Sequence,
                ExceptionType = ParserError,
                ErrorCode = checkedLine.Match.Groups["id"].Value,
                Message = checkedLine.Match.Groups["msg"].Value,
                Frames = [frame],
                CulpritFrame = frame,
            };
        })
        .ToList();

    /// <summary>Every error record a run printed, in order - PowerShell carries on past most of them.</summary>
    public static IReadOnlyList<ParsedError> RunErrors(IReadOnlyList<CapturedLine> lines)
    {
        var found = new List<ParsedError>();
        var recordStart = 0;

        for (var index = 0; index < lines.Count; index++)
        {
            if (!ErrorIdPattern().IsMatch(lines[index].Text)) continue;

            if (Record(lines, recordStart, index) is { } error) found.Add(error);
            recordStart = index + 1;
        }

        return found;
    }

    /// <summary>The record that ends at its FullyQualifiedErrorId line, <paramref name="end"/>, read back to where it starts.</summary>
    private static ParsedError? Record(IReadOnlyList<CapturedLine> lines, int searchFrom, int end)
    {
        var at = -1;
        for (var index = searchFrom; index < end && at < 0; index++)
            if (Location(lines, index) is not null) at = index;

        // The place is written first when PowerShell could not read the code; otherwise the message comes before it.
        var placeFirst = at >= 0 && Enumerable.Range(searchFrom, at - searchFrom).All(index => lines[index].Text.Trim().Length == 0);

        int start;
        string message;

        if (at < 0)
        {
            start = end;
            while (start > searchFrom && IsRecordBody(lines[start - 1].Text)) start--;
            if (start > searchFrom) start--;
            message = lines[start].Text.Trim();
        }
        else if (placeFirst)
        {
            start = at;
            var said = Enumerable.Range(at + 1, end - at - 1).FirstOrDefault(index => !IsRecordBody(lines[index].Text) && lines[index].Text.Trim().Length > 0, -1);
            message = said >= 0 ? lines[said].Text.Trim() : "";
        }
        else
        {
            // The message is the line before the place, and the lines before it that were cut to fit.
            start = at - 1;
            while (start > searchFrom && lines[start - 1].Text.Length >= CutLength) start--;
            if (start < searchFrom) return null;
            message = string.Concat(Enumerable.Range(start, at - start).Select(index => lines[index].Text)).Trim();
        }

        var category = "";
        var type = (string?)null;
        var errorId = (string?)null;

        for (var index = start; index <= end; index++)
        {
            var text = lines[index].Text;

            // A CategoryInfo line cut to fit goes on, indented, on the next line.
            if (text.TrimStart().StartsWith("+ CategoryInfo", StringComparison.Ordinal) && index + 1 <= end && !lines[index + 1].Text.TrimStart().StartsWith('+'))
                text += lines[index + 1].Text.Trim();

            if (CategoryPattern().Match(text) is { Success: true } categoryMatch)
            {
                category = categoryMatch.Groups["category"].Value.Trim();
                type = categoryMatch.Groups["type"].Value;
            }

            if (ErrorIdPattern().Match(text) is { Success: true } id) errorId = id.Groups["id"].Value.Trim();
        }

        var header = placeFirst ? Match.Empty : HeaderPattern().Match(message);
        var symbol = header.Success && !message.StartsWith("At ", StringComparison.Ordinal) ? header.Groups["sym"].Value : null;
        var withoutCommand = symbol is not null ? header.Groups["msg"].Value : message;
        var location = at >= 0 ? Location(lines, at) : null;

        var frames = location is null && symbol is null
            ? Array.Empty<ErrorFrame>()
            :
            [
                new ErrorFrame
                {
                    Order = 0,
                    Symbol = symbol,
                    File = location is null ? null : ParserHelpers.CleanFilePath(location.Groups["file"].Value),
                    Line = location is null ? 0 : int.Parse(location.Groups["line"].Value),
                    Column = location is null ? 0 : int.Parse(location.Groups["col"].Value),
                    RawLine = lines[start].Text,
                },
            ];

        return new ParsedError
        {
            LanguageId = "powershell",
            Confidence = 85,
            RawText = ParserHelpers.RawTextOf(lines, start, end + 1),
            FirstLineSequence = lines[start].Sequence,
            ExceptionType = category == ParserError ? ParserError : type,
            ErrorCode = errorId,
            Message = withoutCommand is { Length: > 0 } ? withoutCommand : null,
            Frames = frames,
        };
    }

    private static bool IsRecordBody(string text) =>
        text.TrimStart().StartsWith('+') ||
        text.StartsWith("At ", StringComparison.Ordinal) ||
        LocationTailPattern().IsMatch(text) ||
        text.Trim().Length == 0;
}
