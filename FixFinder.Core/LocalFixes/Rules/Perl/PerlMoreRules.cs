using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>
/// "Missing right curly or square bracket", at the end of the file: a { is never closed. The { left open is found as the program
/// is laid out - each } closes the open { at its own indentation - and its } goes where its indentation ends.
/// </summary>
public sealed partial class PerlClosingBrace : ILocalFixRule
{
    public string Id => "perl-closing-brace";

    [GeneratedRegex(@"^Missing right curly or square bracket")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PerlCode.IsCompilationError(context) || !Message().IsMatch(context.Error.Message ?? "")) return null;
        if (context.Read(context.Frame?.File) is not { } source || !PerlCode.IsPerl(source)) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.Perl);
        if (LeftOpen.NeverClosedBraces(masked) is not [var opened]) return null;

        return LocalFix.Insert(Id, $"Close the {{ on line {opened + 1}",
            $"The {{ on line {opened + 1} is never closed, so Perl reached the end of the file inside it. Its }} goes where its indentation " +
            "ends - after the last line indented inside it.",
            source.Path, LeftOpen.BlockEndsBefore(source.Lines, masked, opened), [CodeText.Indentation(source.Lines[opened]) + "}"]);
    }
}

/// <summary>
/// A string whose closing quote is missing: "Can't find string terminator '"' anywhere before EOF", at the line it starts on, when
/// no quote follows - and when one does, whatever Perl stopped at, with its own hint "(Might be a runaway multi-line "" string
/// starting on line 4)". That line gets its closing quote, before the line's ; or ).
/// </summary>
public sealed partial class PerlClosingQuote : ILocalFixRule
{
    public string Id => "perl-closing-quote";

    [GeneratedRegex(@"^Can't find string terminator '[""']' anywhere before EOF")]
    private static partial Regex Message();

    [GeneratedRegex(@"\(Might be a runaway multi-line (?:""""|'') string starting on line (?<line>\d+)\)")]
    private static partial Regex RunawayHint();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PerlCode.IsCompilationError(context)) return null;

        var hint = RunawayHint().Match(context.Error.RawText);
        if (!hint.Success && !Message().IsMatch(context.Error.Message ?? "")) return null;
        if (context.Read(context.Frame?.File) is not { } source || !PerlCode.IsPerl(source)) return null;

        var index = hint.Success ? int.Parse(hint.Groups["line"].Value) - 1 : (context.Frame?.Line ?? 0) - 1;
        if (index < 0 || index >= source.Count || BraceRules.CloseString(source.Lines[index], Syntax.Perl) is not { } closed) return null;

        return LocalFix.ReplaceLine(Id, $"Close the string on line {index + 1}", BraceRules.CloseStringExplanation, source.Path, index + 1, closed);
    }
}

/// <summary>
/// "Can't modify constant item in scalar assignment", at total = ...: the variable has no $ before its name, so Perl read the
/// name as a constant, which cannot be given a value. The $ goes in - with my, under use strict, when nothing declares it.
/// </summary>
public sealed partial class PerlMissingSigil : ILocalFixRule
{
    public string Id => "perl-missing-sigil";

    [GeneratedRegex(@"^Can't modify constant item in scalar assignment")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?<lead>\s*)(?<name>[A-Za-z_]\w*)\s*(?:[-+*/.]?=)(?![=~>])")]
    private static partial Regex NameGiven();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PerlCode.IsCompilationError(context) || !Message().IsMatch(context.Error.Message ?? "") || PerlCode.Locate(context) is not { } at) return null;
        if (NameGiven().Match(CodeText.Mask(at.Line, Syntax.Perl)) is not { Success: true } given) return null;

        var name = given.Groups["name"].Value;
        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.Perl);
        var strict = masked.Any(line => Regex.IsMatch(line, @"^\s*use\s+strict\b"));
        var declared = masked.Take(at.Number - 1).Any(line => Regex.IsMatch(line, $@"\b(?:my|our)\s+\${Regex.Escape(name)}\b"));
        var written = strict && !declared ? $"my ${name}" : $"${name}";

        var nameAt = given.Groups["name"].Index;
        return LocalFix.ReplaceLine(Id, $"Write the variable as {written}",
            $"A Perl variable holding one value is written with a $ before its name. Without it, Perl read {name} as a constant - a name " +
            "for a value that never changes - and stopped at the =." + (written.StartsWith("my", StringComparison.Ordinal) ? " Under use strict it is declared with my as well." : ""),
            at.Source.Path, at.Number, at.Line[..nameAt] + written + at.Line[(nameAt + name.Length)..]);
    }
}
