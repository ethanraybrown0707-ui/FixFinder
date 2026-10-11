using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>
/// A panic of "attempt to divide by zero": the one whole-number division on the line it names is given an answer for when
/// the divisor is zero, as an if - which in Rust gives a value either way.
/// </summary>
public sealed partial class RustDivisionGuard : ILocalFixRule
{
    public string Id => "rust-division-guard";

    /// <summary>A division of a name, a number, a .len() or a bracketed expression, by a name, a .len() or a bracketed expression.</summary>
    [GeneratedRegex(@"(?<dividend>[A-Za-z_][\w.]*(?:\(\))?|\d+|\([^()]*\))\s*/(?![/=*])\s*(?<divisor>\([^()]*\)|[A-Za-z_][\w.]*(?:\(\))?)")]
    private static partial Regex Division();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.LanguageId != "rust" || context.Error.ExceptionType != "panic" ||
            !(context.Error.Message ?? "").StartsWith("attempt to divide by zero", StringComparison.Ordinal))
            return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source || !RustProgram.IsRust(source.Path)) return null;
        if (source.Line(number) is not { } line || Division().Matches(CodeText.Mask(line, Syntax.CLike)) is not [var division]) return null;

        var divisor = line.Substring(division.Groups["divisor"].Index, division.Groups["divisor"].Length);
        var expression = line.Substring(division.Index, division.Length);
        var corrected = line[..division.Index] + $"if {divisor} == 0 {{ 0 }} else {{ {expression} }}" + line[(division.Index + division.Length)..];

        return LocalFix.ReplaceLine(Id, "Give an answer for when the divisor is zero",
            "Dividing a whole number by zero has no answer, so Rust stops the program. Checking the divisor first and giving 0 in that " +
            "case keeps the program going - change the 0 to whatever an empty case should give.",
            source.Path, number, corrected);
    }
}

/// <summary>A panic of "index out of bounds" from a loop over <c>0..=v.len()</c>, which counts one past the end.</summary>
public sealed class RustRangeToLength : ILocalFixRule
{
    public string Id => "rust-range-to-length";

    /// <summary>How far above the panic's line a loop's heading is looked for.</summary>
    private const int LinesAboveLooked = 6;

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.LanguageId != "rust" || context.Error.ExceptionType != "panic" ||
            !(context.Error.Message ?? "").StartsWith("index out of bounds", StringComparison.Ordinal))
            return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source || !RustProgram.IsRust(source.Path)) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        for (var index = number - 1; index >= Math.Max(0, number - 1 - LinesAboveLooked); index--)
        {
            if (RustLoops.RangeToLength().Match(masked[index]) is { Success: true } loop) return RustLoops.ExclusiveFix(Id, source, index + 1, loop);
        }

        return null;
    }
}

/// <summary>The change the crash rule and the logic check both make to a loop over 0..=v.len(), so the two are one fix.</summary>
internal static partial class RustLoops
{
    /// <summary><c>for i in 0..=marks.len()</c>.</summary>
    [GeneratedRegex(@"\bfor\s+(?<variable>[a-z_]\w*)\s+in\s+0\s*(?<range>\.\.=)\s*(?<collection>[a-z_]\w*)\.len\(\)")]
    public static partial Regex RangeToLength();

    public static LocalFix ExclusiveFix(string id, SourceFile source, int lineNumber, Match loop)
    {
        var line = source.Lines[lineNumber - 1];
        var range = loop.Groups["range"];
        var collection = loop.Groups["collection"].Value;

        return LocalFix.ReplaceLine(id, $"Stop before {collection}.len(): ..",
            $"0..={collection}.len() includes {collection}.len() itself, one more than {collection} has positions - they run from 0 to " +
            $"{collection}.len() - 1. 0..{collection}.len() stops one before.",
            source.Path, lineNumber, line[..range.Index] + ".." + line[(range.Index + range.Length)..]);
    }
}
