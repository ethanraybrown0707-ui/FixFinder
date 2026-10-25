using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// n % 2 == 1 asked to find an odd number. % - and mod, rem and Fortran's mod - keeps the sign of the number divided, so a
/// negative odd number leaves -1: -3 % 2 is -1, and -3 was taken for even. Each was run with the language's own toolchain,
/// with -3, and each printed even, with nothing said by gcc and g++ -Wall -Wextra, javac -Xlint:all, the C# compiler, go vet,
/// rustc, Scala CLI, gfortran -Wall, erlc +warn_all, Elixir, F# Interactive, PHP, Node.js or PowerShell. Python, Ruby, Lua,
/// Perl and Dart give -3 % 2 as 1, so their n % 2 == 1 is right, and they are not here. The change asks whether anything is
/// left over - n % 2 != 0 - which is true for every odd number. A number that cannot be negative - a length, a count, an
/// unsigned number, or a loop counter counting up from 0 or more - is left alone.
/// </summary>
public static partial class OddTestPatterns
{
    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        Odd("logic-odd-test-negative", CodePattern.Files(".c", ".cpp", ".cc", ".cxx", ".c++", ".java", ".cs", ".js", ".mjs", ".cjs"), Syntax.CLike, CLikeRemainder(), CLikeChange, "%"),
        Odd("logic-go-odd-test-negative", CodePattern.Files(".go"), Syntax.CLike, CLikeRemainder(), CLikeChange, "%"),
        Odd("logic-rust-odd-test-negative", CodePattern.Files(".rs"), Syntax.CLike, CLikeRemainder(), CLikeChange, "%"),
        Odd("logic-scala-odd-test-negative", CodePattern.Files(".scala", ".sc"), Syntax.Scala, CLikeRemainder(), CLikeChange, "%"),
        Odd("logic-php-odd-test-negative", CodePattern.Files(".php"), Syntax.CLike, CLikeRemainder(), CLikeChange, "%"),
        Odd("logic-fortran-odd-test-negative", CodePattern.Files(".f90", ".f95", ".f03", ".f08"), Syntax.Fortran, FortranRemainder(), FortranChange, "mod"),
        Odd("logic-erlang-odd-test-negative", CodePattern.Files(".erl"), Syntax.Erlang, ErlangRemainder(), ErlangChange, "rem"),
        Odd("logic-elixir-odd-test-negative", CodePattern.Files(".exs", ".ex"), Syntax.Python, ElixirRemainder(), CLikeChange, "rem"),
        Odd("logic-fsharp-odd-test-negative", CodePattern.Files(".fsx"), Syntax.FSharp, FSharpRemainder(), FSharpChange, "%"),
        Odd("logic-powershell-odd-test-negative", CodePattern.Files(".ps1"), Syntax.Python, PowerShellRemainder(), PowerShellChange, "%"),
    ];

    private static CodePattern Odd(string id, IReadOnlySet<string> extensions, Syntax syntax, Regex remainder, Func<string, string> change, string operation) =>
        new(id, extensions, syntax, (patternId, source, masked) => OddTest(patternId, source, masked, remainder, change, operation), Severity.Warning, Confidence.Likely);

    /// <summary><c>n % 2 == 1</c>, <c>n % 2 != 1</c>, <c>xs[i] % 2 === 1</c> - in C, C++, Java, C#, JavaScript, Go, Rust, Scala and PHP.</summary>
    [GeneratedRegex(@"(?<![\w.$\])])(?<number>\$?[A-Za-z_]\w*(?:\.[A-Za-z_]\w*|\[[^\]]+\])*|\([^()]+\))\s*%\s*2\s*(?<test>===?|!==?)\s*1(?!\w|\.\d)")]
    private static partial Regex CLikeRemainder();

    /// <summary><c>mod(n, 2) == 1</c>, <c>mod(n, 2) .eq. 1</c>, <c>/= 1</c> or <c>.ne. 1</c>, in any case.</summary>
    [GeneratedRegex(@"(?<![\w%])mod\s*\(\s*(?<number>[A-Za-z_]\w*(?:\([^()]*\))?)\s*,\s*2\s*\)\s*(?<test>==|/=|\.eq\.|\.ne\.)\s*1(?!\w|\.\d)", RegexOptions.IgnoreCase)]
    private static partial Regex FortranRemainder();

    /// <summary><c>N rem 2 == 1</c>, <c>=:= 1</c>, <c>/= 1</c> or <c>=/= 1</c>.</summary>
    [GeneratedRegex(@"(?<![\w])(?<number>[A-Z_]\w*|\([^()]+\))\s+rem\s+2\s*(?<test>=:=|=/=|==|/=)\s*1(?!\w|\.\d)")]
    private static partial Regex ErlangRemainder();

    /// <summary><c>rem(n, 2) == 1</c>, <c>=== 1</c>, <c>!= 1</c> or <c>!== 1</c>.</summary>
    [GeneratedRegex(@"(?<![\w.])rem\(\s*(?<number>[a-z_]\w*(?:\.\w+)*)\s*,\s*2\s*\)\s*(?<test>===?|!==?)\s*1(?!\w|\.\d)")]
    private static partial Regex ElixirRemainder();

    /// <summary><c>n % 2 = 1</c> or <c>n % 2 &lt;&gt; 1</c> - an = on its own, not part of &lt;=, &gt;= or ==.</summary>
    [GeneratedRegex(@"(?<![\w.\])])(?<number>[a-z_]\w*(?:\.[A-Za-z_]\w*)*|\([^()]+\))\s*%\s*2\s*(?<test>(?<![<>=!])=(?!=)|<>)\s*1(?!\w|\.\d)")]
    private static partial Regex FSharpRemainder();

    /// <summary><c>$n % 2 -eq 1</c> or <c>-ne 1</c> - and -ceq, -ieq, -cne and -ine - in any case.</summary>
    [GeneratedRegex(@"(?<number>\$[\w:]+(?:\.\w+|\[[^\]]+\])*|\([^()]+\))\s*%\s*2\s*(?<test>-[ci]?(?:eq|ne))\s+1(?![\w.])", RegexOptions.IgnoreCase)]
    private static partial Regex PowerShellRemainder();

    private static string CLikeChange(string test) => test switch { "==" => "!= 0", "===" => "!== 0", "!=" => "== 0", _ => "=== 0" };

    private static string FortranChange(string test) => test.ToLowerInvariant() switch { "==" => "/= 0", ".eq." => ".ne. 0", "/=" => "== 0", _ => ".eq. 0" };

    private static string ErlangChange(string test) => test switch { "==" => "/= 0", "=:=" => "=/= 0", "/=" => "== 0", _ => "=:= 0" };

    private static string FSharpChange(string test) => test == "=" ? "<> 0" : "= 0";

    private static string PowerShellChange(string test) => test.ToLowerInvariant().EndsWith("eq", StringComparison.Ordinal) ? test[..^2] + "ne 0" : test[..^2] + "eq 0";

    /// <summary>A length or a count - xs.length, xs.size(), xs.Count, xs.len(), len(xs), length(xs), count($xs) - or an absolute value.</summary>
    [GeneratedRegex(@"\.(?:length|size|count|len)(?:\(\))?$|^(?:len|length|count|size|abs|Math\.abs|math\.Abs)\(|\.abs\(\)$|^\(.*\.(?:length|size|count)(?:\(\))?\)$", RegexOptions.IgnoreCase)]
    private static partial Regex NeverNegativeValue();

    /// <summary>
    /// Whether <paramref name="name"/> cannot be negative here: declared unsigned - unsigned, size_t, uint, ulong, usize, u32 -
    /// or a loop counter that starts at 0 or more and counts up: for (int i = 0; ...; i++), for i := 0, for i in 0..n, for
    /// i in 1 to n, do i = 1, n, foreach ($i in 1..10), lists:seq(1, N), or the position a Go range gives.
    /// </summary>
    private static bool NeverNegative(string name, IReadOnlyList<string> masked)
    {
        var word = Regex.Escape(name.TrimStart('$'));
        var unsigned = new Regex($@"\b(?:unsigned(?:\s+(?:int|long|short|char))?|size_t|uint\d*_t|uint|ulong|ushort|usize|u8|u16|u32|u64|u128)\b[\s*]*(?:[A-Za-z_]\w*\s*(?:=[^,;]*)?,\s*)*{word}\b|\b{word}\s*:\s*(?:usize|u8|u16|u32|u64|u128)\b|\bvar\s+{word}\s+uint");
        var countsUp = new Regex(
            $@"\bfor\s*\(\s*(?:\w+\s+)?\$?{word}\s*=\s*\d+\s*;[^;]*;\s*(?:\$?{word}\s*\+\+|\+\+\s*\$?{word}|\$?{word}\s*\+=\s*\d+)" +
            $@"|\bfor\s+{word}\s*:=\s*\d+\s*;[^;]*;\s*{word}\s*(?:\+\+|\+=\s*\d+)" +
            $@"|\bfor\s+{word}\s*(?:,\s*\w+\s*)?:=\s*range\b|\bfor\s+(?:\(\s*)?{word}\s*,\s*\w+\s*(?:\)\s*)?in\b" +
            $@"|\bfor\s+(?:\(\s*)?(?:val\s+)?{word}\s+(?:in|<-)\s+\(?\s*\d+\s*(?:\.\.=?|\.\.\.|\s+to\s+|\s+until\s+)" +
            $@"|\bdo\s+{word}\s*=\s*\d+\s*," +
            $@"|\bforeach\s*\(\s*\$?{word}\s+in\s+\d+\s*\.\." +
            $@"|<-\s*lists:seq\(\s*\d+\s*,|\b{word}\s*<-\s*\d+\.\." +
            $@"|\bfor\s+{word}\s*=\s*\d+\s+to\b|\bfor\s+{word}\s+in\s+\d+\s*\.\.",
            RegexOptions.IgnoreCase);
        return masked.Any(line => unsigned.IsMatch(line) || countsUp.IsMatch(line));
    }

    private static IEnumerable<LogicFinding> OddTest(string id, SourceFile source, IReadOnlyList<string> masked, Regex remainder, Func<string, string> change, string operation)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match oddTest in remainder.Matches(masked[index]))
            {
                var number = oddTest.Groups["number"].Value;
                if (NeverNegativeValue().IsMatch(number) || (Regex.IsMatch(number, @"^\$?[A-Za-z_]\w*$") && NeverNegative(number, masked))) continue;

                var line = source.Lines[index];
                var test = oddTest.Groups["test"];
                var asked = line.Substring(oddTest.Index, oddTest.Length);
                var changed = line.Substring(oddTest.Index, test.Index - oddTest.Index) + change(test.Value);
                var askingOdd = !Regex.IsMatch(test.Value, "^(?:!=|!==|/=|=/=|<>|\\.ne\\.|-[ci]?ne)$", RegexOptions.IgnoreCase);
                var example = operation switch { "mod" => "mod(-3, 2) is -1", "rem" when id.Contains("erlang") => "-3 rem 2 is -1", "rem" => "rem(-3, 2) is -1", _ => "-3 % 2 is -1" };

                yield return new LogicFinding(id, index + 1,
                    $"{operation} keeps the sign of the number divided, so a negative odd number leaves -1, not 1 - {example} - and {asked} is " +
                    $"{(askingOdd ? "false" : "true")} for it: -3 is taken for even",
                    LocalFix.ReplaceLine(id, $"Ask whether anything is left over: {changed}",
                        "An odd number leaves something over - 1, or -1 when it is negative - and an even one leaves 0, so asking about 0 is right " +
                        "for every number.",
                        source.Path, index + 1, line[..oddTest.Index] + changed + line[(oddTest.Index + oddTest.Length)..]));
            }
        }
    }
}
