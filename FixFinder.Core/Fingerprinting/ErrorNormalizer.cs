using System.Text.RegularExpressions;

namespace FixFinder.Core.Fingerprinting;

/// <summary>Strips the parts of an error message that are unique to this machine and this run, leaving the part that other people
/// would also have seen.</summary>
public static partial class ErrorNormalizer
{
    [GeneratedRegex(@"[A-Za-z]:\\[^\s""'<>|]+")]
    private static partial Regex WindowsPath();

    [GeneratedRegex(@"(?<![\w:])/(?:[\w.\-]+/)+[\w.\-]+")]
    private static partial Regex UnixPath();

    [GeneratedRegex(@"\b[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}\b")]
    private static partial Regex Guid();

    [GeneratedRegex(@"\b0x[0-9a-fA-F]{4,}\b")]
    private static partial Regex HexAddress();

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(?:\.\d+)?Z?|\b\d{2}:\d{2}:\d{2}(?:\.\d+)?\b")]
    private static partial Regex Timestamp();

    [GeneratedRegex(@"(?i)\b(pid|process|thread|port|worker)\s*[=:#]?\s*\d+")]
    private static partial Regex ProcessId();

    [GeneratedRegex(@":line\s+\d+|,\s*line\s+\d+|:\d+:\d+\b")]
    private static partial Regex LineReference();

    [GeneratedRegex(@"\b\d{4,}\b")]
    private static partial Regex BigNumber();

    [GeneratedRegex(@"'[^']{3,}'|""[^""]{3,}""")]
    private static partial Regex QuotedLiteral();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static IReadOnlyList<NormalizationRule> Rules { get; } =
    [
        new()
        {
            Name = "WindowsPath",
            Pattern = WindowsPath(),
            Replace = m => Path.GetFileName(m.Value.TrimEnd('.', ',', ')')) is { Length: > 0 } n ? n : "<path>",
            Reason = "an absolute path is unique to this machine; the file name is not",
        },
        new()
        {
            Name = "UnixPath",
            Pattern = UnixPath(),
            Replace = m => Path.GetFileName(m.Value.TrimEnd('.', ',', ')')) is { Length: > 0 } n ? n : "<path>",
            Reason = "an absolute path is unique to this machine; the file name is not",
        },
        new()
        {
            Name = "Guid",
            Pattern = Guid(),
            Replace = _ => "<guid>",
            Reason = "a GUID is unique to this run",
        },
        new()
        {
            Name = "HexAddress",
            Pattern = HexAddress(),
            Replace = _ => "<addr>",
            Reason = "a memory address differs on every run",
        },
        new()
        {
            Name = "Timestamp",
            Pattern = Timestamp(),
            Replace = _ => "<time>",
            Reason = "a timestamp is unique to this run",
        },
        new()
        {
            Name = "ProcessId",
            Pattern = ProcessId(),
            Replace = m => $"{m.Groups[1].Value} <num>",
            Reason = "a process, thread or port number differs on every run",
        },
        new()
        {
            Name = "LineReference",
            Pattern = LineReference(),
            Replace = _ => "",
            Reason = "your line numbers are not the same as anyone else's",
        },
        new()
        {
            Name = "BigNumber",
            Pattern = BigNumber(),
            Replace = _ => "<num>",
            Reason = "large numbers are usually counts, sizes or ids specific to this run",
        },
        new()
        {
            Name = "QuotedLiteral",
            Pattern = QuotedLiteral(),
            Replace = _ => "<val>",
            RelaxedOnly = true,
            Reason = "relaxed query only - the quoted value is often the error itself, so the tight query keeps it",
        },
        new()
        {
            Name = "Whitespace",
            Pattern = Whitespace(),
            Replace = _ => " ",
            Reason = "collapsed so the text compares consistently",
        },
    ];

    public static (string Text, IReadOnlyList<AppliedNormalization> Trace) Normalize(string text, bool relaxed)
    {
        var trace = new List<AppliedNormalization>();
        var current = text;

        foreach (var rule in Rules)
        {
            if (rule.RelaxedOnly && !relaxed) continue;

            var replacements = 0;
            var next = rule.Pattern.Replace(current, match =>
            {
                replacements++;
                return rule.Replace(match);
            });

            if (replacements == 0) continue;

            current = next;
            trace.Add(new AppliedNormalization(rule.Name, rule.Reason, replacements, current.Trim()));
        }

        return (current.Trim(), trace);
    }
}
