using System.Text.RegularExpressions;
using FixFinder.Core.Parsing.Parsers;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>What the Perl rules share: whether an error is Perl's, and the line it is on.</summary>
internal static class PerlCode
{
    public static bool IsPerl(SourceFile source) => Path.GetExtension(source.Path).ToLowerInvariant() is ".pl" or ".pm";

    public static (SourceFile Source, int Number, string Line)? Locate(LocalFixContext context)
    {
        if (context.Error.LanguageId != "perl" || context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (!IsPerl(source) || source.Line(number) is not { } line) return null;

        return (source, number, line);
    }

    public static bool IsCompilationError(LocalFixContext context) => context.Error.ExceptionType == PerlParser.CompilationError;

    /// <summary>A warning, from perl -wc before the program ran or from the program as it ran.</summary>
    public static bool IsWarning(LocalFixContext context) => context.Error.ExceptionType is PerlParser.Warning or "compile warning";
}

/// <summary>
/// "syntax error at marks.pl line 5, near "print"": the line before has no semicolon at its end, so Perl read on into this
/// one as part of the same statement - and stopped at its first word. The semicolon goes at the end of the line before.
/// </summary>
public sealed partial class PerlMissingSemicolon : ILocalFixRule
{
    public string Id => "perl-missing-semicolon";

    /// <summary>A line that can end without a semicolon: one that opens or closes a block, or goes on to the next line.</summary>
    [GeneratedRegex(@"[;{}(\[,.=]\s*$")]
    private static partial Regex EndsWithoutOne();

    /// <summary>Perl's own hint: "Scalar found where operator expected (Missing semicolon on previous line?)".</summary>
    [GeneratedRegex(@"\(Missing semicolon on previous line\?\)$")]
    private static partial Regex PerlsOwnHint();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PerlCode.IsCompilationError(context) || PerlCode.Locate(context) is not { } at) return null;

        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.Perl);
        var hinted = PerlsOwnHint().IsMatch(context.Error.Message ?? "");
        var firstWord = Regex.Match(masked[at.Number - 1].TrimStart(), @"^[$@%]?\w+").Value;

        // Without Perl's hint, a syntax error counts only when Perl stopped at the first word of the line it names.
        if (!hinted && (context.Error.Message != "syntax error" || PerlParser.NearOf(context.Error) is not { Length: > 0 } near ||
                        firstWord.Length == 0 || !near.StartsWith(firstWord, StringComparison.Ordinal))) return null;

        var before = at.Number - 2;
        while (before >= 0 && masked[before].Trim().Length == 0) before--;
        if (before < 0 || EndsWithoutOne().IsMatch(masked[before])) return null;

        var (code, tail) = CodeText.SplitComment(at.Source.Lines[before], Syntax.Perl);

        return LocalFix.ReplaceLine(Id, "Put a ; at the end of the line before",
            "Each statement in Perl ends with a semicolon. Without one, Perl reads on into the next line as part of the same statement, " +
            $"and stops at its first word - {firstWord}, on line {at.Number}.",
            at.Source.Path, before + 1, code.TrimEnd() + ";" + tail);
    }
}

/// <summary>
/// "syntax error ... near "if $mark "": Perl's if, elsif, unless and while need their condition in brackets before the
/// block, as Python's and Ruby's do not. The condition is put in brackets.
/// </summary>
public sealed partial class PerlConditionInBrackets : ILocalFixRule
{
    public string Id => "perl-condition-in-brackets";

    [GeneratedRegex(@"^(?<indent>\s*)(?<close>\}\s*)?(?<word>if|elsif|unless|while|until)\s+(?!\()(?<condition>[^{]+?)\s*\{(?<rest>.*)$")]
    private static partial Regex Unbracketed();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PerlCode.IsCompilationError(context) || context.Error.Message != "syntax error" || PerlParser.NearOf(context.Error) is not { } near) return null;
        if (!Regex.IsMatch(near, @"^(?:if|elsif|unless|while|until)\b") || PerlCode.Locate(context) is not { } at) return null;
        if (Unbracketed().Match(at.Line) is not { Success: true } heading) return null;

        var word = heading.Groups["word"].Value;
        var condition = heading.Groups["condition"].Value;

        return LocalFix.ReplaceLine(Id, $"Put the condition in brackets: {word} ({condition})",
            $"In Perl, the condition of {word} goes in round brackets before its block. Without them, Perl stopped reading at {word}.",
            at.Source.Path, at.Number,
            $"{heading.Groups["indent"].Value}{heading.Groups["close"].Value}{word} ({condition}) {{{heading.Groups["rest"].Value}");
    }
}

/// <summary>
/// "Global symbol "$totl" requires explicit package name (did you forget to declare "my $totl"?)": with use strict, every
/// variable is declared with my before it is used. When the file declares one whose name is within two letters of it - and
/// only one - the name is changed to that one; when the line is the variable's first, giving it its first value, my is put
/// before it, as Perl itself suggests.
/// </summary>
public sealed partial class PerlUndeclaredVariable : ILocalFixRule
{
    public string Id => "perl-undeclared-variable";

    [GeneratedRegex(@"^Global symbol ""(?<sigil>[$@%])(?<name>\w+)"" requires explicit package name")]
    private static partial Regex Message();

    [GeneratedRegex(@"\b(?:my|our)\s+(?:(?<sigil>[$@%])(?<name>\w+)|\((?<list>[^)]*)\))")]
    private static partial Regex Declared();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PerlCode.IsCompilationError(context) || Message().Match(context.Error.Message ?? "") is not { Success: true } said) return null;
        if (PerlCode.Locate(context) is not { } at) return null;

        var sigil = said.Groups["sigil"].Value;
        var wrong = said.Groups["name"].Value;
        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.Perl);
        var declared = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match declaration in Declared().Matches(string.Join("\n", masked)))
        {
            if (declaration.Groups["name"].Success && declaration.Groups["sigil"].Value == sigil) declared.Add(declaration.Groups["name"].Value);

            foreach (Match listed in Regex.Matches(declaration.Groups["list"].Value, $@"{Regex.Escape(sigil)}(\w+)"))
                declared.Add(listed.Groups[1].Value);
        }

        if (CodeText.Nearest(wrong, declared) is { } right)
        {
            var place = Regex.Matches(masked[at.Number - 1], $@"{Regex.Escape(sigil)}{Regex.Escape(wrong)}(?!\w)");
            if (place is [var only])
            {
                return LocalFix.ReplaceLine(Id, $"Change {sigil}{wrong} to {sigil}{right}",
                    $"{sigil}{wrong} is declared nowhere, so use strict stopped Perl before any of the program ran. {sigil}{right}, a letter or two " +
                    "apart, is the variable the file declares with my.",
                    at.Source.Path, at.Number, at.Line[..only.Index] + sigil + right + at.Line[(only.Index + only.Length)..]);
            }
        }

        // The variable's first line, giving it its first value: Perl's own suggestion, my, makes it a variable of its own.
        var first = Enumerable.Range(0, masked.Count).FirstOrDefault(line => Regex.IsMatch(masked[line], $@"{Regex.Escape(sigil)}{Regex.Escape(wrong)}(?!\w)"), -1);
        var assigned = Regex.Match(at.Line, $@"^(?<indent>\s*){Regex.Escape(sigil)}{Regex.Escape(wrong)}\s*=(?!=)");
        if (first != at.Number - 1 || !assigned.Success) return null;

        return LocalFix.ReplaceLine(Id, $"Declare it with my: my {sigil}{wrong}",
            $"With use strict, each variable is declared with my before it is used, and this line gives {sigil}{wrong} its first value - so it " +
            "is declared here, as Perl's message suggests.",
            at.Source.Path, at.Number, assigned.Groups["indent"].Value + "my " + at.Line[assigned.Groups["indent"].Length..]);
    }
}

/// <summary>"elseif should be elsif": Perl spells it elsif, and says so. The word is changed on the line it names.</summary>
public sealed partial class PerlElsif : ILocalFixRule
{
    public string Id => "perl-elsif";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!(context.Error.Message ?? "").StartsWith("elseif should be elsif", StringComparison.Ordinal) || PerlCode.Locate(context) is not { } at) return null;
        if (CodeText.ReplaceWord(at.Line, "elseif", "elsif", Syntax.Perl) is not { } corrected) return null;

        return LocalFix.ReplaceLine(Id, "Spell it elsif",
            "Perl spells \"else if\" as elsif, with no e in the middle - and its message says so.",
            at.Source.Path, at.Number, corrected);
    }
}

/// <summary>
/// "Found = in conditional, should be ==": = stores a value; in a condition it stores it and the condition is that value,
/// the same every time. It becomes == to compare numbers - or eq, when what it is compared with is text in quotes.
/// </summary>
public sealed partial class PerlCompareInCondition : ILocalFixRule
{
    public string Id => "perl-compare-in-condition";

    [GeneratedRegex(@"\b(?:if|elsif|unless|while|until)\s*\(\s*(?<left>[$@%]?\w+(?:\[[^\]]*\]|\{[^}]*\})?)\s*(?<assign>=)(?![=~])\s*(?<right>[^)]+?)\s*\)")]
    private static partial Regex StoredInCondition();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PerlCode.IsWarning(context) || !(context.Error.Message ?? "").StartsWith("Found = in conditional", StringComparison.Ordinal)) return null;
        if (PerlCode.Locate(context) is not { } at) return null;
        if (StoredInCondition().Match(CodeText.Mask(at.Line, Syntax.Perl)) is not { Success: true } condition) return null;

        var right = at.Line.Substring(condition.Groups["right"].Index, condition.Groups["right"].Length).Trim();
        var compare = right.StartsWith('"') || right.StartsWith('\'') ? "eq" : "==";
        var assign = condition.Groups["assign"];

        return LocalFix.ReplaceLine(Id, $"Compare with {compare}",
            compare == "eq"
                ? "eq compares two pieces of text; = stored the text in the variable, and the condition was then the same every time."
                : "== compares two numbers; = stored the number in the variable, and the condition was then the same every time.",
            at.Source.Path, at.Number, at.Line[..assign.Index] + compare + at.Line[(assign.Index + assign.Length)..]);
    }
}

/// <summary>
/// "Argument "no" isn't numeric in numeric eq (==)": ==, !=, &lt; and their like compare numbers, and Perl turned the text
/// into a number to do it - every word becomes 0, so "yes" == "no" is true. The one such comparison on the line becomes the
/// one for text: eq, ne, lt, gt, le or ge.
/// </summary>
public sealed partial class PerlTextComparison : ILocalFixRule
{
    public string Id => "perl-text-comparison";

    [GeneratedRegex(@"^Argument "".*"" isn't numeric in numeric (?<word>eq|ne|lt|gt|le|ge) \((?<operator>==|!=|<|>|<=|>=)\)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PerlCode.IsWarning(context) || Message().Match(context.Error.Message ?? "") is not { Success: true } said || PerlCode.Locate(context) is not { } at) return null;

        var word = said.Groups["word"].Value;
        var symbol = said.Groups["operator"].Value;
        var places = new Regex($@"(?<![=!<>~]){Regex.Escape(symbol)}(?![=~>])").Matches(CodeText.Mask(at.Line, Syntax.Perl));
        if (places is not [var place]) return null;

        return LocalFix.ReplaceLine(Id, $"Compare text with {word}",
            $"{symbol} compares numbers, so Perl turned the text into numbers - and text that is not a number becomes 0, so the answer " +
            $"was wrong. {word} compares text as text.",
            at.Source.Path, at.Number, at.Line[..place.Index] + word + at.Line[(place.Index + place.Length)..]);
    }
}

/// <summary>
/// "Argument "Total: " isn't numeric in addition (+)": + adds numbers, and . joins text - the one + beside text in quotes on
/// the line becomes . .
/// </summary>
public sealed partial class PerlJoinText : ILocalFixRule
{
    public string Id => "perl-join-text";

    [GeneratedRegex(@"^Argument "".*"" isn't numeric in addition \(\+\)$")]
    private static partial Regex Message();

    [GeneratedRegex(@"[""']\s*\+(?!\+)|(?<!\+)\+\s*[""']")]
    private static partial Regex PlusBesideText();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PerlCode.IsWarning(context) || !Message().IsMatch(context.Error.Message ?? "") || PerlCode.Locate(context) is not { } at) return null;

        var masked = CodeText.Mask(at.Line, Syntax.Perl);
        if (PlusBesideText().Matches(masked) is not [var beside]) return null;

        var place = masked.IndexOf('+', beside.Index);

        return LocalFix.ReplaceLine(Id, "Join the text with .",
            "In Perl, + adds numbers and . joins text. + turned the text into a number - 0, for text that is not one - and added it.",
            at.Source.Path, at.Number, at.Line[..place].TrimEnd() + " . " + at.Line[(place + 1)..].TrimStart());
    }
}

/// <summary>"Illegal division by zero", "Illegal modulus zero": the one division on the line Perl names is given an answer for when the divisor is zero.</summary>
public sealed partial class PerlDivisionGuard : ILocalFixRule
{
    public string Id => "perl-division-guard";

    [GeneratedRegex(@"(?<dividend>\$\w+(?:\[[^\]]*\]|\{[^}]*\})?|\d+(?:\.\d+)?|scalar\(\s*@\w+\s*\)|\([^()]*\))\s*(?<operator>/|%)(?![/=])\s*(?<divisor>\$\w+(?:\[[^\]]*\]|\{[^}]*\})?|scalar\(\s*@\w+\s*\)|\([^()]*\))")]
    private static partial Regex Division();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.Message is not ("Illegal division by zero" or "Illegal modulus zero") || PerlCode.Locate(context) is not { } at) return null;
        if (Division().Matches(CodeText.Mask(at.Line, Syntax.Perl)) is not [var division]) return null;

        var divisor = at.Line.Substring(division.Groups["divisor"].Index, division.Groups["divisor"].Length);
        var expression = at.Line.Substring(division.Index, division.Length);
        var before = at.Line[..division.Index];
        var after = at.Line[(division.Index + division.Length)..];

        // print (...) takes what is in the brackets as all it prints, so right after print, up to a comma, the answer is left
        // without brackets - which ?: needs none of there.
        var guarded = Regex.IsMatch(before, @"\bprint\s+$") && Regex.IsMatch(after, @"^\s*[,;]")
            ? $"{divisor} == 0 ? 0 : {expression}"
            : $"({divisor} == 0 ? 0 : {expression})";
        var corrected = before + guarded + after;

        return LocalFix.ReplaceLine(Id, "Give an answer for when the divisor is zero",
            "Dividing by zero has no answer, so Perl stopped the program. The divisor is checked first, and 0 given when it is zero - change " +
            "the 0 to whatever an empty case should give.",
            at.Source.Path, at.Number, corrected);
    }
}

/// <summary>
/// "Undefined subroutine &amp;main::averge called": no sub by that name - but when the file has one sub whose name is within two
/// letters of it, the call is changed to that name.
/// </summary>
public sealed partial class PerlNearestSub : ILocalFixRule
{
    public string Id => "perl-nearest-sub";

    [GeneratedRegex(@"^Undefined subroutine &(?:main::)?(?<name>\w+) called$")]
    private static partial Regex Message();

    [GeneratedRegex(@"^\s*sub\s+(?<name>\w+)", RegexOptions.Multiline)]
    private static partial Regex SubDeclaration();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (Message().Match(context.Error.Message ?? "") is not { Success: true } said || PerlCode.Locate(context) is not { } at) return null;

        var wrong = said.Groups["name"].Value;
        var subs = SubDeclaration().Matches(string.Join("\n", CodeText.MaskAll(at.Source.Lines, Syntax.Perl))).Select(sub => sub.Groups["name"].Value);
        if (CodeText.Nearest(wrong, subs) is not { } right || CodeText.ReplaceWord(at.Line, wrong, right, Syntax.Perl) is not { } corrected) return null;

        return LocalFix.ReplaceLine(Id, $"Change {wrong} to {right}",
            $"There is no sub called {wrong}, so Perl stopped when the line called it. {right}, a letter or two apart, is the sub the file has.",
            at.Source.Path, at.Number, corrected);
    }
}
