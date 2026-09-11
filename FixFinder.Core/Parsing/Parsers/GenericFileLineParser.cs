using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>
/// Last-resort parser for output no specific parser claimed: finds a severity keyword and
/// harvests whatever <c>file:line</c> references sit near it.
/// </summary>
/// <remarks>
/// This is what makes "works with any language" true rather than aspirational - a Perl script,
/// a Makefile, a language nobody wrote a parser for. It gets something useful out of all of them.
/// <para>
/// Its confidence is <b>hard-capped at 20</b> so it can only ever win by default. That cap is
/// load-bearing: this parser matches something in almost any noisy output, and without it a
/// vaguely error-shaped log line would outrank a real Python traceback in the same run.
/// </para>
/// <para>
/// The extension allow-list is the other half. Harvesting bare <c>word:number</c> would pull in
/// <c>http://host:8080</c>, <c>12:34:56</c> timestamps and <c>key: 42</c> from any structured
/// log, and each false path would be one more chance to map a patch onto the wrong file.
/// </para>
/// </remarks>
public sealed partial class GenericFileLineParser : IStackTraceParser
{
    public string LanguageId => "generic";
    public string DisplayName => "Generic";

    /// <summary>The ceiling on this parser's confidence. See the class remarks.</summary>
    public const int MaxConfidence = 20;

    private static readonly string[] SeverityKeywords =
    [
        "error", "exception", "panic", "fatal", "traceback", "assertion failed",
        "assertionerror", "abort", "segmentation fault", "stack trace", "unhandled",
    ];

    private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "c", "cc", "cpp", "cxx", "h", "hpp", "cs", "fs", "vb", "java", "kt", "kts", "scala",
        "py", "pyw", "rb", "js", "mjs", "cjs", "jsx", "ts", "tsx", "go", "rs", "php", "pl", "pm",
        "swift", "m", "mm", "lua", "r", "jl", "ex", "exs", "erl", "dart", "sh", "bash", "ps1",
        "sql", "html", "css", "scss", "vue", "svelte", "json", "yaml", "yml", "toml", "xml",
    };

    [GeneratedRegex(@"(?<file>[A-Za-z]:\\[^\s:*?""<>|]+\.\w{1,6}|[\w./\\\-]+\.\w{1,6}):(?<line>\d+)(?::(?<col>\d+))?")]
    private static partial Regex FileLinePattern();

    public int Detect(IReadOnlyList<string> lines)
    {
        foreach (var line in lines)
        {
            if (!ContainsSeverityKeyword(line)) continue;
            return MaxConfidence;
        }

        return 0;
    }

    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines)
    {
        var severityLines = new List<int>();
        for (var i = lines.Count - 1; i >= 0; i--)
            if (ContainsSeverityKeyword(lines[i].Text)) severityLines.Add(i);

        if (severityLines.Count == 0) return null;

        // Latest first, but keep looking for one that actually carries a location. A program
        // often prints the real failure and then a bland closing line - "build aborted",
        // "exiting" - which matches a severity keyword while telling us nothing. Taking the
        // last match unconditionally would report that closing line and throw away the only
        // file and line in the whole run.
        foreach (var index in severityLines)
        {
            var built = BuildFrom(lines, index);
            if (built.Frames.Count > 0) return built;
        }

        // Nothing anywhere had a location. Still report the most recent severity line: knowing
        // that something failed, and what it said, beats reporting nothing.
        return BuildFrom(lines, severityLines[0]);
    }

    private ParsedError BuildFrom(IReadOnlyList<CapturedLine> lines, int index)
    {
        var frames = new List<ErrorFrame>();

        // The severity line and the handful after it - close enough to be related, narrow
        // enough not to sweep in the next unrelated log entry.
        var limit = Math.Min(index + 12, lines.Count);

        for (var i = index; i < limit; i++)
        {
            foreach (Match match in FileLinePattern().Matches(lines[i].Text))
            {
                var file = match.Groups["file"].Value;
                var extension = Path.GetExtension(file).TrimStart('.');
                if (!SourceExtensions.Contains(extension)) continue;

                frames.Add(new ErrorFrame
                {
                    Order = frames.Count,
                    File = ParserHelpers.CleanFilePath(file),
                    Line = int.Parse(match.Groups["line"].Value),
                    Column = match.Groups["col"].Success ? int.Parse(match.Groups["col"].Value) : null,
                    RawLine = lines[i].Text,
                });
            }
        }

        return new ParsedError
        {
            LanguageId = LanguageId,
            Confidence = MaxConfidence,
            RawText = ParserHelpers.RawTextOf(lines, index, limit),
            FirstLineSequence = lines[index].Sequence,
            // No type is claimed on purpose. Inventing one from a keyword would put a made-up
            // word into the search query and the ranker's exact-type match.
            ExceptionType = null,
            Message = lines[index].Text.Trim(),
            Frames = frames,
        };
    }

    private static bool ContainsSeverityKeyword(string line)
    {
        foreach (var keyword in SeverityKeywords)
            if (line.Contains(keyword, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
