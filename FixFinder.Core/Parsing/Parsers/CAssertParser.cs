using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>
/// Reads what a C or C++ program prints when it stops at an assert of its own: assert.h prints what it checked, and where,
/// then aborts. Windows' C runtime - the one MinGW's gcc and MSVC build against - prints
/// <c>Assertion failed: total(marks, 3) == 210, file C:\work\marks.c, line 14</c>; the GNU C library prints
/// <c>marks: marks.c:14: main: Assertion `total(marks, 3) == 210' failed.</c>, with the function it was in.
/// </summary>
public sealed partial class CAssertParser : IStackTraceParser
{
    /// <summary>What a program that stopped at an assert of its own is said to have stopped with: the words assert.h prints.</summary>
    public const string AssertionFailed = "Assertion failed";

    public string LanguageId => "c-assert";
    public string DisplayName => "C and C++ assert";

    [GeneratedRegex(@"^Assertion failed: (?<checked>.+), file (?<file>.+), line (?<line>\d+)\s*$")]
    private static partial Regex WindowsRuntime();

    [GeneratedRegex(@"^(?:.+?: )?(?<file>(?:[A-Za-z]:[\\/])?[^:]+?):(?<line>\d+): (?<function>[^:]+): Assertion [`'](?<checked>.+)' failed\.\s*$")]
    private static partial Regex GnuRuntime();

    private static Match Read(string line) => WindowsRuntime().Match(line) is { Success: true } windows ? windows : GnuRuntime().Match(line);

    public int Detect(IReadOnlyList<string> lines) => lines.Any(line => Read(line).Success) ? 90 : 0;

    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines)
    {
        for (var index = lines.Count - 1; index >= 0; index--)
        {
            if (Read(lines[index].Text) is not { Success: true } stopped) continue;

            return new ParsedError
            {
                LanguageId = LanguageId,
                Confidence = 90,
                RawText = lines[index].Text,
                FirstLineSequence = lines[index].Sequence,
                ExceptionType = AssertionFailed,
                Message = stopped.Groups["checked"].Value.Trim(),
                Frames =
                [
                    new ErrorFrame
                    {
                        Order = 0,
                        Symbol = stopped.Groups["function"].Success ? stopped.Groups["function"].Value : null,
                        File = ParserHelpers.CleanFilePath(stopped.Groups["file"].Value.Trim()),
                        Line = int.Parse(stopped.Groups["line"].Value),
                        RawLine = lines[index].Text,
                    },
                ],
            };
        }

        return null;
    }
}
