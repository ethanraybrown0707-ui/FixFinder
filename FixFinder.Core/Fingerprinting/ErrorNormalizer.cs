using System.Text.RegularExpressions;

namespace FixFinder.Core.Fingerprinting;

/// <summary>
/// Strips the parts of an error message that are unique to this machine and this run, leaving
/// the part that other people would also have seen.
/// </summary>
/// <remarks>
/// Absolute paths, GUIDs, addresses, PIDs, timestamps and line numbers are what make an error
/// message unique to you. They are also exactly what makes a search for it return nothing.
/// <para>
/// <b>The one genuinely hard call is quoted literals.</b> In
/// <c>KeyError: 'user_id'</c>, <c>No module named 'requests'</c> and
/// <c>Could not load file or assembly 'Newtonsoft.Json'</c>, the quoted token <i>is</i> the
/// error - strip it and the query becomes a generic search for the exception type. But when the
/// quoted text is user data - a customer name, a file the user happens to have - keeping it
/// makes the query unmatchable. There is no rule that gets both right, so FixFinder does not
/// try to guess: the <b>tight</b> query keeps the literal and the <b>relaxed</b> query drops it,
/// and both are run.
/// </para>
/// </remarks>
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

    /// <summary>The rules, in the order they are applied. Order matters - see the remarks below.</summary>
    public static IReadOnlyList<NormalizationRule> Rules { get; } =
    [
        // Paths run first, and reduce to the bare file name rather than vanishing: "Program.cs"
        // is a useful search term, "C:\Users\ethan\..." is not. Running these after the number
        // rules would leave mangled path fragments behind.
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

    /// <summary>
    /// Applies the rules to <paramref name="text"/>, returning the result and what changed.
    /// </summary>
    /// <param name="relaxed">
    /// True to also apply the relaxed-only rules - i.e. to build the fallback query rather than
    /// the precise one.
    /// </param>
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
