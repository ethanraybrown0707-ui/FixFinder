using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>Reads Node.js / V8 stack traces.</summary>
/// <remarks>
/// Two details do real work here. Frames from <c>node:internal/*</c> are marked
/// <see cref="FrameOrigin.Runtime"/> at parse time rather than left to the path-based
/// classifier, because those are pseudo-paths that exist nowhere on disk and would otherwise
/// look like unrecognised first-party files. And ESM prints locations as
/// <c>file:///C:/src/app.js</c>, which <see cref="ParserHelpers.CleanFilePath"/> converts back
/// to a real path - without that, nothing under the source root ever matches.
/// </remarks>
public sealed partial class NodeStackTraceParser : IStackTraceParser
{
    public string LanguageId => "node";
    public string DisplayName => "Node.js";

    [GeneratedRegex(@"^\s+at\s+(?:async\s+)?(?:(?<sym>.+?)\s+\()?(?<file>(?:[A-Za-z]:[\\/]|file:\/\/|\/|node:|[\w.\-]+[\\/]).*?):(?<line>\d+):(?<col>\d+)\)?\s*$")]
    private static partial Regex FramePattern();

    /// <remarks>A plain <c>Error</c> counts too - <c>Error: Cannot find module 'fss'</c> - but only when frames follow it.</remarks>
    [GeneratedRegex(@"^(?<type>(?:[A-Z]\w*)?(?:Error|Exception))(?:\s*\[(?<code>ERR_\w+)\])?(?:\s*:\s*(?<msg>.*))?$")]
    private static partial Regex HeaderPattern();

    /// <summary>
    /// The line Node prints above an uncaught error - <c>C:\src\app.js:4</c> - followed by that source line and a caret.
    /// </summary>
    /// <remarks>
    /// For a <c>SyntaxError</c> this is the only place the program's own file is named: the file never ran, so every frame
    /// in the stack is Node's module loader. Without it the error pointed at <c>node:internal</c> and nowhere a person could
    /// fix anything.
    /// </remarks>
    [GeneratedRegex(@"^(?<file>(?:[A-Za-z]:[\\/]|file:\/\/|\/).+?\.[mc]?js):(?<line>\d+)\s*$")]
    private static partial Regex LocationPattern();

    [GeneratedRegex(@"^\s*\^+\s*$")]
    private static partial Regex CaretPattern();

    public int Detect(IReadOnlyList<string> lines)
    {
        var score = 0;

        foreach (var line in lines)
        {
            if (line.Contains("node:internal/", StringComparison.Ordinal)) score += 35;
            if (line.Contains("node_modules", StringComparison.Ordinal)) score += 10;

            var frame = FramePattern().Match(line);
            if (frame.Success)
            {
                // file:line:col with a column is the V8 signature - Python and .NET never
                // print a column on a frame.
                score += 18;
                if (frame.Groups["sym"].Success) score += 4;
            }

            if (HeaderPattern().IsMatch(line)) score += 12;
        }

        return Math.Min(score, 100);
    }

    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines)
    {
        // Work backwards to the last header that is actually followed by V8 frames: a crashing
        // program often logs earlier, non-fatal errors too.
        var headerIndex = -1;
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (!HeaderPattern().IsMatch(lines[i].Text)) continue;

            // "Cannot find module" lists the files that asked for it before the stack starts.
            for (var j = i + 1; j < Math.Min(i + 12, lines.Count); j++)
            {
                if (FramePattern().IsMatch(lines[j].Text))
                {
                    headerIndex = i;
                    break;
                }

                if (!IsRequireStack(lines[j].Text) && j >= i + 2) break;
            }
            if (headerIndex >= 0) break;
        }

        if (headerIndex < 0) return null;

        var header = HeaderPattern().Match(lines[headerIndex].Text);
        var frames = new List<ErrorFrame>();
        var end = headerIndex + 1;

        for (var i = headerIndex + 1; i < lines.Count; i++)
        {
            var text = lines[i].Text;
            var frame = FramePattern().Match(text);
            if (!frame.Success)
            {
                // A frame with no place to point at - `at Array.reduce (<anonymous>)` - is still part of the stack, and the
                // program's own frame comes after it.
                if (text.Trim().Length == 0 || (frames.Count == 0 && IsRequireStack(text)) ||
                    (text.Length > 0 && char.IsWhiteSpace(text[0]) && text.TrimStart().StartsWith("at ", StringComparison.Ordinal))) continue;
                break;
            }

            var file = ParserHelpers.CleanFilePath(frame.Groups["file"].Value);
            frames.Add(new ErrorFrame
            {
                Order = frames.Count,
                Symbol = frame.Groups["sym"].Success ? frame.Groups["sym"].Value.Trim() : null,
                File = file,
                Line = int.Parse(frame.Groups["line"].Value),
                Column = int.Parse(frame.Groups["col"].Value),
                Origin = file is not null && file.StartsWith("node:", StringComparison.Ordinal)
                    ? FrameOrigin.Runtime
                    : FrameOrigin.Unknown,
                RawLine = text,
            });
            end = i + 1;
        }

        if (frames.Count == 0) return null;

        if (Location(lines, headerIndex) is { } location &&
            !(string.Equals(frames[0].File, location.File, StringComparison.OrdinalIgnoreCase) && frames[0].Line == location.Line))
        {
            frames =
            [
                location,
                .. frames.Select((frame, i) => new ErrorFrame
                {
                    Order = i + 1, Symbol = frame.Symbol, File = frame.File, Line = frame.Line, Column = frame.Column,
                    Module = frame.Module, Origin = frame.Origin, RawLine = frame.RawLine,
                }),
            ];
        }

        return new ParsedError
        {
            LanguageId = LanguageId,
            Confidence = 88,
            RawText = ParserHelpers.RawTextOf(lines, headerIndex, end),
            FirstLineSequence = lines[headerIndex].Sequence,
            ExceptionType = header.Groups["type"].Value,
            ErrorCode = header.Groups["code"].Success ? header.Groups["code"].Value : null,
            Message = header.Groups["msg"].Success && header.Groups["msg"].Value.Trim().Length > 0
                ? header.Groups["msg"].Value.Trim()
                : null,
            Frames = frames,
        };
    }

    private static bool IsRequireStack(string text) =>
        text.StartsWith("Require stack:", StringComparison.Ordinal) || text.StartsWith("- ", StringComparison.Ordinal);

    /// <summary>The file, line and caret column Node printed a few lines above the error, when it names a real file.</summary>
    private static ErrorFrame? Location(IReadOnlyList<CapturedLine> lines, int headerIndex)
    {
        for (var k = headerIndex - 1; k >= Math.Max(0, headerIndex - 6); k--)
        {
            if (LocationPattern().Match(lines[k].Text) is not { Success: true } location) continue;

            int? column = k + 2 < headerIndex && CaretPattern().IsMatch(lines[k + 2].Text) ? lines[k + 2].Text.IndexOf('^') + 1 : null;

            return new ErrorFrame
            {
                Order = 0,
                File = ParserHelpers.CleanFilePath(location.Groups["file"].Value),
                Line = int.Parse(location.Groups["line"].Value),
                Column = column,
                RawLine = lines[k].Text,
            };
        }

        return null;
    }
}
