using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A loop that stops one short of the place its own body guards: the loop goes while c &lt; r, and inside it a read is
/// guarded by c &lt; r - upright = rows[r - 1][c] if c &lt; r else 0 - a test that can never fail there. The guard is written
/// for the place the loop never reaches, c = r. Pascal's triangle is the case: row r has r + 1 numbers, and stopping at r
/// each row comes out one short - and, built from the short row before it, its reads go past that row's end: Python's
/// IndexError, Java's IndexOutOfBoundsException, or rows printed one number short where a missing number reads as 0 or nil.
/// QuixBugs' pascal is this mistake, in Python and in Java. The change takes the loop up to r: range(0, r + 1), c &lt; r + 1,
/// 0 .. r, as QuixBugs' correct programs have it.
/// </summary>
/// <remarks>
/// Said only when the file shows all of it: a loop's bound, as the language writes it - range(0, r), c &lt; r, $c -lt $r,
/// 0..r, (0...r), 0 until r, List.init r, and 1, r - 1, 0 .. $r - 1, 0, r - 1, 0 .. r - 1, 0..(r - 1), lists:seq(0, R - 1)
/// for the languages whose ranges hold their last number - and in the loop a test of the loop's name against that same
/// bound with a read at the loop's name beside it, rows[r - 1][c], rows.get(r-1).get(c), rows(r - 1, c), List.nth previous
/// c, Enum.at(previous, c), lists:nth(C + 1, Previous).
/// </remarks>
public static partial class LoopOneShortPatterns
{
    /// <summary>How a loop is headed in one language: the heading up to its bound, what ends the bound, and whether the bound is the last place gone through - r - 1 - rather than the one the loop stops at.</summary>
    /// <param name="Heading">The heading up to where its bound starts, with the loop's name in the group named name.</param>
    /// <param name="Ends">What ends the bound, at the top level of its brackets.</param>
    /// <param name="LastPlace">Whether the bound is the last place gone through, as Lua's, Perl's, Fortran's, F#'s, Elixir's and Erlang's ranges hold it.</param>
    private sealed record Loop(Regex Heading, string[] Ends, bool LastPlace = false);

    /// <param name="Comprehension">Whether the loop is one expression on a line or two, its guard beside it, as F#'s, OCaml's, Elixir's and Erlang's are.</param>
    private sealed record Dialect(string Language, IReadOnlySet<string> Files, Syntax Syntax, Loop[] Loops, string Below = "<", bool Comprehension = false, bool IgnoreCase = false);

    /// <summary>for (int c = 0; c &lt; r; c++) - the C family's loop, its bound after the &lt;.</summary>
    private static Loop Counted(string below = "<") =>
        new(new($@"\bfor\s*\(\s*(?:(?:int|var|let|long|auto|final)\s+)?(?<name>[$]?[A-Za-z_]\w*)\s*=\s*[^;]+;\s*\k<name>\s*{Regex.Escape(below)}\s*", RegexOptions.IgnoreCase), [";"]);

    private static readonly Dialect[] Dialects =
    [
        new("python", CodePattern.Files(".py", ".pyw"), Syntax.Python, [new(new(@"\bfor\s+(?<name>[A-Za-z_]\w*)\s+in\s+range\s*\(\s*[^,()]+,\s*"), [")"])]),
        new("java", CodePattern.Files(".java"), Syntax.CLike, [Counted()]),
        new("csharp", CodePattern.Files(".cs"), Syntax.CLike, [Counted()]),
        new("js", CodePattern.Files(".js", ".mjs", ".cjs", ".ts", ".mts", ".cts"), Syntax.CLike, [Counted()]),
        new("c", CodePattern.Files(".c"), Syntax.CLike, [Counted()]),
        new("cpp", CodePattern.Files(".cpp", ".cc", ".cxx", ".c++"), Syntax.CLike, [Counted()]),
        new("go", CodePattern.Files(".go"), Syntax.CLike, [new(new(@"\bfor\s+(?<name>[A-Za-z_]\w*)\s*:=\s*[^;]+;\s*\k<name>\s*<\s*"), [";"])]),
        new("rust", CodePattern.Files(".rs"), Syntax.CLike, [new(new(@"\bfor\s+(?<name>[A-Za-z_]\w*)\s+in\s+[\w.()]+?\.\.(?!=)"), ["{"])]),
        new("ruby", CodePattern.Files(".rb"), Syntax.Python, [new(new(@"\(\s*[^.()]+\.\.\.(?=[^.])"), [")"])]),
        new("lua", CodePattern.Files(".lua"), Syntax.Lua, [new(new(@"\bfor\s+(?<name>[A-Za-z_]\w*)\s*=\s*[^,]+,\s*"), [" do", ","], LastPlace: true)]),
        new("php", CodePattern.Files(".php"), Syntax.CLike, [Counted()]),
        new("perl", CodePattern.Files(".pl", ".pm"), Syntax.Perl, [new(new(@"\bfor(?:each)?\s+my\s+(?<name>\$[A-Za-z_]\w*)\s*\(\s*[^.()]+\.\.\s*"), [")"], LastPlace: true)]),
        new("powershell", CodePattern.Files(".ps1"), Syntax.Python, [Counted("-lt")], Below: "-lt", IgnoreCase: true),
        new("dart", CodePattern.Files(".dart"), Syntax.CLike, [Counted()]),
        new("scala", CodePattern.Files(".scala", ".sc"), Syntax.Scala, [new(new(@"\bfor\s*\(\s*(?<name>[A-Za-z_]\w*)\s*<-\s*\S+\s+until\s+"), [")"])]),
        new("fortran", CodePattern.Files(".f90", ".f95", ".f03", ".f08", ".f"), Syntax.Fortran, [new(new(@"^\s*do\s+(?<name>[A-Za-z_]\w*)\s*=\s*[^,]+,\s*", RegexOptions.IgnoreCase), [","], LastPlace: true)], IgnoreCase: true),
        new("fsharp", CodePattern.Files(".fs", ".fsx"), Syntax.FSharp, [new(new(@"\bfor\s+(?<name>[A-Za-z_]\w*)\s+in\s+[^.\s]+\s*\.\.\s*"), ["->", " do"], LastPlace: true)], Comprehension: true),
        new("ocaml", CodePattern.Files(".ml"), Syntax.OCaml, [new(new(@"\bList\.init\s+"), [" (fun"])], Comprehension: true),
        new("elixir", CodePattern.Files(".ex", ".exs"), Syntax.Python, [new(new(@"\bfor\s+(?<name>[a-z_]\w*)\s*<-\s*[^.\s]+?\.\."), [","], LastPlace: true)], Comprehension: true),
        new("erlang", CodePattern.Files(".erl"), Syntax.Erlang, [new(new(@"(?<name>[A-Z_]\w*)\s*<-\s*lists:seq\(\s*[^,()]+,\s*"), [")"], LastPlace: true)], Comprehension: true),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        .. Dialects.Select(dialect => new CodePattern($"logic-{dialect.Language}-loop-one-short", dialect.Files, dialect.Syntax,
            (id, source, masked) => LoopsOneShort(id, source, masked, dialect), Severity.Error, Confidence.Likely)),
    ];

    private static IEnumerable<LogicFinding> LoopsOneShort(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        var options = dialect.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
        for (var line = 0; line < masked.Count; line++)
        {
            var text = masked[line];
            if (!text.Contains(dialect.Below, StringComparison.OrdinalIgnoreCase) && !text.Contains("..", StringComparison.Ordinal) &&
                !text.Contains(',') && !text.Contains("init", StringComparison.Ordinal) && !text.Contains("until", StringComparison.Ordinal)) continue;

            foreach (var loop in dialect.Loops)
            {
                if (loop.Heading.Match(text) is not { Success: true } heading) continue;
                var boundStart = heading.Index + heading.Length;
                if (BoundEnd(text, boundStart, loop.Ends) is not { } boundEnd) continue;
                var bound = text[boundStart..boundEnd].Trim();
                boundStart += text[boundStart..boundEnd].IndexOf(bound, StringComparison.Ordinal);
                boundEnd = boundStart + bound.Length;

                // The place the loop stops at: the bound itself, or one past the last place gone through - r for r - 1.
                string stop;
                if (loop.LastPlace)
                {
                    var lessOne = Regex.Match(bound, @"^\(?\s*(?<stop>[$]?[A-Za-z_]\w*)\s*-\s*1\s*\)?$");
                    if (!lessOne.Success) continue;
                    stop = lessOne.Groups["stop"].Value;
                }
                else
                {
                    var whole = Regex.Match(bound, @"^\(?\s*(?<stop>[$]?[A-Za-z_]\w*)\s*\)?$");
                    if (!whole.Success) continue;
                    stop = whole.Groups["stop"].Value;
                }

                // The loop's name: from the heading, or for Ruby's (0...r).each do |c| and OCaml's List.init r (fun c -> after the bound.
                var name = heading.Groups["name"].Success ? heading.Groups["name"].Value
                    : Regex.Match(text[boundEnd..], @"^\s*\)?\s*(?:\.each\s+do\s*\|\s*|\(fun\s+)(?<name>[A-Za-z_]\w*)") is { Success: true } after ? after.Groups["name"].Value : null;
                if (name is null || Same(name, stop, options)) continue;

                if (!GuardsTheStop(masked, line, name, stop, dialect, options)) continue;

                var written = source.Lines[line];
                var spaced = !Regex.IsMatch(text[..boundStart], $@"{Regex.Escape(dialect.Below)}$");
                var reaching = loop.LastPlace ? stop
                    : dialect.Language == "ocaml" ? $"({stop} + 1)"
                    : spaced ? $"{bound} + 1" : $"{bound}+1";
                var changed = written[..boundStart] + reaching + written[boundEnd..];
                var told = $"Line {line + 1} goes through `{name}` up to `{stop}` and not to it - but inside the loop a read is guarded by `{name} {dialect.Below} {stop}`, " +
                           $"a test that can never fail there: it is written for the place the loop never reaches, `{name}` = `{stop}`. Built so, each row " +
                           "of Pascal's triangle comes out one number short, and the next row's reads go past its end.";
                yield return new LogicFinding(id, line + 1, told,
                    LocalFix.ReplaceLine(id, $"Go up to {stop} as well: {reaching}",
                        $"The loop's own guard expects `{name}` to reach `{stop}` - for row `{stop}` of Pascal's triangle, its last number.",
                        source.Path, line + 1, changed));
                break;
            }
        }
    }

    private static bool Same(string first, string second, RegexOptions options) =>
        string.Equals(first, second, options.HasFlag(RegexOptions.IgnoreCase) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>Where the bound ends: the first of what ends it at the top level of its brackets - or null.</summary>
    private static int? BoundEnd(string text, int from, string[] ends)
    {
        var depth = 0;
        for (var at = from; at < text.Length; at++)
        {
            if (depth == 0 && ends.Any(end => string.CompareOrdinal(text, at, end, 0, end.Length) == 0) && at > from) return at;
            if (text[at] is '(' or '[' or '{') depth++;
            else if (text[at] is ')' or ']' or '}')
            {
                if (depth == 0) return ends.Contains(")") ? at : null;
                depth--;
            }
        }

        // Fortran's and Rust's bound may run to the line's end.
        return text.Length > from ? text.Length : null;
    }

    /// <summary>
    /// Whether the loop - its block, or for a loop in one expression the lines beside it - tests its name against the place it
    /// stops at, with a read at its name on the same line or the next: c &lt; r ? rows[r - 1][c] : 0, if (c &lt; r) { upright =
    /// rows.get(r-1).get(c); }.
    /// </summary>
    private static bool GuardsTheStop(IReadOnlyList<string> masked, int heading, string name, string stop, Dialect dialect, RegexOptions options)
    {
        var lines = dialect.Comprehension
            ? Enumerable.Range(Math.Max(0, heading - 2), Math.Min(masked.Count, heading + 3) - Math.Max(0, heading - 2)).ToList()
            : BlockUnder(masked, heading);
        var plain = Regex.Escape(name.TrimStart('$'));
        // The bound alone: i < n, not i < n - 1, whose last place the loop does reach.
        var guard = new Regex($@"(?<![\w$@.])[$]?{plain}\s*{Regex.Escape(dialect.Below)}\s*[$]?{Regex.Escape(stop.TrimStart('$'))}(?!\w)(?!\s*[-+*/.(\[])", options | RegexOptions.IgnoreCase);
        var read = new Regex($@"[\[(,]\s*[$]?{plain}\s*[\])]|\bnth\b[^\n]*(?<![\w$@.]){Regex.Escape(name)}(?!\w)|\bnth\s*\(\s*{Regex.Escape(name)}\s*\+\s*1", options);

        // The other end guarded as well - c > 0 with a read at c - 1, Lua's c > 1 - as a row built from its neighbours in a
        // row one shorter is: a loop whose bound is its list's length and whose guard only repeats it is left alone.
        var lowerGuard = new Regex($@"(?<![\w$@.])[$]?{plain}\s*(?:>|-gt)\s*[01](?![\w.])", options | RegexOptions.IgnoreCase);
        var lowerRead = new Regex($@"[\[(,]\s*[$]?{plain}\s*-\s*1\s*[\])]|\bnth\b[^\n]*\(\s*{Regex.Escape(name)}\s*-\s*1\s*\)|\bnth\s*\(\s*{Regex.Escape(name)}\s*,", options);

        bool Guarded(Regex test, Regex reading) => lines.Any(at => test.IsMatch(masked[at]) &&
            (reading.IsMatch(test.Replace(masked[at], " ")) || at + 1 < masked.Count && reading.IsMatch(masked[at + 1])));

        // The read on the guard's line - before it too, as Python's rows[r - 1][c] if c < r else 0 has it - or on the next.
        return Guarded(guard, read) && Guarded(lowerGuard, lowerRead);
    }

    /// <summary>The lines of the block under a heading - those more indented than it, until one that is not.</summary>
    private static List<int> BlockUnder(IReadOnlyList<string> masked, int heading)
    {
        var lines = new List<int>();
        var indent = CodeText.Indentation(masked[heading]).Length;
        for (var at = heading + 1; at < masked.Count; at++)
        {
            if (masked[at].Trim().Length == 0) continue;
            if (CodeText.Indentation(masked[at]).Length <= indent)
            {
                // A { on a line of its own under the heading opens its block, as C# writes it.
                if (at == heading + 1 && masked[at].Trim() == "{") continue;
                break;
            }

            lines.Add(at);
        }

        return lines;
    }
}
