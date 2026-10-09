using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>
/// A name Scala does not know, when Scala itself names the one it thinks was meant: Scala 3's "Not found: prinln - did you
/// mean println?" and "value lenght is not a member of List[String] - did you mean words.length?", and Scala 2's "did you
/// mean length?" on the line after its "value lenght is not a member of". The change is made only where the misspelt name
/// stands, and offered only when the program then compiles.
/// </summary>
public sealed partial class ScalaDidYouMean : ILocalFixRule
{
    public string Id => "scala-did-you-mean";

    [GeneratedRegex(@"^(?:Not found: (?:type )?(?<wrong>[A-Za-z_]\w*)|value (?<wrong>[A-Za-z_]\w*) is not a member of [^\n]+?)(?: - |\n)did you mean (?:[\w.]+\.)?(?<right>[A-Za-z_]\w*)\?")]
    private static partial Regex Suggested();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (ScalaCode.CompileMessage(context.Error, Suggested()) is not { } message || ScalaCode.Locate(context) is not { } at) return null;

        var wrong = message.Groups["wrong"].Value;
        var right = message.Groups["right"].Value;
        if (wrong == right) return null;

        var start = ScalaCode.WordAt(at.Line, wrong, context.Frame?.Column);
        if (start < 0) return null;

        var corrected = at.Line[..start] + right + at.Line[(start + wrong.Length)..];

        return LocalFix.ReplaceLine(Id, $"Change {wrong} to {right}",
            $"Nothing called `{wrong}` can be seen from this line, and Scala itself suggests `{right}`, the name it knows that is closest to it.",
            at.Source.Path, at.Number, corrected);
    }
}
