using System.Numerics;
using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// Whole numbers written in the code whose sum or product is too big for an int - run with javac, Scala 3.8.4 and Scala
/// 2.13.18, which said nothing: long millisPerYear = 365 * 24 * 60 * 60 * 1000 printed 1471228928, not 31536000000, as the
/// numbers are ints and their product wraps round before it is stored in the long; and 50000 * 50000 printed -1794967296.
/// 365L * 24 * ... printed 31536000000. (gcc warns of it in C and C++, with -Woverflow; C# refuses it, CS0220; and Go, whose
/// constants are exact, and Rust, whose numbers take the type they are stored as, both printed 31536000000.)
/// </summary>
public static partial class ConstantOverflowPatterns
{
    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        new CodePattern("logic-java-constant-overflow", CodePattern.Files(".java"), Syntax.CLike,
            (id, source, masked) => ConstantOverflow(id, source, masked, "int", "long"), Severity.Error, Confidence.Certain),
        new CodePattern("logic-scala-constant-overflow", CodePattern.Files(".scala", ".sc"), Syntax.Scala,
            (id, source, masked) => ConstantOverflow(id, source, masked, "Int", "Long"), Severity.Error, Confidence.Certain),
    ];

    /// <summary><c>365 * 24 * 60 * 60 * 1000</c> - whole numbers, with nothing after them to make them longs, joined by * + or -.</summary>
    [GeneratedRegex(@"(?<![\w.$])(?<first>\d[\d_]*)(?:\s*[*+\-]\s*\d[\d_]*)+(?![\w.])")]
    private static partial Regex WholeNumberArithmetic();

    /// <summary>The exact value of whole numbers joined by * + and -, multiplying first - worked out with no limit on its size.</summary>
    private static BigInteger ExactValue(string arithmetic)
    {
        var total = BigInteger.Zero;
        foreach (Match term in Regex.Matches(arithmetic, @"(?<sign>^|[+\-])\s*(?<product>\d[\d_]*(?:\s*\*\s*\d[\d_]*)*)"))
        {
            var product = term.Groups["product"].Value.Split('*').Aggregate(BigInteger.One, (sofar, factor) => sofar * BigInteger.Parse(factor.Trim().Replace("_", "")));
            total += term.Groups["sign"].Value == "-" ? -product : product;
        }

        return total;
    }

    /// <summary>What an int holds of a whole number too big for it: the number wrapped round, as Java and Scala work it out.</summary>
    private static int Wrapped(BigInteger exact) => unchecked((int)(uint)(exact & uint.MaxValue));

    /// <summary>
    /// Whole numbers whose exact sum or product is outside an int's range: they are ints, so the arithmetic wraps round before
    /// the result goes anywhere - into a long too. The change makes the first number a long, so the arithmetic is done in
    /// longs, and makes a variable declared as an int a long, so the result fits.
    /// </summary>
    private static IEnumerable<LogicFinding> ConstantOverflow(string id, SourceFile source, IReadOnlyList<string> masked, string intType, string longType)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match arithmetic in WholeNumberArithmetic().Matches(masked[index]))
            {
                var exact = ExactValue(arithmetic.Value);
                if (exact >= int.MinValue && exact <= int.MaxValue) continue;

                var line = source.Lines[index];
                var first = arithmetic.Groups["first"];
                var changed = line[..(first.Index + first.Length)] + "L" + line[(first.Index + first.Length)..];
                changed = Regex.Replace(changed, $@"^(\s*)(?:{intType}\s+(?=[A-Za-z_]\w*\s*=)|(?<scala>(?:val|var)\s+[A-Za-z_]\w*\s*:\s*){intType}\b)",
                    match => match.Groups["scala"].Success ? match.Groups[1].Value + match.Groups["scala"].Value + longType : match.Groups[1].Value + longType + " ");

                yield return new LogicFinding(id, index + 1,
                    FormattableString.Invariant(
                        $"{arithmetic.Value} is worked out in {intType}s, whose largest is 2,147,483,647 - and its value, {exact:N0}, is outside that, so ") +
                    FormattableString.Invariant($"it wraps round to {Wrapped(exact):N0} before it is stored anywhere"),
                    LocalFix.ReplaceLine(id, $"Work it out in {longType}s: {first.Value}L",
                        $"The L makes {first.Value} a {longType}, so the arithmetic is done in {longType}s, which hold numbers up to " +
                        $"9,223,372,036,854,775,807.",
                        source.Path, index + 1, changed));
                break;
            }
        }
    }
}
