using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>
/// <c>java.lang.ArithmeticException: / by zero</c> in a Scala program: a whole number divided by zero. The division is
/// given an answer for when the divisor is zero, as an if, which in Scala gives a value either way.
/// </summary>
public sealed class ScalaDivisionGuard : ILocalFixRule
{
    public string Id => "scala-division-guard";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!ScalaCode.Crashed(context.Error, "java.lang.ArithmeticException") || !(context.Error.Message ?? "").Contains("by zero")) return null;
        if (ScalaCode.Locate(context) is not { } at || Guards.GuardDivision(at.Line, Syntax.Scala, python: false) is not { } guarded) return null;

        return LocalFix.ReplaceLine(Id, "Give an answer for when the divisor is zero",
            "Dividing a whole number by zero has no answer, so the program stops with an ArithmeticException. Checking the divisor first and " +
            "giving 0 in that case keeps the program going - change the 0 to whatever an empty case should give.",
            at.Source.Path, at.Number, guarded);
    }
}

/// <summary>
/// An index past the end of an Array or a List in a Scala program, from a loop over <c>0 to xs.length</c> - which counts
/// up to and including xs.length, one past the last position. <c>until</c> stops one before it.
/// </summary>
public sealed class ScalaRangeUntil : ILocalFixRule
{
    public string Id => "scala-range-until";

    /// <summary>How far above the failing line a loop's heading is looked for.</summary>
    private const int LinesAboveLooked = 6;

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!ScalaCode.Crashed(context.Error, "java.lang.ArrayIndexOutOfBoundsException", "java.lang.IndexOutOfBoundsException", "java.lang.StringIndexOutOfBoundsException"))
            return null;
        if (ScalaCode.Locate(context) is not { } at) return null;

        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.Scala);

        for (var index = at.Number - 1; index >= Math.Max(0, at.Number - 1 - LinesAboveLooked); index--)
        {
            if (ScalaLoops.RangeToLength().Match(masked[index]) is not { Success: true } range) continue;

            return ScalaLoops.UntilFix(Id, at.Source, index + 1, range);
        }

        return null;
    }
}

/// <summary>The change both the crash rule and the logic check make to a range that runs one past the end, so the two are one fix.</summary>
internal static partial class ScalaLoops
{
    /// <summary><c>0 to xs.length</c> or <c>0 to xs.size</c> - and not <c>0 to xs.length - 1</c>, which stops in time.</summary>
    [GeneratedRegex(@"\b0\s+to\s+(?<collection>[A-Za-z_]\w*)\.(?<measure>length|size)\b(?!\s*-\s*1)")]
    public static partial Regex RangeToLength();

    public static LocalFix UntilFix(string id, SourceFile source, int lineNumber, Match range)
    {
        var line = source.Lines[lineNumber - 1];
        var counted = $"{range.Groups["collection"].Value}.{range.Groups["measure"].Value}";
        var to = range.Index + Regex.Match(range.Value, @"\bto\b").Index;
        var corrected = line[..to] + "until" + line[(to + "to".Length)..];

        return LocalFix.ReplaceLine(id, $"Count with 0 until {counted}",
            $"0 to {counted} counts up to and including {counted}, one past the last position - the positions run from 0 to one less than " +
            "the length. until stops one before it.",
            source.Path, lineNumber, corrected);
    }
}
