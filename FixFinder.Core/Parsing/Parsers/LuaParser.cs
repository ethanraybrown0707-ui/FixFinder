using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>Reads Lua error output.</summary>
/// <remarks>
/// Lua's error line is just <c>file:line: message</c>, which on its own is the same shape as half
/// the diagnostics ever printed - gcc's, Ruby's, and anything that has ever formatted a log entry
/// that way. <b>The <c>stack traceback:</c> banner is what makes this parser safe to have</b>, so
/// it carries nearly all the detection weight and the bare error line carries almost none.
/// <para>
/// Frames come in two kinds. <c>app.lua:5: in main chunk</c> names a file; <c>[C]: in ?</c> is a
/// C function inside the interpreter and names nothing. The second is kept without a location,
/// because a traceback that is mostly <c>[C]</c> frames is itself the useful signal - it means the
/// failure happened inside a library binding rather than in the script.
/// </para>
/// </remarks>
public sealed partial class LuaParser : IStackTraceParser
{
    public string LanguageId => "lua";
    public string DisplayName => "Lua";

    /// <summary>The error line, with the interpreter's own <c>lua:</c> prefix optional.</summary>
    [GeneratedRegex(@"^(?:lua(?:\d[\d.]*)?(?:\.exe)?:\s*)?(?<file>[^\s:]+):(?<line>\d+):\s*(?<msg>.+?)\s*$")]
    private static partial Regex HeaderPattern();

    [GeneratedRegex(@"^stack traceback:\s*$")]
    private static partial Regex TracebackPattern();

    [GeneratedRegex(@"^\s+(?<file>[^\s:]+):(?<line>\d+):\s+in\s+(?<sym>.+?)\s*$")]
    private static partial Regex FramePattern();

    [GeneratedRegex(@"^\s+\[C\]:\s+in\s+(?<sym>.+?)\s*$")]
    private static partial Regex NativeFramePattern();

    public int Detect(IReadOnlyList<string> lines)
    {
        var score = 0;

        foreach (var line in lines)
        {
            if (TracebackPattern().IsMatch(line)) score += 60;
            if (NativeFramePattern().IsMatch(line)) score += 20;
            if (line.Contains("in main chunk", StringComparison.Ordinal)) score += 20;
            if (line.StartsWith("lua:", StringComparison.Ordinal)) score += 25;
        }

        // Never claimed on the error line alone: without the traceback banner or a [C] frame this
        // is indistinguishable from a gcc diagnostic, and guessing wrong sends the whole search
        // after the wrong language.
        return score == 0 ? 0 : Math.Min(score, 100);
    }

    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines)
    {
        var tracebackIndex = -1;
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (!TracebackPattern().IsMatch(lines[i].Text)) continue;
            tracebackIndex = i;
            break;
        }

        // The error line sits immediately above the banner when there is one; without a banner
        // there is nothing here worth claiming over the generic parser.
        var headerIndex = tracebackIndex > 0 ? tracebackIndex - 1 : -1;
        if (headerIndex < 0 || !HeaderPattern().IsMatch(lines[headerIndex].Text)) return null;

        var header = HeaderPattern().Match(lines[headerIndex].Text);

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

        var end = tracebackIndex + 1;

        for (var i = tracebackIndex + 1; i < lines.Count; i++)
        {
            var text = lines[i].Text;

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

                end = i + 1;
                continue;
            }

            if (NativeFramePattern().Match(text) is { Success: true } native)
            {
                frames.Add(new ErrorFrame
                {
                    Order = frames.Count,
                    Symbol = $"[C] {native.Groups["sym"].Value}",
                    RawLine = text,
                });

                end = i + 1;
                continue;
            }

            break;
        }

        return new ParsedError
        {
            LanguageId = LanguageId,
            Confidence = 85,
            RawText = ParserHelpers.RawTextOf(lines, headerIndex, end),
            FirstLineSequence = lines[headerIndex].Sequence,
            Message = header.Groups["msg"].Value.Trim() is { Length: > 0 } message ? message : null,
            Frames = frames,
        };
    }
}
