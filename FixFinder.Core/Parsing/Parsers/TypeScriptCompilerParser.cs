using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>
/// What TypeScript's compiler, tsc, says of a program's types, as TypeScript 7.0.2 writes it with --pretty false: one message
/// to a line - <c>main.ts(4,5): error TS2322: Type 'string' is not assignable to type 'number'.</c> - and, under a message
/// that says more, lines of its own indented beneath it, which are joined to it.
/// </summary>
public static partial class TypeScriptCompilerParser
{
    /// <summary>What a message of tsc's is filed as: a type error, which tsc refuses and Node.js runs past.</summary>
    public const string TypeError = "type error";

    public const string LanguageId = "typescript";

    [GeneratedRegex(@"^(?<file>.+?)\((?<line>\d+),(?<column>\d+)\): error (?<code>TS\d+): (?<message>.*?)\s*$")]
    private static partial Regex Located();

    /// <summary><c>error TS5058: The specified path does not exist: 'x'.</c> - a message about how tsc was set up, at no place in the code.</summary>
    [GeneratedRegex(@"^error (?<code>TS\d+): (?<message>.*?)\s*$")]
    private static partial Regex Unplaced();

    /// <summary>Every error tsc placed in a file, in the order it wrote them, each once.</summary>
    public static IReadOnlyList<ParsedError> Errors(IReadOnlyList<CapturedLine> lines)
    {
        var found = new List<ParsedError>();

        for (var index = 0; index < lines.Count; index++)
        {
            if (Located().Match(lines[index].Text) is not { Success: true } said) continue;

            var start = index;
            var words = new List<string> { said.Groups["message"].Value };
            while (index + 1 < lines.Count && lines[index + 1].Text.StartsWith(' ') && lines[index + 1].Text.Trim().Length > 0)
            {
                index++;
                words.Add(lines[index].Text.Trim());
            }

            var frame = new ErrorFrame
            {
                Order = 0,
                File = ParserHelpers.CleanFilePath(said.Groups["file"].Value),
                Line = int.Parse(said.Groups["line"].Value),
                Column = int.Parse(said.Groups["column"].Value),
                RawLine = lines[start].Text,
            };

            found.Add(new ParsedError
            {
                LanguageId = LanguageId,
                Confidence = 95,
                RawText = ParserHelpers.RawTextOf(lines, start, index + 1),
                FirstLineSequence = lines[start].Sequence,
                ExceptionType = TypeError,
                ErrorCode = said.Groups["code"].Value,
                Message = string.Join(" ", words),
                Frames = [frame],
                CulpritFrame = frame,
            });
        }

        return found.DistinctBy(error => (error.ErrorCode, error.Frames[0].File, error.Frames[0].Line, error.Frames[0].Column)).ToList();
    }

    /// <summary>
    /// What tsc said of how it was set up rather than of the code - a tsconfig.json it could not read, an option it does not
    /// know, no files to check - each as tsc wrote it: at no place, or at a place in the tsconfig.json itself.
    /// </summary>
    public static IReadOnlyList<string> SetupProblems(IReadOnlyList<CapturedLine> lines) =>
        lines.Select(line => line.Text.Trim())
            .Where(text => Unplaced().IsMatch(text) || Located().Match(text) is { Success: true } said &&
                           Path.GetFileName(said.Groups["file"].Value).Equals("tsconfig.json", StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .ToList();
}
