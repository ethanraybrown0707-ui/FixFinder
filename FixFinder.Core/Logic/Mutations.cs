using System.Text.RegularExpressions;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>One small change to one line, to be tried against the expected output.</summary>
/// <param name="Line">The 1-based line changed.</param>
/// <param name="NewText">The whole line after the change.</param>
/// <param name="From">The text replaced, for the title.</param>
/// <param name="To">What replaced it.</param>
/// <param name="Kind">What sort of mistake this change would correct, in words.</param>
/// <param name="Cost">How unlikely the change is as a mistake: lower is tried first on the same line.</param>
public sealed record Mutation(int Line, string NewText, string From, string To, string Kind, int Cost);

/// <summary>
/// The small edits that correct most one-line logic mistakes, generated for a line without knowing what it means.
/// </summary>
/// <remarks>
/// Studies of real bug fixes keep finding the same few shapes behind a large share of one-line fixes: a comparison off by one
/// (<c>&lt;</c> for <c>&lt;=</c>), a bound off by one (<c>n</c> for <c>n - 1</c>), the wrong arithmetic or logical operator, an
/// integer division where a real one was meant, a constant one away, <c>max</c> for <c>min</c>. Every one of those is a change
/// to a single token, which makes them few enough to try. None is ever offered on its own merits - only a change that makes
/// the program print what was expected, for every run given, is.
/// </remarks>
public static partial class Mutations
{
    [GeneratedRegex(@"(?<![<>=!+\-*/%&|^:])(?<op><=|>=|==|!=|<|>)(?![<>=\-])")]
    private static partial Regex Relational();

    [GeneratedRegex(@"(?<=[\w)\]]\s*)(?<op>\+|-|\*|//|/)(?![+\-=/*>])(?=\s*[\w(\[.])")]
    private static partial Regex Arithmetic();

    [GeneratedRegex(@"(?<op>\+=|-=|\*=|//=|/=)")]
    private static partial Regex Compound();

    [GeneratedRegex(@"(?<op>\+\+|--)")]
    private static partial Regex Step();

    [GeneratedRegex(@"(?<![\w.$])(?<number>\d+)(?![\w.])")]
    private static partial Regex Integer();

    [GeneratedRegex(@"\s*(?<op>[-+])\s*1(?![\w.])")]
    private static partial Regex PlusMinusOne();

    [GeneratedRegex(@"(?<![\w.$:])(?:(?:Math|math|std|Mathf)\s*(?:\.|::)\s*)?(?<name>max|min|Max|Min)(?=\s*\()")]
    private static partial Regex MaxMin();

    [GeneratedRegex(@"(?<![\w.$])(?<value>True|False|true|false)(?![\w$])")]
    private static partial Regex Boolean();

    [GeneratedRegex(@"^\s*(?:#|//|/\*|\*|import\b|from\s+\S+\s+import\b|#include\b|package\b|using\s+[\w.]+\s*;|namespace\b|@)")]
    private static partial Regex NotCode();

    /// <summary>Every candidate change to one line, cheapest first.</summary>
    public static IReadOnlyList<Mutation> For(SourceFile source, int number)
    {
        if (source.Line(number) is not { } line) return [];

        var python = Path.GetExtension(source.Path).Equals(".py", StringComparison.OrdinalIgnoreCase);
        var syntax = python ? Syntax.Python : Syntax.CLike;
        var masked = CodeText.MaskAll(source.Lines, syntax)[number - 1];

        if (masked.Trim().Length == 0 || NotCode().IsMatch(line)) return [];
        if (python && Regex.IsMatch(masked, @"^\s*(?:async\s+)?(?:def|class)\s")) return [];

        var found = new List<Mutation>();

        void Add(int index, int length, string replacement, string kind, int cost)
        {
            var from = line.Substring(index, length);
            if (from == replacement) return;
            found.Add(new Mutation(number, line[..index] + replacement + line[(index + length)..], from.Trim(), replacement.Trim(), kind, cost));
        }

        var clike = !python;

        // A comparison that decides a branch or a loop is where an off-by-one most often lives.
        var decides = Regex.IsMatch(masked, @"\b(?:if|elif|while|for|return)\b|\?");
        var generic = clike && Regex.IsMatch(masked, @"\b[A-Z]\w*\s*<[\w\s,<>?\[\]]*>");

        foreach (Match m in Relational().Matches(masked))
        {
            var op = m.Groups["op"].Value;

            // A < or > with no space either side, in a line that names a generic type, is a bracket, not a comparison.
            if (op is "<" or ">" && generic && !(m.Index > 0 && masked[m.Index - 1] == ' ' && m.Index + 1 < masked.Length && masked[m.Index + 1] == ' ')) continue;
            if (op == ">" && m.Index > 0 && masked[m.Index - 1] is '-' or '=') continue;

            var (near, far) = op switch
            {
                "<" => ("<=", ">"), "<=" => ("<", ">="), ">" => (">=", "<"), ">=" => (">", "<="), "==" => ("!=", (string?)null), "!=" => ("==", null),
                _ => (null, null),
            };

            if (near is not null) Add(m.Index, op.Length, near, "a comparison off by one, or the wrong way round", op is "==" or "!=" ? 2 : decides ? 0 : 1);
            if (far is not null) Add(m.Index, op.Length, far, "a comparison the wrong way round", 3);
        }

        foreach (Match m in Arithmetic().Matches(masked))
        {
            var op = m.Groups["op"].Value;
            if (clike && op == "/" && m.Index + 1 < masked.Length && masked[m.Index + 1] == '/') continue;

            foreach (var (to, cost) in op switch
            {
                "+" => new[] { ("-", 2) },
                "-" => [("+", 2)],
                "*" => [("/", 3), ("+", 4)],
                "/" when python => [("//", 2), ("*", 3)],
                "//" => [("/", 2)],
                "/" => [("*", 3)],
                _ => [],
            })
            {
                Add(m.Index, op.Length, to, "the wrong arithmetic operator", cost);
            }
        }

        foreach (Match m in Compound().Matches(masked))
        {
            var op = m.Groups["op"].Value;
            var to = op switch { "+=" => "-=", "-=" => "+=", "*=" => "+=", "/=" when python => "//=", "//=" => "/=", _ => null };
            if (to is not null) Add(m.Index, op.Length, to, "the wrong arithmetic operator", 2);
        }

        if (clike)
        {
            foreach (Match m in Step().Matches(masked))
                Add(m.Index, 2, m.Groups["op"].Value == "++" ? "--" : "++", "a count going the wrong way", 2);
        }

        // Logical operators.
        foreach (Match m in Regex.Matches(masked, python ? @"(?<![\w.])(?<op>and|or)(?![\w])" : @"(?<op>&&|\|\|)"))
        {
            var op = m.Groups["op"].Value;
            var to = op switch { "and" => "or", "or" => "and", "&&" => "||", _ => "&&" };
            Add(m.Index, op.Length, to, "and where or was meant, or the reverse", 2);
        }

        foreach (Match m in Regex.Matches(masked, python ? @"(?<![\w.])not\s+" : @"!(?![=])(?=\s*[\w(])"))
            Add(m.Index, m.Length, "", "a condition negated by mistake", 3);

        // An off-by-one written into the code - n + 1 where n was meant - and one missing from it.
        foreach (Match m in PlusMinusOne().Matches(masked))
            Add(m.Index, m.Length, "", "a bound off by one", 1);

        foreach (var (start, length) in Bounds(masked, python))
        {
            var operand = line.Substring(start, length);
            Add(start, length, operand + " + 1", "a bound off by one", 2);
            Add(start, length, operand + " - 1", "a bound off by one", 2);
        }

        foreach (Match m in Integer().Matches(masked))
        {
            // A number inside an identifier-like context (an array size in a declaration, a version) is still fair game; a
            // number in a subscript or a range bound is the commonest place an off-by-one lives.
            var value = long.Parse(m.Groups["number"].Value);
            if (value > 1_000_000) continue;

            Add(m.Index, m.Length, (value + 1).ToString(), "a constant off by one", value <= 1 ? 2 : 3);
            if (value > 0) Add(m.Index, m.Length, (value - 1).ToString(), "a constant off by one", value <= 1 ? 2 : 3);
        }

        foreach (Match m in MaxMin().Matches(masked))
        {
            var name = m.Groups["name"].Value;
            var to = name switch { "max" => "min", "min" => "max", "Max" => "Min", _ => "Max" };
            Add(m.Groups["name"].Index, name.Length, to, "the largest where the smallest was meant, or the reverse", 3);
        }

        foreach (Match m in Boolean().Matches(masked))
        {
            var value = m.Groups["value"].Value;
            var to = value switch { "True" => "False", "False" => "True", "true" => "false", _ => "true" };
            if ((python && value is "True" or "False") || (clike && value is "true" or "false")) Add(m.Index, value.Length, to, "the opposite result", 3);
        }

        // An integer division where a real one was meant: sum / count stored in a double. Only C-like languages divide
        // integers into integers without saying so; Python 3's / never does.
        if (clike && !source.Path.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
        {
            foreach (Match m in Regex.Matches(masked, @"(?<![\w.)\]])(?<left>[A-Za-z_]\w*)\s*/\s*(?<right>[A-Za-z_]\w*|\d+)(?![\w.(])"))
            {
                var left = m.Groups["left"];
                var cast = source.Path.EndsWith(".go", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : $"(double) {left.Value}";
                if (cast is not null) Add(left.Index, left.Length, cast, "a whole-number division where the fraction was wanted", 2);
            }
        }

        return found.GroupBy(f => f.NewText).Select(g => g.OrderBy(f => f.Cost).First()).OrderBy(f => f.Cost).ToList();
    }

    /// <summary>The right-hand side of a comparison, and the last argument of <c>range</c>: where a loop's bound is written.</summary>
    private static IEnumerable<(int Start, int Length)> Bounds(string masked, bool python)
    {
        var operand = @"[A-Za-z_][\w.]*(?:\([^()]*\))?(?:\[[^\[\]]*\])?";

        foreach (Match m in Regex.Matches(masked, $@"(?:<=?|>=?)\s*(?<bound>{operand})(?=\s*(?:[;):,]|and\b|or\b|&&|\|\||$))"))
            yield return (m.Groups["bound"].Index, m.Groups["bound"].Length);

        if (python)
        {
            foreach (Match m in Regex.Matches(masked, $@"\brange\s*\((?:[^(),]+,\s*)?(?<bound>{operand})\s*[,)]"))
                yield return (m.Groups["bound"].Index, m.Groups["bound"].Length);
        }
    }
}
