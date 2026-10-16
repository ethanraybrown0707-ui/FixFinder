using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>
/// "Expected ')', got '{'", as Node.js reads TypeScript before stripping its types: the bracket opened after if, for or while
/// is not closed before the { that starts the block, so Node read the { as more of the condition. The ) goes before the {.
/// </summary>
public sealed partial class TsConditionClosingBracket : ILocalFixRule
{
    public string Id => "ts-condition-closing-bracket";

    [GeneratedRegex(@"^Expected '\)', got '\{'$")]
    private static partial Regex Message();

    [GeneratedRegex(@"^\s*(?:\}\s*)?(?:else\s+)?(?<keyword>if|for|while|switch)\s*\(")]
    private static partial Regex Heading();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaScriptCode.ErrorMessage(context.Error, "SyntaxError", Message()) is null || JavaScriptCode.Locate(context) is not { } at) return null;

        var (code, _) = CodeText.SplitComment(CodeText.Mask(at.Line, Syntax.CLike), Syntax.CLike);
        var trimmed = code.TrimEnd();
        if (Heading().Match(trimmed) is not { Success: true } heading || !trimmed.EndsWith('{')) return null;
        if (trimmed.Count(character => character == '(') - trimmed.Count(character => character == ')') != 1) return null;

        var brace = trimmed.Length - 1;
        var closeAt = trimmed[..brace].TrimEnd().Length;

        return LocalFix.ReplaceLine(Id, $"Close the {heading.Groups["keyword"].Value}'s bracket before the {{",
            $"The ( after {heading.Groups["keyword"].Value} is not closed before the {{ that starts the block, so Node.js read the {{ as more " +
            "of what is in the brackets, and stopped there. The ) goes where the brackets' inside ends.",
            at.Source.Path, at.Number, at.Line[..closeAt] + ")" + at.Line[closeAt..]);
    }
}

/// <summary>
/// "Expected ',', got 'numeric literal'" - or a string literal, or an identifier - at the item Node.js marks: two items of a
/// list, or two arguments, with no comma between them. The comma goes after the item before the one marked.
/// </summary>
public sealed partial class TsMissingComma : ILocalFixRule
{
    public string Id => "ts-missing-comma";

    [GeneratedRegex(@"^Expected ',', got '(?:numeric literal|string literal|identifier)'$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaScriptCode.ErrorMessage(context.Error, "SyntaxError", Message()) is null || JavaScriptCode.Locate(context) is not { } at) return null;
        if (context.Frame?.Column is not { } column || column < 2 || column > at.Line.Length) return null;

        var masked = CodeText.Mask(at.Line, Syntax.CLike);
        var before = masked[..(column - 1)].TrimEnd();
        if (before.Length == 0 || before.Length == column - 1 || !(char.IsLetterOrDigit(before[^1]) || before[^1] is '"' or '\'' or '`' or ']' or ')' or '_' or '$')) return null;

        return LocalFix.ReplaceLine(Id, "Put a comma between the two",
            "The items of a list, and the arguments of a call, are separated by commas. Here two stand side by side with only a space " +
            "between them, so Node.js expected a comma where the second one starts.",
            at.Source.Path, at.Number, at.Line[..before.Length] + "," + at.Line[before.Length..]);
    }
}
