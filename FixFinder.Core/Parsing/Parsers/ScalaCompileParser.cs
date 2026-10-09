using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>
/// Reads what Scala says when a program does not build - in the words of the Scala 3 compiler, of the Scala 2 compiler,
/// or of Scala CLI itself, which builds and runs it.
/// </summary>
/// <remarks>
/// <para>
/// Scala 3 heads each message with its kind, its code, and its file, line and column - the column counted from 0 - then
/// shows the line with a bar before it, marks the place with carets, and gives the message on the barred lines after:
/// </para>
/// <code>
/// -- [E007] Type Mismatch Error: Total.scala:3:21
/// 3 |    val count: Int = "three"
///   |                     ^^^^^^^
///   |                     Found:    ("three" : String)
///   |                     Required: Int
/// </code>
/// <para>
/// Scala 2 gives the file and line, then the message - over as many lines as it needs - then the line itself and a caret
/// under the place: <c>Total.scala:3: error: type mismatch;</c>, <c> found   : String("three")</c>, <c> required: Int</c>.
/// Scala CLI's own errors are tagged <c>[error]</c>, in colour even when nobody is watching, and the colour is taken off:
/// a library or a version of Scala it could not find in its cache, and a program with no main, or several.
/// </para>
/// </remarks>
public sealed partial class ScalaCompileParser : IStackTraceParser, IMultiErrorParser
{
    public string LanguageId => "scala";

    public string DisplayName => "Scala compiler";

    /// <summary>What Scala CLI's errors are called when it could not find something in its cache, which FixFinder never downloads.</summary>
    public const string MissingDownload = "missing download";

    [GeneratedRegex(@"^-- (?:\[(?<code>E\d+)\] )?(?<kind>.*?)(?<severity>Error|Warning|Info): (?<file>.+?):(?<line>\d+):(?<column>\d+)\s*-*\s*$")]
    private static partial Regex Scala3Heading();

    /// <summary>A barred line of a Scala 3 message: the line of code, numbered, or the carets and the message, not.</summary>
    [GeneratedRegex(@"^\s*(?<number>\d+)?\s*\|(?<text>.*)$")]
    private static partial Regex Scala3Barred();

    [GeneratedRegex(@"^(?<file>(?:[A-Za-z]:)?[^:\r\n]*?\.(?:scala|sc)):(?<line>\d+): (?<severity>error|warning): (?<message>.*)$")]
    private static partial Regex Scala2Heading();

    [GeneratedRegex(@"^\s*\^+\s*$")]
    private static partial Regex CaretsOnly();

    [GeneratedRegex(@"^(?:\d+ (?:errors?|warnings?)(?: found)?|Compilation failed)\s*$")]
    private static partial Regex Summary();

    [GeneratedRegex(@"^longer explanation available when compiling with|^Run with -explain")]
    private static partial Regex PointerToMore();

    [GeneratedRegex(@"^\[error\]\s+(?<text>.*?)\s*$")]
    private static partial Regex CliError();

    [GeneratedRegex(@"^(?<file>(?:[A-Za-z]:)?[^:\r\n]*?\.(?:scala|sc)):(?<line>\d+):(?<column>\d+)$")]
    private static partial Regex CliPlace();

    [GeneratedRegex(@"^(?:Error downloading \S+|No main class found|Found several main classes: .+)$")]
    private static partial Regex CliProblem();

    [GeneratedRegex(@"\x1B\[[0-9;]*[A-Za-z]")]
    private static partial Regex Colour();

    public int Detect(IReadOnlyList<string> lines)
    {
        var score = 0;

        foreach (var line in lines.Select(Plain))
        {
            if (Scala3Heading().IsMatch(line)) score += 45;
            else if (Scala2Heading().IsMatch(line)) score += 40;
            else if (CliError().Match(line) is { Success: true } error && CliProblem().IsMatch(error.Groups["text"].Value)) score += 60;
        }

        return Math.Min(score, 100);
    }

    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines) => ParseAll(lines).FirstOrDefault();

    /// <summary>Every error, in the order Scala gave them.</summary>
    public IReadOnlyList<ParsedError> ParseAll(IReadOnlyList<CapturedLine> lines) => Read(lines, wantWarnings: false);

    /// <summary>Every warning, in the order Scala gave them - each with its file and line, as a summary of them is not one.</summary>
    public static IReadOnlyList<ParsedError> ParseWarnings(IReadOnlyList<CapturedLine> lines) => Read(lines, wantWarnings: true);

    private static IReadOnlyList<ParsedError> Read(IReadOnlyList<CapturedLine> lines, bool wantWarnings)
    {
        var found = new List<ParsedError>();
        var text = lines.Select(line => Plain(line.Text)).ToList();
        (string File, int Line, int Column)? cliPlace = null;

        for (var index = 0; index < text.Count; index++)
        {
            if (Scala3Heading().Match(text[index]) is { Success: true } scala3)
            {
                var end = index + 1;
                while (end < text.Count && Scala3Barred().IsMatch(text[end]) && !Scala3Heading().IsMatch(text[end])) end++;

                var isWarning = scala3.Groups["severity"].Value != "Error";
                if (isWarning == wantWarnings)
                    found.Add(Scala3Message(lines, text, index, end, scala3));

                index = end - 1;
                continue;
            }

            if (Scala2Heading().Match(text[index]) is { Success: true } scala2)
            {
                var end = index + 1;
                while (end < text.Count && !EndsScala2Message(text[end])) end++;

                if ((scala2.Groups["severity"].Value == "warning") == wantWarnings)
                    found.Add(Scala2Message(lines, text, index, end, scala2));

                index = end - 1;
                continue;
            }

            if (wantWarnings || CliError().Match(text[index]) is not { Success: true } cli) continue;

            var said = cli.Groups["text"].Value;

            // Scala CLI counts the columns of its own places from 1, unlike Scala 3's.
            if (CliPlace().Match(said) is { Success: true } place)
            {
                cliPlace = (place.Groups["file"].Value, int.Parse(place.Groups["line"].Value), int.Parse(place.Groups["column"].Value));
                continue;
            }

            if (!CliProblem().IsMatch(said)) continue;

            var type = said.StartsWith("Error downloading", StringComparison.Ordinal) ? MissingDownload : "build error";
            found.Add(Error(lines, index, index + 1, type, null, said, cliPlace is { } where ? [Frame(where.File, where.Line, where.Column, text[index])] : []));
            cliPlace = null;
        }

        return found;
    }

    private static ParsedError Scala3Message(IReadOnlyList<CapturedLine> lines, List<string> text, int heading, int end, Match scala3)
    {
        var said = new List<string>();

        for (var index = heading + 1; index < end; index++)
        {
            var barred = Scala3Barred().Match(text[index]);
            if (barred.Groups["number"].Success) continue;

            var words = barred.Groups["text"].Value.Trim();
            if (words.Length == 0 || CaretsOnly().IsMatch(words) || PointerToMore().IsMatch(words)) continue;

            said.Add(words);
        }

        var kind = scala3.Groups["kind"].Value.Trim();
        var message = said.Count > 0 ? string.Join("\n", said) : kind.Length > 0 ? kind : "error";
        var type = scala3.Groups["severity"].Value == "Error" ? "compile error" : "compile warning";

        // Scala 3 counts columns from 0; FixFinder, as most compilers, from 1.
        var frame = Frame(scala3.Groups["file"].Value, int.Parse(scala3.Groups["line"].Value), int.Parse(scala3.Groups["column"].Value) + 1, text[heading]);

        return Error(lines, heading, end, type, scala3.Groups["code"].Success ? scala3.Groups["code"].Value : null, message, [frame]);
    }

    private static ParsedError Scala2Message(IReadOnlyList<CapturedLine> lines, List<string> text, int heading, int end, Match scala2)
    {
        // The line of code is the one just above the caret; what comes between the heading and it is more of the message.
        var caret = Enumerable.Range(heading + 1, end - heading - 1).LastOrDefault(index => CaretsOnly().IsMatch(text[index]), -1);
        var messageEnds = caret > heading + 1 ? caret - 1 : end;

        var said = new List<string> { scala2.Groups["message"].Value.Trim() };
        said.AddRange(text.Skip(heading + 1).Take(messageEnds - heading - 1).Select(line => line.Trim()).Where(line => line.Length > 0));

        int? column = caret > 0 ? text[caret].IndexOf('^') + 1 : null;
        var frame = Frame(scala2.Groups["file"].Value, int.Parse(scala2.Groups["line"].Value), column, text[heading]);
        var type = scala2.Groups["severity"].Value == "error" ? "compile error" : "compile warning";

        return Error(lines, heading, end, type, null, string.Join("\n", said), [frame]);
    }

    /// <summary>Whether a line ends a Scala 2 message: the next message, the count at the end, or Scala CLI saying something of its own.</summary>
    private static bool EndsScala2Message(string line) =>
        Scala2Heading().IsMatch(line) || Scala3Heading().IsMatch(line) || Summary().IsMatch(line) || CliError().IsMatch(line) ||
        line.StartsWith("warning: ", StringComparison.Ordinal);

    private static ErrorFrame Frame(string file, int line, int? column, string rawLine) => new()
    {
        Order = 0,
        File = ParserHelpers.CleanFilePath(file),
        Line = line,
        Column = column,
        RawLine = rawLine,
        Origin = FrameOrigin.FirstParty,
    };

    private static ParsedError Error(IReadOnlyList<CapturedLine> lines, int start, int end, string type, string? code, string message, List<ErrorFrame> frames) => new()
    {
        LanguageId = "scala",
        Confidence = 85,
        RawText = Plain(ParserHelpers.RawTextOf(lines, start, end)),
        FirstLineSequence = lines[start].Sequence,
        ExceptionType = type,
        ErrorCode = code,
        Message = message,
        Frames = frames,
        CulpritFrame = frames.FirstOrDefault(),
    };

    /// <summary>A line without the codes that colour it in a terminal.</summary>
    public static string Plain(string line) => Colour().Replace(line, "");
}
