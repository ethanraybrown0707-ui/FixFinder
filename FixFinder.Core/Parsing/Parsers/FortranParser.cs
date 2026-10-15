using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>
/// Reads what gfortran says of a Fortran program, as gfortran 13 says it, and what stops a program it built.
/// </summary>
/// <remarks>
/// <para>
/// A compiler message is a block: the file, line and column - <c>marks.f90:5:15:</c> - then the line itself, with a 1 under the
/// place (and a 2 under a second place, on its own line, when the message names two), then <c>Error: Symbol 'totl' at (1) has
/// no IMPLICIT type; did you mean 'total'?</c>, or <c>Warning: ...</c>, its flag in brackets at the end when it has one.
/// A <c>note:</c> block says more of the message before it. After the last error the compiler itself may add
/// <c>f951.exe: Error: Unexpected end of file in 'marks.f90'</c>, which names no line.
/// </para>
/// <para>
/// A program built with -fcheck=all stops at a check that fails with <c>At line 6 of file marks.f90</c> and
/// <c>Fortran runtime error: Index '0' of dimension 1 of array 'scores' below lower bound of 1</c>. A whole number divided by
/// zero stops it with <c>Program received signal SIGFPE: Floating-point exception - erroneous arithmetic operation.</c>, and
/// the backtrace after it names no lines on Windows, so neither does what is read from it.
/// </para>
/// </remarks>
public sealed partial class FortranParser : IStackTraceParser, IMultiErrorParser
{
    /// <summary>What a check of -fcheck=all, or reading the input, stopped the program with.</summary>
    public const string RuntimeError = "Fortran runtime error";

    public string LanguageId => "fortran";
    public string DisplayName => "Fortran";

    [GeneratedRegex(@"^(?<file>(?:[A-Za-z]:[\\/])?[^:]+?\.(?:f|for|ftn|f77|f90|f95|f03|f08)):(?<line>\d+):(?<col>\d+):\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex LocationPattern();

    [GeneratedRegex(@"^(?<severity>Error|Warning|Fatal Error|note): (?<msg>.+?)\s*$")]
    private static partial Regex VerdictPattern();

    [GeneratedRegex(@"\s*\[(?<flag>-W[\w=-]+)\]$")]
    private static partial Regex FlagPattern();

    [GeneratedRegex(@"^At line (?<line>\d+) of file (?<file>.+?)(?: \(unit = .*\))?\s*$")]
    private static partial Regex RuntimeLocationPattern();

    [GeneratedRegex(@"^Fortran runtime error: (?<msg>.+?)\s*$")]
    private static partial Regex RuntimeErrorPattern();

    [GeneratedRegex(@"^Program received signal (?<signal>SIG[A-Z]+): (?<msg>.+?)\.?\s*$")]
    private static partial Regex SignalPattern();

    public int Detect(IReadOnlyList<string> lines)
    {
        var score = 0;

        foreach (var line in lines)
        {
            if (LocationPattern().IsMatch(line)) score += 30;
            if (VerdictPattern().IsMatch(line)) score += 30;
            if (RuntimeErrorPattern().IsMatch(line) || SignalPattern().IsMatch(line)) score += 70;
            if (RuntimeLocationPattern().IsMatch(line)) score += 30;
        }

        return Math.Min(score, 100);
    }

    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines) => ParseAll(lines).FirstOrDefault() ?? Runtime(lines);

    public IReadOnlyList<ParsedError> ParseAll(IReadOnlyList<CapturedLine> lines) => Compiled(lines, "Error", "compile error");

    /// <summary>The warnings gfortran gave while building - each with its flag, such as -Wsurprising, as its code.</summary>
    public static IReadOnlyList<ParsedError> ParseWarnings(IReadOnlyList<CapturedLine> lines) => Compiled(lines, "Warning", "compile warning");

    private static IReadOnlyList<ParsedError> Compiled(IReadOnlyList<CapturedLine> lines, string wanted, string kind)
    {
        var found = new List<ParsedError>();

        for (var index = 0; index < lines.Count; index++)
        {
            if (LocationPattern().Match(lines[index].Text) is not { Success: true } location) continue;

            // The block runs to its verdict line - Error:, Warning: or note: - through the code it shows.
            var verdictIndex = index + 1;
            while (verdictIndex < lines.Count && verdictIndex <= index + 8 && !VerdictPattern().IsMatch(lines[verdictIndex].Text) &&
                   !LocationPattern().IsMatch(lines[verdictIndex].Text)) verdictIndex++;
            if (verdictIndex >= lines.Count || VerdictPattern().Match(lines[verdictIndex].Text) is not { Success: true } verdict) continue;

            var severity = verdict.Groups["severity"].Value;
            if (severity != wanted && !(wanted == "Error" && severity == "Fatal Error")) continue;

            var message = verdict.Groups["msg"].Value;
            var flag = FlagPattern().Match(message);
            var frame = new ErrorFrame
            {
                Order = 0,
                File = ParserHelpers.CleanFilePath(location.Groups["file"].Value),
                Line = int.Parse(location.Groups["line"].Value),
                Column = int.Parse(location.Groups["col"].Value),
                RawLine = lines[index].Text,
            };

            found.Add(new ParsedError
            {
                LanguageId = "fortran",
                Confidence = 95,
                RawText = ParserHelpers.RawTextOf(lines, index, verdictIndex + 1),
                FirstLineSequence = lines[index].Sequence,
                ExceptionType = kind,
                ErrorCode = flag.Success ? flag.Groups["flag"].Value : null,
                Message = flag.Success ? message[..flag.Index] : message,
                Frames = [frame],
                CulpritFrame = frame,
            });

            index = verdictIndex;
        }

        return found;
    }

    /// <summary>What stopped the program: a check that failed, at the line it names, or a signal, which names none.</summary>
    private ParsedError? Runtime(IReadOnlyList<CapturedLine> lines)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            if (RuntimeErrorPattern().Match(lines[index].Text) is { Success: true } error)
            {
                var location = index > 0 ? RuntimeLocationPattern().Match(lines[index - 1].Text) : Match.Empty;
                var start = location.Success ? index - 1 : index;

                return new ParsedError
                {
                    LanguageId = LanguageId,
                    Confidence = 95,
                    RawText = ParserHelpers.RawTextOf(lines, start, index + 1),
                    FirstLineSequence = lines[start].Sequence,
                    ExceptionType = RuntimeError,
                    Message = error.Groups["msg"].Value,
                    Frames = location.Success
                        ?
                        [
                            new ErrorFrame
                            {
                                Order = 0,
                                File = ParserHelpers.CleanFilePath(location.Groups["file"].Value),
                                Line = int.Parse(location.Groups["line"].Value),
                                RawLine = lines[start].Text,
                            },
                        ]
                        : [],
                };
            }

            if (SignalPattern().Match(lines[index].Text) is { Success: true } signal)
            {
                return new ParsedError
                {
                    LanguageId = LanguageId,
                    Confidence = 90,
                    RawText = ParserHelpers.RawTextOf(lines, index, index + 1),
                    FirstLineSequence = lines[index].Sequence,
                    ExceptionType = signal.Groups["signal"].Value,
                    Message = signal.Groups["msg"].Value,
                    Frames = [],
                };
            }
        }

        return null;
    }
}
