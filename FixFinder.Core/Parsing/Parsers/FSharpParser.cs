using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>
/// Reads F#'s messages, as F# Interactive 15.2 - F# 10 - prints them for a script it checks or runs.
/// </summary>
/// <remarks>
/// <para>
/// What F# refuses before any of the script runs is <c>main.fsx(3,15): error FS0039: The value or constructor 'totl' is not
/// defined. Maybe you want one of the following:</c> - the file, the line and the column, error, warning or info, F#'s
/// number for the message, and the message - running on over the lines after it, up to a blank line: <c>   total</c>, the
/// names F# suggests, or <c>    'int'    </c> and <c>but here has type</c> for two types that do not match. F# Interactive
/// stops at its first error, so a check names one; its warnings come before it, and a script that only warns runs.
/// </para>
/// <para>
/// One message is not the script's: checked with --typecheck-only, a script that is one expression and nothing else - a
/// single printfn, or a single for loop - gets <c>error FS0971: Undefined value 'it'</c>, and runs as it should. A script that
/// uses a name it that it never defined is told so with FS0039, like any other name.
/// </para>
/// <para>
/// What stops a running script is .NET's own exception - <c>System.DivideByZeroException: Attempted to divide by zero.</c> -
/// with the calls that led there, the latest first: <c>at FSI_0001.describe(Int32 mark) in C:\marks\main.fsx:line 4</c>
/// for a function of the script, <c>at &lt;StartupCode$FSI_0001&gt;.$FSI_0001.main@() in ...main.fsx:line 5</c> for the script
/// itself, F#'s own library at the path it was built from, and then F# Interactive's own calls, which are not the
/// script's. F# Interactive ends with <c>Stopped due to error</c>. A function that calls itself forever ends the process
/// with <c>Stack overflow.</c> and the calls it repeated, which name no line.
/// </para>
/// </remarks>
public sealed partial class FSharpParser : IStackTraceParser, IMultiErrorParser
{
    /// <summary>What F# refused before any of the script ran: a name it does not know, two types that do not match.</summary>
    public const string CompileError = "compile error";

    /// <summary>What F# warned of, or noted, as it checked the script - and carried on past.</summary>
    public const string Warning = "compile warning";

    /// <summary>A function that called itself until the program ran out of room - .NET says only "Stack overflow."</summary>
    public const string StackOverflow = "System.StackOverflowException";

    public string LanguageId => "fsharp";
    public string DisplayName => "F#";

    /// <summary><c>main.fsx(3,15): error FS0039: The value or constructor 'totl' is not defined.</c></summary>
    [GeneratedRegex(@"^(?<file>.+?\.fsx?)\((?<line>\d+),(?<column>\d+)\): (?<severity>error|warning|info) (?<code>FS\d+): (?<msg>.*?)\s*$")]
    private static partial Regex DiagnosticPattern();

    /// <summary>A call .NET names in a file of the script's: <c>   at FSI_0001.describe(Int32 mark) in C:\marks\main.fsx:line 4</c>.</summary>
    [GeneratedRegex(@"^\s+at\s+.+\s+in\s+.+\.fsx?:line\s+\d+\s*$")]
    private static partial Regex ScriptFramePattern();

    /// <summary>The first of F# Interactive's own calls, under the script's: it runs the script by reflection.</summary>
    [GeneratedRegex(@"^\s+at\s+(?:System\.Reflection\.|System\.RuntimeType\.InvokeMember|FSharp\.Compiler\.Interactive\.)")]
    private static partial Regex InteractiveFramePattern();

    /// <summary>A call a stack overflow repeated, or led there through: <c>   at FSI_0001.count(Int32)</c>.</summary>
    [GeneratedRegex(@"^\s+at\s+(?<sym>\S.*?)\s*$")]
    private static partial Regex OverflowFramePattern();

    private const string Stopped = "Stopped due to error";

    /// <summary>What --typecheck-only says of a script that is one expression and nothing else, which runs as it should.</summary>
    public const string CheckOnlyMessage = "Undefined value 'it'";

    private const string Overflowed = "Stack overflow.";

    public int Detect(IReadOnlyList<string> lines)
    {
        var score = 0;

        foreach (var line in lines)
        {
            if (DiagnosticPattern().IsMatch(line)) score += 70;
            if (ScriptFramePattern().IsMatch(line)) score += 30;
            if (line.Contains("FSI_", StringComparison.Ordinal)) score += 20;
            if (line.StartsWith(Stopped, StringComparison.Ordinal)) score += 40;
        }

        return Math.Min(score, 100);
    }

    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines)
    {
        // What F# refused stops the script before any of it runs; nothing after it was the script's doing.
        if (Diagnostics(lines, "error").FirstOrDefault() is { } refused) return refused;

        return Overflow(lines) ?? Exception(lines);
    }

    /// <summary>The error F# refused the script for, or what stopped it running - one, as F# Interactive stops at its first.</summary>
    public IReadOnlyList<ParsedError> ParseAll(IReadOnlyList<CapturedLine> lines) => Parse(lines) is { } error ? [error] : [];

    /// <summary>Every warning and note F# gave as it checked the script - each a message of its own, once.</summary>
    public static IReadOnlyList<ParsedError> Warnings(IReadOnlyList<CapturedLine> lines) =>
        Diagnostics(lines, "warning", "info").DistinctBy(warning => (warning.ErrorCode, warning.Frames[0].File, warning.Frames[0].Line)).ToList();

    /// <summary>"a warning", for a warning F# gave and carried on past - or null for anything else.</summary>
    public static string? CarriedOnPast(ParsedError error) => error.LanguageId == "fsharp" && error.ExceptionType == Warning ? "a warning" : null;

    /// <summary>
    /// The messages of the given severities, in the order F# wrote them, each with the lines that run on after it joined to it:
    /// the names it suggests joined by commas, and every other line by a space.
    /// </summary>
    private static IReadOnlyList<ParsedError> Diagnostics(IReadOnlyList<CapturedLine> lines, params string[] severities)
    {
        var found = new List<ParsedError>();

        for (var index = 0; index < lines.Count; index++)
        {
            if (DiagnosticPattern().Match(lines[index].Text) is not { Success: true } said || !severities.Contains(said.Groups["severity"].Value)) continue;
            if (said.Groups["code"].Value == "FS0971" && said.Groups["msg"].Value == CheckOnlyMessage) continue;

            var start = index;
            var words = new List<string> { said.Groups["msg"].Value };
            var suggested = new List<string>();

            while (index + 1 < lines.Count && lines[index + 1].Text.Trim().Length > 0 && !DiagnosticPattern().IsMatch(lines[index + 1].Text))
            {
                index++;
                var more = lines[index].Text.Trim();

                if (words[^1].EndsWith("Maybe you want one of the following:", StringComparison.Ordinal) || suggested.Count > 0) suggested.Add(more);
                else words.Add(more);
            }

            var message = string.Join(" ", words) + (suggested.Count > 0 ? " " + string.Join(", ", suggested) : "");

            found.Add(new ParsedError
            {
                LanguageId = "fsharp",
                Confidence = 95,
                RawText = ParserHelpers.RawTextOf(lines, start, index + 1),
                FirstLineSequence = lines[start].Sequence,
                ExceptionType = said.Groups["severity"].Value == "error" ? CompileError : Warning,
                ErrorCode = said.Groups["code"].Value,
                Message = message,
                Frames =
                [
                    new ErrorFrame
                    {
                        Order = 0,
                        File = ParserHelpers.CleanFilePath(said.Groups["file"].Value),
                        Line = int.Parse(said.Groups["line"].Value),
                        Column = int.Parse(said.Groups["column"].Value),
                        RawLine = lines[start].Text,
                    },
                ],
            });
        }

        return found;
    }

    /// <summary>
    /// .NET's exception, read as .NET writes it, as F#'s: its calls up to F# Interactive's own, which ran the script and are
    /// none of the script's.
    /// </summary>
    private static ParsedError? Exception(IReadOnlyList<CapturedLine> lines)
    {
        var interactiveFrom = Enumerable.Range(0, lines.Count).FirstOrDefault(index => InteractiveFramePattern().IsMatch(lines[index].Text), lines.Count);
        if (new DotNetStackTraceParser().Parse(lines.Take(interactiveFrom).ToList()) is not { } thrown) return null;

        return AsFSharp(thrown);
    }

    private static ParsedError AsFSharp(ParsedError thrown) => new()
    {
        LanguageId = "fsharp",
        Confidence = Math.Max(thrown.Confidence, 90),
        RawText = thrown.RawText,
        FirstLineSequence = thrown.FirstLineSequence,
        ExceptionType = thrown.ExceptionType,
        ErrorCode = thrown.ErrorCode,
        Message = thrown.Message,
        Frames = thrown.Frames,
        Causes = thrown.Causes.Select(AsFSharp).ToList(),
    };

    /// <summary>
    /// <c>Stack overflow.</c>, then the calls .NET saw repeated - <c>Repeated 32002 times:</c> between lines of dashes - and the
    /// ones that led there; none names a line, so each call is named by its function alone.
    /// </summary>
    private static ParsedError? Overflow(IReadOnlyList<CapturedLine> lines)
    {
        var start = Enumerable.Range(0, lines.Count).FirstOrDefault(index => lines[index].Text.Trim() == Overflowed, -1);
        if (start < 0) return null;

        var frames = new List<ErrorFrame>();
        var end = start + 1;

        for (var index = start + 1; index < lines.Count; index++)
        {
            var text = lines[index].Text;
            if (InteractiveFramePattern().IsMatch(text)) break;

            end = index + 1;
            if (OverflowFramePattern().Match(text) is not { Success: true } call) continue;

            var symbol = call.Groups["sym"].Value;
            if (frames.All(frame => frame.Symbol != symbol))
                frames.Add(new ErrorFrame { Order = frames.Count, Symbol = symbol, RawLine = text });
        }

        return new ParsedError
        {
            LanguageId = "fsharp",
            Confidence = 90,
            RawText = ParserHelpers.RawTextOf(lines, start, end),
            FirstLineSequence = lines[start].Sequence,
            ExceptionType = StackOverflow,
            // "Stack overflow. Repeated 32002 times: FSI_0001.count(Int32)" - the call it repeated, as .NET named it.
            Message = lines.Skip(start + 1).Select(line => line.Text.Trim()).FirstOrDefault(text => text.StartsWith("Repeated ", StringComparison.Ordinal)) is { } repeated &&
                      frames.Count > 0
                ? $"{Overflowed} {repeated} {frames[0].Symbol}"
                : Overflowed,
            Frames = frames,
        };
    }
}
