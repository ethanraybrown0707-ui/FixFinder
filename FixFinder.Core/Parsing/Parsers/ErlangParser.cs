using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>
/// Reads what erlc says of an Erlang program as it compiles it, and what stopped the program as it ran - as Erlang/OTP 29
/// writes them.
/// </summary>
/// <remarks>
/// <para>
/// erlc names the file, the line and the column, then the message - <c>marks.erl:7:24: variable 'Totl' is unbound, did you
/// mean 'Total'?</c> - and shows the line under it, each line of it starting with %, with ^ under the place. A warning says
/// Warning: before its message, and the program is still compiled.
/// </para>
/// <para>
/// What stopped a program is written as Erlang's shell writes it, by FixFinder's starting module:
/// <c>** exception error: an error occurred when evaluating an arithmetic expression</c>, then the functions it was in, the
/// latest first - <c>in function  marks:average/1 (marks.erl:4)</c>, <c>in call from marks:main/0 (marks.erl:8)</c> - with
/// what a built-in function was called as under it. The words name Erlang's own reason for the error - badarith, badmatch,
/// function_clause - and the error is given that name.
/// </para>
/// </remarks>
public sealed partial class ErlangParser : IStackTraceParser, IMultiErrorParser
{
    public const string CompileError = "compile error";
    public const string CompileWarning = "compile warning";

    public string LanguageId => "erlang";
    public string DisplayName => "Erlang";

    /// <summary>The words Erlang's shell writes for each of its reasons, as Erlang/OTP 29 was seen to write them, and the reason's own name.</summary>
    private static readonly (string Words, string Reason)[] Reasons =
    [
        ("an error occurred when evaluating an arithmetic expression", "badarith"),
        ("no match of right hand side value", "badmatch"),
        ("no function clause matching", "function_clause"),
        ("no case clause matching", "case_clause"),
        ("no true branch found when evaluating an if expression", "if_clause"),
        ("undefined function", "undef"),
        ("bad argument", "badarg"),
        ("bad key:", "badkey"),
        ("bad map:", "badmap"),
    ];

    /// <summary>Erlang/OTP's own modules, whose functions are not the program's.</summary>
    private static readonly IReadOnlySet<string> OwnModules = new HashSet<string>(StringComparer.Ordinal)
    {
        "lists", "io", "io_lib", "erlang", "string", "maps", "dict", "orddict", "sets", "ordsets", "gb_trees", "gb_sets",
        "proplists", "file", "filename", "timer", "ets", "gen_server", "gen_statem", "gen_event", "supervisor", "application",
        "erl_eval", "init", "escript", "shell", "unicode", "binary", "math", "rand", "queue", "array", "calendar", "re", "code",
        "os", "erl_error", "unicode_util", "proc_lib", "error_handler",
    };

    [GeneratedRegex(@"^(?<file>.+?\.(?:erl|hrl)):(?<line>\d+):(?<column>\d+): (?<warning>Warning: )?(?<msg>.+?)\s*$")]
    private static partial Regex DiagnosticPattern();

    /// <summary>A line of the code erlc shows under its message: <c>%    7|     io:format("~p~n", [Totl]).</c></summary>
    [GeneratedRegex(@"^%\s*\d*\|")]
    private static partial Regex ExcerptPattern();

    [GeneratedRegex(@"^\*\* exception (?<class>error|exit|throw):\s?(?<msg>.*?)\s*$")]
    private static partial Regex HeaderPattern();

    /// <summary><c>  in function  marks:average/1 (marks.erl:4)</c>, <c>  in call from ...</c>, <c>  in operator  +/2</c>.</summary>
    [GeneratedRegex(@"^\s+in (?:function|call from|operator)\s+(?<sym>.+?)(?:\s+\((?<file>[^()]+?\.erl):(?<line>\d+)\))?\s*$")]
    private static partial Regex FramePattern();

    /// <summary>A call with its place and no more - the clause no clause matched: <c>marks:letter(20) (marks.erl:4)</c>.</summary>
    [GeneratedRegex(@"^\s*(?<sym>\S.*?)\s+\((?<file>[^()]+?\.erl):(?<line>\d+)\)\s*$")]
    private static partial Regex PlacedCallPattern();

    /// <summary>The same call at the end of the header's words: <c>no function clause matching lists:nth(0,"FP") (lists.erl:318)</c>.</summary>
    [GeneratedRegex(@"\s(?<sym>'?[a-z][\w@]*'?:'?[a-z_][\w@]*'?\(.*\))\s+\((?<file>[^()]+?\.erl):(?<line>\d+)\)$")]
    private static partial Regex TrailingPlacedCallPattern();

    public int Detect(IReadOnlyList<string> lines)
    {
        var score = 0;

        foreach (var line in lines)
        {
            if (DiagnosticPattern().IsMatch(line)) score += 40;
            if (ExcerptPattern().IsMatch(line)) score += 15;
            if (HeaderPattern().IsMatch(line)) score += 70;
            if (FramePattern().IsMatch(line)) score += 20;
        }

        return Math.Min(score, 100);
    }

    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines)
    {
        if (Diagnostics(lines, warnings: false).FirstOrDefault() is { } refused) return refused;

        var header = Enumerable.Range(0, lines.Count).LastOrDefault(index => HeaderPattern().IsMatch(lines[index].Text), -1);
        return header >= 0 ? ReadException(lines, header) : null;
    }

    public IReadOnlyList<ParsedError> ParseAll(IReadOnlyList<CapturedLine> lines) =>
        Diagnostics(lines, warnings: false) is { Count: > 0 } refused ? refused : Parse(lines) is { } error ? [error] : [];

    /// <summary>Every warning erlc gave - the program is compiled all the same.</summary>
    public static IReadOnlyList<ParsedError> ParseWarnings(IReadOnlyList<CapturedLine> lines) => Diagnostics(lines, warnings: true);

    /// <summary>Erlang's own name for what stopped the program - badarith for its words - or null for words it was not seen to write.</summary>
    public static string? ReasonOf(string message) =>
        Reasons.FirstOrDefault(reason => message.StartsWith(reason.Words, StringComparison.Ordinal)).Reason;

    private static List<ParsedError> Diagnostics(IReadOnlyList<CapturedLine> lines, bool warnings)
    {
        var found = new List<ParsedError>();

        for (var index = 0; index < lines.Count; index++)
        {
            if (DiagnosticPattern().Match(lines[index].Text) is not { Success: true } said || said.Groups["warning"].Success != warnings) continue;

            var end = index + 1;
            while (end < lines.Count && ExcerptPattern().IsMatch(lines[end].Text)) end++;

            var frame = new ErrorFrame
            {
                Order = 0,
                File = ParserHelpers.CleanFilePath(said.Groups["file"].Value),
                Line = int.Parse(said.Groups["line"].Value),
                Column = int.Parse(said.Groups["column"].Value),
                RawLine = lines[index].Text,
            };

            found.Add(new ParsedError
            {
                LanguageId = "erlang",
                Confidence = 92,
                RawText = ParserHelpers.RawTextOf(lines, index, end),
                FirstLineSequence = lines[index].Sequence,
                ExceptionType = warnings ? CompileWarning : CompileError,
                Message = said.Groups["msg"].Value,
                Frames = [frame],
                CulpritFrame = frame,
            });
        }

        return found;
    }

    /// <summary>
    /// What stopped the program: the header's words - with the call on the line under them, when the header ends without it -
    /// and the functions it was in, which end the message. "called as" and *** lines say what a built-in was given.
    /// </summary>
    private static ParsedError ReadException(IReadOnlyList<CapturedLine> lines, int headerIndex)
    {
        var header = HeaderPattern().Match(lines[headerIndex].Text);
        var message = header.Groups["msg"].Value;
        var frames = new List<ErrorFrame>();

        // The call no clause matched, placed, ends the header or is the line under it.
        if (TrailingPlacedCallPattern().Match(message) is { Success: true } inHeader)
        {
            frames.Add(Frame(frames.Count, inHeader, lines[headerIndex].Text));
            message = $"{message[..inHeader.Index]} {inHeader.Groups["sym"].Value}";
        }

        var end = headerIndex + 1;

        for (var index = headerIndex + 1; index < lines.Count && index < headerIndex + 60; index++)
        {
            var text = lines[index].Text;

            if (FramePattern().Match(text) is { Success: true } frame)
            {
                frames.Add(Frame(frames.Count, frame, text));
                end = index + 1;
                continue;
            }

            if (frames.Count == 0 && index == headerIndex + 1 && PlacedCallPattern().Match(text) is { Success: true } placed)
            {
                frames.Add(Frame(frames.Count, placed, text));
                message = $"{message} {placed.Groups["sym"].Value}".Trim();
                end = index + 1;
                continue;
            }

            // What a built-in was called as, and what was wrong with its argument.
            if (text.Length > 0 && char.IsWhiteSpace(text[0]) && (text.TrimStart().StartsWith("called as", StringComparison.Ordinal) || text.TrimStart().StartsWith("***", StringComparison.Ordinal)))
            {
                end = index + 1;
                continue;
            }

            break;
        }

        return new ParsedError
        {
            LanguageId = "erlang",
            Confidence = 90,
            RawText = ParserHelpers.RawTextOf(lines, headerIndex, end),
            FirstLineSequence = lines[headerIndex].Sequence,
            ExceptionType = ReasonOf(message) ?? header.Groups["class"].Value,
            Message = message.Length > 0 ? message : null,
            Frames = frames,
        };
    }

    private static ErrorFrame Frame(int order, Match found, string rawLine)
    {
        var symbol = found.Groups["sym"].Value.Trim();
        var module = symbol.Contains(':') ? symbol[..symbol.IndexOf(':')] : null;
        var file = found.Groups["file"].Success ? ParserHelpers.CleanFilePath(found.Groups["file"].Value) : null;

        return new ErrorFrame
        {
            Order = order,
            Symbol = symbol,
            File = file,
            Line = found.Groups["line"].Success ? int.Parse(found.Groups["line"].Value) : null,
            Module = module,
            // A built-in - list_to_integer/1, +/2 - has no file, and Erlang/OTP's own modules are not the program's.
            Origin = file is null || module is not null && OwnModules.Contains(module) ? FrameOrigin.Runtime : FrameOrigin.Unknown,
            RawLine = rawLine,
        };
    }
}
