using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>
/// OCaml's own "Hint: Did you mean "value1"?" - for a name it does not know, and for a number written as the wrong kind,
/// such as 123 where a float is expected ("123."). What OCaml pointed at is changed to what it suggests, and only that.
/// </summary>
public sealed partial class OCamlHint : ILocalFixRule
{
    public string Id => "ocaml-hint";

    [GeneratedRegex(@"Hint: Did you mean ""?(?<right>[^""?\s]+)""?\?")]
    private static partial Regex Suggested();

    [GeneratedRegex(@"^(?:[A-Za-z_][\w']*|\d[\d_]*(?:\.\d*)?)$")]
    private static partial Regex OneWordOrNumber();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (OCamlCode.CompileMessage(context.Error, Suggested()) is not { } hint || OCamlCode.Locate(context) is not { } at) return null;
        if (OCamlCode.Pointed(context.Error, at.Line) is not { } pointed || !OneWordOrNumber().IsMatch(pointed.Text)) return null;

        var right = hint.Groups["right"].Value;
        if (right == pointed.Text) return null;

        var corrected = at.Line[..pointed.Start] + right + at.Line[pointed.End..];

        return LocalFix.ReplaceLine(Id, $"Change {pointed.Text} to {right}",
            $"OCaml itself suggests `{right}` here, in place of `{pointed.Text}`.",
            at.Source.Path, at.Number, corrected);
    }
}

/// <summary>
/// "Unbound value "facto"", with OCaml's hint that a recursive definition needs rec on the line it names: rec is added to
/// that line's let.
/// </summary>
public sealed partial class OCamlAddRec : ILocalFixRule
{
    public string Id => "ocaml-add-rec";

    [GeneratedRegex(@"add the ['""]rec['""] keyword on line (?<line>\d+)")]
    private static partial Regex RecHint();

    [GeneratedRegex(@"^(?<before>\s*(?:let|and))\s+(?!rec\b)")]
    private static partial Regex LetWithoutRec();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (OCamlCode.CompileMessage(context.Error, RecHint()) is not { } hint || OCamlCode.Locate(context) is not { } at) return null;

        var number = int.Parse(hint.Groups["line"].Value);
        if (at.Source.Line(number) is not { } line || LetWithoutRec().Match(line) is not { Success: true } let) return null;
        if (!let.Groups["before"].Value.TrimStart().StartsWith("let", StringComparison.Ordinal)) return null;

        var corrected = line[..let.Groups["before"].Length] + " rec" + line[let.Groups["before"].Length..];

        return LocalFix.ReplaceLine(Id, "Write let rec",
            "A function can only call itself when it is defined with let rec - rec makes its own name usable inside it - and OCaml " +
            $"names line {number} as where rec belongs.",
            at.Source.Path, number, corrected);
    }
}
