using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution.Versions;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Execution;

/// <summary>
/// The Python a program's own code needs, read from how it is written: the newest part of the language or of its standard
/// library it uses - := is Python 3.8's, a match statement 3.10's, except* 3.11's, a type statement 3.12's, a template
/// string t"..." 3.14's - so it runs with a Python that has it, and how it ran says which and why.
/// </summary>
/// <remarks>
/// Which Python added each is from the What's New pages of docs.python.org for 3.9 to 3.14, and the PEPs before them.
/// Only forms that cannot be anything else are counted, so the Python said to be needed is never more than the code needs.
/// Nothing older than 3.8 is looked for - an f-string is 3.6's, but every Python still in use has it, and saying so would
/// only be noise. What a Python only stopped forbidding is not counted either - except A, B: without brackets, which 3.14
/// allows and every Python 3 before it reports as a mistake - as finding it says nothing of which Python the code is for.
/// </remarks>
public static partial class PythonFeaturesUsed
{
    /// <summary>Something Python made part of its language or library, with how to find it in the code with its comments and text blanked out.</summary>
    private sealed record Feature(LanguageVersion Since, string Uses, Regex Written, bool Library = false, bool UnlessPostponedAnnotations = false);

    private const RegexOptions Compiled = RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline;

    private static LanguageVersion Python(int minor) => new(3, minor);

    /// <summary>What each Python added, newest first, so the first one found is the one that decides.</summary>
    private static readonly Feature[] Features =
    [
        new(Python(14), "uses a template string, t\"...\"", new(@"(?<![\w.'""])(?:[tT][rR]?|[rR][tT])(?=[""'])", Compiled)),
        new(Python(14), "uses annotationlib, compression.zstd, concurrent.interpreters or string.templatelib", new(@"^\s*(?:import|from)\s+(?:annotationlib|compression\.zstd|concurrent\.interpreters|string\.templatelib)\b", Compiled), Library: true),
        new(Python(14), "uses a max-heap function of heapq, map(..., strict=...) or Path.copy", new(@"\bheapq\s*\.\s*(?:heapify_max|heappush_max|heappop_max|heapreplace_max|heappushpop_max)\s*\(|\bmap\s*\([^()\n]*(?:\([^()\n]*\)[^()\n]*)*\bstrict\s*=|\.\s*(?:copy_into|move_into)\s*\(", Compiled), Library: true),
        new(Python(13), "gives a type parameter a default", new(@"\b(?:def|class|type)\s+[A-Za-z_]\w*\s*\[[^\]\n]*=", Compiled)),
        new(Python(13), "uses copy.replace, warnings.deprecated, math.fma, typing.ReadOnly or typing.TypeIs", new(@"\bcopy\s*\.\s*replace\s*\(|\bwarnings\s*\.\s*deprecated\b|^\s*from\s+warnings\s+import\s+[^\n]*\bdeprecated\b|\bmath\s*\.\s*fma\s*\(|\btyping\s*\.\s*(?:ReadOnly|TypeIs)\b|^\s*from\s+typing\s+import\s+[^\n]*\b(?:ReadOnly|TypeIs)\b|\bbase64\s*\.\s*z85(?:en|de)code\s*\(|^\s*(?:import|from)\s+dbm\.sqlite3\b", Compiled), Library: true),
        new(Python(12), "uses a type statement", new(@"^[ \t]*type\s+[A-Za-z_]\w*\s*(?:\[[^\]\n]*\])?\s*=", Compiled)),
        new(Python(12), "declares a type parameter in brackets", new(@"^[ \t]*(?:async\s+)?def\s+[A-Za-z_]\w*\s*\[|^[ \t]*class\s+[A-Za-z_]\w*\s*\[", Compiled)),
        new(Python(12), "uses itertools.batched, typing.override or calendar.Month", new(@"\bitertools\s*\.\s*batched\s*\(|^\s*from\s+itertools\s+import\s+[^\n]*\bbatched\b|\btyping\s*\.\s*override\b|^\s*from\s+typing\s+import\s+[^\n]*\boverride\b|\bcalendar\s*\.\s*(?:Month|Day)\b", Compiled), Library: true),
        new(Python(11), "uses except*", new(@"\bexcept\s*\*", Compiled)),
        new(Python(11), "uses tomllib, ExceptionGroup, typing.Self, asyncio.TaskGroup or datetime.UTC", new(@"^\s*(?:import|from)\s+tomllib\b|\bExceptionGroup\b|\btyping\s*\.\s*(?:Self|Never|LiteralString)\b|^\s*from\s+typing\s+import\s+[^\n]*\b(?:Self|Never|LiteralString)\b|\basyncio\s*\.\s*(?:TaskGroup|timeout)\b|\bdatetime\s*\.\s*UTC\b|^\s*from\s+datetime\s+import\s+[^\n]*\bUTC\b|\benum\s*\.\s*StrEnum\b|^\s*from\s+enum\s+import\s+[^\n]*\bStrEnum\b|\bhashlib\s*\.\s*file_digest\s*\(", Compiled), Library: true),
        new(Python(10), "uses a match statement", new(@"^(?<indent>[ \t]*)match\b[^\n=]*:[ \t]*\n(?:[ \t]*(?:#[^\n]*)?\n)*\k<indent>[ \t]+case\b", Compiled)),
        new(Python(10), "writes a union of types with |", new(@"\bisinstance\s*\([^,\n]+,\s*[A-Za-z_][\w.]*\s*\|\s*[A-Za-z_]", Compiled)),
        new(Python(10), "writes a union of types with | in an annotation", new(@"\bdef\s+\w+\s*\([^)]*:\s*[A-Za-z_][\w.\[\], ]*\|\s*[A-Za-z_]|->\s*[A-Za-z_][\w.\[\], ]*\|\s*[A-Za-z_]", Compiled), UnlessPostponedAnnotations: true),
        new(Python(10), "uses itertools.pairwise, zip(..., strict=...), int.bit_count or a dataclass with slots or kw_only", new(@"\bitertools\s*\.\s*pairwise\s*\(|^\s*from\s+itertools\s+import\s+[^\n]*\bpairwise\b|\bzip\s*\([^()\n]*(?:\([^()\n]*\)[^()\n]*)*\bstrict\s*=|\.\s*bit_count\s*\(\s*\)|@\s*(?:dataclasses\s*\.\s*)?dataclass\s*\([^)]*\b(?:slots|kw_only)\s*=", Compiled), Library: true),
        new(Python(9), "uses a built-in collection as a generic in an annotation, such as list[int]", new(@"(?:\bdef\s+\w+\s*\([^)]*:\s*|->\s*)(?:list|dict|set|frozenset|tuple|type)\s*\[", Compiled), UnlessPostponedAnnotations: true),
        new(Python(9), "uses str.removeprefix or removesuffix, zoneinfo, graphlib or math.lcm", new(@"\.\s*remove(?:prefix|suffix)\s*\(|^\s*(?:import|from)\s+(?:zoneinfo|graphlib)\b|\bmath\s*\.\s*lcm\s*\(", Compiled), Library: true),
        new(Python(8), "uses :=", new(@":=", Compiled)),
        new(Python(8), "declares positional-only parameters with /", new(@"\bdef\s+\w+\s*\((?:[^()]|\([^()]*\))*?[,(]\s*/\s*[,)]", Compiled)),
    ];

    [GeneratedRegex(@"^\s*from\s+__future__\s+import\s+[^\n]*\bannotations\b", RegexOptions.Multiline)]
    private static partial Regex PostponedAnnotations();

    private static readonly ConcurrentDictionary<string, (DateTime Written, IReadOnlyList<(LanguageVersion Since, string Because)> Needs)> Remembered =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The newest Python the program this file is part of needs - the file and the program's other files - and why; or null when nothing was found.</summary>
    public static ToolchainChoice.AtLeast? For(string pythonFile)
    {
        IEnumerable<string> files;

        try
        {
            files = ProgramFiles.Of(pythonFile).Where(file => Path.GetExtension(file).Equals(".py", StringComparison.OrdinalIgnoreCase)).Prepend(pythonFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            files = [pythonFile];
        }

        return Of(files.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    public static ToolchainChoice.AtLeast? Of(IEnumerable<string> pythonFiles)
    {
        var newest = pythonFiles
            .SelectMany(NeedsOf)
            .OrderByDescending(need => need.Since)
            .FirstOrDefault();

        return newest.Because is null ? null : new ToolchainChoice.AtLeast(newest.Since, newest.Because);
    }

    private static IReadOnlyList<(LanguageVersion Since, string Because)> NeedsOf(string file)
    {
        try
        {
            var written = File.GetLastWriteTimeUtc(file);
            if (Remembered.TryGetValue(file, out var known) && known.Written == written) return known.Needs;

            var text = File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal);
            var code = string.Join("\n", CodeText.MaskAll(text.Split('\n'), Syntax.Python));
            var name = Path.GetFileName(file);
            var postponed = PostponedAnnotations().IsMatch(code);

            var needs = new List<(LanguageVersion, string)>();

            if (QuoteReusedInsideAnFString(text) is { } reused)
                needs.Add((Python(12), $"{name} uses the same quote inside an f-string's braces as around it at line {LineOf(text, reused)}, which Python 3.12 added"));

            foreach (var feature in Features)
            {
                if (feature.UnlessPostponedAnnotations && postponed) continue;
                if (needs.Any(need => need.Item1 >= feature.Since)) continue;
                if (feature.Written.Match(code) is not { Success: true } found) continue;

                needs.Add((feature.Since, $"{name} {feature.Uses} at line {LineOf(code, found.Index)}, which Python {feature.Since} added"));
            }

            Remembered[file] = (written, needs);
            return needs;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static int LineOf(string text, int index) => 1 + text.AsSpan(0, index).Count('\n');

    /// <summary>
    /// Where an f-string first has the quote it is written in inside its braces - f"{names["first"]}" - which Python 3.12 made
    /// part of the language (PEP 701); before it, that quote ended the f-string.
    /// </summary>
    internal static int? QuoteReusedInsideAnFString(string text)
    {
        for (var at = 0; at < text.Length; at++)
        {
            var character = text[at];

            if (character == '#')
            {
                var lineEnd = text.IndexOf('\n', at);
                if (lineEnd < 0) return null;
                at = lineEnd;
                continue;
            }

            if (character is not ('"' or '\'')) continue;

            var prefixStart = at;
            while (prefixStart > 0 && char.IsAsciiLetter(text[prefixStart - 1]) && at - prefixStart < 2) prefixStart--;
            var prefix = text[prefixStart..at].ToLowerInvariant();
            var isPrefix = prefixStart == 0 || !(char.IsAsciiLetterOrDigit(text[prefixStart - 1]) || text[prefixStart - 1] == '_');
            var isFString = isPrefix && prefix.Contains('f');
            var raw = isPrefix && prefix.Contains('r');

            var triple = at + 2 < text.Length && text[at + 1] == character && text[at + 2] == character;
            var quote = triple ? new string(character, 3) : character.ToString();
            var depth = 0;
            var position = at + quote.Length;

            while (position < text.Length)
            {
                if (!raw && text[position] == '\\' && depth == 0)
                {
                    position += 2;
                    continue;
                }

                if (isFString && text[position] == '{')
                {
                    if (depth == 0 && position + 1 < text.Length && text[position + 1] == '{')
                    {
                        position += 2;
                        continue;
                    }

                    depth++;
                }
                else if (isFString && text[position] == '}' && depth > 0)
                {
                    depth--;
                }
                else if (string.CompareOrdinal(text, position, quote, 0, quote.Length) == 0)
                {
                    if (depth > 0) return position;
                    break;
                }
                else if (!triple && text[position] == '\n')
                {
                    break;
                }

                position++;
            }

            at = Math.Min(position + quote.Length - 1, text.Length - 1);
        }

        return null;
    }
}
