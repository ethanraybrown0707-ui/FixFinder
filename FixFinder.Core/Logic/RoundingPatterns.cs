using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// Rounding to two places that loses them - run with javac, which said nothing: for a price of 12.345,
/// double rounded = Math.round(price * 100) / 100 printed 12.0, as Math.round gives a whole number, a long, and a long divided
/// by the whole number 100 is whole-number division. Math.round(price * 100) / 100.0 printed 12.35.
/// </summary>
public static partial class RoundingPatterns
{
    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        new CodePattern("logic-java-round-divided-as-whole-number", CodePattern.Files(".java"), Syntax.CLike, JavaRoundDivided,
            Severity.Error, Confidence.Certain),
    ];

    /// <summary><c>Math.round(price * 100) / 100</c> - Math.round's whole number divided by a whole number written in the code.</summary>
    [GeneratedRegex(@"\bMath\s*\.\s*round\s*\((?:[^()]|\((?:[^()]|\([^()]*\))*\))*\)\s*/\s*(?<divisor>\d+)(?![\w.])")]
    private static partial Regex RoundDividedByWholeNumber();

    /// <summary>A double or float given a value - double rounded = - which says the result was meant to have a fraction.</summary>
    [GeneratedRegex(@"\b(?:double|float|Double|Float)\s+(?<name>[A-Za-z_]\w*)\s*[=;,)]")]
    private static partial Regex DecimalDeclared();

    /// <summary>
    /// Math.round(...) / 100 where the result is meant to keep its fraction - given to a double or a float, or printed: the
    /// division is of two whole numbers, so the places Math.round kept are lost. Writing the divisor as 100.0 divides as decimals.
    /// </summary>
    private static IEnumerable<LogicFinding> JavaRoundDivided(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var decimals = masked.SelectMany(code => DecimalDeclared().Matches(code)).Select(found => found.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);

        for (var index = 0; index < masked.Count; index++)
        {
            if (RoundDividedByWholeNumber().Match(masked[index]) is not { Success: true } division) continue;

            var code = masked[index];
            var keepsAFraction = Regex.IsMatch(code, @"\bSystem\s*\.\s*out\s*\.\s*print") ||
                                 Regex.Match(code, @"^\s*(?:(?:double|float|Double|Float)\s+)?(?<name>[A-Za-z_]\w*)\s*=(?!=)") is { Success: true } assigned &&
                                 decimals.Contains(assigned.Groups["name"].Value);
            if (!keepsAFraction) continue;

            var line = source.Lines[index];
            var divisor = division.Groups["divisor"];

            yield return new LogicFinding(id, index + 1,
                $"Math.round gives a whole number - a long - and a whole number divided by {divisor.Value} is whole-number division, so the " +
                $"places Math.round kept are cut off: for 12.345, Math.round(price * 100) / 100 is 12.0, not 12.35",
                LocalFix.ReplaceLine(id, $"Divide as decimals: / {divisor.Value}.0",
                    $"{divisor.Value}.0 is a double, and a whole number divided by a double is a double - so the result keeps its fraction.",
                    source.Path, index + 1, line[..(divisor.Index + divisor.Length)] + ".0" + line[(divisor.Index + divisor.Length)..]));
        }
    }
}
