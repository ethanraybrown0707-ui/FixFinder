using System.Globalization;
using System.Text.RegularExpressions;
using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// Two comparisons of one number joined by an and or an or, which between them decide less than they seem to, in the
/// languages whose values FixFinder does not follow through the code: <c>mark &gt; 100 &amp;&amp; mark &lt; 0</c>, which no
/// number makes true; <c>answer != 1 || answer != 2</c>, which every number makes true; and <c>score &gt;= 50 &amp;&amp; score &gt;
/// 40</c>, whose second half adds nothing - often a range with one side facing the wrong way. In Python, Java, C#,
/// JavaScript, Go, C and C++ the same is found by following the values: a condition that can never be true, or is always
/// true.
/// </summary>
/// <remarks>
/// Each comparison is of the same name with a number - <c>score &gt;= 50</c>, <c>50 &lt;= score</c> - and the two are joined by
/// one and or one or with nothing else between them. A pair joined by an or is passed over when an and on either side of it
/// would take one of its halves first, and either half after a not is passed over. What the pair decides is worked out for
/// every number, whole or not, so <c>x &gt; 5 &amp;&amp; x &lt; 6</c> is not said to be never true.
/// </remarks>
public static partial class OneNumberComparisonPatterns
{
    /// <summary>
    /// How one language writes a comparison and joins two: its and, its or, its not, what its names look like, its
    /// comparisons, and whether case matters. TypeScript is read with JavaScript's files, as every TypeScript file is, and said
    /// only of a TypeScript file.
    /// </summary>
    private sealed record Dialect(string Language, IReadOnlySet<string> Files, Syntax Syntax, string And, string Or, string Not, string Name,
        string Comparisons, bool IgnoreCase = false, bool OnlyTypeScript = false);

    private const string CLikeComparisons = @"===|!==|<=|>=|==|!=|(?<![<>=!-])<(?![<=>])|(?<![<>=!-])>(?![>=])";

    private static readonly Dialect[] Dialects =
    [
        new("typescript", CodePattern.Files(".js", ".mjs", ".cjs"), Syntax.CLike, "&&", @"\|\|", "!", @"[A-Za-z_$][\w$]*(?:\.[A-Za-z_$][\w$]*)*", CLikeComparisons, OnlyTypeScript: true),
        new("ruby", CodePattern.Files(".rb"), Syntax.Python, @"&&|\band\b", @"\|\||\bor\b", @"!|\bnot\b", @"@{0,2}[a-z_]\w*(?:\.[a-z_]\w*)*", @"<=(?!>)|>=|==|!=|(?<![<>=!-])<(?![<=>])|(?<![<>=!-])>(?![>=])"),
        new("php", CodePattern.Files(".php"), Syntax.CLike, @"&&|\band\b", @"\|\||\bor\b", "!", @"\$[A-Za-z_]\w*(?:->[A-Za-z_]\w*)*", @"===|!==|<=(?!>)|>=|==|!=|<>|(?<![<>=!-])<(?![<=>])|(?<![<>=!-])>(?![>=])"),
        new("lua", CodePattern.Files(".lua"), Syntax.Lua, @"\band\b", @"\bor\b", @"\bnot\b", @"[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*", @"<=|>=|==|~=|(?<![<>=~])<(?![<=])|(?<![<>=~])>(?![>=])"),
        new("perl", CodePattern.Files(".pl", ".pm"), Syntax.Perl, @"&&|\band\b", @"\|\||\bor\b", @"!|\bnot\b", @"\$[A-Za-z_]\w*", @"<=(?!>)|>=|==|!=|(?<![<>=!-])<(?![<=>])|(?<![<>=!-])>(?![>=])"),
        new("dart", CodePattern.Files(".dart"), Syntax.CLike, "&&", @"\|\|", "!", @"[A-Za-z_$][\w$]*(?:\.[A-Za-z_$][\w$]*)*", CLikeComparisons),
        new("powershell", CodePattern.Files(".ps1"), Syntax.Python, @"-and\b", @"-or\b", @"-not\b|!", @"\$[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*", @"-(?:lt|le|gt|ge|eq|ne)\b", IgnoreCase: true),
        new("scala", CodePattern.Files(".scala", ".sc"), Syntax.Scala, "&&", @"\|\|", "!", @"[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*", @"<=|>=|==|!=|(?<![<>=!-])<(?![<=>-])|(?<![<>=!-])>(?![>=])"),
        new("fortran", CodePattern.Files(".f90", ".f95", ".f03", ".f08", ".f"), Syntax.Fortran, @"\.and\.", @"\.or\.", @"\.not\.", @"[A-Za-z]\w*(?:%[A-Za-z]\w*)*", @"\.(?:lt|le|gt|ge|eq|ne)\.|<=|>=|==|/=|(?<![<>=/])<(?![<=])|(?<![<>=/])>(?![>=])", IgnoreCase: true),
        new("rust", CodePattern.Files(".rs"), Syntax.CLike, "&&", @"\|\|", "!", @"[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*", CLikeComparisons),
        new("elixir", CodePattern.Files(".ex", ".exs"), Syntax.Python, @"&&|\band\b", @"\|\||\bor\b", @"!|\bnot\b", @"[a-z_]\w*(?:\.[a-z_]\w*)*", @"===|!==|<=|>=|==|!=|(?<![<>=!|-])<(?![<=>-])|(?<![<>=!-])>(?![>=])"),
        new("erlang", CodePattern.Files(".erl", ".hrl"), Syntax.Erlang, @"\bandalso\b|\band\b", @"\borelse\b|\bor\b", @"\bnot\b", @"[A-Z_]\w*", @"=:=|=/=|=<|>=|==|/=|(?<![<>=/-])<(?![<=-])|(?<![<>=/-])>(?![>=])"),
        new("fsharp", CodePattern.Files(".fs", ".fsx"), Syntax.FSharp, "&&", @"\|\|", @"\bnot\b", @"[a-z_]\w*(?:\.[A-Za-z_]\w*)*", @"<>|<=|>=|(?<![<>=:|-])=(?![=>])|(?<![<>=-])<(?![<=>|-])|(?<![<>=|-])>(?![>=])"),
        new("ocaml", CodePattern.Files(".ml"), Syntax.OCaml, "&&", @"\|\|", @"\bnot\b", @"[a-z_]\w*(?:\.[a-z_]\w*)*", @"<>|<=|>=|(?<![<>=:|-])=(?![=>])|(?<![<>=-])<(?![<=>|-])|(?<![<>=|-])>(?![>=])"),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        .. Dialects.Select(dialect => new CodePattern($"logic-{dialect.Language}-comparisons-of-one-number", dialect.Files, dialect.Syntax,
            (id, source, masked) => Decided(id, source, masked, dialect))),
    ];

    /// <summary>One comparison of a name with a number, where it is on the line, and what it says of the name.</summary>
    private sealed record Comparison(int Start, int End, string Name, NumberSet Holds, bool NumberFirst);

    private static IEnumerable<LogicFinding> Decided(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        if (dialect.OnlyTypeScript && !TypeScriptCheck.IsTypeScript(source.Path)) yield break;

        var options = dialect.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
        var number = @"-?\d+(?:\.\d+)?(?:[eEdD][-+]?\d+)?(?:_\w+|[A-Za-z]{1,3}\d{0,2})?";
        var comparison = new Regex($@"(?<![\w$@.%>-])(?:(?<name>(?>{dialect.Name}))\s*(?<op>{dialect.Comparisons})\s*(?<number>(?>{number}))|(?<first>(?>{number}))\s*(?<op2>{dialect.Comparisons})\s*(?<name2>(?>{dialect.Name})))(?![\w$.(\[%])",
            options, TimeSpan.FromSeconds(1));
        var joiner = new Regex($@"^\s*(?:(?<and>{dialect.And})|(?<or>{dialect.Or}))\s*$", options, TimeSpan.FromSeconds(1));
        var andAround = new Regex($@"(?:{dialect.And})\s*$", options, TimeSpan.FromSeconds(1));
        var andAfter = new Regex($@"^\s*(?:{dialect.And})", options, TimeSpan.FromSeconds(1));
        var notBefore = new Regex($@"(?:{dialect.Not})\s*\(?\s*$", options, TimeSpan.FromSeconds(1));
        var comparer = dialect.IgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        for (var line = 0; line < masked.Count; line++)
        {
            var text = masked[line];
            var found = comparison.Matches(text).Select(Read).OfType<Comparison>().ToList();

            for (var index = 0; index + 1 < found.Count; index++)
            {
                var (first, second) = (found[index], found[index + 1]);
                if (!comparer.Equals(first.Name, second.Name)) continue;
                if (joiner.Match(text[first.End..second.Start]) is not { Success: true } joined) continue;
                if (notBefore.IsMatch(text[..first.Start])) continue;

                var isAnd = joined.Groups["and"].Success;
                if (!isAnd && (andAround.IsMatch(text[..first.Start]) || andAfter.IsMatch(text[second.End..]))) continue;

                var written = source.Line(line + 1) ?? text;
                var (a, b, whole) = (written[first.Start..first.End], written[second.Start..second.End], written[first.Start..second.End]);
                if (Said(isAnd, first.Holds, second.Holds, a, b, whole) is { } said) yield return new LogicFinding(id, line + 1, said, null);
            }
        }
    }

    /// <summary>What a pair decides that is worth saying, or null when each half decides something of its own.</summary>
    private static string? Said(bool isAnd, NumberSet first, NumberSet second, string a, string b, string whole)
    {
        var firstInSecond = first.Within(second);
        var secondInFirst = second.Within(first);

        if (isAnd)
        {
            if (first.Intersect(second).IsEmpty)
                return $"`{a}` and `{b}` cannot both be true - no number makes both hold - so `{whole}` is never true, and what it decides never happens";
            if (firstInSecond && secondInFirst) return $"`{a}` and `{b}` are the same test, so `{whole}` asks one thing twice";
            if (firstInSecond) return $"`{b}` adds nothing to `{a}`: every number that makes `{a}` hold makes `{b}` hold too, so `{whole}` is the same as `{a}` - if a range was meant, one side may face the wrong way";
            if (secondInFirst) return $"`{a}` adds nothing to `{b}`: every number that makes `{b}` hold makes `{a}` hold too, so `{whole}` is the same as `{b}` - if a range was meant, one side may face the wrong way";
            return null;
        }

        if (first.Complement().Within(second))
            return $"`{whole}` is always true - every number makes `{a}` or `{b}` hold - so it decides nothing";
        if (firstInSecond && secondInFirst) return $"`{a}` and `{b}` are the same test, so `{whole}` asks one thing twice";
        if (firstInSecond) return $"`{a}` adds nothing to `{b}`: every number that makes `{a}` hold makes `{b}` hold too, so `{whole}` is the same as `{b}`";
        if (secondInFirst) return $"`{b}` adds nothing to `{a}`: every number that makes `{b}` hold makes `{a}` hold too, so `{whole}` is the same as `{a}`";
        return null;
    }

    /// <summary>A comparison as the numbers it holds for, with the name first: 50 &lt;= score is score &gt;= 50.</summary>
    private static Comparison? Read(Match match)
    {
        var numberFirst = match.Groups["first"].Success;
        var name = numberFirst ? match.Groups["name2"].Value : match.Groups["name"].Value;
        var written = numberFirst ? match.Groups["first"].Value : match.Groups["number"].Value;
        var op = (numberFirst ? match.Groups["op2"].Value : match.Groups["op"].Value).ToLowerInvariant();
        if (NumberOf(written) is not { } value) return null;

        var holds = Normalised(op) switch
        {
            "<" => numberFirst ? NumberSet.Above(value, inclusive: false) : NumberSet.Below(value, inclusive: false),
            "<=" => numberFirst ? NumberSet.Above(value, inclusive: true) : NumberSet.Below(value, inclusive: true),
            ">" => numberFirst ? NumberSet.Below(value, inclusive: false) : NumberSet.Above(value, inclusive: false),
            ">=" => numberFirst ? NumberSet.Below(value, inclusive: true) : NumberSet.Above(value, inclusive: true),
            "==" => NumberSet.Only(value),
            "!=" => NumberSet.Only(value).Complement(),
            _ => null,
        };

        return holds is null ? null : new Comparison(match.Index, match.Index + match.Length, name, holds, numberFirst);
    }

    /// <summary>Each language's way of writing a comparison, as one of six.</summary>
    private static string Normalised(string op) => op switch
    {
        "<" or "-lt" or ".lt." => "<",
        "<=" or "=<" or "-le" or ".le." => "<=",
        ">" or "-gt" or ".gt." => ">",
        ">=" or "-ge" or ".ge." => ">=",
        "==" or "===" or "=:=" or "=" or "-eq" or ".eq." => "==",
        "!=" or "!==" or "<>" or "~=" or "/=" or "=/=" or "-ne" or ".ne." => "!=",
        _ => "",
    };

    /// <summary>A number as written, with its kind or type after it - 50, 0.5d0, 5_8, 5i32, 5L, 5.0f - as a double.</summary>
    private static double? NumberOf(string written)
    {
        var digits = NumberPart().Match(written);
        if (!digits.Success) return null;
        var normal = digits.Value.Replace('d', 'e').Replace('D', 'e');
        return double.TryParse(normal, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    [GeneratedRegex(@"^-?\d+(?:\.\d+)?(?:[eEdD][-+]?\d+)?")]
    private static partial Regex NumberPart();

    /// <summary>The numbers a comparison holds for, as ranges with each end in or out.</summary>
    private sealed record NumberSet(IReadOnlyList<(double Low, bool LowIn, double High, bool HighIn)> Ranges)
    {
        public static NumberSet Below(double value, bool inclusive) => new([(double.NegativeInfinity, false, value, inclusive)]);

        public static NumberSet Above(double value, bool inclusive) => new([(value, inclusive, double.PositiveInfinity, false)]);

        public static NumberSet Only(double value) => new([(value, true, value, true)]);

        public bool IsEmpty => Ranges.All(Empty);

        private static bool Empty((double Low, bool LowIn, double High, bool HighIn) range) =>
            range.Low > range.High || range.Low == range.High && !(range.LowIn && range.HighIn);

        public NumberSet Intersect(NumberSet other) => new([
            .. from mine in Ranges
               from theirs in other.Ranges
               let low = mine.Low > theirs.Low ? (mine.Low, mine.LowIn) : theirs.Low > mine.Low ? (theirs.Low, theirs.LowIn) : (mine.Low, mine.LowIn && theirs.LowIn)
               let high = mine.High < theirs.High ? (mine.High, mine.HighIn) : theirs.High < mine.High ? (theirs.High, theirs.HighIn) : (mine.High, mine.HighIn && theirs.HighIn)
               select (low.Item1, low.Item2, high.Item1, high.Item2)]);

        /// <summary>Every number this does not hold for.</summary>
        public NumberSet Complement()
        {
            var result = new NumberSet([(double.NegativeInfinity, false, double.PositiveInfinity, false)]);
            foreach (var range in Ranges.Where(range => !Empty(range)))
            {
                var outside = new NumberSet([
                    (double.NegativeInfinity, false, range.Low, !range.LowIn),
                    (range.High, !range.HighIn, double.PositiveInfinity, false)]);
                result = result.Intersect(outside);
            }

            return result;
        }

        /// <summary>Whether every number this holds for, the other holds for too.</summary>
        public bool Within(NumberSet other) => Intersect(other.Complement()).IsEmpty;
    }
}
