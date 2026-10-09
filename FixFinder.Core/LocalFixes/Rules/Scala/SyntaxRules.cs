using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>
/// Scala 3's "'=' expected, but '{' found" on a method written <c>def main(args: Array[String]) {</c> - the procedure
/// syntax of Scala 2, which Scala 3 no longer reads. The method gives nothing back, so it is declared as giving back Unit,
/// with the = Scala 3 needs before its body.
/// </summary>
public sealed partial class ScalaProcedureSyntax : ILocalFixRule
{
    public string Id => "scala-procedure-syntax";

    [GeneratedRegex(@"^'=' expected, but '\{' found")]
    private static partial Regex Message();

    /// <summary>A method's heading with no result type and no =, ending in the { its body starts with.</summary>
    [GeneratedRegex(@"^(?<heading>\s*(?:(?:override|private|protected|final|implicit)\s+)*def\s+[A-Za-z_]\w*(?:\[[^\]]*\])?(?:\([^()]*\))*)\s*\{(?<rest>.*)$")]
    private static partial Regex Heading();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (ScalaCode.CompileMessage(context.Error, Message()) is null || ScalaCode.Locate(context) is not { } at) return null;
        if (Heading().Match(at.Line) is not { Success: true } heading) return null;

        var corrected = $"{heading.Groups["heading"].Value}: Unit = {{{heading.Groups["rest"].Value}";

        return LocalFix.ReplaceLine(Id, "Write : Unit = before the method's {",
            "Scala 2 let a method that gives nothing back leave out its result type and = before a body in braces. Scala 3 no longer reads " +
            "that, so the method says it gives back Unit - nothing - and has the = Scala 3 needs.",
            at.Source.Path, at.Number, corrected);
    }
}
