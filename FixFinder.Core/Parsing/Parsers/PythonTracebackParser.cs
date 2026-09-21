using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>Reads Python tracebacks, including chained "during handling" / "direct cause" blocks.</summary>
public sealed partial class PythonTracebackParser : IStackTraceParser
{
    public string LanguageId => "python";
    public string DisplayName => "Python";

    private const string TracebackHeader = "Traceback (most recent call last):";

    [GeneratedRegex(@"^\s+File\s+""(?<file>.+?)"",\s+line\s+(?<line>\d+),\s+in\s+(?<sym>.+?)\s*$")]
    private static partial Regex FramePattern();

    [GeneratedRegex(@"^(?<type>[A-Za-z_][A-Za-z0-9_.]*)(?:\s*:\s*(?<msg>.*))?$")]
    private static partial Regex TerminatorPattern();

    [GeneratedRegex(@"^\s+File\s+""(?<file>.+?)"",\s+line\s+(?<line>\d+)\s*$")]
    private static partial Regex LocationPattern();

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

    private ParsedError? ParseCompileTimeError(IReadOnlyList<CapturedLine> lines)
    {
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
        }

        return null;
    }

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
                    Order = frames.Count,
                    Symbol = frame.Groups["sym"].Value.Trim(),
                    File = ParserHelpers.CleanFilePath(frame.Groups["file"].Value),
                    Line = int.Parse(frame.Groups["line"].Value),
                    RawLine = text,
                });
                end = i + 1;
                continue;
            }

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

        // Python prints the outermost call first; every parser puts the line that failed at index 0.
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
