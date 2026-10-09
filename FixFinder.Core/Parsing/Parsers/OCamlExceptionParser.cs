using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>
/// Reads the exception that stopped an OCaml program, and where it was raised - as ocamlrun prints it when the program was
/// compiled with -g and OCAMLRUNPARAM has b in it, in the words of OCaml 5.2's own tests:
/// </summary>
/// <remarks>
/// <code>
/// Fatal error: exception Invalid_argument("index out of bounds")
/// Raised by primitive operation at Backtrace in file "backtrace.ml", line 21, characters 12-24
/// Called from Backtrace in file "backtrace.ml", line 21, characters 9-25
/// </code>
/// <para>
/// A frame is "Raised at", "Raised by primitive operation at", "Re-raised at" or "Called from", then - from OCaml 4.12 -
/// the function, then the file and where in it. A frame in OCaml's own library - Stdlib.failwith, in stdlib.ml - is not
/// the program's, so the crash is placed at the first frame that is.
/// </para>
/// </remarks>
public sealed partial class OCamlExceptionParser : IStackTraceParser
{
    public string LanguageId => "ocaml";

    public string DisplayName => "OCaml exception";

    [GeneratedRegex(@"^Fatal error: exception (?<type>[\w.']+)(?<argument>.*)$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^(?:Raised at|Raised by primitive operation at|Re-raised at|Called from)\s+(?:(?<symbol>[\w.$']+) in )?file ""(?<file>[^""]+)""(?: \(inlined\))?(?:, line (?<line>\d+)(?:, characters (?<start>\d+)-\d+)?)?")]
    private static partial Regex Frame();

    /// <summary>The files of OCaml's own library a frame can be in when, before OCaml 4.12, it names no function.</summary>
    private static readonly HashSet<string> LibraryFiles = new(StringComparer.Ordinal)
    {
        "stdlib.ml", "list.ml", "array.ml", "string.ml", "bytes.ml", "char.ml", "hashtbl.ml", "map.ml", "set.ml", "buffer.ml",
        "printf.ml", "format.ml", "scanf.ml", "int.ml", "float.ml", "option.ml", "result.ml", "seq.ml", "queue.ml", "stack.ml",
        "lazy.ml", "camlinternalFormat.ml", "camlinternalLazy.ml", "in_channel.ml", "out_channel.ml", "sys.ml", "fun.ml",
    };

    public int Detect(IReadOnlyList<string> lines)
    {
        var heading = lines.Any(line => Heading().IsMatch(line));
        if (!heading) return 0;

        return Math.Min(100, 70 + 10 * lines.Count(line => Frame().IsMatch(line)));
    }

    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            if (Heading().Match(lines[index].Text) is not { Success: true } heading) continue;

            var frames = new List<ErrorFrame>();
            var end = index + 1;

            for (; end < lines.Count && Frame().Match(lines[end].Text) is { Success: true } frame; end++)
            {
                var symbol = frame.Groups["symbol"].Success ? frame.Groups["symbol"].Value : null;
                var file = frame.Groups["file"].Value;

                frames.Add(new ErrorFrame
                {
                    Order = frames.Count,
                    Symbol = symbol,
                    File = ParserHelpers.CleanFilePath(file),
                    Line = frame.Groups["line"].Success ? int.Parse(frame.Groups["line"].Value) : null,
                    Column = frame.Groups["start"].Success ? int.Parse(frame.Groups["start"].Value) + 1 : null,
                    RawLine = lines[end].Text,
                    Origin = IsOCamlsOwn(symbol, file) ? FrameOrigin.Runtime : FrameOrigin.Unknown,
                });
            }

            return new ParsedError
            {
                LanguageId = LanguageId,
                Confidence = 90,
                RawText = ParserHelpers.RawTextOf(lines, index, end),
                FirstLineSequence = lines[index].Sequence,
                ExceptionType = heading.Groups["type"].Value,
                Message = ArgumentOf(heading.Groups["argument"].Value),
                Frames = frames,
            };
        }

        return null;
    }

    /// <summary>Whether a frame is in OCaml's own library: a function of Stdlib or of OCaml's internals, or one of its files.</summary>
    private static bool IsOCamlsOwn(string? symbol, string file) =>
        symbol is not null
            ? symbol.StartsWith("Stdlib", StringComparison.Ordinal) || symbol.StartsWith("Camlinternal", StringComparison.Ordinal)
            : LibraryFiles.Contains(Path.GetFileName(file));

    /// <summary>
    /// What the exception carries, as a reader reads it: <c>("index out of bounds")</c> is index out of bounds; a
    /// Match_failure's <c>("marks.ml", 4, 2)</c> is left as it is, as it names the place.
    /// </summary>
    private static string? ArgumentOf(string argument)
    {
        var trimmed = argument.Trim();
        if (trimmed.Length == 0) return null;

        return Regex.Match(trimmed, @"^\(""(?<text>(?:[^""\\]|\\.)*)""\)$") is { Success: true } text ? text.Groups["text"].Value : trimmed;
    }
}
