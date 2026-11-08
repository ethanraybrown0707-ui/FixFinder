using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Logic;

/// <summary>
/// A shortest-path search whose new length starts from the wrong end of the edge: it takes the smaller of the length kept for
/// the place an edge goes to and that same length plus the edge's - min(dist[next], dist[next] + length) - or asks whether
/// dist[next] + length is shorter than dist[next]. Either way the edge's length is added to the length of the place it goes
/// to, not of the place it comes from, so the length of the way through the place the search is at is never worked out.
/// QuixBugs' shortest_path_length, Dijkstra's search, is this mistake, in Python and in Java. The change starts the sum from
/// the place the edge comes from: dist[node] + length, or the length the search took with that place - distance, node =
/// heappop(heap).
/// </summary>
/// <remarks>
/// Said only when the file shows all of it: the two lengths are the same text, a table's entry or a call naming the place, and
/// what is added to one is not a number; for a test, its block sets that entry. The change is offered when the place the edge
/// comes from is plain - named beside the place it goes to in the edge's length, length[node, next], or the list the loop goes
/// through, graph[node] - and so is its length: the table's entry for it, where nothing takes entries out of the table, or a
/// name given it before.
/// </remarks>
public static partial class ShortestPathPatterns
{
    /// <summary>How one language is read: its comments and strings, whether a colon ends its tests, and whether its names' case counts.</summary>
    /// <param name="ParenIndex">Whether a table's entry is read with brackets, dist(next), as Scala's and Fortran's are.</param>
    private sealed record Dialect(string Language, IReadOnlySet<string> Files, Syntax Syntax, bool ColonEnds = false, bool IgnoreCase = false, bool ParenIndex = false);

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
        new("lua", CodePattern.Files(".lua"), Syntax.Lua),
        new("php", CodePattern.Files(".php"), Syntax.CLike),
        new("perl", CodePattern.Files(".pl", ".pm"), Syntax.Perl),
        new("powershell", CodePattern.Files(".ps1"), Syntax.Python, IgnoreCase: true),
        new("dart", CodePattern.Files(".dart"), Syntax.CLike),
        new("scala", CodePattern.Files(".scala", ".sc"), Syntax.Scala, ParenIndex: true),
        new("fortran", CodePattern.Files(".f90", ".f95", ".f03", ".f08", ".f"), Syntax.Fortran, IgnoreCase: true, ParenIndex: true),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
        [.. Dialects.Select(dialect => new CodePattern($"logic-{dialect.Language}-path-from-wrong-end", dialect.Files, dialect.Syntax,
            (id, source, masked) => FromWrongEnd(id, source, masked, dialect), Severity.Error, Confidence.Likely))];

    /// <summary>A call taking the smaller of two lengths: min(a, b), Math.min, std::min, math.min, [math]::Min, fmin.</summary>
    [GeneratedRegex(@"(?<![\w.$:])(?:Math\.min|Math\.Min|std::min|math\.min|\[math\]::Min|Integer\.min|Long\.min|Double\.min|fmin|minOf|min)\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex Smaller();

    /// <summary>A test's word: if, elif, else if, elsif, elseif, unless.</summary>
    [GeneratedRegex(@"(?<![\w$.@-])(?:else\s+if|elseif|elsif|elif|if|unless)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TestWord();

    /// <summary>A comparison of two sides: a &lt; b, a &lt;= b, a -lt b, a .lt. b - and the other way round.</summary>
    [GeneratedRegex(@"^(?<a>.+?)\s*(?<sign><=|>=|<(?![<=])|>(?![>=])|-(?:lt|le|gt|ge)\b|\.(?:lt|le|gt|ge)\.)\s*(?<b>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex Compared();

    /// <summary>The length kept for a place: a table's entry or a call, dist[next], $dist{$next}, dist.get(next), get(heap, next).</summary>
    [GeneratedRegex(@"^(?<table>[$@]?[A-Za-z_][\w.]*?)\s*(?:\[(?<at>[^\[\]]+)\]|\{(?<at>[^{}]+)\}|\.(?:get|at|getOrDefault|GetValueOrDefault)\s*\((?<at>[^()]+)\)|\((?<at>[^()]+)\))$")]
    private static partial Regex Kept();

    /// <summary>A line taking something out of a table: del dist[x], dist.pop(x), dist.remove(x), delete dist[x], heappop(dist).</summary>
    private const string TakesOut = @"\bdel\s+{t}\s*\[|\bdelete\s+\$?{t}\s*[\[{]|(?<![\w.]){t}\s*\.\s*(?:pop|remove|Remove|delete|poll|clear|Clear)\s*\(|\b(?:heappop|unset|array_splice|table\.remove)\s*\(\s*\$?{t}\b";

    /// <summary>The sum of the length kept for a place and something added to it - `dist[next] + length` - as two parts.</summary>
    private sealed record Sum(string Kept, string Added, int KeptAt);

    /// <summary>Where the mistake is: its line, the sum, the length it is weighed against, and whether that is a test's.</summary>
    private sealed record Wrong(int Line, Sum Sum, bool Test);

    private static IEnumerable<LogicFinding> FromWrongEnd(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        var options = dialect.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
        for (var start = 0; start < masked.Count; start++)
        {
            var text = masked[start];
            if (!text.Contains('+') && !Smaller().IsMatch(text)) continue;
            if (Weighed(masked, start, dialect) is not { } wrong) continue;

            var line = wrong.Line;
            var kept = wrong.Sum.Kept;
            var place = Kept().Match(kept) is { Success: true } entry ? LastName(entry.Groups["at"].Value) : null;
            if (place is null) continue;

            // The place the edge comes from shows it is an edge: min(x, x + g) is only a search's mistake beside one.
            if (FromPlace(masked, line, wrong.Sum, place, options, dialect.ParenIndex) is not { } from) continue;
            var fromLength = LengthOf(masked, line, kept, place, from, options, dialect.ParenIndex);
            var said = wrong.Test
                ? $"Line {line + 1} asks whether `{kept} + {wrong.Sum.Added}` is shorter than `{kept}`"
                : $"Line {line + 1} takes the smaller of `{kept}` and `{kept} + {wrong.Sum.Added}`";
            var told = $"{said}: `{kept} + {wrong.Sum.Added}` adds the edge's length to `{kept}` - the length kept for `{place}`, the place the edge goes to - " +
                       $"not to the length to `{from}`, the place it comes from. The length of a way through `{from}` is never worked out.";

            yield return new LogicFinding(id, line + 1, told, fromLength is null ? null : Change(id, source, masked, wrong, fromLength, options))
            {
                Severity = Severity.Error,
                Confidence = Confidence.Likely,
            };
        }
    }

    /// <summary>
    /// The line's mistake: min(A, A + W) - or min(A or inf, A + W), as QuixBugs' writes it - or a test of A + W against A
    /// whose block sets A; A a place's entry, W not a number. Null for anything else.
    /// </summary>
    private static Wrong? Weighed(IReadOnlyList<string> masked, int line, Dialect dialect)
    {
        var text = masked[line];
        foreach (Match smaller in Smaller().Matches(text))
        {
            // A call may go on over the next lines: they are read with it, a line break between each.
            var joined = text;
            var open = smaller.Index + smaller.Length - 1;
            var close = Brackets.ClosingParenthesis(joined, open);
            for (var more = line + 1; close is null && more < masked.Count && more <= line + 5; more++)
            {
                joined += "\n" + masked[more];
                close = Brackets.ClosingParenthesis(joined, open);
            }

            if (close is null) continue;
            var sides = Pieces(joined[(open + 1)..close.Value]);
            if (sides.Count != 2) continue;

            foreach (var (one, other) in new[] { (sides[0], sides[1]), (sides[1], sides[0]) })
            {
                var at = open + 1 + one.Start + (one.Text.Length - one.Text.TrimStart().Length);
                if (SumOf(one.Text, at) is not { } sum || !SameLength(other.Text, sum.Kept)) continue;

                // Said of the line the sum is on, where it is on that line.
                var lineOfSum = line + joined[..at].Count(c => c == '\n');
                var column = at - (joined.LastIndexOf('\n', Math.Max(0, at - 1)) + 1);
                if (lineOfSum == line) column = at;
                return new Wrong(lineOfSum, sum with { KeptAt = column }, false);
            }
        }

        // Ruby's [a, b].min, and Rust's and Scala's a.min(b).
        foreach (var (first, second) in MethodMins(text))
        {
            foreach (var (one, other) in new[] { (first, second), (second, first) })
            {
                var at = one.Start + (one.Text.Length - one.Text.TrimStart().Length);
                if (SumOf(one.Text, at) is { } sum && SameLength(other.Text, sum.Kept)) return new Wrong(line, sum, false);
            }
        }

        foreach (Match word in TestWord().Matches(text))
        {
            if (text[..word.Index].Trim() is { Length: > 0 } before && before != "}") continue;
            if (Reaching.Written(text, word.Index + word.Length, dialect.ColonEnds) is not { } wording) continue;
            if (Conjuncts(wording.Condition).Select(part => (Part: part, Compared: Compared().Match(part.Text)))
                    .Select(each => (each.Part, each.Compared, Sum: TestedSum(each.Compared, wording.Start + each.Part.Start)))
                    .FirstOrDefault(each => each.Sum is not null) is not { Sum: { } sum }) continue;

            // Its block sets the entry: a relaxation, not a test of a sum going past what a number holds.
            var block = wording.Rest.Length > 0 ? [wording.Rest] : BlockBelow(masked, line);
            var sets = new Regex($@"^\s*(?:{Regex.Escape(sum.Kept).Replace(@"\ ", @"\s*", StringComparison.Ordinal)})\s*(?::=|=(?!=))");
            if (block.Any(each => sets.IsMatch(each))) return new Wrong(line, sum, true);
        }

        return null;
    }

    [GeneratedRegex(@"\[(?<pair>(?:[^\[\]]|\[[^\[\]]*\])*)\]\s*\.min\b")]
    private static partial Regex ListMin();

    [GeneratedRegex(@"(?<![\w.$])(?<receiver>[A-Za-z_]\w*(?:\[[^\[\]]*\]|\([^()]*\))?)\s*\.min\s*\(")]
    private static partial Regex MethodMin();

    /// <summary>The two sides of a min written as a method: [a, b].min, a.min(b) - each with where it is on the line.</summary>
    private static IEnumerable<((string Text, int Start) First, (string Text, int Start) Second)> MethodMins(string text)
    {
        foreach (Match listed in ListMin().Matches(text))
        {
            var pair = listed.Groups["pair"];
            var sides = Pieces(pair.Value);
            if (sides.Count == 2) yield return ((sides[0].Text, pair.Index + sides[0].Start), (sides[1].Text, pair.Index + sides[1].Start));
        }

        foreach (Match method in MethodMin().Matches(text))
        {
            var open = method.Index + method.Length - 1;
            if (Brackets.ClosingParenthesis(text, open) is not { } close) continue;
            var receiver = method.Groups["receiver"];
            yield return ((receiver.Value, receiver.Index), (text[(open + 1)..close], open + 1));
        }
    }

    /// <summary>The parts of a condition joined by and - a &gt; 0 &amp;&amp; dist[next] + w &lt; dist[next] - each with where it starts.</summary>
    private static List<(string Text, int Start)> Conjuncts(string condition)
    {
        var parts = new List<(string, int)>();
        var depth = 0;
        var from = 0;
        for (var at = 0; at < condition.Length; at++)
        {
            if (condition[at] is '(' or '[' or '{') depth++;
            else if (condition[at] is ')' or ']' or '}') depth--;
            else if (depth == 0 && Regex.Match(condition[at..], @"^(?:&&|\band\b|-and\b|\.and\.)", RegexOptions.IgnoreCase) is { Success: true } joined &&
                     (at == 0 || !char.IsLetterOrDigit(condition[at - 1]) || joined.Value.StartsWith('&')))
            {
                parts.Add((condition[from..at], from));
                from = at + joined.Length;
                at = from - 1;
            }
        }

        parts.Add((condition[from..], from));
        return [.. parts.Select(part => (part.Item1.Trim(), part.Item2 + (part.Item1.Length - part.Item1.TrimStart().Length)))];
    }

    /// <summary>The sum a comparison weighs against its own first part - A + W &lt; A, A &gt; A + W - with where it is, or null.</summary>
    private static Sum? TestedSum(Match compared, int at)
    {
        if (!compared.Success) return null;
        var sign = compared.Groups["sign"].Value.ToLowerInvariant();
        var shorter = sign is "<" or "<=" or "-lt" or "-le" or ".lt." or ".le.";
        var (sumSide, keptSide) = shorter ? (compared.Groups["a"], compared.Groups["b"]) : (compared.Groups["b"], compared.Groups["a"]);
        return SumOf(sumSide.Value, at + sumSide.Index) is { } sum && SameLength(keptSide.Value, sum.Kept) ? sum : null;
    }

    /// <summary>The lines of the block below a line, by indentation - after a { on a line of its own.</summary>
    private static List<string> BlockBelow(IReadOnlyList<string> masked, int header)
    {
        var indent = CodeText.Indentation(masked[header]).Length;
        var lines = new List<string>();
        for (var at = header + 1; at < masked.Count; at++)
        {
            if (masked[at].Trim().Length == 0) continue;
            var width = CodeText.Indentation(masked[at]).Length;
            if (lines.Count == 0 && width == indent && masked[at].Trim() == "{") continue;
            if (width <= indent) break;
            lines.Add(masked[at].Trim());
        }

        return lines;
    }

    /// <summary>
    /// A + W, where A is a place's entry - the first part, a table's entry or a call - and W anything but a number; with where
    /// A is on the line. Null for anything else.
    /// </summary>
    private static Sum? SumOf(string text, int at)
    {
        var written = text.Trim();
        var plus = TopLevelPlus(written);
        if (plus < 0) return null;
        var kept = written[..plus].Trim();
        var added = written[(plus + 1)..].Trim();
        if (added.Length == 0 || Regex.IsMatch(added, @"^[\d.]+$") || !Kept().IsMatch(kept)) return null;
        return new Sum(kept, added, at);
    }

    /// <summary>Where the first + outside any brackets is, or -1.</summary>
    private static int TopLevelPlus(string text)
    {
        var depth = 0;
        for (var at = 0; at < text.Length; at++)
        {
            if (text[at] is '(' or '[' or '{') depth++;
            else if (text[at] is ')' or ']' or '}') depth--;
            else if (text[at] == '+' && depth == 0 && (at + 1 >= text.Length || text[at + 1] != '+') && (at == 0 || text[at - 1] != '+')) return at;
        }

        return -1;
    }

    /// <summary>Whether the side is the same length as the sum starts from: A itself, or A or inf, A ?? inf, A || Infinity.</summary>
    private static bool SameLength(string side, string kept)
    {
        var written = Spaceless(side);
        while (written.StartsWith('(') && Brackets.ClosingParenthesis(written, 0) == written.Length - 1) written = written[1..^1];
        var length = Spaceless(kept);
        return written == length || Regex.IsMatch(written, $@"^{Regex.Escape(length)}(?:or|\?\?|\|\||//)") && written.Length > length.Length;
    }

    /// <summary>The last name in what a table's entry is read at: next for dist[next], next for get(heap, next), $v for $dist{$v}, edge.to for dist[edge.to].</summary>
    private static string? LastName(string at)
    {
        var last = Pieces(at).LastOrDefault().Text?.Trim().TrimStart('&', '*');
        return last is not null && Regex.IsMatch(last, @"^[$@]?[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*$") ? last : null;
    }

    /// <summary>
    /// The place the edge comes from: named beside the place it goes to in what is added - length[node, next] - or the place
    /// whose edges the loop around the line goes through: for next, length in graph[node], for nextnode in node.successors.
    /// </summary>
    private static string? FromPlace(IReadOnlyList<string> masked, int line, Sum sum, string place, RegexOptions options, bool parenIndex)
    {
        string Whole(string name) => $@"(?<![\w$@]){Regex.Escape(name)}(?!\w)";
        var named = Regex.Matches(sum.Added, @"[$@]?[A-Za-z_]\w*").Select(name => name.Value).Distinct().ToList();
        if (named.Any(name => SameName(name, place, options)))
        {
            var others = named.Where(name => !SameName(name, place, options) && Regex.IsMatch(sum.Added, $@"[\[(,{{]\s*{Regex.Escape(name)}\s*[\]),}}]")).ToList();
            if (others is [var other]) return other;
        }

        // The loop the line is in, going through the place's edges.
        var indent = CodeText.Indentation(masked[line]).Length;
        for (var above = line - 1; above >= 0 && indent > 0; above--)
        {
            if (masked[above].Trim().Length == 0) continue;
            var width = CodeText.Indentation(masked[above]).Length;
            if (width >= indent) continue;
            indent = width;

            // The innermost loop is the one through the edges - its place named in its first line, or in the lines under it, int next = edge[0].
            // A { on a line of its own belongs to the line above it, as C# writes it.
            var headerLine = above;
            if (masked[above].Trim() == "{")
            {
                headerLine = above - 1;
                while (headerLine > 0 && masked[headerLine].Trim().Length == 0) headerLine--;
            }

            var header = masked[Math.Max(0, headerLine)];
            if (!Regex.IsMatch(header, @"^\s*(?:\}\s*)?(?:for|foreach|do)\b|\.each\b", options)) continue;
            var through = $@"\[\s*(?<from>[$@]?[A-Za-z_]\w*)\s*\]|\.(?:get|at)\s*\(\s*(?<from>[$@]?[A-Za-z_]\w*)\s*\)|(?<![\w.$])(?<from>[$@]?[A-Za-z_]\w*)\s*(?:\.|->)\s*(?:successors|neighbors|neighbours|edges|adj|children|getSuccessors|getNeighbors|getEdges)\b|\{{\s*(?<from>\$[A-Za-z_]\w*)\s*\}}" +
                          (parenIndex ? @"|<-\s*[A-Za-z_]\w*\(\s*(?<from>[A-Za-z_]\w*)\s*\)" : "");
            foreach (Match from in Regex.Matches(header, through, options))
            {
                if (!SameName(from.Groups["from"].Value, place, options)) return from.Groups["from"].Value;
            }

            return null;
        }

        return null;
    }

    /// <summary>
    /// The length to the place the edge comes from, as the file has it: its own entry in the same table - dist[node] - where
    /// nothing takes entries out of the table; a name given that entry before - int distance = unvisitedNodes.get(node); - or
    /// the name taken with the place - distance, node = heappop(heap). Null when none is plain.
    /// </summary>
    private static string? LengthOf(IReadOnlyList<string> masked, int line, string kept, string place, string from, RegexOptions options, bool parenIndex)
    {
        var entry = Kept().Match(kept);
        var table = entry.Groups["table"].Value;
        var at = entry.Groups["at"];
        var itsOwn = kept[..at.Index] + kept[at.Index..(at.Index + at.Length)].Replace(place, from, StringComparison.Ordinal) + kept[(at.Index + at.Length)..];
        var tableEntry = parenIndex || !kept.StartsWith(table + "(", StringComparison.Ordinal) || kept.Contains('.');

        var scope = Scope(masked, line);
        var takesOut = new Regex(TakesOut.Replace("{t}", Regex.Escape(table.TrimStart('$', '@')), StringComparison.Ordinal), options);
        if (tableEntry && !scope.Any(each => takesOut.IsMatch(masked[each]))) return itsOwn;

        // A name given the entry before the line, or taken with the place.
        foreach (var each in scope.Where(each => each < line).Reverse())
        {
            var text = masked[each];
            if (Regex.Match(text, $@"^\s*(?:[\w<>\[\]]+\s+)?(?<name>[$@]?[A-Za-z_]\w*)\s*(?::=|=(?!=))\s*{Regex.Escape(Spaceless(itsOwn)).Replace(@"\(", @"\s*\(\s*").Replace(@"\)", @"\s*\)").Replace(",", @"\s*,\s*")}\s*;?\s*$", options) is { Success: true } given)
                return given.Groups["name"].Value;
            if (Regex.Match(text, $@"^\s*(?:let\s+|const\s+|var\s+|my\s+|local\s+)?[\[(]?\s*(?<name>[$@]?[A-Za-z_]\w*)\s*,\s*{Regex.Escape(from)}\s*[\])]?\s*(?::=|=(?!=))", options) is { Success: true } taken)
                return taken.Groups["name"].Value;
        }

        return null;
    }

    /// <summary>The lines of the top-level block the line is in: from the last line before it at no indentation to the next one.</summary>
    private static List<int> Scope(IReadOnlyList<string> masked, int line)
    {
        var first = line;
        while (first > 0 && !(masked[first].Trim().Length > 0 && CodeText.Indentation(masked[first]).Length == 0)) first--;
        var last = line + 1;
        while (last < masked.Count && !(masked[last].Trim().Length > 0 && CodeText.Indentation(masked[last]).Length == 0)) last++;
        return [.. Enumerable.Range(first, Math.Min(last, masked.Count) - first)];
    }

    /// <summary>
    /// The change: the sum started from the place the edge comes from - on the line, and, for a test, on the line of its block
    /// that sets the entry to the same sum.
    /// </summary>
    private static LocalFix Change(string id, SourceFile source, IReadOnlyList<string> masked, Wrong wrong, string fromLength, RegexOptions options)
    {
        var text = source.Lines[wrong.Line];
        var changed = text[..wrong.Sum.KeptAt] + fromLength + text[(wrong.Sum.KeptAt + wrong.Sum.Kept.Length)..];

        // A test whose statement is on its own line: if (...) dist[next] = dist[next] + length; - that sum is the same.
        var after = wrong.Sum.KeptAt + fromLength.Length;
        if (wrong.Test && Regex.Match(changed[after..], $@"=\s*(?<sum>{Regex.Escape(wrong.Sum.Kept)}\s*\+)", options) is { Success: true } alsoSets)
        {
            var sumAt = after + alsoSets.Groups["sum"].Index;
            changed = changed[..sumAt] + fromLength + changed[(sumAt + wrong.Sum.Kept.Length)..];
        }

        var lines = new List<string> { changed };

        if (wrong.Test && !Regex.IsMatch(text, $@"=\s*{Regex.Escape(wrong.Sum.Kept)}\s*\+", options))
        {
            // The block's line setting the entry to the same sum: dist[next] = dist[next] + length.
            var below = wrong.Line + 1;
            if (below < masked.Count && Regex.Match(masked[below], $@"=\s*(?<sum>{Regex.Escape(wrong.Sum.Kept)}\s*\+)", options) is { Success: true } sets)
                lines.Add(source.Lines[below][..sets.Groups["sum"].Index] + fromLength + source.Lines[below][(sets.Groups["sum"].Index + wrong.Sum.Kept.Length)..]);
        }

        return new LocalFix
        {
            RuleId = id,
            Title = $"Start from where the edge comes from: {fromLength} + {wrong.Sum.Added}",
            Explanation = $"`{fromLength} + {wrong.Sum.Added}` is the length of the way through the place the edge comes from - its own length, and the edge's - " +
                          "which is what the search weighs against the length kept so far.",
            File = source.Path,
            StartLine = wrong.Line + 1,
            RemoveCount = lines.Count,
            NewLines = lines,
        };
    }

    /// <summary>The pieces of a list between its commas at the top level, each with where it starts.</summary>
    private static List<(string Text, int Start)> Pieces(string text)
    {
        var pieces = new List<(string, int)>();
        var depth = 0;
        var from = 0;
        for (var at = 0; at < text.Length; at++)
        {
            if (text[at] is '(' or '[' or '{') depth++;
            else if (text[at] is ')' or ']' or '}') depth--;
            else if (text[at] == ',' && depth == 0)
            {
                pieces.Add((text[from..at], from));
                from = at + 1;
            }
        }

        pieces.Add((text[from..], from));
        return pieces;
    }

    private static string Spaceless(string text) => Regex.Replace(text, @"\s+", "");

    private static bool SameName(string first, string second, RegexOptions options) =>
        string.Equals(first, second, options.HasFlag(RegexOptions.IgnoreCase) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
