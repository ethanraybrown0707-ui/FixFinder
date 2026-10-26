using System.Text.RegularExpressions;
using FixFinder.Core.Parsing.Parsers;

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

/// <summary>
/// TypeScript's compiler's own suggestion for a name or a property it does not know - "Cannot find name 'mraks'. Did you mean
/// 'marks'?" (TS2552), "Property 'lenght' does not exist on type 'number[]'. Did you mean 'length'?" (TS2551) - made at the
/// line and the column tsc names.
/// </summary>
public sealed partial class TsDidYouMean : ILocalFixRule
{
    public string Id => "ts-did-you-mean";

    [GeneratedRegex(@"^(?<kind>Cannot find name|Property) '(?<wrong>[^']+)'.*? Did you mean '(?<right>[^']+)'\?")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.LanguageId != TypeScriptCompilerParser.LanguageId || context.Error.ErrorCode is not ("TS2551" or "TS2552")) return null;
        if (Message().Match(context.Error.Message ?? "") is not { Success: true } said || JavaScriptCode.Locate(context) is not { } at) return null;

        var wrong = said.Groups["wrong"].Value;
        var right = said.Groups["right"].Value;
        if (CodeText.ReplaceWord(at.Line, wrong, right, Syntax.CLike, context.Frame?.Column - 1) is not { } corrected) return null;

        var known = said.Groups["kind"].Value == "Property" ? "no property of that name on this type" : "nothing of that name at this point in the program";
        return LocalFix.ReplaceLine(Id, $"Change {wrong} to {right}",
            $"TypeScript's compiler knows {known}. It compared {wrong} with the names it knew here, and suggested {right}.",
            at.Source.Path, at.Number, corrected);
    }
}

/// <summary>
/// "Cannot assign to 'count' because it is a constant." (TS2588): tsc's word, before the program runs, for what Node.js stops
/// it with when it gets there - Assignment to constant variable. The const that declared it, above the line, becomes let:
/// the change js-const-reassigned makes for Node's error, so the two are one finding.
/// </summary>
public sealed partial class TsConstReassigned : ILocalFixRule
{
    public string Id => "ts-const-reassigned";

    [GeneratedRegex(@"^Cannot assign to '(?<name>[A-Za-z_$][\w$]*)' because it is a constant\.$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.LanguageId != TypeScriptCompilerParser.LanguageId || context.Error.ErrorCode != "TS2588") return null;
        if (Message().Match(context.Error.Message ?? "") is not { Success: true } said || JavaScriptCode.Locate(context) is not { } at) return null;

        var (source, number, _) = at;
        var name = said.Groups["name"].Value;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var declaration = new Regex($@"\bconst\s+(?={Regex.Escape(name)}\b)");

        for (var index = number - 1; index >= 0; index--)
        {
            if (declaration.Match(masked[index]) is not { Success: true } declared) continue;

            var original = source.Lines[index];
            return LocalFix.ReplaceLine(Id, $"Declare {name} with let",
                $"`const` promises `{name}` will never be given another value, and line {number} gives it one. `let` declares a variable " +
                "that is allowed to change.",
                source.Path, index + 1, original[..declared.Index] + "let" + original[(declared.Index + "const".Length)..]);
        }

        return null;
    }
}
