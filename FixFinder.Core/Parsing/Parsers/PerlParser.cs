using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>
/// Reads Perl's messages, as Perl 5.42 prints them: the message, then " at FILE line N", and a full stop.
/// </summary>
/// <remarks>
/// <para>
/// While a program reads its input Perl adds which line of it: <c>Illegal division by zero at marks.pl line 6, &lt;STDIN&gt; line 1.</c>
/// A compilation error ends instead with where Perl was reading - <c>, near "print"</c>, <c>, at end of line</c> or
/// <c>, at EOF</c> - and the errors are followed by <c>marks.pl had compilation errors.</c> from perl -c, or
/// <c>Execution of marks.pl aborted due to compilation errors.</c> when it was run; such an error is a compilation error.
/// </para>
/// <para>
/// What die stops a program with is any text the program chose, so it is given no type of its own. Warnings - Use of
/// uninitialized value, Argument "x" isn't numeric - are written the same way, and Perl carries on past them; they are
/// told apart by their words, each one Perl was seen to print and carry on past, and a run that printed nothing else is said to have
/// printed a warning. Callers Carp lists follow on lines of their own: <c>	main::check(-1) called at marks.pl line 9</c>.
/// </para>
/// </remarks>
public sealed partial class PerlParser : IStackTraceParser
{
    /// <summary>What Perl refused before any of the program ran.</summary>
    public const string CompilationError = "compilation error";

    /// <summary>What Perl printed and carried on past.</summary>
    public const string Warning = "warning";

    public string LanguageId => "perl";
    public string DisplayName => "Perl";

    [GeneratedRegex(
        @"^(?<msg>\S.*?) at (?<file>.+?\.(?:pl|pm|t)) line (?<line>\d+)(?:, <\w+> (?:line|chunk) \d+)?(?:, (?:near ""(?<near>.*)|at end of line|at EOF))?\.?\s*$")]
    private static partial Regex HeaderPattern();

    [GeneratedRegex(@"^\s+(?<sym>[\w:]+)\(.*\)\s+called at\s+(?<file>.+?)\s+line\s+(?<line>\d+)\.?\s*$")]
    private static partial Regex FramePattern();

    /// <summary>
    /// What Perl adds after the errors that stopped it compiling - and after one a use line raised, which runs as Perl compiles:
    /// <c>BEGIN failed--compilation aborted at marks.pl line 3.</c>, below the <c>Can't locate Missing/Marks.pm in @INC</c> it stopped for.
    /// </summary>
    [GeneratedRegex(@"(?:had compilation errors|aborted due to compilation errors|compilation aborted)\.?\s*$|^BEGIN failed--compilation aborted\b")]
    private static partial Regex CompilationFailedPattern();

    /// <summary>The start of each warning Perl 5.42 was seen to print and carry on past.</summary>
    [GeneratedRegex(
        @"^(?:Use of uninitialized value|Argument "".*"" isn't numeric|Found = in conditional|Name "".*"" used only once|Useless use of|" +
        @"Odd number of elements in|Deep recursion on|Scalar value .* better written as|Possible precedence|Unquoted string|"".*"" variable .* masks earlier|" +
        @"Possible unintended interpolation|readline\(\) on (?:closed|unopened) filehandle|print\(\) on (?:closed|unopened) filehandle|Misplaced _ in number|" +
        @"length\(\) used on @)")]
    private static partial Regex WarningPattern();

    public int Detect(IReadOnlyList<string> lines)
    {
        var score = 0;

        foreach (var line in lines)
        {
            if (HeaderPattern().IsMatch(line)) score += 55;
            if (FramePattern().IsMatch(line)) score += 30;
            if (CompilationFailedPattern().IsMatch(line)) score += 30;
        }

        return Math.Min(score, 100);
    }

    /// <summary>"a warning", for a warning Perl printed and carried on past - or null for anything else.</summary>
    public static string? CarriedOnPast(ParsedError error) => error.LanguageId == "perl" && error.ExceptionType == Warning ? "a warning" : null;

    /// <summary>Whether a message is one Perl prints and carries on past.</summary>
    public static bool IsWarning(string message) => WarningPattern().IsMatch(message);

    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines)
    {
        var headers = Enumerable.Range(0, lines.Count)
            .Select(index => (Index: index, Match: HeaderPattern().Match(lines[index].Text)))
            .Where(header => header.Match.Success && !FramePattern().IsMatch(lines[header.Index].Text))
            .ToList();
        if (headers.Count == 0) return null;

        var stopping = headers.Where(header => !IsWarning(header.Match.Groups["msg"].Value)).ToList();

        // A compilation error is the first Perl found - the ones after it can follow from it; what stopped a running program is
        // the last thing it printed of the kind. A string with no closing quote and nothing after it is found as Perl reads the file,
        // though Perl says only "Can't find string terminator", with no "had compilation errors" after it.
        if ((lines.Any(line => CompilationFailedPattern().IsMatch(line.Text)) ||
             stopping.Any(header => header.Match.Groups["msg"].Value.StartsWith("Can't find string terminator", StringComparison.Ordinal))) && stopping.Count > 0)
            return Read(lines, stopping[0].Index, stopping[0].Match, CompilationError);

        if (stopping.Count > 0) return Read(lines, stopping[^1].Index, stopping[^1].Match, null);

        return Read(lines, headers[^1].Index, headers[^1].Match, Warning);
    }

    /// <summary>Every warning Perl printed while the program ran - each a message of its own, once, however often a loop printed it.</summary>
    public static IReadOnlyList<ParsedError> RunWarnings(IReadOnlyList<CapturedLine> lines) => Enumerable.Range(0, lines.Count)
        .Select(index => (Index: index, Match: HeaderPattern().Match(lines[index].Text)))
        .Where(header => header.Match.Success && IsWarning(header.Match.Groups["msg"].Value))
        .Select(header => Read(lines, header.Index, header.Match, Warning))
        .DistinctBy(warning => (warning.Message, warning.Frames[0].File, warning.Frames[0].Line))
        .ToList();

    /// <summary>The word or symbol Perl said it was near when it stopped reading - print, for near "print" - or null when it said none.</summary>
    public static string? NearOf(ParsedError error) =>
        error.RawText.Split('\n').Select(line => HeaderPattern().Match(line.TrimEnd('\r'))).FirstOrDefault(header => header.Success) is { } header &&
        header.Groups["near"].Success
            ? header.Groups["near"].Value.TrimEnd('"')
            : null;

    private static ParsedError Read(IReadOnlyList<CapturedLine> lines, int headerIndex, Match header, string? kind)
    {
        var frames = new List<ErrorFrame>
        {
            new()
            {
                Order = 0,
                File = ParserHelpers.CleanFilePath(header.Groups["file"].Value),
                Line = int.Parse(header.Groups["line"].Value),
                RawLine = lines[headerIndex].Text,
            },
        };

        var end = headerIndex + 1;

        for (var index = headerIndex + 1; index < lines.Count; index++)
        {
            var text = lines[index].Text;

            if (FramePattern().Match(text) is { Success: true } frame)
            {
                frames.Add(new ErrorFrame
                {
                    Order = frames.Count,
                    Symbol = frame.Groups["sym"].Value,
                    File = ParserHelpers.CleanFilePath(frame.Groups["file"].Value),
                    Line = int.Parse(frame.Groups["line"].Value),
                    RawLine = text,
                });

                end = index + 1;
                continue;
            }

            // A compilation error's near "..." can run onto the lines after it, and Perl adds a line of its own in brackets:
            // "  (Might be a runaway multi-line "" string starting on line 4)".
            if (kind == CompilationError && !HeaderPattern().IsMatch(text) && !CompilationFailedPattern().IsMatch(text) && text.Trim().Length > 0)
            {
                end = index + 1;
                continue;
            }

            break;
        }

        return new ParsedError
        {
            LanguageId = "perl",
            Confidence = kind == CompilationError ? 90 : frames.Count > 1 ? 82 : 70,
            RawText = ParserHelpers.RawTextOf(lines, headerIndex, end),
            FirstLineSequence = lines[headerIndex].Sequence,
            ExceptionType = kind,
            Message = header.Groups["msg"].Value.Trim() is { Length: > 0 } message ? message : null,
            Frames = frames,
        };
    }
}
