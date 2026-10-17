using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// ^ written as a power - side ^ 2 for side squared. In each language here ^ is exclusive or, which compares two whole
/// numbers bit by bit: with side = 5, side ^ 2 printed 7, not 25, in C, C++, Java, C#, JavaScript, Python, Go, Rust, Dart,
/// Ruby, PHP and Perl, and gcc and g++ -Wall -Wextra, javac -Xlint:all, the C# analyzers at latest-all, go vet, rustc,
/// dart analyze, ruby -w, php -l and perl -w said nothing of it. gcc does warn of 2 ^ 8 - two plain numbers - so only a
/// name, or a bracketed sum, is taken here as the number being raised. Lua's ^ is a power, so Lua is not one of them.
/// CaretAsPowerLiveTests runs each mistake, and its change.
/// </summary>
public static partial class CaretAsPowerPatterns
{
    private static readonly IReadOnlySet<string> Braced = CodePattern.Files(".c", ".cpp", ".cc", ".cxx", ".c++", ".java", ".cs", ".js", ".mjs", ".cjs");

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        Multiplied("logic-caret-as-power", Braced),
        Multiplied("logic-go-caret-as-power", CodePattern.Files(".go")),
        Multiplied("logic-rust-caret-as-power", CodePattern.Files(".rs")),
        Multiplied("logic-dart-caret-as-power", CodePattern.Files(".dart")),
        Raised("logic-python-caret-as-power", CodePattern.Files(".py", ".pyw"), Syntax.Python, NamedBase()),
        Raised("logic-ruby-caret-as-power", CodePattern.Files(".rb"), Syntax.Python, NamedBase()),
        Raised("logic-php-caret-as-power", CodePattern.Files(".php"), Syntax.CLike, DollarBase()),
        Raised("logic-perl-caret-as-power", CodePattern.Files(".pl", ".pm"), Syntax.Perl, DollarBase()),
    ];

    /// <summary>The languages with no power operator, where the change multiplies the number by itself.</summary>
    private static CodePattern Multiplied(string id, IReadOnlySet<string> extensions) =>
        new(id, extensions, Syntax.CLike, (patternId, source, masked) => CaretAsPower(patternId, source, masked, NamedBase(), MultipliedChange),
            Severity.Error, Confidence.Likely);

    /// <summary>The languages whose power is **, where the change raises the number with it.</summary>
    private static CodePattern Raised(string id, IReadOnlySet<string> extensions, Syntax syntax, Regex caret) =>
        new(id, extensions, syntax, (patternId, source, masked) => CaretAsPower(patternId, source, masked, caret, RaisedChange),
            Severity.Error, Confidence.Likely);

    /// <summary>side ^ 2, rect.width ^ 2, marks[i] ^ 3, (a + b) ^ 2 - a name, or a bracketed sum, ^ 2 or 3.</summary>
    [GeneratedRegex(@"(?<![\w.$])(?<base>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*(?:\[[^\[\]]*\])?|\([^()]*\))\s*\^\s*(?<exponent>[23])(?![\w.])")]
    private static partial Regex NamedBase();

    /// <summary>
    /// $side ^ 2, $marks[0] ^ 3, ($a + $b) ^ 2 - PHP's and Perl's names start with $ - and what they hold: PHP's
    /// $shape->side, and Perl's $shape{side} and $shape->{side}.
    /// </summary>
    [GeneratedRegex(@"(?<![\w$])(?<base>\$[A-Za-z_]\w*(?:->[A-Za-z_]\w*|(?:->)?(?:\[[^\[\]]*\]|\{[^{}]*\}))*|\([^()]*\))\s*\^\s*(?<exponent>[23])(?![\w.])")]
    private static partial Regex DollarBase();

    private static (string Title, string Explanation, string Code) MultipliedChange(string number, int exponent)
    {
        var multiplied = string.Join(" * ", Enumerable.Repeat(number, exponent));
        return ($"Multiply it by itself: {multiplied}", $"{multiplied} is {number} {PowerName(exponent)}.", multiplied);
    }

    private static (string Title, string Explanation, string Code) RaisedChange(string number, int exponent) =>
        ($"Raise it with **: {number} ** {exponent}", $"** is the power: {number} ** {exponent} is {number} {PowerName(exponent)}.", $"{number} ** {exponent}");

    private static string PowerName(int exponent) => exponent == 2 ? "squared" : "cubed";

    /// <summary>
    /// A name raised with ^ to 2 or 3: ^ compares the two numbers' bits, keeping those that differ, so 5 ^ 2 - 101 and 010
    /// in binary - is 111, which is 7.
    /// </summary>
    private static IEnumerable<LogicFinding> CaretAsPower(
        string id, SourceFile source, IReadOnlyList<string> masked, Regex caret, Func<string, int, (string Title, string Explanation, string Code)> change)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (caret.Match(masked[index]) is not { Success: true } power) continue;

            var line = source.Lines[index];
            var number = line.Substring(power.Groups["base"].Index, power.Groups["base"].Length);
            var exponent = int.Parse(power.Groups["exponent"].Value);
            var (title, explanation, code) = change(number, exponent);

            yield return new LogicFinding(id, index + 1,
                $"^ is not a power here but exclusive or, which compares two whole numbers bit by bit - so {number} ^ {exponent} is not " +
                $"{number} {PowerName(exponent)}: were {number} 5, it would be {5 ^ exponent}, not {(int)Math.Pow(5, exponent)}",
                LocalFix.ReplaceLine(id, title, explanation,
                    source.Path, index + 1, line[..power.Index] + code + line[(power.Index + power.Length)..]));
        }
    }
}
