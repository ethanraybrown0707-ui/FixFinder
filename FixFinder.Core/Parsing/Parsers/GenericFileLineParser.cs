using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>Last-resort parser for output no specific parser claimed: finds a severity keyword and harvests whatever
/// <c>file:line</c> references sit near it.</summary>
public sealed partial class GenericFileLineParser : IStackTraceParser
{
    public string LanguageId => "generic";
    public string DisplayName => "Generic";

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

        foreach (var index in severityLines)
        {
            var built = BuildFrom(lines, index);
            if (built.Frames.Count > 0) return built;
        }

        return BuildFrom(lines, severityLines[0]);
    }

    private ParsedError BuildFrom(IReadOnlyList<CapturedLine> lines, int index)
    {
        var frames = new List<ErrorFrame>();

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
