using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A count of a number's set bits taking the lowest one away with xor - n ^= n - 1 - where and is meant: n &amp;= n - 1. n - 1
/// has n's lowest set bit cleared and every bit below it set, so and clears that bit, and the loop runs once for each set
/// bit; xor gives those bits instead, which is never 0 once n is above 0 - 6 became 3, then 1, and stayed 1 - so a loop
/// waiting for n to reach 0 never ends. QuixBugs' bitcount is this mistake, in Python and in Java.
/// </summary>
/// <remarks>
/// Said only when the file shows all of it: n xor (n - 1) - n ^= n - 1, n = n ^ (n - 1), $n -bxor ($n - 1), n ~ (n - 1),
/// ieor(n, n - 1), n ^^^ (n - 1), n lxor (n - 1), bxor(n, n - 1), N bxor (N - 1) - is given back to n in a loop whose test
/// asks n to be 0 or not - while n, while (n != 0), until n.zero?, do while (n /= 0) - or is given in n's place to the
/// function it is in, which stops when n is 0: count(0, Count) -&gt; Count.
/// </remarks>
public static partial class XorInsteadOfAndPatterns
{
    /// <summary>How one language writes n xor (n - 1), and and in its place.</summary>
    /// <param name="Forms">The ways xor is written, each with the name in the group named number and the xor in the group named xor.</param>
    /// <param name="And">What is written in the xor's place.</param>
    /// <param name="Recursive">Whether the language counts by calling itself - F#, OCaml, Elixir and Erlang - rather than by a loop.</param>
    private sealed record Dialect(string Language, IReadOnlySet<string> Files, Syntax Syntax, Regex[] Forms, string And, bool Recursive = false, bool IgnoreCase = false);

    /// <summary>n - 1, in brackets or not, with the name in the group named again.</summary>
    private const string LessOne = @"\(?\s*(?<again>[$@]?[A-Za-z_]\w*)\s*-\s*1\s*\)?";

    /// <summary>n ^= n - 1, and n = n ^ (n - 1), n = (n - 1) ^ n - with or without brackets round n - 1 where - binds first.</summary>
    private static Regex[] Caret(bool bracketsNeeded) =>
    [
        new($@"(?<![\w$@.])(?<number>[$@]?[A-Za-z_]\w*)\s*(?<xor>\^)=\s*{LessOne}\s*;?\s*$"),
        new($@"(?<![\w$@.])(?<number>[$@]?[A-Za-z_]\w*)\s*=\s*\(?\s*[$@]?[A-Za-z_]\w*\s*(?<xor>\^)\s*{(bracketsNeeded ? @"\(\s*(?<again>[$@]?[A-Za-z_]\w*)\s*-\s*1\s*\)" : LessOne)}\s*\)?\s*;?\s*$"),
        new($@"(?<![\w$@.])(?<number>[$@]?[A-Za-z_]\w*)\s*=\s*\(?\s*\(\s*(?<again>[$@]?[A-Za-z_]\w*)\s*-\s*1\s*\)\s*(?<xor>\^)\s*[$@]?[A-Za-z_]\w*\s*\)?\s*;?\s*$"),
    ];

    private static readonly Dialect[] Dialects =
    [
        new("python", CodePattern.Files(".py", ".pyw"), Syntax.Python, Caret(false), "&"),
        new("java", CodePattern.Files(".java"), Syntax.CLike, Caret(false), "&"),
        new("csharp", CodePattern.Files(".cs"), Syntax.CLike, Caret(false), "&"),
        new("js", CodePattern.Files(".js", ".mjs", ".cjs", ".ts", ".mts", ".cts"), Syntax.CLike, Caret(false), "&"),
        new("c", CodePattern.Files(".c"), Syntax.CLike, Caret(false), "&"),
        new("cpp", CodePattern.Files(".cpp", ".cc", ".cxx", ".c++"), Syntax.CLike, Caret(false), "&"),

        // Go's ^ binds as - does, so n ^ n - 1 is (n ^ n) - 1: only n ^ (n - 1) is read.
        new("go", CodePattern.Files(".go"), Syntax.CLike, Caret(true), "&"),
        new("rust", CodePattern.Files(".rs"), Syntax.CLike, Caret(false), "&"),
        new("ruby", CodePattern.Files(".rb"), Syntax.Python, Caret(false), "&"),
        new("php", CodePattern.Files(".php"), Syntax.CLike, Caret(false), "&"),
        new("perl", CodePattern.Files(".pl", ".pm"), Syntax.Perl, Caret(false), "&"),
        new("dart", CodePattern.Files(".dart"), Syntax.CLike, Caret(false), "&"),
        new("scala", CodePattern.Files(".scala", ".sc"), Syntax.Scala, Caret(false), "&"),
        new("powershell", CodePattern.Files(".ps1"), Syntax.Python,
            [new($@"(?<![\w$@.])(?<number>\$[A-Za-z_]\w*)\s*=\s*\(?\s*\$[A-Za-z_]\w*\s*(?<xor>-bxor)\s+\(\s*(?<again>\$[A-Za-z_]\w*)\s*-\s*1\s*\)\s*\)?\s*$", RegexOptions.IgnoreCase)],
            "-band", IgnoreCase: true),
        new("lua", CodePattern.Files(".lua"), Syntax.Lua,
            [new($@"(?<![\w.])(?<number>[A-Za-z_]\w*)\s*=\s*\(?\s*[A-Za-z_]\w*\s*(?<xor>~)(?!=)\s*{LessOne}\s*\)?\s*$")], "&"),
        new("fortran", CodePattern.Files(".f90", ".f95", ".f03", ".f08", ".f"), Syntax.Fortran,
            [new(@"(?<![\w%])(?<number>[A-Za-z_]\w*)\s*=\s*(?<xor>ieor)\s*\(\s*[A-Za-z_]\w*\s*,\s*(?<again>[A-Za-z_]\w*)\s*-\s*1\s*\)\s*$", RegexOptions.IgnoreCase)],
            "iand", IgnoreCase: true),
        new("fsharp", CodePattern.Files(".fs", ".fsx"), Syntax.FSharp,
            [new(@"\(\s*(?<number>[A-Za-z_]\w*)\s*(?<xor>\^\^\^)\s*\(\s*(?<again>[A-Za-z_]\w*)\s*-\s*1\s*\)\s*\)")], "&&&", Recursive: true),
        new("ocaml", CodePattern.Files(".ml"), Syntax.OCaml,
            [new(@"\(\s*(?<number>[A-Za-z_]\w*)\s*(?<xor>lxor)\s*\(\s*(?<again>[A-Za-z_]\w*)\s*-\s*1\s*\)\s*\)")], "land", Recursive: true),
        new("elixir", CodePattern.Files(".ex", ".exs"), Syntax.Python,
            [new(@"(?<![\w.])(?:Bitwise\.)?(?<xor>bxor)\s*\(\s*(?<number>[a-z_]\w*)\s*,\s*(?<again>[a-z_]\w*)\s*-\s*1\s*\)"),
             new(@"(?<![\w.])(?<number>[a-z_]\w*)\s*(?<xor>\^\^\^)\s*\(\s*(?<again>[a-z_]\w*)\s*-\s*1\s*\)")], "band", Recursive: true),
        new("erlang", CodePattern.Files(".erl"), Syntax.Erlang,
            [new(@"(?<![\w.])(?<number>[A-Z_]\w*)\s+(?<xor>bxor)\s+\(\s*(?<again>[A-Z_]\w*)\s*-\s*1\s*\)")], "band", Recursive: true),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        .. Dialects.Select(dialect => new CodePattern($"logic-{dialect.Language}-xor-instead-of-and", dialect.Files, dialect.Syntax,
            (id, source, masked) => XorInsteadOfAnd(id, source, masked, dialect), Severity.Error, Confidence.Likely)),
    ];

    /// <summary>What every way of writing xor has in it: ^, ~, bxor, ieor, lxor - a file without one has nothing to read.</summary>
    [GeneratedRegex(@"\^|~|\bbxor\b|\bieor\b|\blxor\b", RegexOptions.IgnoreCase)]
    private static partial Regex XorWritten();

    private static IEnumerable<LogicFinding> XorInsteadOfAnd(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        if (!masked.Any(line => XorWritten().IsMatch(line))) yield break;
        var options = dialect.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
        var names = dialect.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        for (var line = 0; line < masked.Count; line++)
        {
            // Only a line with both an xor and a 1 taken away - C#'s ranges, items[..^1], have the one without the other.
            if (!masked[line].Contains('-') || !masked[line].Contains('1') || !XorWritten().IsMatch(masked[line])) continue;
            foreach (var form in dialect.Forms)
            {
                if (form.Match(masked[line]) is not { Success: true } written) continue;
                var number = written.Groups["number"].Value;
                if (!string.Equals(number, written.Groups["again"].Value, names)) continue;

                // n ^ (n - 1) given back to n: the name on the other side of the xor is n too.
                if (!dialect.Recursive && !Regex.IsMatch(written.Value, $@"(?<![\w$@.]){Regex.Escape(number)}(?!\w).*(?<![\w$@.]){Regex.Escape(number)}(?!\w).*(?<![\w$@.]){Regex.Escape(number)}(?!\w)|(?<![\w$@.]){Regex.Escape(number)}\s*\^=", options)) continue;

                if (dialect.Recursive ? !StopsAtZero(masked, line, written, number, options) : !LoopsUntilZero(masked, line, number, options)) continue;

                var xor = written.Groups["xor"];
                var text = source.Lines[line];
                var and = dialect.Language == "fortran" && xor.Value == xor.Value.ToUpperInvariant() ? dialect.And.ToUpperInvariant() : dialect.And;
                var changed = text[..xor.Index] + and + text[(xor.Index + xor.Length)..];
                var told = $"Line {line + 1} takes the lowest set bit away from `{number}` with `{xor.Value}` - but `{number} - 1` has that bit cleared " +
                           $"and every bit below it set, so `{xor.Value}` gives those bits instead, which is never 0 once `{number}` is above 0: 6 " +
                           $"becomes 3, then 1, and stays 1. Waiting for `{number}` to reach 0, it never ends. `{and}` clears the lowest set bit, so " +
                           "it runs once for each set bit.";
                yield return new LogicFinding(id, line + 1, told,
                    LocalFix.ReplaceLine(id, $"Clear the lowest set bit with and: {and}",
                        $"`{number} {and} ({number} - 1)` is `{number}` with its lowest set bit cleared - it reaches 0 after as many steps as `{number}` has set bits.",
                        source.Path, line + 1, changed));
                break;
            }
        }
    }

    /// <summary>
    /// Whether the line is in a loop whose test asks the number to be 0 or not: while n, while (n != 0), while n &gt; 0,
    /// until n.zero?, for n != 0 {, for (; n != 0; ...), do while (n /= 0), while ($n -ne 0).
    /// </summary>
    private static bool LoopsUntilZero(IReadOnlyList<string> masked, int line, string number, RegexOptions options)
    {
        if (Reaching.InnermostLoop(masked, 0, line) is not { } loop) return false;
        var name = Regex.Escape(number.TrimStart('$', '@'));
        var heading = masked[loop.Heading];
        return Regex.IsMatch(heading,
            $@"\b(?:while|until|for)\b\s*\(?\s*(?:;\s*)?!?\s*[$@]?{name}\s*(?:\)|:|\{{|$|\bdo\b|(?:!==?|~=|/=|>|==|<>|\.ne\.|\.gt\.|-ne|-gt)\s*0\b|\.zero\?)|\bwhile\s*\(\s*[$@]?{name}\s*(?:!=|/=|>|\.ne\.|\.gt\.)\s*0\s*\)", options);
    }

    /// <summary>
    /// Whether n xor (n - 1) is given in n's place to the function the line is in - count(N bxor (N - 1), Count + 1),
    /// countBits (n ^^^ (n - 1)) (count + 1) - and the file stops it when n is 0: if n = 0, count(0, Count) -&gt; Count.
    /// </summary>
    private static bool StopsAtZero(IReadOnlyList<string> masked, int line, Match written, string number, RegexOptions options)
    {
        var before = masked[line][..written.Index];
        if (Regex.Match(before, @"(?<function>[A-Za-z_]\w*)\s*\(?\s*$") is not { Success: true } called) return false;
        var function = Regex.Escape(called.Groups["function"].Value);
        var writes = masked.Any(text => Regex.IsMatch(text, $@"^\s*(?:let\s+(?:rec\s+)?|and\s+|defp?\s+)?{function}\b"));
        var name = Regex.Escape(number);
        var stops = masked.Any(text => Regex.IsMatch(text, $@"(?<![\w.])(?:if\s+)?{name}\s*==?\s*0\b") ||
                                       Regex.IsMatch(text, $@"^\s*(?:defp?\s+)?{function}\s*\(\s*0\s*[,)]"));
        return writes && stops;
    }
}
