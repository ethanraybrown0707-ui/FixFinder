using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>
/// A float where an int is expected, beside an int operator - <c>2.5 + 1.5</c> - or the other way round: OCaml has
/// operators of its own for floats, with a dot, and never turns an int into a float by itself. The operator beside what
/// OCaml pointed at is made the one for the type the numbers are.
/// </summary>
public sealed partial class OCamlFloatOperator : ILocalFixRule
{
    public string Id => "ocaml-float-operator";

    /// <summary>An arithmetic operator for ints, not followed by the dot that makes it a float's.</summary>
    [GeneratedRegex(@"^\s*(?<operator>[-+*/])(?![.\w])")]
    private static partial Regex IntOperatorAfter();

    [GeneratedRegex(@"(?<operator>[-+*/])\s*$")]
    private static partial Regex IntOperatorBefore();

    /// <remarks>
    /// Only a float where an int is expected is changed this way. An int where a float is expected is left to OCaml's own
    /// hint - 2. for 2 - which changes the number rather than the operator.
    /// </remarks>
    public LocalFix? Propose(LocalFixContext context)
    {
        if (OCamlCode.CompileMessage(context.Error, OCamlCode.TypeMismatch()) is not { } mismatch || OCamlCode.Locate(context) is not { } at) return null;
        if (OCamlCode.Pointed(context.Error, at.Line) is not { } pointed) return null;
        if (mismatch.Groups["found"].Value.Trim() != "float" || mismatch.Groups["expected"].Value.Trim() != "int") return null;

        var masked = CodeText.Mask(at.Line, Syntax.OCaml);

        if (IntOperatorAfter().Match(masked[pointed.End..]) is { Success: true } next)
            return Change(at, pointed.End + next.Groups["operator"].Index, insert: ".", "float");

        return IntOperatorBefore().Match(masked[..pointed.Start]) is { Success: true } previous
            ? Change(at, previous.Groups["operator"].Index, insert: ".", "float")
            : null;
    }

    private LocalFix Change((SourceFile Source, int Number, string Line) at, int operatorAt, string insert, string type)
    {
        var symbol = at.Line[operatorAt];
        var corrected = at.Line[..(operatorAt + 1)] + insert + at.Line[(operatorAt + 1)..];

        return LocalFix.ReplaceLine(Id, $"Use {symbol}{insert}, the {type} operator",
            $"{symbol} works on ints, and these numbers are {type}s: OCaml's operators for floats have a dot - +. -. *. /. - and OCaml never turns " +
            "one kind of number into the other by itself.",
            at.Source.Path, at.Number, corrected);
    }
}

/// <summary>
/// A print function given the wrong kind of value - print_int with a float, print_string with an int: the value is
/// printed with the function for its type, or turned into text first for print_string and print_endline.
/// </summary>
public sealed partial class OCamlPrintFunction : ILocalFixRule
{
    public string Id => "ocaml-print-function";

    [GeneratedRegex(@"\b(?<function>print_int|print_float|print_string|print_endline)\s+$")]
    private static partial Regex PrintBefore();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (OCamlCode.CompileMessage(context.Error, OCamlCode.TypeMismatch()) is not { } mismatch || OCamlCode.Locate(context) is not { } at) return null;
        if (OCamlCode.Pointed(context.Error, at.Line) is not { } pointed) return null;
        if (PrintBefore().Match(CodeText.Mask(at.Line, Syntax.OCaml)[..pointed.Start]) is not { Success: true } print) return null;

        var found = mismatch.Groups["found"].Value.Trim();
        var function = print.Groups["function"].Value;
        var start = print.Groups["function"].Index;

        string? corrected = (function, found) switch
        {
            ("print_int", "float") => at.Line[..start] + "print_float" + at.Line[(start + function.Length)..],
            ("print_float", "int") => at.Line[..start] + "print_int" + at.Line[(start + function.Length)..],
            ("print_string" or "print_endline", "int") => at.Line[..pointed.Start] + $"(string_of_int {pointed.Text})" + at.Line[pointed.End..],
            ("print_string" or "print_endline", "float") => at.Line[..pointed.Start] + $"(string_of_float {pointed.Text})" + at.Line[pointed.End..],
            _ => null,
        };

        if (corrected is null) return null;

        var title = function is "print_int" or "print_float"
            ? $"Print it with {(found == "float" ? "print_float" : "print_int")}"
            : $"Turn the {found} into text with string_of_{found}";

        return LocalFix.ReplaceLine(Id, title,
            $"{function} prints {(function == "print_int" ? "an int" : function == "print_float" ? "a float" : "text")}, and the value given to it is {(found == "int" ? "an int" : "a float")} - " +
            "OCaml has a print function for each type, and turns no value into another type by itself.",
            at.Source.Path, at.Number, corrected);
    }
}
