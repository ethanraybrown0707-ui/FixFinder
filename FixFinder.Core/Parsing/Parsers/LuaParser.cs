using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>
/// Reads Lua's errors, as Lua 5.4 prints them. Lua gives its errors no types: what stops a program while it runs is a
/// runtime error, and code Lua cannot read is a syntax error - the two kinds lua.h itself names.
/// </summary>
/// <remarks>
/// <para>
/// A runtime error is printed after the name lua was started by - the whole path of lua.exe, when it was started by its
/// path - then the file, the line and the message, with a stack traceback under it, the latest call first:
/// <c>C:\Lua\bin\lua.exe: marks.lua:2: attempt to perform arithmetic on a nil value (global 'totl')</c>.
/// </para>
/// <para>
/// A syntax error is one line, from luac -p or from lua itself, with no traceback, as none of the program ran:
/// <c>luac: marks.lua:2: 'then' expected near '!'</c>. It is read only after luac's or lua's name, so a line a program
/// prints that happens to look like one is not taken for one.
/// </para>
/// <para>A file brought in with require is named from the folder the program runs in: <c>.\stats.lua:4:</c>.</para>
/// <para>
/// An error raised inside Lua's own C code can name no place of its own - ipairs given nil prints only
/// <c>lua: attempt to index a nil value</c> - and then its place is the first line of the program's in the traceback.
/// </para>
/// </remarks>
public sealed partial class LuaParser : IStackTraceParser
{
    /// <summary>What Lua could not read: luac -p's error, or lua's for a file it could not load.</summary>
    public const string SyntaxError = "syntax error";

    /// <summary>What stopped a program while it ran.</summary>
    public const string RuntimeError = "runtime error";

    public string LanguageId => "lua";
    public string DisplayName => "Lua";

    [GeneratedRegex(
        @"^(?:(?<program>.*?luac?(?:5\.?\d)?(?:\.exe)?):\s+)?(?<file>(?:[A-Za-z]:[\\/])?[^:\t]+?\.lua):(?<line>\d+):\s*(?<msg>.+?)\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex HeaderPattern();

    /// <summary>A runtime error's message after lua's name, with no file and line of its own.</summary>
    [GeneratedRegex(@"^(?<program>.*?lua(?:5\.?\d)?(?:\.exe)?):\s+(?<msg>.+?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PlacelessHeaderPattern();

    [GeneratedRegex(@"^stack traceback:\s*$")]
    private static partial Regex TracebackPattern();

    [GeneratedRegex(@"^\s+(?<file>(?:[A-Za-z]:[\\/])?[^:\t]+?\.lua):(?<line>\d+):\s+in\s+(?<sym>.+?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex FramePattern();

    [GeneratedRegex(@"^\s+\[C\]:\s+in\s+(?<sym>.+?)\s*$")]
    private static partial Regex NativeFramePattern();

    /// <summary>A line of a traceback that stands for calls not listed: (...tail calls...), or ... (skipping 199975 levels).</summary>
    [GeneratedRegex(@"^\s+(?:\(\.\.\.tail calls\.\.\.\)|\.\.\.\s*\(skipping \d+ levels\))\s*$")]
    private static partial Regex CallsLeftOutPattern();

    public int Detect(IReadOnlyList<string> lines)
    {
        var score = 0;

        foreach (var line in lines)
        {
            if (TracebackPattern().IsMatch(line)) score += 60;
            if (NativeFramePattern().IsMatch(line)) score += 20;
            if (line.Contains("in main chunk", StringComparison.Ordinal)) score += 20;
            if (HeaderPattern().Match(line) is { Success: true } header && header.Groups["program"].Success) score += 40;
        }

        return Math.Min(score, 100);
    }

    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines)
    {
        var tracebackIndex = -1;

        for (var index = lines.Count - 1; index >= 0; index--)
        {
            if (!TracebackPattern().IsMatch(lines[index].Text)) continue;
            tracebackIndex = index;
            break;
        }

        if (tracebackIndex > 0 && HeaderPattern().Match(lines[tracebackIndex - 1].Text) is { Success: true } runtime)
            return Read(lines, tracebackIndex - 1, runtime, RuntimeError, ReadTraceback(lines, tracebackIndex, firstOrder: 1));

        if (tracebackIndex > 0 && PlacelessHeaderPattern().Match(lines[tracebackIndex - 1].Text) is { Success: true } placeless)
            return ReadPlaceless(lines, tracebackIndex - 1, placeless, ReadTraceback(lines, tracebackIndex, firstOrder: 0));

        if (tracebackIndex >= 0) return null;

        for (var index = 0; index < lines.Count; index++)
        {
            if (HeaderPattern().Match(lines[index].Text) is { Success: true } syntax && syntax.Groups["program"].Success)
                return Read(lines, index, syntax, SyntaxError, ([], index + 1));
        }

        return null;
    }

    /// <summary>The calls a traceback lists, the latest first - numbered from <paramref name="firstOrder"/> - and where the traceback ends.</summary>
    private static (List<ErrorFrame> Frames, int End) ReadTraceback(IReadOnlyList<CapturedLine> lines, int tracebackIndex, int firstOrder)
    {
        var frames = new List<ErrorFrame>();
        var end = tracebackIndex + 1;

        for (var index = tracebackIndex + 1; index < lines.Count; index++)
        {
            var text = lines[index].Text;

            if (FramePattern().Match(text) is { Success: true } frame)
            {
                frames.Add(new ErrorFrame
                {
                    Order = firstOrder + frames.Count,
                    Symbol = frame.Groups["sym"].Value,
                    File = ParserHelpers.CleanFilePath(frame.Groups["file"].Value),
                    Line = int.Parse(frame.Groups["line"].Value),
                    RawLine = text,
                });
            }
            else if (NativeFramePattern().Match(text) is { Success: true } native)
            {
                frames.Add(new ErrorFrame { Order = firstOrder + frames.Count, Symbol = $"[C] {native.Groups["sym"].Value}", RawLine = text });
            }
            else if (!CallsLeftOutPattern().IsMatch(text))
            {
                break;
            }

            end = index + 1;
        }

        return (frames, end);
    }

    /// <summary>A runtime error that names no place of its own: its frames are the traceback's alone.</summary>
    private ParsedError ReadPlaceless(IReadOnlyList<CapturedLine> lines, int headerIndex, Match header, (List<ErrorFrame> Frames, int End) traceback) => new()
    {
        LanguageId = LanguageId,
        Confidence = 80,
        RawText = ParserHelpers.RawTextOf(lines, headerIndex, traceback.End),
        FirstLineSequence = lines[headerIndex].Sequence,
        ExceptionType = RuntimeError,
        Message = header.Groups["msg"].Value.Trim() is { Length: > 0 } message ? message : null,
        Frames = traceback.Frames,
    };

    private ParsedError Read(IReadOnlyList<CapturedLine> lines, int headerIndex, Match header, string kind, (List<ErrorFrame> Frames, int End) traceback)
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

        frames.AddRange(traceback.Frames);

        return new ParsedError
        {
            LanguageId = LanguageId,
            Confidence = kind == SyntaxError ? 90 : 85,
            RawText = ParserHelpers.RawTextOf(lines, headerIndex, traceback.End),
            FirstLineSequence = lines[headerIndex].Sequence,
            ExceptionType = kind,
            Message = header.Groups["msg"].Value.Trim() is { Length: > 0 } message ? message : null,
            Frames = frames,
        };
    }
}
