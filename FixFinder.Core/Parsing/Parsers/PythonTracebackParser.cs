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

    /// <summary>
    /// A <c>File "...", line N</c> line with no <c>, in &lt;symbol&gt;</c> after it.
    /// </summary>
    /// <remarks>
    /// The form Python uses when it never got as far as calling anything, so there is no function
    /// name to report. <see cref="FramePattern"/> requires the symbol and therefore does not match
    /// this at all - which is the whole reason a syntax error used to lose its file and line.
    /// </remarks>
    [GeneratedRegex(@"^\s+File\s+""(?<file>.+?)"",\s+line\s+(?<line>\d+)\s*$")]
    private static partial Regex LocationPattern();

    /// <summary>
    /// The errors Python raises while reading a file, before running a line of it.
    /// </summary>
    /// <remarks>
    /// Listed by name rather than matched loosely, and these three are the whole list -
    /// <c>IndentationError</c> and <c>TabError</c> are subclasses of <c>SyntaxError</c>. Being
    /// strict matters because the block they appear in has no header to anchor on: all that is
    /// left to recognise is "a File line, then a name at column 0", which is a shape ordinary
    /// output could stumble into.
    /// </remarks>
    [GeneratedRegex(@"^(?<type>SyntaxError|IndentationError|TabError)(?:\s*:\s*(?<msg>.*))?$")]
    private static partial Regex CompileTimePattern();

    private static readonly string[] ChainMarkers =
    [
        "During handling of the above exception, another exception occurred:",
        "The above exception was the direct cause of the following exception:",
    ];

    public int Detect(IReadOnlyList<string> lines)
    {
        var score = 0;
        var located = false;
        var compileTime = false;

        foreach (var line in lines)
        {
            if (line.Contains(TracebackHeader, StringComparison.Ordinal)) score += 70;
            if (FramePattern().IsMatch(line)) score += 15;
            if (LocationPattern().IsMatch(line)) located = true;
            if (CompileTimePattern().IsMatch(line)) compileTime = true;

            foreach (var marker in ChainMarkers)
                if (line.Contains(marker, StringComparison.Ordinal)) score += 20;
        }

        // Scored only as a pair. A file that will not parse prints no "Traceback" header - there
        // is no call stack, because nothing was ever called - so on its own this block scores
        // nothing and the generic parser wins with a confidence of 20, an empty exception type
        // and no file or line at all.
        if (compileTime && located) score += 85;

        return Math.Min(score, 100);
    }

    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines)
    {
        var headers = new List<int>();
        for (var i = 0; i < lines.Count; i++)
            if (lines[i].Text.TrimEnd().EndsWith(TracebackHeader, StringComparison.Ordinal))
                headers.Add(i);

        if (headers.Count == 0) return ParseCompileTimeError(lines);

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

    /// <summary>
    /// Reads the block Python prints for a file it could not parse.
    /// </summary>
    /// <remarks>
    /// There is no traceback, because nothing ran:
    /// <code>
    ///   File "reader.py", line 4
    ///     return payload.get("user_id"
    ///                       ^
    /// SyntaxError: '(' was never closed
    /// </code>
    /// Worth parsing properly rather than leaving to the generic reader, which kept the message
    /// and threw away everything else - no type, no file, no line, and a confidence of 20, for
    /// what is probably the most common error anybody writing Python ever sees. The query it
    /// produced was three loose words, and it matched strangers' unrelated questions.
    /// <para>
    /// It is also, always, first-party. A syntax error is in the file being read by definition,
    /// so the frame recovered here is what lets the rest of FixFinder say the useful thing -
    /// that this one is yours to fix and no amount of searching will turn up a patch for it.
    /// </para>
    /// </remarks>
    private ParsedError? ParseCompileTimeError(IReadOnlyList<CapturedLine> lines)
    {
        // Searched from the end: when a program prints its own diagnostics before dying, the
        // real error is the last thing said, not the first.
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var error = CompileTimePattern().Match(lines[i].Text);
            if (!error.Success) continue;

            for (var j = i - 1; j >= 0 && j >= i - LocationLookBehind; j--)
            {
                var location = LocationPattern().Match(lines[j].Text);
                if (!location.Success) continue;

                var message = error.Groups["msg"].Success && error.Groups["msg"].Value.Trim().Length > 0
                    ? error.Groups["msg"].Value.Trim()
                    : null;

                return new ParsedError
                {
                    LanguageId = LanguageId,
                    Confidence = 90,
                    RawText = ParserHelpers.RawTextOf(lines, j, i + 1),
                    FirstLineSequence = lines[j].Sequence,
                    ExceptionType = error.Groups["type"].Value,
                    Message = message,
                    Frames =
                    [
                        new ErrorFrame
                        {
                            Order = 0,
                            File = ParserHelpers.CleanFilePath(location.Groups["file"].Value),
                            Line = int.Parse(location.Groups["line"].Value),
                            RawLine = lines[j].Text,
                        },
                    ],
                };
            }

            // An error with no file above it is not this shape. Keep looking rather than
            // returning a type with nothing to locate it by.
        }

        return null;
    }

    /// <summary>
    /// How far above the error line the <c>File</c> line may sit.
    /// </summary>
    /// <remarks>
    /// Between them come the echoed source and its caret, plus the occasional extra note, so a
    /// handful of lines is plenty. Searching the whole output instead would happily pair an error
    /// with a file name printed by something else entirely.
    /// </remarks>
    private const int LocationLookBehind = 8;

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
