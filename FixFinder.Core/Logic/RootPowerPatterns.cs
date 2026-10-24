using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A root written as a power of one over a number - side ** 1/2 for the square root of side. The power comes before the
/// division, so it is (side ** 1) / 2: half of side. With 16 it printed 8, not 4, in Python, Ruby, JavaScript, PHP, Perl and
/// Fortran, and so did Lua's 16^1/2 - and python -W error, ruby -w, node --check, php -l, perl -w, gfortran -Wall and luac -p
/// said nothing of it. The change raises it to the fraction itself: side ** 0.5, or side ** (1.0 / 3) for a cube root - with
/// 1.0, as Ruby's and Fortran's 1 / 3 is a whole number's division, and 0. RootPowerLiveTests runs each mistake, and its change.
/// </summary>
public static partial class RootPowerPatterns
{
    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        Rooted("logic-python-power-of-one-over", CodePattern.Files(".py", ".pyw"), Syntax.Python, NamedBase()),
        Rooted("logic-ruby-power-of-one-over", CodePattern.Files(".rb"), Syntax.Python, NamedBase()),
        Rooted("logic-js-power-of-one-over", CodePattern.Files(".js", ".mjs", ".cjs"), Syntax.CLike, NamedBase()),
        Rooted("logic-php-power-of-one-over", CodePattern.Files(".php"), Syntax.CLike, DollarBase()),
        Rooted("logic-perl-power-of-one-over", CodePattern.Files(".pl", ".pm"), Syntax.Perl, DollarBase()),
        Rooted("logic-fortran-power-of-one-over", CodePattern.Files(".f90", ".f95", ".f03", ".f08"), Syntax.Fortran, NamedBase()),
        Rooted("logic-lua-power-of-one-over", CodePattern.Files(".lua"), Syntax.Lua, LuaBase()),
        new CodePattern("logic-pow-whole-number-fraction", CodePattern.Files(".c", ".cpp", ".cc", ".cxx", ".c++", ".java", ".cs"), Syntax.CLike, PowWholeNumberFraction, Severity.Error, Confidence.Certain),
        new CodePattern("logic-go-pow-whole-number-fraction", CodePattern.Files(".go"), Syntax.CLike, PowWholeNumberFraction, Severity.Error, Confidence.Certain),
    ];

    /// <summary>
    /// <c>Math.pow(side, 1/2)</c> - and Math.Pow, pow, std::pow and math.Pow - given a power written as one whole number over
    /// another.
    /// </summary>
    [GeneratedRegex(@"(?<call>\bMath\.pow|\bMath\.Pow|\bmath\.Pow|\bstd::pow|(?<![\w.:])pow)\s*\((?<base>(?:[^(),]|\([^()]*\))+),\s*(?<fraction>(?<top>\d+)\s*/\s*(?<bottom>\d+))\s*\)")]
    private static partial Regex PowerOfWholeNumbers();

    /// <summary>
    /// A power of one whole number over another: the division comes first, and in Java, C#, C, C++ and Go two whole numbers
    /// divide to a whole number - 1/2 is 0 - so Math.pow(16, 1/2) printed 1 in each, with nothing said by javac -Xlint:all,
    /// gcc -Wall -Wextra or go vet. The change writes the top as a decimal, 1.0 / 2, which printed 4.
    /// </summary>
    private static IEnumerable<LogicFinding> PowWholeNumberFraction(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match power in PowerOfWholeNumbers().Matches(masked[index]))
            {
                var top = long.Parse(power.Groups["top"].Value);
                var bottom = long.Parse(power.Groups["bottom"].Value);
                if (bottom == 0 || top % bottom == 0) continue;

                var line = source.Lines[index];
                var call = power.Groups["call"].Value;
                var number = power.Groups["base"].Value.Trim();
                var fraction = power.Groups["fraction"];
                var whole = top / bottom;

                yield return new LogicFinding(id, index + 1,
                    $"{top}/{bottom} divides two whole numbers, which throws the fraction away - it is {whole} - so {call}({number}, {top}/{bottom}) is " +
                    $"{number} to the power {whole}{whole switch { 0 => $": 1, whatever {number} is", 1 => $": {number} itself", _ => "" }}",
                    LocalFix.ReplaceLine(id, $"Divide as decimals: {top}.0 / {bottom}",
                        $"With a decimal point, {top}.0 / {bottom} is worked out as decimals and keeps its fraction - " +
                        $"{((double)top / bottom).ToString(System.Globalization.CultureInfo.InvariantCulture)}.",
                        source.Path, index + 1, line[..fraction.Index] + $"{top}.0 / {bottom}" + line[(fraction.Index + fraction.Length)..]));
            }
        }
    }

    private static CodePattern Rooted(string id, IReadOnlySet<string> extensions, Syntax syntax, Regex rooted) =>
        new(id, extensions, syntax, (patternId, source, masked) => PowerOfOneOver(patternId, source, masked, rooted), Severity.Error, Confidence.Likely);

    /// <summary>
    /// side ** 1/2, 16 ** 1 / 3, marks[0] ** 1/2, (a + b) ** 1/2 - a name, a number or a bracketed sum, raised to 1 and divided
    /// by a whole number from 2 to 9.
    /// </summary>
    [GeneratedRegex(@"(?<![\w.$])(?<base>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*(?:\[[^\[\]]*\])?|\d+(?:\.\d+)?|\([^()]*\))\s*(?<power>\*\*)\s*1\s*/\s*(?<root>[2-9])(?:\.0)?(?![\w.(])")]
    private static partial Regex NamedBase();

    /// <summary>The same, with PHP's and Perl's $ names: $side ** 1/2, $marks[0] ** 1/2.</summary>
    [GeneratedRegex(@"(?<![\w$])(?<base>\$[A-Za-z_]\w*(?:->[A-Za-z_]\w*|(?:->)?(?:\[[^\[\]]*\]|\{[^{}]*\}))*|\d+(?:\.\d+)?|\([^()]*\))\s*(?<power>\*\*)\s*1\s*/\s*(?<root>[2-9])(?:\.0)?(?![\w.(])")]
    private static partial Regex DollarBase();

    /// <summary>Lua's power is ^: side^1/2.</summary>
    [GeneratedRegex(@"(?<![\w.])(?<base>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*(?:\[[^\[\]]*\])?|\d+(?:\.\d+)?|\([^()]*\))\s*(?<power>\^)\s*1\s*/\s*(?<root>[2-9])(?:\.0)?(?![\w.(])")]
    private static partial Regex LuaBase();

    private static string RootName(int root) => root switch { 2 => "square root", 3 => "cube root", _ => $"root {root}" };

    /// <summary>
    /// A number raised to 1 and then divided: the power comes before the division, so side ** 1/2 is side ** 1 - side itself -
    /// divided by 2. The change raises it to the fraction, in brackets where it is not 0.5.
    /// </summary>
    private static IEnumerable<LogicFinding> PowerOfOneOver(string id, SourceFile source, IReadOnlyList<string> masked, Regex rooted)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (rooted.Match(masked[index]) is not { Success: true } power) continue;

            var line = source.Lines[index];
            var number = line.Substring(power.Groups["base"].Index, power.Groups["base"].Length);
            var raise = power.Groups["power"].Value;
            var root = int.Parse(power.Groups["root"].Value);
            var fraction = root == 2 ? "0.5" : $"(1.0 / {root})";
            var written = line.Substring(power.Index, power.Length);

            yield return new LogicFinding(id, index + 1,
                $"{raise} comes before /, so {written} is {number} {raise} 1 - {number} itself - divided by {root}: not the {RootName(root)} " +
                $"of {number} but {(root == 2 ? "half" : $"a {Ordinal(root)}")} of it",
                LocalFix.ReplaceLine(id, $"Raise it to the fraction: {number} {raise} {fraction}",
                    $"{number} {raise} {fraction} is the {RootName(root)} of {number}" +
                    (root == 2 ? "." : " - with 1.0, as in some languages 1 / 3 divides whole numbers, and is 0."),
                    source.Path, index + 1, line[..power.Index] + $"{number} {raise} {fraction}" + line[(power.Index + power.Length)..]));
        }
    }

    private static string Ordinal(int root) => root switch { 3 => "third", 4 => "quarter", 5 => "fifth", 6 => "sixth", 7 => "seventh", 8 => "eighth", _ => "ninth" };
}
