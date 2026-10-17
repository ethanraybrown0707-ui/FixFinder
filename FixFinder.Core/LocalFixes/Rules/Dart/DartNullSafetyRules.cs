using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>
/// missing_default_value_for_parameter, "The parameter 'name' can't have a value of 'null' because of its type, but the implicit
/// default value is 'null'": a named parameter - in { } - is left out of a call unless it is required, and then it is null,
/// which its type does not allow. The parameter is made required, as the analyzer suggests.
/// </summary>
public sealed partial class DartRequiredParameter : ILocalFixRule
{
    public string Id => "dart-required-parameter";

    [GeneratedRegex(@"^The parameter '(?<name>\w+)' can't have a value of 'null' because of its type")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (DartCode.AnalyzerCode(context) != "missing_default_value_for_parameter" || Message().Match(context.Error.Message ?? "") is not { Success: true } said) return null;
        if (DartCode.Locate(context) is not { } at) return null;

        var name = said.Groups["name"].Value;
        var masked = CodeText.Mask(at.Line, Syntax.CLike);
        var parameter = new Regex($@"(?<=[{{,]\s*)(?<type>[A-Za-z_][\w<>,?\s]*?)\s+{Regex.Escape(name)}\b(?!\s*=)");
        if (parameter.Matches(masked) is not [var found] || masked[..found.Index].LastIndexOf('{') < 0) return null;

        return LocalFix.ReplaceLine(Id, $"Make {name} required",
            $"A named parameter - one in {{ }} - can be left out of a call, and is then null; {name}'s type does not allow null. Marked " +
            $"required, {name} has to be given in every call, so it is never null.",
            at.Source.Path, at.Number, at.Line[..found.Index] + "required " + at.Line[found.Index..]);
    }
}

/// <summary>
/// invalid_assignment, "A value of type 'Null' can't be assigned to a variable of type 'String'": a variable whose type does not
/// allow null is given null. Its type is made one that does - String? - as the analyzer suggests.
/// </summary>
public sealed partial class DartNullableType : ILocalFixRule
{
    public string Id => "dart-nullable-type";

    [GeneratedRegex(@"^A value of type 'Null' can't be assigned to a variable of type '(?<type>\w+)'\.")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (DartCode.AnalyzerCode(context) != "invalid_assignment" || Message().Match(context.Error.Message ?? "") is not { Success: true } said) return null;
        if (DartCode.Locate(context) is not { } at) return null;

        var type = said.Groups["type"].Value;
        if (Regex.Match(CodeText.Mask(at.Line, Syntax.CLike), $@"^\s*(?:final\s+|late\s+)?(?<type>{Regex.Escape(type)})\s+\w+\s*=\s*null\s*;") is not { Success: true } declared) return null;

        var typeEnd = declared.Groups["type"].Index + declared.Groups["type"].Length;
        return LocalFix.ReplaceLine(Id, $"Let it hold null: {type}?",
            $"A variable of type {type} always holds a {type}, never null - Dart checks that before the program runs. {type}? is a {type} " +
            "or null, so the variable can start as null.",
            at.Source.Path, at.Number, at.Line[..typeEnd] + "?" + at.Line[typeEnd..]);
    }
}
