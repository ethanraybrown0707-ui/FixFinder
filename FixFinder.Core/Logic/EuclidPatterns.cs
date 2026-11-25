using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Logic;

/// <summary>
/// Euclid's greatest common divisor going on with a % b and b - the pair in the wrong order - where it goes on with b and
/// a % b. The search stops when the second number is 0, and given back as it was it never is: a % b is below b, so taking
/// b from it again changes nothing, and gcd(35, 21) goes on as gcd(14, 21) for ever - a call that never ends until the
/// stack runs out, or a loop that never ends. QuixBugs' gcd is this mistake, in Python and in Java. The change swaps the
/// pair: gcd(b, a % b), as QuixBugs' correct programs have it.
/// </summary>
/// <remarks>
/// Said only when the file shows all of it: a function calls itself with a remainder and the number it divides by -
/// gcd(a % b, b), gcd (a mod b) b, gcd(rem(a, b), b), gcd(A rem B, B), Get-Gcd ($a % $b) $b - or a loop gives the pair
/// back so - a, b = a % b, b, (first, second) = (first % second, second) - and the search stops when that number is 0:
/// if b == 0, while b != 0, gcd(A, 0) -&gt; A. A function or loop that also tests the other number - if a &lt; b: return a,
/// as a remainder taken by hand does - has a stop of its own, and is left alone.
/// </remarks>
public static partial class EuclidPatterns
{
    /// <summary>How one language is read, and whether its calls put their arguments one after another without brackets - gcd b (a % b) - as F#'s, OCaml's and PowerShell's do.</summary>
    private sealed record Dialect(string Language, IReadOnlySet<string> Files, Syntax Syntax, bool Juxtaposed = false, bool IgnoreCase = false);

    private static readonly Dialect[] Dialects =
    [
        new("python", CodePattern.Files(".py", ".pyw"), Syntax.Python),
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
        new("powershell", CodePattern.Files(".ps1"), Syntax.Python, Juxtaposed: true, IgnoreCase: true),
        new("dart", CodePattern.Files(".dart"), Syntax.CLike),
        new("scala", CodePattern.Files(".scala", ".sc"), Syntax.Scala),
        new("fortran", CodePattern.Files(".f90", ".f95", ".f03", ".f08", ".f"), Syntax.Fortran, IgnoreCase: true),
        new("fsharp", CodePattern.Files(".fs", ".fsx"), Syntax.FSharp, Juxtaposed: true),
        new("ocaml", CodePattern.Files(".ml"), Syntax.OCaml, Juxtaposed: true),
        new("elixir", CodePattern.Files(".ex", ".exs"), Syntax.Python),
        new("erlang", CodePattern.Files(".erl"), Syntax.Erlang),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        .. Dialects.Select(dialect => new CodePattern($"logic-{dialect.Language}-euclid-pair-not-moved", dialect.Files, dialect.Syntax,
            (id, source, masked) => PairsNotMoved(id, source, masked, dialect), Severity.Error, Confidence.Likely)),
    ];

    /// <summary>A remainder of one name by another: a % b, (a % b), a mod b, A rem B, $a % $b, mod(a, b), rem(a, b).</summary>
    [GeneratedRegex(@"^\(?\s*(?<number>[$]?[A-Za-z_]\w*)\s*(?:%|\bmod\b|\brem\b)\s*(?<divisor>[$]?[A-Za-z_]\w*)\s*\)?$|^(?:mod|rem)\s*\(\s*(?<number>[A-Za-z_]\w*)\s*,\s*(?<divisor>[A-Za-z_]\w*)\s*\)$", RegexOptions.IgnoreCase)]
    private static partial Regex Remainder();

    /// <summary>What every remainder written has in it: %, mod, rem - a file without one has nothing to read.</summary>
    [GeneratedRegex(@"%|\bmod\b|\brem\b", RegexOptions.IgnoreCase)]
    private static partial Regex RemainderWritten();

    /// <summary>A pair given back at once: a, b = ..., (a, b) = (...), [$a, $b] = [...], ($a, $b) = (...).</summary>
    [GeneratedRegex(@"^\s*(?:my\s+)?[(\[]?\s*(?<first>[$]?[A-Za-z_]\w*)\s*,\s*(?<second>[$]?[A-Za-z_]\w*)\s*[)\]]?\s*=(?!=)\s*(?<open>[(\[]?)\s*(?<pair>.+?)\s*[)\]]?\s*;?\s*$")]
    private static partial Regex PairGiven();

    private static IEnumerable<LogicFinding> PairsNotMoved(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        if (!masked.Any(line => RemainderWritten().IsMatch(line))) yield break;
        var options = dialect.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;

        for (var line = 0; line < masked.Count; line++)
        {
            if (!RemainderWritten().IsMatch(masked[line])) continue;
            if ((Called(masked, line, dialect, options) ?? Looped(masked, line, options)) is not { } pair) continue;

            var text = source.Lines[line];
            var changed = text[..pair.Start] + pair.Divisor + pair.Between + pair.RemainderText + text[pair.End..];
            var told = $"Line {line + 1} goes on with `{pair.RemainderText}` and `{pair.Divisor}` - but it stops only when `{pair.Divisor}` is 0, and " +
                       $"`{pair.Divisor}` is given back as it was, so it never is: `{pair.RemainderText}` is below `{pair.Divisor}`, and taking the " +
                       $"remainder by `{pair.Divisor}` again changes nothing. Euclid's way goes on with `{pair.Divisor}` and `{pair.RemainderText}`.";
            yield return new LogicFinding(id, line + 1, told,
                LocalFix.ReplaceLine(id, $"Go on with the divisor and the remainder: {pair.Divisor}{pair.Between}{pair.RemainderText}",
                    $"Euclid's greatest common divisor keeps the pair moving: the number it divided by comes first, and the remainder takes its " +
                    $"place, until the remainder is 0.",
                    source.Path, line + 1, changed));
        }
    }

    /// <summary>The pair gone on with, where it is on the line: the remainder and the divisor as written, and what is between them.</summary>
    private sealed record Pair(int Start, int End, string RemainderText, string Between, string Divisor);

    /// <summary>
    /// A call of a function the file writes with the remainder and the number divided by - gcd(a % b, b), gcd (a mod b) b,
    /// Get-Gcd ($a % $b) $b - in a function that stops when that number is 0 and tests the other one nowhere.
    /// </summary>
    private static Pair? Called(IReadOnlyList<string> masked, int line, Dialect dialect, RegexOptions options)
    {
        var text = masked[line];
        foreach (Match call in Regex.Matches(text, dialect.Juxtaposed ? @"(?<![\w$.-])(?<function>[A-Za-z_][\w-]*)\s+(?=[($])" : @"(?<![\w$.:])(?<function>[A-Za-z_]\w*)\s*\("))
        {
            var function = call.Groups["function"].Value;
            if (Regex.IsMatch(function, @"^(?:if|while|for|return|print|elif|and|or|not|then|else|mod|rem|max|min)$", RegexOptions.IgnoreCase)) continue;
            if (Arguments(text, call.Index + call.Length - (dialect.Juxtaposed ? 0 : 1), dialect.Juxtaposed) is not { } arguments) continue;

            var (first, second) = arguments;
            var firstText = text[first.Start..first.End].Trim();
            var secondText = text[second.Start..second.End].Trim();
            if (Remainder().Match(firstText) is not { Success: true } remainder) continue;
            var number = remainder.Groups["number"].Value;
            var divisor = remainder.Groups["divisor"].Value;
            if (!Same(secondText, divisor, options) || Same(number, divisor, options)) continue;

            // The function the call is in, by its heading above - and the lines from it to the call, read for its tests.
            if (Heading(masked, line, call.Index, function, options) is not { } heading) continue;

            // Each number in its own place: the remainder in the first parameter's, the divisor in the second's, which it is -
            // gcd(b % a, a) in gcd(a, b) moves a into b's place, as Euclid's way does when it stops at a == 0.
            if (ParametersOf(masked, heading, function, options) is not [var firstParameter, var secondParameter, ..] ||
                !Same(firstParameter, number, options) || !Same(secondParameter, divisor, options)) continue;
            if (!StopsAtZero(masked, heading, line, function, divisor, options)) continue;
            if (Tests(masked, heading, line, number, call.Index, options)) continue;

            var firstStart = first.Start + text[first.Start..first.End].IndexOf(firstText, StringComparison.Ordinal);
            var secondStart = second.Start + text[second.Start..second.End].IndexOf(secondText, StringComparison.Ordinal);
            return new Pair(firstStart, secondStart + secondText.Length, firstText, text[(firstStart + firstText.Length)..secondStart], secondText);
        }

        return null;
    }

    /// <summary>
    /// The call's first two arguments, with where each is - in brackets, split at the top level's commas, two and no more;
    /// or one after another, each a bracketed piece or a name, for a call without brackets.
    /// </summary>
    private static ((int Start, int End), (int Start, int End))? Arguments(string text, int at, bool juxtaposed)
    {
        if (!juxtaposed)
        {
            if (Brackets.ClosingParenthesis(text, at) is not { } close) return null;
            var depth = 0;
            var commas = new List<int>();
            for (var index = at + 1; index < close; index++)
            {
                if (text[index] is '(' or '[' or '{') depth++;
                else if (text[index] is ')' or ']' or '}') depth--;
                else if (text[index] == ',' && depth == 0) commas.Add(index);
            }

            return commas.Count == 1 ? ((at + 1, commas[0]), (commas[0] + 1, close)) : null;
        }

        var pieces = new List<(int Start, int End)>();
        var from = at;
        while (pieces.Count < 3 && from < text.Length)
        {
            while (from < text.Length && text[from] == ' ') from++;
            if (from >= text.Length) break;
            if (text[from] == '(')
            {
                if (Brackets.ClosingParenthesis(text, from) is not { } close) return null;
                pieces.Add((from, close + 1));
                from = close + 1;
            }
            else if (Regex.Match(text[from..], @"^[$]?[A-Za-z_]\w*") is { Success: true } name && !Regex.IsMatch(name.Value, @"^(?:then|else|in|with|and|or|do)$"))
            {
                pieces.Add((from, from + name.Length));
                from += name.Length;
            }
            else break;
        }

        return pieces.Count == 2 ? (pieces[0], pieces[1]) : null;
    }

    private static bool Same(string first, string second, RegexOptions options) =>
        string.Equals(first, second, options.HasFlag(RegexOptions.IgnoreCase) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static Regex Named(string name, RegexOptions options) =>
        new($@"(?<![\w$@.])[$]?{Regex.Escape(name.TrimStart('$'))}(?!\w)", options);

    /// <summary>
    /// The line heading the function, at or above the call: def gcd(, function gcd(, func gcd(, fn gcd(, sub gcd, let rec
    /// gcd, int gcd(, gcd(A, B) -&gt; - the nearest above that writes the name as a function.
    /// </summary>
    private static int? Heading(IReadOnlyList<string> masked, int line, int callAt, string function, RegexOptions options)
    {
        var name = Regex.Escape(function);
        var heading = new Regex($@"\b(?:def|defp|function|func|fn|sub|let\s+rec|let)\s+{name}\b|^\s*(?:(?:public|private|protected|static|final|inline|recursive|integer|int|long|unsigned|u?int\d*|i\d+|u\d+|Int|Long|BigInteger|number|var)\s+)+{name}\s*\(|^{name}\s*\(", options);
        for (var at = line; at >= 0; at--)
        {
            var text = at == line ? masked[at][..callAt] : masked[at];
            if (heading.IsMatch(text)) return at;
        }

        return null;
    }

    /// <summary>
    /// The function's parameters' names in order, from its heading: (a, b), (int a, int b), (a: u32, b: u32), ([int]$a,
    /// [int]$b), (int $a, int $b), gcd a b = - or Perl's from the line copying them out of @_.
    /// </summary>
    private static List<string>? ParametersOf(IReadOnlyList<string> masked, int heading, string function, RegexOptions options)
    {
        var text = masked[heading];
        var named = Regex.Match(text, $@"(?<![\w$.-]){Regex.Escape(function)}(?![\w-])", options);
        if (!named.Success) return null;
        var after = text[(named.Index + named.Length)..].TrimStart();

        if (after.StartsWith('('))
        {
            if (Brackets.ClosingParenthesis(after, 0) is not { } close) return null;
            return [.. after[1..close].Split(',').Select(piece => piece.Contains(':')
                    ? Regex.Match(piece[..piece.IndexOf(':')], @"[$]?[A-Za-z_]\w*\s*$").Value.Trim()
                    : Regex.Match(piece, @"[$]?[A-Za-z_]\w*\s*$").Value.Trim())
                .Where(name => name.Length > 0)];
        }

        // F# and OCaml: let rec gcd a b = ...
        if (Regex.Match(after, @"^(?<names>(?:[A-Za-z_]\w*\s+)+)=") is { Success: true } juxtaposed)
            return [.. juxtaposed.Groups["names"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)];

        // Perl: sub gcd { my ($a, $b) = @_;
        for (var at = heading; at < Math.Min(masked.Count, heading + 3); at++)
        {
            if (Regex.Match(masked[at], @"\bmy\s*\((?<names>[^)]*)\)\s*=\s*@_") is { Success: true } copied)
                return [.. copied.Groups["names"].Value.Split(',').Select(name => name.Trim())];
        }

        return null;
    }

    /// <summary>
    /// Whether the search stops when the divisor is 0: a test of it from the heading to the call - b == 0, b = 0, b === 0,
    /// b.zero?, $b -eq 0, !b, not b - or a clause of the function's own with 0 in its place, gcd(A, 0) -&gt; A.
    /// </summary>
    private static bool StopsAtZero(IReadOnlyList<string> masked, int heading, int line, string function, string divisor, RegexOptions options)
    {
        var name = Regex.Escape(divisor.TrimStart('$'));
        var zero = new Regex($@"(?<![\w$@.])[$]?{name}\s*(?:===?|=|-eq)\s*0(?![\w.])|(?<![\w$@.])[$]?{name}\.zero\?|(?:!|\bnot\s+)\s*[$]?{name}(?!\w)", options);
        var clause = new Regex($@"^\s*(?:defp?\s+)?{Regex.Escape(function)}\s*\(\s*[$]?[A-Za-z_]\w*\s*,\s*0\s*\)", options);
        for (var at = heading; at <= line; at++)
        {
            if (zero.IsMatch(masked[at])) return true;
        }

        return masked.Any(text => clause.IsMatch(text));
    }

    /// <summary>Whether the other number is tested from the heading to the call - if a &lt; b, a == 0 - which gives the function a stop of its own.</summary>
    private static bool Tests(IReadOnlyList<string> masked, int heading, int line, string number, int callAt, RegexOptions options)
    {
        var name = Regex.Escape(number.TrimStart('$'));
        var tested = new Regex($@"(?<![\w$@.])[$]?{name}\s*(?:[<>]=?|===?|!==?|~=|/=|-(?:eq|ne|lt|le|gt|ge)\b)|(?:[<>]=?|===?|!==?|~=|/=)\s*[$]?{name}(?!\w)", options);
        for (var at = heading; at <= line; at++)
        {
            var text = at == line ? masked[at][..callAt] : masked[at];
            if (at == heading) text = Regex.Replace(text, @"\([^)]*\)", "");
            if (tested.IsMatch(text)) return true;
        }

        return false;
    }

    /// <summary>
    /// The pair given back in a loop - a, b = a % b, b - whose test asks the divisor not to be 0, and which tests the other
    /// number nowhere: while b != 0, while b, for b != 0, while ($b != 0).
    /// </summary>
    private static Pair? Looped(IReadOnlyList<string> masked, int line, RegexOptions options)
    {
        var text = masked[line];
        if (PairGiven().Match(text) is not { Success: true } given) return null;
        var pair = given.Groups["pair"];
        var pieces = pair.Value.Split(',');
        if (pieces.Length != 2) return null;
        var firstText = pieces[0].Trim();
        var secondText = pieces[1].Trim();
        if (Remainder().Match(firstText) is not { Success: true } remainder) return null;
        var number = remainder.Groups["number"].Value;
        var divisor = remainder.Groups["divisor"].Value;
        if (!Same(given.Groups["first"].Value, number, options) || !Same(given.Groups["second"].Value, divisor, options) || !Same(secondText, divisor, options)) return null;

        if (Reaching.InnermostLoop(masked, 0, line) is not { } loop) return null;
        var name = Regex.Escape(divisor.TrimStart('$'));
        if (!Regex.IsMatch(masked[loop.Heading], $@"\b(?:while|for|until)\b\s*\(?\s*[$]?{name}\s*(?:!==?|~=|/=|>|-ne\b|-gt\b)\s*0\b|\bwhile\b\s*\(?\s*[$]?{name}\s*(?:\)|:|\{{|$)", options)) return null;
        if (Tests(masked, loop.Heading, line, number, text.Length, options)) return null;

        var firstStart = pair.Index + pair.Value.IndexOf(firstText, StringComparison.Ordinal);
        var secondStart = pair.Index + pair.Value.LastIndexOf(secondText, StringComparison.Ordinal);
        return new Pair(firstStart, secondStart + secondText.Length, firstText, text[(firstStart + firstText.Length)..secondStart], secondText);
    }
}
