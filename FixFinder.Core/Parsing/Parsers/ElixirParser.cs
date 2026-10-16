using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>
/// Reads Elixir's messages, as Elixir 1.20 prints them, and the Erlang errors beneath them.
/// </summary>
/// <remarks>
/// <para>
/// What stops a running program is <c>** (ArithmeticError) bad argument in arithmetic expression</c>, with what it said
/// running on over the lines after it when it needs more - KeyError's map and its "Did you mean:" - and then the calls that
/// led there, the latest first: <c>marks.exs:2: Marks.average/1</c>, <c>marks.exs:5: (file)</c> for the file itself, and
/// Elixir's and Erlang's own calls, in brackets - <c>(elixir 1.20.4) lib/code.ex:1639: Code.require_file/2</c> - or with
/// no file: <c>:erlang.+("Total: ", 10)</c>.
/// </para>
/// <para>
/// What Elixir refuses before any of the program runs is written in a box: <c>error:</c> and what is wrong, the line with ^
/// marks under the place, and <c>└─ marks.exs:3:9</c> - the file, the line and the column, and in a module the function it
/// is in. Once every error has been written, <c>** (CompileError) marks.exs: cannot compile file (errors have been
/// logged)</c> follows. A syntax error names its own kind first - <c>** (TokenMissingError) token missing on
/// marks.exs:6:4:</c> - and then the same box, with a hint when Elixir has one. A warning is boxed the same way, after
/// <c>warning:</c>, and Elixir carries on past it.
/// </para>
/// </remarks>
public sealed partial class ElixirParser : IStackTraceParser, IMultiErrorParser
{
    /// <summary>What Elixir refused to compile, having written it in a box: an undefined variable, an undefined function.</summary>
    public const string CompileError = "compile error";

    /// <summary>What Elixir warned of as it compiled the program, and carried on past.</summary>
    public const string Warning = "compile warning";

    /// <summary>The kinds of error Elixir gives for code it cannot read at all.</summary>
    public static readonly IReadOnlySet<string> SyntaxErrors = new HashSet<string>(StringComparer.Ordinal)
    {
        "SyntaxError", "TokenMissingError", "MismatchedDelimiterError",
    };

    /// <summary>Elixir's and Erlang's own applications, whose calls are not the program's.</summary>
    private static readonly IReadOnlySet<string> OwnApplications = new HashSet<string>(StringComparer.Ordinal)
    {
        "elixir", "stdlib", "kernel", "erts", "compiler", "iex", "mix", "logger", "eex", "ex_unit",
    };

    public string LanguageId => "elixir";
    public string DisplayName => "Elixir";

    [GeneratedRegex(@"^\*\*\s+\((?<type>[A-Za-z_][\w.]*)\)\s*(?<msg>.*?)\s*$")]
    private static partial Regex HeaderPattern();

    [GeneratedRegex(@"^\s+(?:\((?<app>[^)]+)\)\s+)?(?<file>(?:[A-Za-z]:)?[^\s:()]+\.(?:ex|exs|erl)):(?<line>\d+):\s*(?<sym>.+?)\s*$")]
    private static partial Regex FramePattern();

    [GeneratedRegex(@"^\s+(?<sym>:[a-z_]\w*\.[^\s(]+)\(.*\)\s*$")]
    private static partial Regex BuiltInPattern();

    /// <summary>A call with no file: <c>(elixir 1.20.4) Enum.summ([1, 2])</c>, <c>Grade.letter/1</c>.</summary>
    [GeneratedRegex(@"^\s+(?:\((?<app>[^)]+)\)\s+)?(?<sym>(?:[A-Z]\w*\.)+[a-z_]\w*[?!]?)(?:/\d+|\(.*\))\s*$")]
    private static partial Regex CallPattern();

    /// <summary>The first line of a boxed message: <c>    error: undefined variable "totl"</c>.</summary>
    [GeneratedRegex(@"^\s*(?<severity>error|warning): (?<msg>.+?)\s*$")]
    private static partial Regex DiagnosticPattern();

    /// <summary>The last line of a box: <c>    └─ marks.exs:3:9</c>, and in a module <c>    └─ marks.exs:3:34: Marks.report/1</c>.</summary>
    [GeneratedRegex(@"^\s*└─\s+(?<file>.+?\.(?:exs|ex)):(?<line>\d+)(?::(?<column>\d+))?(?::\s*(?<context>.+?))?\s*$")]
    private static partial Regex BoxEndPattern();

    /// <summary>Where a syntax error's own header says it is: <c>token missing on marks.exs:6:4:</c>.</summary>
    [GeneratedRegex(@"\bon (?<file>.+?\.(?:exs|ex)):(?<line>\d+)(?::(?<column>\d+))?:?$")]
    private static partial Regex HeaderPlacePattern();

    public int Detect(IReadOnlyList<string> lines)
    {
        var score = 0;

        foreach (var line in lines)
        {
            if (HeaderPattern().IsMatch(line)) score += 65;
            if (FramePattern().IsMatch(line)) score += 20;
            if (BuiltInPattern().IsMatch(line)) score += 15;
            if (BoxEndPattern().IsMatch(line)) score += 40;
        }

        return Math.Min(score, 100);
    }

    /// <summary>"a warning", for a warning Elixir gave and carried on past - or null for anything else.</summary>
    public static string? CarriedOnPast(ParsedError error) => error.LanguageId == "elixir" && error.ExceptionType == Warning ? "a warning" : null;

    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines)
    {
        var headers = Enumerable.Range(0, lines.Count)
            .Select(index => (Index: index, Match: HeaderPattern().Match(lines[index].Text)))
            .Where(header => header.Match.Success)
            .ToList();

        // Code Elixir cannot read at all: nothing else is said of it.
        var syntaxErrors = headers.Where(header => SyntaxErrors.Contains(header.Match.Groups["type"].Value)).ToList();
        if (syntaxErrors.Count > 0) return ReadSyntaxError(lines, syntaxErrors[^1].Index, syntaxErrors[^1].Match);

        // Code it read but would not compile: the first of the errors it wrote out - the ones after it can follow from it.
        if (CompileErrors(lines).FirstOrDefault() is { } refused) return refused;

        // What stopped the running program is the last thing it printed of the kind; a run that only warned stopped with nothing.
        return headers.Count > 0 ? ReadException(lines, headers[^1].Index, headers[^1].Match) : null;
    }

    /// <summary>Every error Elixir wrote out before it would not compile the program - or, for any other error, that one.</summary>
    public IReadOnlyList<ParsedError> ParseAll(IReadOnlyList<CapturedLine> lines) =>
        CompileErrors(lines) is { Count: > 0 } refused ? refused : Parse(lines) is { } error ? [error] : [];

    /// <summary>Every warning Elixir gave as it compiled the program - each a message of its own, once.</summary>
    public static IReadOnlyList<ParsedError> Warnings(IReadOnlyList<CapturedLine> lines) =>
        Boxed(lines, "warning", Warning).DistinctBy(warning => (warning.Message, warning.Frames[0].File, warning.Frames[0].Line)).ToList();

    private static IReadOnlyList<ParsedError> CompileErrors(IReadOnlyList<CapturedLine> lines) => Boxed(lines, "error", CompileError);

    /// <summary>
    /// The boxed messages of one severity, each ending at the └─ line that places it. An error: line under a syntax error's
    /// own header belongs to that header, so it is not one of these.
    /// </summary>
    private static List<ParsedError> Boxed(IReadOnlyList<CapturedLine> lines, string severity, string kind)
    {
        var found = new List<ParsedError>();

        for (var index = 0; index < lines.Count; index++)
        {
            if (DiagnosticPattern().Match(lines[index].Text) is not { Success: true } said || said.Groups["severity"].Value != severity) continue;
            if (index > 0 && HeaderPattern().Match(lines[index - 1].Text) is { Success: true } above && SyntaxErrors.Contains(above.Groups["type"].Value)) continue;
            if (BoxEnd(lines, index + 1) is not { } end) continue;

            var place = BoxEndPattern().Match(lines[end].Text);
            var frame = new ErrorFrame
            {
                Order = 0,
                File = ParserHelpers.CleanFilePath(place.Groups["file"].Value),
                Line = int.Parse(place.Groups["line"].Value),
                Column = place.Groups["column"].Success ? int.Parse(place.Groups["column"].Value) : null,
                Symbol = place.Groups["context"].Success ? place.Groups["context"].Value : null,
                RawLine = lines[end].Text,
            };

            found.Add(new ParsedError
            {
                LanguageId = "elixir",
                Confidence = 92,
                RawText = ParserHelpers.RawTextOf(lines, index, end + 1),
                FirstLineSequence = lines[index].Sequence,
                ExceptionType = kind,
                Message = said.Groups["msg"].Value,
                Frames = [frame],
                CulpritFrame = frame,
            });

            index = end;
        }

        return found;
    }

    /// <summary>The └─ line that ends the box starting just above <paramref name="from"/> - or null when another message starts first.</summary>
    private static int? BoxEnd(IReadOnlyList<CapturedLine> lines, int from)
    {
        for (var index = from; index < lines.Count && index < from + 80; index++)
        {
            var text = lines[index].Text;
            if (BoxEndPattern().IsMatch(text)) return index;
            if (DiagnosticPattern().IsMatch(text) || HeaderPattern().IsMatch(text)) return null;
        }

        return null;
    }

    /// <summary>A syntax error: its kind from the header, what is wrong from the error: line under it, and its place from the box.</summary>
    private static ParsedError ReadSyntaxError(IReadOnlyList<CapturedLine> lines, int headerIndex, Match header)
    {
        var said = headerIndex + 1 < lines.Count ? DiagnosticPattern().Match(lines[headerIndex + 1].Text) : Match.Empty;
        var end = BoxEnd(lines, said.Success ? headerIndex + 2 : headerIndex + 1) ?? headerIndex;
        var place = end > headerIndex ? BoxEndPattern().Match(lines[end].Text) : HeaderPlacePattern().Match(header.Groups["msg"].Value);

        var frames = new List<ErrorFrame>();
        if (place.Success)
        {
            frames.Add(new ErrorFrame
            {
                Order = 0,
                File = ParserHelpers.CleanFilePath(place.Groups["file"].Value),
                Line = int.Parse(place.Groups["line"].Value),
                Column = place.Groups["column"].Success ? int.Parse(place.Groups["column"].Value) : null,
                RawLine = lines[end].Text,
            });
        }

        return new ParsedError
        {
            LanguageId = "elixir",
            Confidence = 94,
            RawText = ParserHelpers.RawTextOf(lines, headerIndex, end + 1),
            FirstLineSequence = lines[headerIndex].Sequence,
            ExceptionType = header.Groups["type"].Value,
            Message = said.Success ? said.Groups["msg"].Value : header.Groups["msg"].Value,
            Frames = frames,
            CulpritFrame = frames.FirstOrDefault(),
        };
    }

    /// <summary>
    /// An exception that stopped the running program: what it said - on as many lines as it took, blank ones and lines of
    /// its own among them, such as KeyError's "Did you mean:" and Protocol.UndefinedError's "Got value:" - and then the
    /// calls that led there, which end the message. With no calls after it, the message is its header and the indented
    /// lines under it.
    /// </summary>
    private static ParsedError ReadException(IReadOnlyList<CapturedLine> lines, int headerIndex, Match header)
    {
        var frames = new List<ErrorFrame>();
        var end = headerIndex + 1;
        var indentedEnd = headerIndex + 1;

        for (var index = headerIndex + 1; index < lines.Count && index < headerIndex + 120; index++)
        {
            var text = lines[index].Text;

            if (FrameOf(text, frames.Count) is { } frame)
            {
                frames.Add(frame);
                end = index + 1;
                continue;
            }

            if (frames.Count > 0 || HeaderPattern().IsMatch(text) || DiagnosticPattern().IsMatch(text)) break;

            if (indentedEnd == index && (text.Length == 0 || char.IsWhiteSpace(text[0]))) indentedEnd = index + 1;
        }

        if (frames.Count == 0) end = indentedEnd;

        return new ParsedError
        {
            LanguageId = "elixir",
            Confidence = 88,
            RawText = ParserHelpers.RawTextOf(lines, headerIndex, end),
            FirstLineSequence = lines[headerIndex].Sequence,
            ExceptionType = header.Groups["type"].Value,
            Message = header.Groups["msg"].Value is { Length: > 0 } message ? message : null,
            Frames = frames,
        };
    }

    private static ErrorFrame? FrameOf(string text, int order)
    {
        if (FramePattern().Match(text) is { Success: true } frame)
        {
            return new ErrorFrame
            {
                Order = order,
                Symbol = frame.Groups["sym"].Value,
                File = ParserHelpers.CleanFilePath(frame.Groups["file"].Value),
                Line = int.Parse(frame.Groups["line"].Value),
                Module = frame.Groups["app"].Success ? frame.Groups["app"].Value : null,
                // A call in an .erl file is Erlang's own - erlang.erl:13498: :erlang.binary_to_integer("12\n").
                Origin = IsElixirsOwn(frame.Groups["app"]) || frame.Groups["file"].Value.EndsWith(".erl", StringComparison.OrdinalIgnoreCase)
                    ? FrameOrigin.Runtime
                    : FrameOrigin.Unknown,
                RawLine = text,
            };
        }

        if (BuiltInPattern().Match(text) is { Success: true } builtIn)
            return new ErrorFrame { Order = order, Symbol = builtIn.Groups["sym"].Value, Origin = FrameOrigin.Runtime, RawLine = text };

        if (CallPattern().Match(text) is { Success: true } call)
        {
            return new ErrorFrame
            {
                Order = order,
                Symbol = call.Groups["sym"].Value,
                Module = call.Groups["app"].Success ? call.Groups["app"].Value : null,
                Origin = IsElixirsOwn(call.Groups["app"]) ? FrameOrigin.Runtime : FrameOrigin.Unknown,
                RawLine = text,
            };
        }

        return null;
    }

    /// <summary>Whether a call's application - "elixir 1.20.4", "stdlib 8.0.4" - is Elixir's or Erlang's own.</summary>
    private static bool IsElixirsOwn(Group application) =>
        application.Success && OwnApplications.Contains(application.Value.Split(' ')[0]);
}
