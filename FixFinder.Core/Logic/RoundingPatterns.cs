using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// Rounding to two places that loses them, as each compiler built it without a word. For a price of 12.345, Java's
/// double rounded = Math.round(price * 100) / 100 printed 12.0, as Math.round gives a whole number, a long, and a long divided
/// by the whole number 100 is whole-number division; and rounding by hand - (int)(price * 100 + 0.5) / 100 - printed 12.00 in C,
/// 12 in C++ and C#, and 12.0 in Java. With / 100.0 each printed 12.35.
/// </summary>
public static partial class RoundingPatterns
{
    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        new CodePattern("logic-java-round-divided-as-whole-number", CodePattern.Files(".java"), Syntax.CLike,
            (id, source, masked) => WholeNumberDivided(id, source, masked, RoundDividedByWholeNumber(), printingKeepsAFraction: true),
            Severity.Error, Confidence.Certain),
        new CodePattern("logic-cast-divided-as-whole-number", CodePattern.Files(".c", ".cpp", ".cc", ".cxx", ".c++", ".java", ".cs"), Syntax.CLike,
            (id, source, masked) => WholeNumberDivided(id, source, masked, CastDividedByWholeNumber(), printingKeepsAFraction: false),
            Severity.Error, Confidence.Certain),
    ];

    /// <summary><c>Math.round(price * 100) / 100</c> - Math.round's whole number divided by a whole number written in the code.</summary>
    [GeneratedRegex(@"\bMath\s*\.\s*round\s*\((?:[^()]|\((?:[^()]|\([^()]*\))*\))*\)\s*/\s*(?<divisor>\d+)(?![\w.])")]
    private static partial Regex RoundDividedByWholeNumber();

    /// <summary><c>(int)(price * 100 + 0.5) / 100</c> - a value cast to a whole number, divided by a whole number written in the code.</summary>
    [GeneratedRegex(@"\(\s*(?<cast>int|long)\s*\)\s*\((?:[^()]|\((?:[^()]|\([^()]*\))*\))*\)\s*/\s*(?<divisor>\d+)(?![\w.])")]
    private static partial Regex CastDividedByWholeNumber();

    /// <summary>A double or float given a value - double rounded = - which says the result was meant to have a fraction.</summary>
    [GeneratedRegex(@"\b(?:double|float|Double|Float)\s+(?<name>[A-Za-z_]\w*)\s*[=;,)]")]
    private static partial Regex DecimalDeclared();

    /// <summary>
    /// A whole number divided by a whole number where the result is meant to keep its fraction - given to a double or a float,
    /// or, for Java's Math.round, printed: the division is of two whole numbers, so the places kept before it are lost. Writing
    /// the divisor as 100.0 divides as decimals. A whole number given to a long or an int may be whole on purpose, and is left alone.
    /// </summary>
    private static IEnumerable<LogicFinding> WholeNumberDivided(
        string id, SourceFile source, IReadOnlyList<string> masked, Regex division, bool printingKeepsAFraction)
    {
        var decimals = masked.SelectMany(code => DecimalDeclared().Matches(code)).Select(found => found.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);

        for (var index = 0; index < masked.Count; index++)
        {
            if (division.Match(masked[index]) is not { Success: true } divided) continue;

            var code = masked[index];
            var printed = printingKeepsAFraction && Regex.IsMatch(code, @"\bSystem\s*\.\s*out\s*\.\s*print");
            var givenToADecimal = Regex.Match(code, @"^\s*(?:(?:double|float|Double|Float)\s+)?(?<name>[A-Za-z_]\w*)\s*=(?!=)") is { Success: true } assigned &&
                                  decimals.Contains(assigned.Groups["name"].Value);
            if (!printed && !givenToADecimal) continue;

            var line = source.Lines[index];
            var divisor = divided.Groups["divisor"];
            var gives = divided.Groups["cast"].Success
                ? $"({divided.Groups["cast"].Value}) makes the value a whole number,"
                : "Math.round gives a whole number - a long -";

            yield return new LogicFinding(id, index + 1,
                $"{gives} and a whole number divided by {divisor.Value} is whole-number division, so the places kept before it are cut " +
                "off: for 12.345, rounding to two places this way gives 12, not 12.35",
                LocalFix.ReplaceLine(id, $"Divide as decimals: / {divisor.Value}.0",
                    $"{divisor.Value}.0 is a double, and a whole number divided by a double is a double - so the result keeps its fraction.",
                    source.Path, index + 1, line[..(divisor.Index + divisor.Length)] + ".0" + line[(divisor.Index + divisor.Length)..]));
        }
    }
}
