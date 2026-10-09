using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>
/// "Reassignment to val count", in Scala 3's words, or "reassignment to val", in Scala 2's: a val given a new value. The
/// val it gives one to is made a var where it is defined - the nearest val of that name above the line.
/// </summary>
public sealed partial class ScalaValToVar : ILocalFixRule
{
    public string Id => "scala-val-to-var";

    [GeneratedRegex(@"^[Rr]eassignment to val(?: (?<name>[A-Za-z_]\w*))?")]
    private static partial Regex Message();

    /// <summary>The value a line gives a new value to: <c>count = count + 1</c>, <c>total += mark</c>.</summary>
    [GeneratedRegex(@"^\s*(?<name>[A-Za-z_]\w*)\s*(?:[-+*/%]?=)(?!=)")]
    private static partial Regex Assigned();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (ScalaCode.CompileMessage(context.Error, Message()) is not { } message || ScalaCode.Locate(context) is not { } at) return null;

        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.Scala);
        var name = message.Groups["name"].Success ? message.Groups["name"].Value
            : Assigned().Match(masked[at.Number - 1]) is { Success: true } assigned ? assigned.Groups["name"].Value
            : null;
        if (name is null) return null;

        var definition = new Regex($@"\bval(?<gap>\s+){Regex.Escape(name)}(?![\w$])");

        for (var index = at.Number - 2; index >= 0; index--)
        {
            if (definition.Match(masked[index]) is not { Success: true } val) continue;

            var line = at.Source.Lines[index];
            var corrected = line[..val.Index] + "var" + line[(val.Index + 3)..];

            return LocalFix.ReplaceLine(Id, $"Make {name} a var",
                $"`{name}` is made with val on line {index + 1}, and a val keeps the value it is first given - but line {at.Number} gives it a new one. " +
                "A value that has to change is made with var.",
                at.Source.Path, index + 1, corrected);
        }

        return null;
    }
}

/// <summary>
/// A value declared as one kind of number and given another - <c>val total: Int = count * price</c>, where the value is a
/// Double. Scala never turns a Double into an Int by itself, as that would lose everything after the point, so the value
/// is declared as the type it is. Only numbers are changed this way: text given where a number is needed is more likely a
/// value written wrongly than a type, and is left to the guide.
/// </summary>
public sealed partial class ScalaDeclaredNumberType : ILocalFixRule
{
    public string Id => "scala-declared-number-type";

    /// <summary>Scala 3's "Found:    Double" over "Required: Int", and Scala 2's "found   : Double" over "required: Int".</summary>
    [GeneratedRegex(@"^(?:Found:\s+|type mismatch;\s*\nfound\s*:\s*)(?<found>[A-Za-z]+)\s*\n\s*(?:Required|required)\s*:\s*(?<required>[A-Za-z]+)\s*$")]
    private static partial Regex Mismatch();

    [GeneratedRegex(@"^(?<declared>\s*(?:(?:private|protected|lazy|final)\s+)*(?:val|var)\s+[A-Za-z_]\w*\s*:\s*)(?<type>[A-Za-z]+)(?<value>\s*=.*)$")]
    private static partial Regex Declaration();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (ScalaCode.CompileMessage(context.Error, Mismatch()) is not { } message || ScalaCode.Locate(context) is not { } at) return null;

        var found = message.Groups["found"].Value;
        var required = message.Groups["required"].Value;
        if (!ScalaCode.NumberTypes.Contains(found) || !ScalaCode.NumberTypes.Contains(required) || found == required) return null;

        if (Declaration().Match(at.Line) is not { Success: true } declaration || declaration.Groups["type"].Value != required) return null;

        var name = Regex.Match(declaration.Groups["declared"].Value, @"(?:val|var)\s+(?<name>[A-Za-z_]\w*)").Groups["name"].Value;
        var corrected = declaration.Groups["declared"].Value + found + declaration.Groups["value"].Value;

        return LocalFix.ReplaceLine(Id, $"Declare {name} as {found}",
            $"`{name}` is declared as {Article(required)} {required}, but the value it is given is {Article(found)} {found}, and Scala does not turn one " +
            $"into the other by itself. Declaring it as {Article(found)} {found} keeps the whole value.",
            at.Source.Path, at.Number, corrected);
    }

    private static string Article(string type) => type is "Int" ? "an" : "a";
}
