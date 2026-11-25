using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A number's prime factors looked for up to its square root, going on with what is left when one is found - and giving
/// back nothing when none is. A number with no factor up to its square root is a prime: it is its own last factor. Given
/// back as nothing, the last prime of every number is lost - 12 comes out as 2 2 where 2 2 3 is right - and a prime has no
/// factors at all. QuixBugs' get_factors is this mistake, in Python and in Java. The change gives back the number itself,
/// as QuixBugs' correct programs do.
/// </summary>
/// <remarks>
/// Said only when the file shows all of it: a loop whose bound is the number's square root - int(n ** 0.5) + 1, i * i &lt;= n,
/// Math.sqrt(n), Integer.sqrt(n), a name given Math.sqrt(n) before the loop - testing whether the loop's name divides the
/// number, n % i == 0, beside a call of the function it is in with the number divided by it, and the function's next
/// statement after the loop giving back an empty list: return [], return new ArrayList&lt;Integer&gt;(), return {}, vec![],
/// found = [integer ::]. For F#, OCaml, Elixir and Erlang, the arm for no factor found - None -&gt; [], nil -&gt; [], [] -&gt; [] -
/// of a search for one up to the square root. A loop going up to the number itself finds it, and its empty list at the end is
/// never reached; it is left alone.
/// </remarks>
public static partial class LastFactorPatterns
{
    /// <summary>How one language gives back an empty list, and the list holding the number in its place.</summary>
    /// <param name="Empty">The empty list given back, as the language writes it - after return, or as the function's last line.</param>
    /// <param name="Holding">The list holding the number, with {number} in it - given the empty list written, for Java's and C#'s, whose type it repeats.</param>
    /// <param name="Arms">Whether the language finds its factor with a search and gives back what is found or not in arms, as F#, OCaml, Elixir and Erlang do.</param>
    private sealed record Dialect(string Language, IReadOnlySet<string> Files, Syntax Syntax, Regex Empty, Func<string, string, IReadOnlyList<string>, string> Holding, bool Arms = false, bool IgnoreCase = false);

    private static readonly Dialect[] Dialects =
    [
        new("python", CodePattern.Files(".py", ".pyw"), Syntax.Python, new(@"^\s*return\s+(?<empty>\[\s*\])\s*$"), (_, number, _) => $"[{number}]"),
        new("java", CodePattern.Files(".java"), Syntax.CLike, new(@"^\s*return\s+(?<empty>new\s+(?:Array|Linked)List\s*<\s*\w*\s*>\s*\(\s*\))\s*;\s*$"),
            (empty, number, lines) => empty[..^1] + (lines.Any(line => Regex.IsMatch(line, @"^\s*import\s+java\.util\.(?:\*|Arrays)\s*;")) ? $"Arrays.asList({number}))" : $"java.util.List.of({number}))")),
        new("csharp", CodePattern.Files(".cs"), Syntax.CLike, new(@"^\s*return\s+(?<empty>new\s+List\s*<\s*\w+\s*>\s*\(\s*\))\s*;\s*$"),
            (empty, number, _) => empty[..empty.LastIndexOf('(')].TrimEnd() + $" {{ {number} }}"),
        new("js", CodePattern.Files(".js", ".mjs", ".cjs", ".ts", ".mts", ".cts"), Syntax.CLike, new(@"^\s*return\s+(?<empty>\[\s*\])\s*;?\s*$"), (_, number, _) => $"[{number}]"),
        new("cpp", CodePattern.Files(".cpp", ".cc", ".cxx", ".c++"), Syntax.CLike, new(@"^\s*return\s+(?<empty>\{\s*\})\s*;\s*$"), (_, number, _) => $"{{{number}}}"),
        new("go", CodePattern.Files(".go"), Syntax.CLike, new(@"^\s*return\s+(?<empty>\[\]\w+\{\s*\})\s*$"), (empty, number, _) => empty[..^1].TrimEnd() + $"{number}}}"),
        new("rust", CodePattern.Files(".rs"), Syntax.CLike, new(@"^\s*(?:return\s+)?(?<empty>vec!\[\s*\]|Vec::new\(\))\s*;?\s*$"), (_, number, _) => $"vec![{number}]"),
        new("ruby", CodePattern.Files(".rb"), Syntax.Python, new(@"^\s*(?:return\s+)?(?<empty>\[\s*\])\s*$"), (_, number, _) => $"[{number}]"),
        new("lua", CodePattern.Files(".lua"), Syntax.Lua, new(@"^\s*return\s+(?<empty>\{\s*\})\s*$"), (_, number, _) => $"{{ {number} }}"),
        new("php", CodePattern.Files(".php"), Syntax.CLike, new(@"^\s*return\s+(?<empty>\[\s*\]|array\(\s*\))\s*;\s*$"), (_, number, _) => $"[{number}]"),
        new("perl", CodePattern.Files(".pl", ".pm"), Syntax.Perl, new(@"^\s*return\s+(?<empty>\(\s*\))\s*;\s*$"), (_, number, _) => $"({number})"),
        new("powershell", CodePattern.Files(".ps1"), Syntax.Python, new(@"^\s*return\s+(?<empty>@\(\s*\))\s*$"), (_, number, _) => $"@({number})", IgnoreCase: true),
        new("dart", CodePattern.Files(".dart"), Syntax.CLike, new(@"^\s*return\s+(?<empty>(?:<\w+>)?\[\s*\])\s*;\s*$"), (_, number, _) => $"[{number}]"),
        new("scala", CodePattern.Files(".scala", ".sc"), Syntax.Scala, new(@"^\s*(?:return\s+)?(?<empty>List\(\s*\)|Nil)\s*$"), (_, number, _) => $"List({number})"),
        new("fortran", CodePattern.Files(".f90", ".f95", ".f03", ".f08", ".f"), Syntax.Fortran, new(@"^\s*\w+\s*=\s*(?<empty>\[\s*integer\s*::\s*\])\s*$", RegexOptions.IgnoreCase), (_, number, _) => $"[{number}]", IgnoreCase: true),
        new("fsharp", CodePattern.Files(".fs", ".fsx"), Syntax.FSharp, new(@"^\s*\|\s*None\s*->\s*(?<empty>\[\s*\])\s*$"), (_, number, _) => $"[ {number} ]", Arms: true),
        new("ocaml", CodePattern.Files(".ml"), Syntax.OCaml, new(@"^\s*\|\s*None\s*->\s*(?<empty>\[\s*\])\s*$"), (_, number, _) => $"[ {number} ]", Arms: true),
        new("elixir", CodePattern.Files(".ex", ".exs"), Syntax.Python, new(@"^\s*nil\s*->\s*(?<empty>\[\s*\])\s*$"), (_, number, _) => $"[{number}]", Arms: true),
        new("erlang", CodePattern.Files(".erl"), Syntax.Erlang, new(@"^\s*\[\s*\]\s*->\s*(?<empty>\[\s*\])\s*;\s*$"), (_, number, _) => $"[{number}]", Arms: true),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        .. Dialects.Select(dialect => new CodePattern($"logic-{dialect.Language}-last-factor-dropped", dialect.Files, dialect.Syntax,
            (id, source, masked) => LastFactorsDropped(id, source, masked, dialect), Severity.Error, Confidence.Likely)),
    ];

    /// <summary>A square root, as the bound of a search for a factor: ** 0.5, sqrt, isqrt, Integer.sqrt, i * i &lt;= n.</summary>
    [GeneratedRegex(@"\*\*\s*0?\.5|\b(?:sqrt|isqrt|Sqrt)\b|(?<![\w$])(?<root>[$]?[A-Za-z_]\w*)\s*\*\s*\k<root>\s*(?:<=|-le)", RegexOptions.IgnoreCase)]
    private static partial Regex SquareRoot();

    /// <summary>The number's remainder by the loop's name tested to be 0: n % i == 0, (n % i).zero?, mod(n, i) == 0, n % i = 0, n mod i = 0, rem(n, i) == 0, N rem I =:= 0 - the number in the group named number.</summary>
    [GeneratedRegex(@"\(?\s*(?<number>[$]?[A-Za-z_]\w*)\s*(?:%|\bmod\b|\brem\b)\s*(?<divisor>[$]?[A-Za-z_]\w*)\s*\)?\s*(?:===?|-eq|=:=|=)\s*0\b|\(\s*(?<number>[$]?[A-Za-z_]\w*)\s*%\s*(?<divisor>[$]?[A-Za-z_]\w*)\s*\)\.zero\?|\b(?:mod|rem)\s*\(\s*(?<number>[A-Za-z_]\w*)\s*,\s*(?<divisor>[A-Za-z_]\w*)\s*\)\s*==\s*0\b", RegexOptions.IgnoreCase)]
    private static partial Regex Divides();

    /// <summary>What a square root as written has in it - sqrt, ** or a * with &lt;= or -le - read before the regex trying every name on the line.</summary>
    private static bool MayBeASquareRoot(string line) =>
        line.Contains("sqrt", StringComparison.OrdinalIgnoreCase) || line.Contains("**", StringComparison.Ordinal) ||
        line.Contains('*') && (line.Contains("<=", StringComparison.Ordinal) || line.Contains("-le", StringComparison.OrdinalIgnoreCase));

    private static bool IsASquareRoot(string line) => MayBeASquareRoot(line) && SquareRoot().IsMatch(line);

    private static IEnumerable<LogicFinding> LastFactorsDropped(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        if (!masked.Any(IsASquareRoot)) yield break;
        var options = dialect.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;

        for (var line = 0; line < masked.Count; line++)
        {
            if (dialect.Empty.Match(masked[line]) is not { Success: true } given) continue;
            var number = dialect.Arms ? AfterASearch(masked, line, options) : AfterALoop(masked, line, options);
            if (number is null) continue;

            var empty = given.Groups["empty"];
            var written = source.Lines[line];
            var holding = dialect.Holding(written.Substring(empty.Index, empty.Length), number, source.Lines);
            var changed = written[..empty.Index] + holding + written[(empty.Index + empty.Length)..];
            var told = $"Line {line + 1} gives back no factors when none of `{number}` was found up to its square root - but then `{number}` is a prime, and " +
                       "its own last factor. Given back as nothing, every number loses its last prime - 12 comes out as 2 2 where 2 2 3 is right - and a " +
                       "prime has no factors at all.";
            yield return new LogicFinding(id, line + 1, told,
                LocalFix.ReplaceLine(id, $"Give back the number itself: {holding}",
                    $"With no factor up to its square root, `{number}` has none but itself: it is the last prime factor.",
                    source.Path, line + 1, changed));
        }
    }

    /// <summary>
    /// The number, when the line is the next statement after a loop going up to its square root that tests whether the loop's
    /// name divides it, beside a call of the function the line is in with the number divided - or null.
    /// </summary>
    private static string? AfterALoop(IReadOnlyList<string> masked, int line, RegexOptions options)
    {
        // The loop just above: the nearest less indented heading above the line's previous statement, at the line's own indent.
        var indent = CodeText.Indentation(masked[line]).Length;
        var heading = -1;
        for (var above = line - 1; above >= 0; above--)
        {
            var text = masked[above];
            // A closing line, or a { on a line of its own under a heading as C# writes it, is passed over.
            if (text.Trim().Length == 0 || Regex.IsMatch(text, @"^\s*(?:\}|\{|end(?:\s+do)?)\s*$", RegexOptions.IgnoreCase)) continue;
            var width = CodeText.Indentation(text).Length;
            if (width < indent) return null;
            if (width == indent)
            {
                if (!Regex.IsMatch(text, @"^\s*(?:for|while|do)\b|\.each\b", RegexOptions.IgnoreCase)) return null;
                heading = above;
                break;
            }
        }

        if (heading < 0) return null;

        // Its bound the square root - in the heading, or a name given it before the loop: int max = (int)(Math.sqrt(n) + 1.0).
        var bounded = IsASquareRoot(masked[heading]) ||
                      Regex.Matches(masked[heading], @"(?<![\w$.])[$]?[A-Za-z_]\w*").Any(name => Enumerable.Range(Math.Max(0, heading - 4), heading - Math.Max(0, heading - 4))
                          .Any(at => Regex.IsMatch(masked[at], $@"(?<![\w$.]){Regex.Escape(name.Value)}\s*=(?!=)") && IsASquareRoot(masked[at])));
        if (!bounded) return null;

        // In the loop, the test that the loop's name divides the number, and a call of the function with the number divided by it.
        var function = FunctionAbove(masked, heading);
        if (function is null) return null;
        for (var at = heading; at < line; at++)
        {
            if (Divides().Match(masked[at]) is not { Success: true } divides) continue;
            var number = divides.Groups["number"].Value;
            var divisor = divides.Groups["divisor"].Value;
            if (CallsWithQuotient(masked, heading, line, function, number, divisor, options)) return number;
        }

        return null;
    }

    /// <summary>The name of the function a line is in: the nearest heading above it - def, function, fn, func, sub, a typed name with brackets, Fortran's function.</summary>
    private static string? FunctionAbove(IReadOnlyList<string> masked, int line)
    {
        for (var above = line; above >= 0; above--)
        {
            var heading = Regex.Match(masked[above], @"\b(?:def|function|fn|func|sub)\s+(?<name>[A-Za-z_][\w-]*)|^\s*(?:(?:public|private|protected|static|recursive)\s+)*(?:[\w<>\[\]:]+\s+)?(?:function\s+)?(?<name>[A-Za-z_]\w*)\s*\([^)]*\)\s*(?:->\s*[^{]+|result\s*\(\w+\)|:\s*[\w<>\[\]]+\s*=?)?\s*\{?\s*$", RegexOptions.IgnoreCase);
            if (heading.Success && !Regex.IsMatch(heading.Groups["name"].Value, @"^(?:if|for|while|switch|return|catch)$")) return heading.Groups["name"].Value;
        }

        return null;
    }

    /// <summary>Whether the lines call the function with the number divided by the loop's name: get_factors(n // i), factors(n / i), Get-Factors ($n / $i), factors(intdiv($n, $i)), factors(n ~/ i).</summary>
    private static bool CallsWithQuotient(IReadOnlyList<string> masked, int from, int to, string function, string number, string divisor, RegexOptions options)
    {
        var n = Regex.Escape(number.TrimStart('$'));
        var d = Regex.Escape(divisor.TrimStart('$'));
        // A spread before the name - [i, ...factors(n / i)] - is the call; one dot, something.factors(...), is another function's.
        var call = new Regex($@"(?<![\w$-])(?<![^.]\.){Regex.Escape(function)}\s*\(?\s*\(?\s*(?:[$]?{n}\s*(?://|/|~/|\bdiv\b)\s*[$]?{d}|(?:intdiv|div)\s*\(\s*[$]?{n}\s*,\s*[$]?{d}\s*\))", options);
        return Enumerable.Range(from, to - from).Any(at => call.IsMatch(masked[at]));
    }

    /// <summary>
    /// The number, when the line is the arm for no factor found of a search for one up to the square root - match ... tryFind
    /// (fun i -&gt; n % i = 0), Enum.find(..., fn i -&gt; ... rem(n, i) == 0 end), [I || I &lt;- lists:seq(2, trunc(math:sqrt(N))), N rem
    /// I =:= 0] - beside an arm going on with the number divided: i :: factors (n / i), [i | factors(div(n, i))].
    /// </summary>
    private static string? AfterASearch(IReadOnlyList<string> masked, int line, RegexOptions options)
    {
        for (var above = line - 1; above >= Math.Max(0, line - 4); above--)
        {
            var text = masked[above];
            if (!IsASquareRoot(text) || Divides().Match(text) is not { Success: true } divides) continue;
            var number = divides.Groups["number"].Value;
            var divisor = divides.Groups["divisor"].Value;
            var function = Regex.Match(string.Join("\n", Enumerable.Range(Math.Max(0, above - 4), above - Math.Max(0, above - 4) + 1).Select(at => masked[at])),
                @"(?:let\s+rec|def|defp)\s+(?<name>[a-z_]\w*)|^(?<name>[a-z_]\w*)\s*\(", RegexOptions.Multiline);
            if (!function.Success) continue;
            var arms = Enumerable.Range(above, Math.Min(masked.Count, line + 3) - above).ToList();
            if (CallsWithQuotient(masked, arms[0], arms[^1] + 1, function.Groups["name"].Value, number, divisor, options)) return number;
        }

        return null;
    }
}
