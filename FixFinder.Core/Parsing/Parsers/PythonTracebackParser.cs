using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>
/// Reads Python tracebacks, including chained "during handling" / "direct cause" blocks.
/// </summary>
/// <remarks>
/// <b>Python prints frames outermost-first.</b> Every other language in this folder prints them
/// innermost-first, and the rest of FixFinder assumes <c>Order == 0</c> means "where it threw",
/// so this parser reverses them. Skipping that reversal does not break anything visibly - it
/// just makes the culprit frame the program's entry point on every single Python crash.
/// <para>
/// Chained blocks read oldest-first in the output: the traceback printed <i>first</i> is the
/// cause, and the last one is the exception that actually escaped. So the last block becomes the
/// outer <see cref="ParsedError"/> and earlier ones nest inside it as causes.
/// </para>
/// </remarks>
public sealed partial class PythonTracebackParser : IStackTraceParser
{
    public string LanguageId => "python";
    public string DisplayName => "Python";

    private const string TracebackHeader = "Traceback (most recent call last):";

    [GeneratedRegex(@"^\s+File\s+""(?<file>.+?)"",\s+line\s+(?<line>\d+),\s+in\s+(?<sym>.+?)\s*$")]
    private static partial Regex FramePattern();

    /// <summary>The terminating "SomeError: message" line, which sits at column 0.</summary>
    /// <summary>
    /// The line that ends a traceback: a dotted type name at column 0, with an optional message.
    /// </summary>
    /// <remarks>
    /// Deliberately does not require the name to end in Error or Exception. It used to, and that
    /// quietly lost every exception a library names differently - <c>requests.exceptions.InvalidSchema</c>,
    /// <c>MissingSchema</c>, <c>TooManyRedirects</c>, <c>socket.timeout</c> - which are exactly
    /// the third-party failures web search is best at answering. The type and message were
    /// dropped and the search query came out empty.
    /// <para>
    /// Position is what identifies the terminator, not the spelling of the name: inside a
    /// traceback block, the first line back at column 0 after the frames <i>is</i> the exception.
    /// Requiring an identifier with no spaces before the colon is enough to keep ordinary log
    /// lines from matching.
    /// </para>
    /// </remarks>
    [GeneratedRegex(@"^(?<type>[A-Za-z_][A-Za-z0-9_.]*)(?:\s*:\s*(?<msg>.*))?$")]
    private static partial Regex TerminatorPattern();

    private static readonly string[] ChainMarkers =
    [
        "During handling of the above exception, another exception occurred:",
        "The above exception was the direct cause of the following exception:",
    ];

    public int Detect(IReadOnlyList<string> lines)
    {
        var score = 0;

        foreach (var line in lines)
        {
            if (line.Contains(TracebackHeader, StringComparison.Ordinal)) score += 70;
            if (FramePattern().IsMatch(line)) score += 15;
            foreach (var marker in ChainMarkers)
                if (line.Contains(marker, StringComparison.Ordinal)) score += 20;
        }

        return Math.Min(score, 100);
    }

    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines)
    {
        var headers = new List<int>();
        for (var i = 0; i < lines.Count; i++)
            if (lines[i].Text.TrimEnd().EndsWith(TracebackHeader, StringComparison.Ordinal))
                headers.Add(i);

        if (headers.Count == 0) return null;

        var blocks = new List<Block>();
        for (var b = 0; b < headers.Count; b++)
        {
            var start = headers[b];
            var limit = b + 1 < headers.Count ? headers[b + 1] : lines.Count;
            var block = ParseBlock(lines, start, limit);
            if (block is not null) blocks.Add(block);
        }

        if (blocks.Count == 0) return null;

        var raw = ParserHelpers.RawTextOf(lines, headers[0], blocks[^1].EndExclusive);

        // Oldest block is the deepest cause; wrap outwards from there.
        ParsedError? built = null;
        foreach (var block in blocks)
        {
            built = new ParsedError
            {
                LanguageId = LanguageId,
                Confidence = block.Type is null ? 60 : 90,
                RawText = raw,
                FirstLineSequence = lines[headers[0]].Sequence,
                ExceptionType = block.Type,
                Message = block.Message,
                Frames = block.Frames,
                Causes = built is null ? [] : [built],
            };
        }

        return built;
    }

    private static Block? ParseBlock(IReadOnlyList<CapturedLine> lines, int start, int limit)
    {
        var frames = new List<ErrorFrame>();
        string? type = null;
        string? message = null;
        var end = start + 1;

        for (var i = start + 1; i < limit; i++)
        {
            var text = lines[i].Text;

            if (ChainMarkers.Any(m => text.Contains(m, StringComparison.Ordinal))) break;

            var frame = FramePattern().Match(text);
            if (frame.Success)
            {
                frames.Add(new ErrorFrame
                {
                    // Placeholder: rewritten below once the whole block is known, because
                    // Order must count from the innermost frame and Python prints the reverse.
                    Order = frames.Count,
                    Symbol = frame.Groups["sym"].Value.Trim(),
                    File = ParserHelpers.CleanFilePath(frame.Groups["file"].Value),
                    Line = int.Parse(frame.Groups["line"].Value),
                    RawLine = text,
                });
                end = i + 1;
                continue;
            }

            // The echoed source line under each frame, and the 3.11+ "~~~~^^^^" pointer that
            // marks the failing sub-expression. Both are indented, and neither is a frame.
            if (text.Length > 0 && char.IsWhiteSpace(text[0]))
            {
                end = i + 1;
                continue;
            }

            var terminator = TerminatorPattern().Match(text);
            if (terminator.Success)
            {
                type = terminator.Groups["type"].Value;
                message = terminator.Groups["msg"].Success && terminator.Groups["msg"].Value.Trim().Length > 0
                    ? terminator.Groups["msg"].Value.Trim()
                    : null;
                end = i + 1;
                break;
            }

            if (text.Trim().Length == 0) continue;
            break;
        }

        if (frames.Count == 0 && type is null) return null;

        // Python prints outermost-first; reverse so index 0 is where it threw.
        frames.Reverse();
        var ordered = frames
            .Select((f, index) => new ErrorFrame
            {
                Order = index,
                Symbol = f.Symbol,
                File = f.File,
                Line = f.Line,
                Column = f.Column,
                Module = f.Module,
                RawLine = f.RawLine,
            })
            .ToList();

        return new Block(type, message, ordered, end);
    }

    private sealed record Block(string? Type, string? Message, IReadOnlyList<ErrorFrame> Frames, int EndExclusive);
}
