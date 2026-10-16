using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>
/// "The term 'total' is not recognized as the name of a cmdlet, function, script file, or operable program", at a line that
/// starts with total = ...: the variable has no $ before its name, so PowerShell took total for a command. The $ goes in.
/// </summary>
public sealed partial class PowerShellMissingDollar : ILocalFixRule
{
    public string Id => "powershell-missing-dollar";

    [GeneratedRegex(@"^The term '(?<name>[A-Za-z_]\w*)' is not recognized as the name of a cmdlet")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.ErrorCode != "CommandNotFoundException" || Message().Match(context.Error.Message ?? "") is not { Success: true } said) return null;
        if (PowerShellCode.Locate(context) is not { } at) return null;

        var name = said.Groups["name"].Value;
        if (Regex.Match(CodeText.Mask(at.Line, Syntax.Python), $@"^(?<lead>\s*){Regex.Escape(name)}\s*(?:[-+*/]?=)(?!=)") is not { Success: true } given) return null;

        var nameAt = given.Groups["lead"].Length;
        return LocalFix.ReplaceLine(Id, $"Write the variable as ${name}",
            $"Every PowerShell variable's name starts with $. Without it, PowerShell took {name} for the name of a command, looked for " +
            "one, and found none.",
            at.Source.Path, at.Number, at.Line[..nameAt] + "$" + at.Line[nameAt..]);
    }
}

/// <summary>
/// "Missing closing '}' in statement block or type definition", at the { PowerShell names - often the outer block the script
/// ends inside. The { left open is found as the script is laid out - each } closes the open { at its own indentation - and its
/// } goes where its indentation ends.
/// </summary>
public sealed class PowerShellClosingBrace : ILocalFixRule
{
    public string Id => "powershell-closing-brace";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PowerShellCode.IsParserError(context) || context.Error.ErrorCode != "MissingEndCurlyBrace" || PowerShellCode.Locate(context) is not { } at) return null;

        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.Python);
        if (LeftOpen.NeverClosedBraces(masked) is not [var opened]) return null;

        return LocalFix.Insert(Id, $"Close the {{ on line {opened + 1}",
            $"The {{ on line {opened + 1} is never closed, so PowerShell reached the end of the script inside it. Its }} goes where its " +
            "indentation ends - after the last line indented inside it.",
            at.Source.Path, LeftOpen.BlockEndsBefore(at.Source.Lines, masked, opened), [CodeText.Indentation(at.Source.Lines[opened]) + "}"]);
    }
}

/// <summary>
/// "The string is missing the terminator", at the quote PowerShell names: the text it starts has no closing quote. The quote
/// goes where the line's text ends.
/// </summary>
public sealed class PowerShellClosingQuote : ILocalFixRule
{
    public string Id => "powershell-closing-quote";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PowerShellCode.IsParserError(context) || context.Error.ErrorCode != "TerminatorExpectedAtEndOfString" || PowerShellCode.Locate(context) is not { } at) return null;
        if (LeftOpen.ClosedText(at.Line, Syntax.Python, escape: '`') is not { } closed) return null;

        return LocalFix.ReplaceLine(Id, "Close the string",
            "The text that starts with this quote has no closing quote, so PowerShell read on to the end of the script inside it. The " +
            "closing quote goes where the text ends - at the end of the line, or before the brackets it is inside.",
            at.Source.Path, at.Number, closed);
    }
}

/// <summary>
/// "Missing closing ')' in expression", at the line whose ( is never closed: the ) goes at the end of the line's code.
/// </summary>
public sealed partial class PowerShellClosingBracket : ILocalFixRule
{
    public string Id => "powershell-closing-bracket";

    [GeneratedRegex(@"^Missing closing '\)' in expression\.$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PowerShellCode.IsParserError(context) || !Message().IsMatch(context.Error.Message ?? "") || PowerShellCode.Locate(context) is not { } at) return null;

        var masked = CodeText.Mask(at.Line, Syntax.Python);
        if (masked.Count(character => character == '(') - masked.Count(character => character == ')') != 1) return null;

        var (code, tail) = CodeText.SplitComment(at.Line, Syntax.Python);
        return LocalFix.ReplaceLine(Id, "Close the bracket",
            "The ( on this line is never closed, so PowerShell read on past the line's end looking for its ). It goes at the end of the line.",
            at.Source.Path, at.Number, code + ")" + tail);
    }
}
