using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>Reads Elixir and Erlang exception output.</summary>
/// <remarks>
/// Elixir's <c>** (ArithmeticError)</c> header is one of the most distinctive in any language, so
/// detection is cheap and precise. The frames are the awkward part: they come in three shapes and
/// only one of them names a file that exists on this machine.
/// <list type="bullet">
/// <item><c>(my_app 0.1.0) lib/my_app.ex:9: MyApp.divide/2</c> - application, file, function.</item>
/// <item><c>lib/my_app.ex:9: MyApp.divide/2</c> - the same without the application.</item>
/// <item><c>:erlang./(1, 0)</c> - an Erlang built-in, which has no file at all.</item>
/// </list>
/// The third is kept as a frame with no location rather than dropped, because it is frequently the
/// innermost one and therefore the thing that actually failed.
/// </remarks>
public sealed partial class ElixirParser : IStackTraceParser
{
    public string LanguageId => "elixir";
    public string DisplayName => "Elixir";

    [GeneratedRegex(@"^\*\*\s+\((?<type>[A-Za-z_][\w.]*)\)\s*(?<msg>.*)$")]
    private static partial Regex HeaderPattern();

    /// <summary>A frame that names a file, with the application prefix optional.</summary>
    [GeneratedRegex(
        @"^\s+(?:\((?<app>[^)]+)\)\s+)?(?<file>[^\s:()]+\.(?:ex|exs|erl)):(?<line>\d+):\s*(?<sym>.+?)\s*$")]
    private static partial Regex FramePattern();

    /// <summary>An Erlang built-in, which has no file.</summary>
    [GeneratedRegex(@"^\s+(?<sym>:[a-z_]\w*\.[^\s(]+)\(.*\)\s*$")]
    private static partial Regex BuiltInPattern();

    public int Detect(IReadOnlyList<string> lines)
    {
        var score = 0;

        foreach (var line in lines)
        {
            if (HeaderPattern().IsMatch(line)) score += 65;
            if (FramePattern().IsMatch(line)) score += 20;
            if (BuiltInPattern().IsMatch(line)) score += 15;
        }

        return Math.Min(score, 100);
    }

    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines)
    {
        var headerIndex = -1;
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (!HeaderPattern().IsMatch(lines[i].Text)) continue;
            headerIndex = i;
            break;
        }

        if (headerIndex < 0) return null;

        var header = HeaderPattern().Match(lines[headerIndex].Text);
        var frames = new List<ErrorFrame>();
        var end = headerIndex + 1;

        for (var i = headerIndex + 1; i < lines.Count; i++)
        {
            var text = lines[i].Text;

            // The message can wrap onto its own indented line before the frames start, and an
            // Elixir trace is always indented, so a blank line is the only reliable terminator.
            if (text.Trim().Length == 0) break;

            if (FramePattern().Match(text) is { Success: true } frame)
            {
                frames.Add(new ErrorFrame
                {
                    Order = frames.Count,
                    Symbol = frame.Groups["sym"].Value,
                    File = ParserHelpers.CleanFilePath(frame.Groups["file"].Value),
                    Line = int.Parse(frame.Groups["line"].Value),
                    Module = frame.Groups["app"].Success ? frame.Groups["app"].Value : null,
                    RawLine = text,
                });

                end = i + 1;
                continue;
            }

            if (BuiltInPattern().Match(text) is { Success: true } builtIn)
            {
                frames.Add(new ErrorFrame
                {
                    Order = frames.Count,
                    Symbol = builtIn.Groups["sym"].Value,
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
            Confidence = 88,
            RawText = ParserHelpers.RawTextOf(lines, headerIndex, end),
            FirstLineSequence = lines[headerIndex].Sequence,
            ExceptionType = header.Groups["type"].Value,
            Message = header.Groups["msg"].Value.Trim() is { Length: > 0 } message ? message : null,
            Frames = frames,
        };
    }
}
