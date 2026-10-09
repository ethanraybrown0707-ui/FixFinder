using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>
/// Reads what OCaml's compiler says when a program does not build: a place, the line it is on with carets under it, then
/// the error or the warning - as OCaml 5.2 writes it in its own tests' expected output:
/// </summary>
/// <remarks>
/// <code>
/// File "marks.ml", line 2, characters 28-33:
/// 2 |    if n = 0 then 1 else n * facto (n-1)
///                                 ^^^^^
/// Error: Unbound value "facto"
/// Hint: If this is a recursive definition,
/// you should add the "rec" keyword on line 1
/// </code>
/// <para>
/// The characters are counted from 0. A message can go on over several lines, and a hint can follow it. A syntax error
/// can be followed by a second place, with no Error of its own, saying which bracket "might be unmatched" - that is kept
/// with the error. A warning is <c>Warning 26 [unused-var]: unused variable spare.</c> - its number, and from OCaml 4.12
/// its name, which is kept as its code. OCaml 5.2 puts names and types in double quotes, and earlier OCamls did not.
/// </para>
/// </remarks>
public sealed partial class OCamlCompileParser : IStackTraceParser, IMultiErrorParser
{
    public string LanguageId => "ocaml";

    public string DisplayName => "OCaml compiler";

    [GeneratedRegex(@"^File ""(?<file>[^""]+)"", lines? (?<line>\d+)(?:-\d+)?, characters (?<start>\d+)-(?<end>\d+):\s*$")]
    private static partial Regex Place();

    /// <summary>A line of the code OCaml shows - "12 | let total = 0" - or the carets under it.</summary>
    [GeneratedRegex(@"^\s*(?:\d+ \||\^+\s*$|\.\.\.)|^\s+\^")]
    private static partial Regex ShownCode();

    [GeneratedRegex(@"^Error: (?<message>.*)$")]
    private static partial Regex ErrorLine();

    [GeneratedRegex(@"^Warning (?<number>\d+)(?: \[(?<name>[\w-]+)\])?: (?<message>.*)$")]
    private static partial Regex WarningLine();

    [GeneratedRegex(@"^\s+This ""?.+?""? might be unmatched\s*$")]
    private static partial Regex Unmatched();

    public int Detect(IReadOnlyList<string> lines)
    {
        var score = 0;

        for (var index = 0; index < lines.Count; index++)
        {
            if (!Place().IsMatch(lines[index])) continue;

            score += 30;
            if (lines.Skip(index + 1).Take(6).Any(line => ErrorLine().IsMatch(line) || WarningLine().IsMatch(line))) score += 30;
        }

        return Math.Min(score, 100);
    }

    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines) => ParseAll(lines).FirstOrDefault();

    public IReadOnlyList<ParsedError> ParseAll(IReadOnlyList<CapturedLine> lines) => Read(lines, wantWarnings: false);

    /// <summary>Every warning, in the order OCaml gave them.</summary>
    public static IReadOnlyList<ParsedError> ParseWarnings(IReadOnlyList<CapturedLine> lines) => Read(lines, wantWarnings: true);

    private static IReadOnlyList<ParsedError> Read(IReadOnlyList<CapturedLine> lines, bool wantWarnings)
    {
        var found = new List<ParsedError>();
        ParsedError? lastError = null;

        for (var index = 0; index < lines.Count; index++)
        {
            if (Place().Match(lines[index].Text) is not { Success: true } place) continue;

            // Past the code OCaml shows, to what it says of it.
            var said = index + 1;
            while (said < lines.Count && ShownCode().IsMatch(lines[said].Text) && !Place().IsMatch(lines[said].Text)) said++;
            if (said >= lines.Count) break;

            var end = said + 1;
            while (end < lines.Count && !Place().IsMatch(lines[end].Text) && lines[end].Text.Trim().Length > 0) end++;

            var heading = lines[said].Text;
            var more = lines.Skip(said + 1).Take(end - said - 1).Select(line => line.Text.Trim()).Where(text => text.Length > 0).ToList();

            if (ErrorLine().Match(heading) is { Success: true } error)
            {
                lastError = Error(lines, index, end, place, "compile error", null, string.Join("\n", [error.Groups["message"].Value.Trim(), .. more]));
                if (!wantWarnings) found.Add(lastError);
            }
            else if (WarningLine().Match(heading) is { Success: true } warning)
            {
                var code = warning.Groups["name"].Success ? warning.Groups["name"].Value : $"warning {warning.Groups["number"].Value}";
                if (wantWarnings) found.Add(Error(lines, index, end, place, "compile warning", code, string.Join("\n", [warning.Groups["message"].Value.Trim(), .. more])));
            }
            else if (Unmatched().IsMatch(heading) && lastError is not null && !wantWarnings)
            {
                // The second place of a syntax error: the bracket that was never closed. It is said with the error, as OCaml says it.
                var withBracket = Error(lines, found.Count > 0 ? IndexOf(lines, lastError) : index, end, PlaceOf(lastError), "compile error", null,
                    lastError.Message + "\n" + heading.Trim());
                found[^1] = withBracket;
                lastError = withBracket;
            }

            index = end - 1;
        }

        return found;
    }

    private static ParsedError Error(IReadOnlyList<CapturedLine> lines, int start, int end, Match place, string type, string? code, string message) =>
        Error(lines, start, end, (place.Groups["file"].Value, int.Parse(place.Groups["line"].Value), int.Parse(place.Groups["start"].Value), int.Parse(place.Groups["end"].Value)),
            type, code, message);

    private static ParsedError Error(IReadOnlyList<CapturedLine> lines, int start, int end, (string File, int Line, int Start, int End) place, string type, string? code, string message)
    {
        // OCaml counts characters from 0; FixFinder's columns, as most compilers', count from 1.
        var frame = new ErrorFrame
        {
            Order = 0,
            File = ParserHelpers.CleanFilePath(place.File),
            Line = place.Line,
            Column = place.Start + 1,
            RawLine = lines[start].Text,
            Origin = FrameOrigin.FirstParty,
        };

        return new ParsedError
        {
            LanguageId = "ocaml",
            Confidence = 85,
            RawText = ParserHelpers.RawTextOf(lines, start, end),
            FirstLineSequence = lines[start].Sequence,
            ExceptionType = type,
            ErrorCode = code,
            Message = message,
            Frames = [frame],
            CulpritFrame = frame,
        };
    }

    /// <summary>Where an error is - its file, line and characters - read again from the first line of what it was read from.</summary>
    private static (string File, int Line, int Start, int End) PlaceOf(ParsedError error)
    {
        var place = Place().Match(error.RawText.Split('\n')[0].TrimEnd('\r'));
        return (place.Groups["file"].Value, int.Parse(place.Groups["line"].Value), int.Parse(place.Groups["start"].Value), int.Parse(place.Groups["end"].Value));
    }

    private static int IndexOf(IReadOnlyList<CapturedLine> lines, ParsedError error)
    {
        for (var index = 0; index < lines.Count; index++)
            if (lines[index].Sequence == error.FirstLineSequence) return index;

        return 0;
    }

    /// <summary>
    /// The characters an error's place covers, read from what OCaml said - "characters 28-33" - so a change can be made to
    /// exactly what OCaml pointed at: the first, counted from 0, and the one after the last.
    /// </summary>
    public static (int Start, int End)? SpanOf(ParsedError error) =>
        Place().Match(error.RawText.Split('\n')[0].TrimEnd('\r')) is { Success: true } place
            ? (int.Parse(place.Groups["start"].Value), int.Parse(place.Groups["end"].Value))
            : null;
}
