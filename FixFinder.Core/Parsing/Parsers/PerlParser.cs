using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>Reads Perl die output.</summary>
/// <remarks>
/// Perl has no exception class and no stack trace unless something asked for one. What it prints
/// is a sentence ending <c>at FILE line N.</c>, which is the loosest error format of any language
/// here - and the reason this parser is the most conservative one in the set.
/// <para>
/// <b>The file must look like Perl.</b> Matching <c>… at anything line 12.</c> on its own would
/// claim ordinary English out of any program's log output; requiring the path to end <c>.pl</c>,
/// <c>.pm</c> or <c>.t</c>, or requiring a <c>called at</c> frame to corroborate it, keeps the
/// pattern to lines Perl actually wrote.
/// </para>
/// <para>
/// There is no exception type to report and none is invented. The searchable part of a Perl
/// failure is its wording - <c>Can't locate object method "x" via package "Y"</c> - and a made-up
/// type would only add a term that appears in nobody's answer.
/// </para>
/// </remarks>
public sealed partial class PerlParser : IStackTraceParser
{
    public string LanguageId => "perl";
    public string DisplayName => "Perl";

    [GeneratedRegex(@"^(?<msg>.+?)\s+at\s+(?<file>\S+\.(?:pl|pm|t))\s+line\s+(?<line>\d+)\.?\s*$")]
    private static partial Regex HeaderPattern();

    /// <summary>A Carp or <c>confess</c> frame: <c>Main::helper() called at x.pl line 12</c>.</summary>
    /// <remarks>
    /// The argument list is matched greedily to the last <c>)</c> on the line rather than to the
    /// first, because Perl stringifies references into the arguments it prints -
    /// <c>load_manifest('Builder=HASH(0x55f1)')</c> - and those brackets nest. Stopping at the
    /// first <c>)</c> makes the line fail to register as a frame at all, which does not show up as
    /// a missing frame: the line still satisfies the header pattern, so the parser reports a
    /// caller as the error and the real failure disappears.
    /// </remarks>
    [GeneratedRegex(
        @"^\s+(?<sym>[\w:]+)\(.*\)\s+called at\s+(?<file>\S+)\s+line\s+(?<line>\d+)\.?\s*$")]
    private static partial Regex FramePattern();

    public int Detect(IReadOnlyList<string> lines)
    {
        var score = 0;

        foreach (var line in lines)
        {
            if (HeaderPattern().IsMatch(line)) score += 55;
            if (FramePattern().IsMatch(line)) score += 30;
        }

        return Math.Min(score, 100);
    }

    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines)
    {
        // A caller frame ends "... called at make.pl line 12", which satisfies the header pattern
        // as well - the message just comes out as "main::run() called". Scanning backwards without
        // excluding frames therefore locks onto the bottom of the stack and reports the outermost
        // caller as the error, with the actual failure nowhere in the result.
        var headerIndex = -1;
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (!HeaderPattern().IsMatch(lines[i].Text)) continue;
            if (FramePattern().IsMatch(lines[i].Text)) continue;

            headerIndex = i;
            break;
        }

        if (headerIndex < 0) return null;

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

        var end = headerIndex + 1;

        for (var i = headerIndex + 1; i < lines.Count; i++)
        {
            var frame = FramePattern().Match(lines[i].Text);
            if (!frame.Success) break;

            frames.Add(new ErrorFrame
            {
                Order = frames.Count,
                Symbol = frame.Groups["sym"].Value,
                File = ParserHelpers.CleanFilePath(frame.Groups["file"].Value),
                Line = int.Parse(frame.Groups["line"].Value),
                RawLine = lines[i].Text,
            });

            end = i + 1;
        }

        return new ParsedError
        {
            LanguageId = LanguageId,

            // Deliberately below the other parsers. One "… at x.pl line 5." line is a weaker claim
            // than a Python traceback, and the confidence is shown to the user precisely so a
            // thin read does not look like a certain one.
            Confidence = frames.Count > 1 ? 82 : 70,
            RawText = ParserHelpers.RawTextOf(lines, headerIndex, end),
            FirstLineSequence = lines[headerIndex].Sequence,
            Message = header.Groups["msg"].Value.Trim() is { Length: > 0 } message ? message : null,
            Frames = frames,
        };
    }
}
