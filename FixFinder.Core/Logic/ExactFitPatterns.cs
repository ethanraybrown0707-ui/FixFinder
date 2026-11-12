using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Logic;

/// <summary>
/// A table filled by taking what fits - a knapsack's items, change's coins - whose test of whether it fits leaves out what
/// fits exactly. Taking a weight w at a room j goes on from the entry for the room left, table[j - w], and that is there to
/// be read from w = j, where the room left is 0. Asked weight &lt; j, the test leaves that out: whatever fills the room
/// exactly is never taken, and the answer can come out wrong. QuixBugs' knapsack is this mistake, in Python and in Java. The
/// change lets it in: weight &lt;= j.
/// </summary>
/// <remarks>
/// Said only when the file shows all of it: the test asks only whether w is less than j - w &lt; j, j &gt; w, -lt, .lt. - and on
/// its own lines, inside a loop, the same table's entry is given a value read from that table at j - w, written as the test
/// writes the two. In Lua and Fortran, whose tables start at 1 unless written otherwise, the table must be seen to start at
/// 0: a loop from 0 setting it, or its declaration's 0: - else the entry at 0 is not there and the test is right.
/// </remarks>
public static partial class ExactFitPatterns
{
    /// <summary>How one language is read: its comments and strings, whether a colon ends its tests, and whether its names' case counts.</summary>
    /// <param name="CountsFromOne">Whether a table starts at 1 unless written otherwise, as Lua's and Fortran's do.</param>
    private sealed record Dialect(string Language, IReadOnlySet<string> Files, Syntax Syntax, bool ColonEnds = false, bool IgnoreCase = false, bool CountsFromOne = false);

    private static readonly Dialect[] Dialects =
    [
        new("python", CodePattern.Files(".py", ".pyw"), Syntax.Python, ColonEnds: true),
        new("java", CodePattern.Files(".java"), Syntax.CLike),
        new("csharp", CodePattern.Files(".cs"), Syntax.CLike),
        new("js", CodePattern.Files(".js", ".mjs", ".cjs", ".ts", ".mts", ".cts"), Syntax.CLike),
        new("c", CodePattern.Files(".c"), Syntax.CLike),
        new("cpp", CodePattern.Files(".cpp", ".cc", ".cxx", ".c++"), Syntax.CLike),
        new("go", CodePattern.Files(".go"), Syntax.CLike),
        new("rust", CodePattern.Files(".rs"), Syntax.CLike),
        new("ruby", CodePattern.Files(".rb"), Syntax.Python),
        new("lua", CodePattern.Files(".lua"), Syntax.Lua, CountsFromOne: true),
        new("php", CodePattern.Files(".php"), Syntax.CLike),
        new("perl", CodePattern.Files(".pl", ".pm"), Syntax.Perl),
        new("powershell", CodePattern.Files(".ps1"), Syntax.Python, IgnoreCase: true),
        new("dart", CodePattern.Files(".dart"), Syntax.CLike),
        new("scala", CodePattern.Files(".scala", ".sc"), Syntax.Scala),
        new("fortran", CodePattern.Files(".f90", ".f95", ".f03", ".f08", ".f"), Syntax.Fortran, IgnoreCase: true, CountsFromOne: true),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        .. Dialects.Select(dialect => new CodePattern($"logic-{dialect.Language}-exact-fit-left-out", dialect.Files, dialect.Syntax,
            (id, source, masked) => ExactFitsLeftOut(id, source, masked, dialect), Severity.Error, Confidence.Likely)),
    ];

    /// <summary>A test's word: if, elif, else if, elsif, elseif.</summary>
    [GeneratedRegex(@"(?<![\w$.@-])(?:else\s+if|elseif|elsif|elif|if)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TestWord();

    /// <summary>A test asking only whether one side is less than the other, strictly: &lt;, &gt;, -lt, -gt, .lt., .gt.</summary>
    [GeneratedRegex(@"^(?<a>[^<>=!&|]+?)\s*(?<sign><(?![<=])|>(?![>=])|-lt\b|-gt\b|\.lt\.|\.gt\.)\s*(?<b>[^<>=!&|]+)$", RegexOptions.IgnoreCase)]
    private static partial Regex Strictly();

    /// <summary>What joins a test to another, or turns it round: and, or, not, &amp;&amp;, ||, -and, .or.</summary>
    [GeneratedRegex(@"&&|\|\||(?<![\w$@])(?:and|or|not)(?!\w)|-(?:and|or|not|xor)\b|\.(?:and|or|not)\.|^\s*!", RegexOptions.IgnoreCase)]
    private static partial Regex Joined();

    /// <summary>A line giving a table's entry a value: table[i][j] = ..., best[j] = ..., table(i, j) = ..., $table[$i][$j] = ...</summary>
    [GeneratedRegex(@"^\s*(?<table>[$@]?[A-Za-z_]\w*)\s*(?<at>(?:\s*(?:->\s*)?[\[(][^=]*?[\])])+)\s*(?::=|=(?!=))\s*(?<value>.+)$")]
    private static partial Regex EntryGiven();

    private static IEnumerable<LogicFinding> ExactFitsLeftOut(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        var options = dialect.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
        for (var line = 0; line < masked.Count; line++)
        {
            var named = false;
            foreach (Match word in TestWord().Matches(masked[line]))
            {
                if (named) break;
                if (Reaching.Written(masked[line], word.Index + word.Length, dialect.ColonEnds) is not { } wording) continue;
                var condition = wording.Condition;
                if (Joined().IsMatch(condition) || Strictly().Match(condition) is not { Success: true } compared) continue;

                // The weight is the side the test asks to be the smaller: w < j, j > w.
                var lessFirst = compared.Groups["sign"].Value is "<" || compared.Groups["sign"].Value.Contains("lt", StringComparison.OrdinalIgnoreCase);
                var weight = (lessFirst ? compared.Groups["a"] : compared.Groups["b"]).Value.Trim();
                var room = (lessFirst ? compared.Groups["b"] : compared.Groups["a"]).Value.Trim();
                if (!Regex.IsMatch(room, @"^[$@]?[A-Za-z_]\w*$") || weight.Length == 0) continue;
                var roomLeft = Spaceless(room + "-" + weight);

                // What fits exactly may have a test of its own - if weight == j: ... elif weight < j: - and is then not left out.
                if (AskedEqual(masked, weight, room, options)) continue;

                foreach (var (taken, from) in GuardedLines(masked, line, wording))
                {
                    if (EntryGiven().Match(masked[taken][from..]) is not { Success: true } given) continue;
                    var table = given.Groups["table"].Value;
                    var valueStart = from + given.Groups["value"].Index;
                    if (!ReadsAt(WholeStatement(masked, taken), valueStart, table, roomLeft, options)) continue;
                    if (!Reaching.InALoop(masked, 0, taken)) continue;
                    if (dialect.CountsFromOne && !StartsAtZero(masked, table, dialect.Language)) continue;

                    var conditionStart = masked[line].IndexOf(condition, wording.Start, StringComparison.Ordinal);
                    if (conditionStart < 0) continue;
                    var signAt = conditionStart + compared.Groups["sign"].Index;
                    var sign = compared.Groups["sign"].Value;
                    var told = $"Line {line + 1} asks `{condition}` before line {taken + 1} reads `{table}` at `{room} - {weight}`. When `{weight}` is exactly " +
                               $"`{room}`, `{room} - {weight}` is 0 - an entry the table has - but the test leaves that case out: whatever fills `{room}` " +
                               "exactly is never taken, and the answer can come out wrong.";
                    yield return new LogicFinding(id, line + 1, told, LetIn(id, source, line, signAt, sign, weight, room, lessFirst))
                    {
                        Severity = Severity.Error,
                        Confidence = Confidence.Likely,
                    };
                    named = true;
                    break;
                }
            }
        }
    }

    /// <summary>The lines the test guards: what follows it on its line, or the lines of its block - each with where on the line it starts.</summary>
    private static List<(int Line, int From)> GuardedLines(IReadOnlyList<string> masked, int line, Reaching.Wording wording)
    {
        if (wording.Rest.Length > 0)
        {
            var from = masked[line].IndexOf(wording.Rest, Math.Min(wording.RestStart, masked[line].Length), StringComparison.Ordinal);
            return from < 0 ? [] : [(line, from)];
        }

        var indent = CodeText.Indentation(masked[line]).Length;
        var block = new List<(int, int)>();
        for (var below = line + 1; below < masked.Count; below++)
        {
            var text = masked[below];
            if (text.Trim().Length == 0) continue;
            var width = CodeText.Indentation(text).Length;

            // A { on a line of its own, under the test, opens the block - as C# writes it.
            if (width == indent && block.Count == 0 && text.Trim() == "{") continue;
            if (width <= indent) break;
            block.Add((below, 0));
        }

        return block;
    }

    /// <summary>
    /// Whether the line, from where its value starts, reads the table at the room left - one of the places in an entry of it,
    /// within any of its brackets, written as j - w: table[i - 1][j - weight], best[j - weight], memo[i - 1, j - weight].
    /// </summary>
    private static bool ReadsAt(string text, int from, string table, string roomLeft, RegexOptions options)
    {
        foreach (Match read in Regex.Matches(text[from..], $@"(?<![\w$@.]){Regex.Escape(table)}\s*(?:->\s*)?(?=[\[(])", options))
        {
            var at = from + read.Index + read.Length;
            while (at < text.Length && text[at] is '[' or '(')
            {
                if (Closing(text, at) is not { } close) break;
                if (Places(text[(at + 1)..close]).Any(place => string.Equals(Spaceless(place), roomLeft, options.HasFlag(RegexOptions.IgnoreCase) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
                    return true;

                at = close + 1;
                while (at < text.Length && (text[at] == ' ' || text[at] == '-' && at + 1 < text.Length && text[at + 1] == '>')) at += text[at] == '-' ? 2 : 1;
            }
        }

        return false;
    }

    /// <summary>Whether the file asks anywhere whether the two are equal: weight == j, j === weight, $w -eq $j, w .eq. j, $w == $j.</summary>
    private static bool AskedEqual(IReadOnlyList<string> masked, string weight, string room, RegexOptions options)
    {
        static string Spaced(string text) => string.Join(@"\s*", Spaceless(text).Select(letter => Regex.Escape(letter.ToString())));
        var (one, other) = (Spaced(weight), Spaced(room));
        const string EqualSign = @"\s*(?:===?|-eq\b|\.eq\.)\s*|\s+eq\s+";
        var equal = new Regex($@"(?<![\w$@.]){one}(?:{EqualSign}){other}(?![\w\[(])|(?<![\w$@.]){other}(?:{EqualSign}){one}(?![\w\[(])", options);
        return masked.Any(line => equal.IsMatch(line));
    }

    /// <summary>The line, with the lines after it joined on while its brackets are still open - a max( over the next lines - up to five.</summary>
    private static string WholeStatement(IReadOnlyList<string> masked, int line)
    {
        var joined = masked[line];
        for (var more = line + 1; more < masked.Count && more <= line + 5 && Open(joined) > 0; more++) joined += " " + masked[more];
        return joined;
    }

    /// <summary>How many brackets are opened and not yet closed.</summary>
    private static int Open(string text) => text.Count(c => c is '(' or '[') - text.Count(c => c is ')' or ']');

    /// <summary>Where the bracket opened at <paramref name="open"/> closes - [ with ], ( with ) - counting both kinds.</summary>
    private static int? Closing(string text, int open)
    {
        var depth = 0;
        for (var at = open; at < text.Length; at++)
        {
            if (text[at] is '[' or '(') depth++;
            else if (text[at] is ']' or ')' && --depth == 0) return at;
        }

        return null;
    }

    /// <summary>The places between a table's brackets, split at its commas at the top level.</summary>
    private static List<string> Places(string written)
    {
        var places = new List<string>();
        var depth = 0;
        var from = 0;
        for (var at = 0; at < written.Length; at++)
        {
            if (written[at] is '(' or '[' or '{') depth++;
            else if (written[at] is ')' or ']' or '}') depth--;
            else if (written[at] == ',' && depth == 0)
            {
                places.Add(written[from..at]);
                from = at + 1;
            }
        }

        places.Add(written[from..]);
        return places;
    }

    /// <summary>
    /// Whether a table of a language counting from 1 is seen to start at 0: Fortran's declaration with 0: in its brackets,
    /// integer :: table(0:count, 0:capacity); Lua's set inside a loop from 0, for j = 0, capacity do table[i][j] = 0.
    /// </summary>
    private static bool StartsAtZero(IReadOnlyList<string> masked, string table, string language)
    {
        var name = Regex.Escape(table);
        if (language == "fortran")
            return masked.Any(line => line.Contains("::", StringComparison.Ordinal) && Regex.IsMatch(line, $@"(?<!\w){name}\s*\([^)]*\b0\s*:", RegexOptions.IgnoreCase));

        for (var line = 0; line < masked.Count; line++)
        {
            if (!Regex.IsMatch(masked[line], @"^\s*for\s+[A-Za-z_]\w*\s*=\s*0\s*,")) continue;
            var indent = CodeText.Indentation(masked[line]).Length;
            for (var below = line + 1; below < masked.Count && CodeText.Indentation(masked[below]).Length > indent; below++)
            {
                if (Regex.IsMatch(masked[below], $@"^\s*{name}\s*\[")) return true;
            }
        }

        return false;
    }

    /// <summary>The change: the test made to let in what fits exactly - &lt; to &lt;=, &gt; to &gt;=, -lt to -le, .lt. to .le.</summary>
    private static LocalFix? LetIn(string id, SourceFile source, int line, int signAt, string sign, string weight, string room, bool lessFirst)
    {
        var text = source.Lines[line];
        if (signAt + sign.Length > text.Length || text.Substring(signAt, sign.Length) != sign) return null;
        var widened = sign.ToLowerInvariant() switch
        {
            "<" => "<=",
            ">" => ">=",
            "-lt" => "-le",
            "-gt" => "-ge",
            ".lt." => ".le.",
            ".gt." => ".ge.",
            _ => null,
        };
        if (widened is null) return null;
        if (char.IsUpper(sign.TrimStart('-', '.')[0])) widened = widened.ToUpperInvariant();

        var asked = lessFirst ? $"{weight} {widened} {room}" : $"{room} {widened} {weight}";
        return LocalFix.ReplaceLine(id, $"Let in what fits exactly: {asked}",
            $"`{room} - {weight}` is 0 when `{weight}` is exactly `{room}`, and the table has that entry: taking what fills `{room}` exactly is one of the ways to fill it.",
            source.Path, line + 1, text[..signAt] + widened + text[(signAt + sign.Length)..]);
    }

    private static string Spaceless(string text) => Regex.Replace(text, @"\s+", "");
}
