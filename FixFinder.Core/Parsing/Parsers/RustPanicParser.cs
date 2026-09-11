using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>Reads Rust panics, in both the pre- and post-1.72 formats.</summary>
/// <remarks>
/// Rust changed its panic format in 1.72: the message used to be quoted on the same line as the
/// location, and now it sits on the <b>next</b> line. Both shapes are still very much in the
/// wild, so both are matched - reading only the current one loses the message entirely on older
/// toolchains, and the message is most of the search query.
/// <para>
/// Backtrace frames only exist when <c>RUST_BACKTRACE=1</c> is set, which is why FixFinder
/// pre-seeds that variable in step 1. Without it a panic yields exactly one location and no
/// frames, which is still parseable but much weaker.
/// </para>
/// </remarks>
public sealed partial class RustPanicParser : IStackTraceParser
{
    public string LanguageId => "rust";
    public string DisplayName => "Rust";

    /// <summary>1.72 and later: location on the header line, message on the next.</summary>
    [GeneratedRegex(@"^thread\s+'(?<thread>[^']*)'\s+panicked\s+at\s+(?<file>.+?):(?<line>\d+):(?<col>\d+):\s*$")]
    private static partial Regex ModernHeaderPattern();

    /// <summary>Before 1.72: message quoted inline, location afterwards.</summary>
    [GeneratedRegex(@"^thread\s+'(?<thread>[^']*)'\s+panicked\s+at\s+'(?<msg>.*)',\s+(?<file>.+?):(?<line>\d+):(?<col>\d+)\s*$")]
    private static partial Regex LegacyHeaderPattern();

    [GeneratedRegex(@"^\s*(?<n>\d+):\s+(?<sym>.+?)\s*$")]
    private static partial Regex BacktraceSymbolPattern();

    [GeneratedRegex(@"^\s+at\s+(?<file>.+?):(?<line>\d+)(?::(?<col>\d+))?\s*$")]
    private static partial Regex BacktraceLocationPattern();

    public int Detect(IReadOnlyList<string> lines)
    {
        var score = 0;

        foreach (var line in lines)
        {
            if (ModernHeaderPattern().IsMatch(line) || LegacyHeaderPattern().IsMatch(line)) score += 75;
            if (line.Contains("stack backtrace:", StringComparison.Ordinal)) score += 25;
            if (line.Contains("RUST_BACKTRACE", StringComparison.Ordinal)) score += 20;
            if (line.Contains("core::panicking", StringComparison.Ordinal)) score += 15;
        }

        return Math.Min(score, 100);
    }

    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines)
    {
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var modern = ModernHeaderPattern().Match(lines[i].Text);
            if (modern.Success)
            {
                var message = i + 1 < lines.Count ? lines[i + 1].Text.Trim() : null;
                return Build(lines, i, modern, message, messageOnNextLine: true);
            }

            var legacy = LegacyHeaderPattern().Match(lines[i].Text);
            if (legacy.Success)
                return Build(lines, i, legacy, legacy.Groups["msg"].Value.Trim(), messageOnNextLine: false);
        }

        return null;
    }

    private ParsedError Build(
        IReadOnlyList<CapturedLine> lines, int headerIndex, Match header, string? message, bool messageOnNextLine)
    {
        var frames = new List<ErrorFrame>
        {
            // The panic location itself is always frame 0. With no backtrace it is the only
            // thing there is, and it is still the most useful single fact in the output.
            new()
            {
                Order = 0,
                Symbol = $"thread '{header.Groups["thread"].Value}'",
                File = ParserHelpers.CleanFilePath(header.Groups["file"].Value),
                Line = int.Parse(header.Groups["line"].Value),
                Column = int.Parse(header.Groups["col"].Value),
                RawLine = lines[headerIndex].Text,
            },
        };

        var end = headerIndex + (messageOnNextLine ? 2 : 1);

        var backtraceStart = -1;
        for (var i = headerIndex; i < lines.Count; i++)
        {
            if (!lines[i].Text.Contains("stack backtrace:", StringComparison.Ordinal)) continue;
            backtraceStart = i;
            break;
        }

        if (backtraceStart >= 0)
        {
            end = backtraceStart + 1;
            for (var i = backtraceStart + 1; i < lines.Count; i++)
            {
                var symbol = BacktraceSymbolPattern().Match(lines[i].Text);
                if (!symbol.Success)
                {
                    if (lines[i].Text.Trim().Length == 0) continue;
                    if (BacktraceLocationPattern().IsMatch(lines[i].Text)) { end = i + 1; continue; }
                    break;
                }

                var location = i + 1 < lines.Count ? BacktraceLocationPattern().Match(lines[i + 1].Text) : Match.Empty;

                frames.Add(new ErrorFrame
                {
                    Order = frames.Count,
                    Symbol = symbol.Groups["sym"].Value.Trim(),
                    File = location.Success ? ParserHelpers.CleanFilePath(location.Groups["file"].Value) : null,
                    Line = location.Success ? int.Parse(location.Groups["line"].Value) : null,
                    RawLine = lines[i].Text,
                });

                end = i + 1;
                if (location.Success) { i++; end = i + 1; }
            }
        }

        return new ParsedError
        {
            LanguageId = LanguageId,
            Confidence = frames.Count > 1 ? 92 : 80,
            RawText = ParserHelpers.RawTextOf(lines, headerIndex, end),
            FirstLineSequence = lines[headerIndex].Sequence,
            // Rust has no exception type. "panic" is the honest label; the message carries the
            // searchable content ("index out of bounds", "called `Option::unwrap()` on a `None`").
            ExceptionType = "panic",
            Message = message is { Length: > 0 } ? message : null,
            Frames = frames,
        };
    }
}
