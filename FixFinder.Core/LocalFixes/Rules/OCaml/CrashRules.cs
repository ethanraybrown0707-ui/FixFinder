using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>
/// <c>Division_by_zero</c>: a whole number divided by zero. The division is given an answer for when the divisor is zero,
/// as an if, which in OCaml gives a value either way.
/// </summary>
public sealed partial class OCamlDivisionGuard : ILocalFixRule
{
    public string Id => "ocaml-division-guard";

    /// <summary>
    /// A whole-number division - / with no dot after it - of a name, a number or a bracketed expression, by a name, a
    /// bracketed expression or a function given one argument, as in <c>total / List.length marks</c>.
    /// </summary>
    [GeneratedRegex(@"(?<dividend>[A-Za-z_][\w.']*|\d+|\([^()]*\))\s*/(?!\.)\s*(?<divisor>\([^()]*\)|[A-Za-z_][\w.']*(?:\s+[a-z_][\w']*)?)")]
    private static partial Regex Division();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!OCamlCode.Raised(context.Error, "Division_by_zero") || OCamlCode.Locate(context) is not { } at) return null;

        var divisions = Division().Matches(CodeText.Mask(at.Line, Syntax.OCaml));
        if (divisions.Count != 1) return null;

        var division = divisions[0];
        var divisor = at.Line.Substring(division.Groups["divisor"].Index, division.Groups["divisor"].Length);
        var expression = at.Line.Substring(division.Index, division.Length);
        var corrected = at.Line[..division.Index] + $"(if {divisor} = 0 then 0 else {expression})" + at.Line[(division.Index + division.Length)..];

        return LocalFix.ReplaceLine(Id, "Give an answer for when the divisor is zero",
            "Dividing a whole number by zero has no answer, so OCaml stops the program with Division_by_zero. Checking the divisor first " +
            "and giving 0 in that case keeps the program going - change the 0 to whatever an empty case should give.",
            at.Source.Path, at.Number, corrected);
    }
}

/// <summary>
/// <c>Invalid_argument("index out of bounds")</c> from a loop <c>for i = 0 to Array.length a do</c>: OCaml's for counts
/// up to and including its last number, so the loop's last pass is one past the end.
/// </summary>
public sealed class OCamlForUpperBound : ILocalFixRule
{
    public string Id => "ocaml-for-upper-bound";

    /// <summary>How far above the failing line a loop's heading is looked for.</summary>
    private const int LinesAboveLooked = 6;

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!OCamlCode.Raised(context.Error, "Invalid_argument") || context.Error.Message != "index out of bounds") return null;
        if (OCamlCode.Locate(context) is not { } at) return null;

        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.OCaml);

        for (var index = at.Number - 1; index >= Math.Max(0, at.Number - 1 - LinesAboveLooked); index--)
        {
            if (OCamlLoops.ForToLength().Match(masked[index]) is { Success: true } loop) return OCamlLoops.UpperBoundFix(Id, at.Source, index + 1, loop);
        }

        return null;
    }
}

/// <summary>The change the crash rule and the logic check both make to a for loop that runs one past the end, so the two are one fix.</summary>
internal static partial class OCamlLoops
{
    /// <summary><c>for i = 0 to Array.length marks do</c> - with no - 1 after the length.</summary>
    [GeneratedRegex(@"\bfor\s+(?<variable>[a-z_][\w']*)\s*=\s*0\s+to\s+(?<length>(?<module>Array|String|Bytes|List)\.length\s+(?<collection>[a-z_][\w']*))\s+do\b")]
    public static partial Regex ForToLength();

    public static LocalFix UpperBoundFix(string id, SourceFile source, int lineNumber, Match loop)
    {
        var line = source.Lines[lineNumber - 1];
        var length = loop.Groups["length"];
        var corrected = line[..(length.Index + length.Length)] + " - 1" + line[(length.Index + length.Length)..];

        return LocalFix.ReplaceLine(id, $"Stop at {length.Value} - 1",
            $"OCaml's for counts up to and including its last number, so for {loop.Groups["variable"].Value} = 0 to {length.Value} takes one step " +
            $"more than {loop.Groups["collection"].Value} has positions - they run from 0 to one less than its length.",
            source.Path, lineNumber, corrected);
    }
}
