using System.Text.RegularExpressions;
using FixFinder.Core.Parsing.Parsers;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>What the Lua rules share: whether an error is Lua's, and the line it is on.</summary>
internal static class LuaCode
{
    public static bool IsLua(SourceFile source) => Path.GetExtension(source.Path).Equals(".lua", StringComparison.OrdinalIgnoreCase);

    public static (SourceFile Source, int Number, string Line)? Locate(LocalFixContext context)
    {
        if (context.Error.LanguageId != "lua" || context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (!IsLua(source) || source.Line(number) is not { } line) return null;

        return (source, number, line);
    }

    public static bool IsSyntaxError(LocalFixContext context) => context.Error.ExceptionType == LuaParser.SyntaxError;

    public static bool IsRuntimeError(LocalFixContext context) => context.Error.ExceptionType == LuaParser.RuntimeError;
}

/// <summary>
/// <c>if mark != 40 then</c>: Lua writes "is not equal to" as ~=, and has no != - so it stops reading at the !, saying
/// what it expected there: "'then' expected near '!'". The one != on the line Lua names becomes ~=.
/// </summary>
public sealed partial class LuaNotEqual : ILocalFixRule
{
    public string Id => "lua-not-equal";

    [GeneratedRegex(@"near '!'$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!LuaCode.IsSyntaxError(context) || !Message().IsMatch(context.Error.Message ?? "") || LuaCode.Locate(context) is not { } at) return null;

        var masked = CodeText.Mask(at.Line, Syntax.Lua);
        var place = masked.IndexOf("!=", StringComparison.Ordinal);
        if (place < 0 || masked.IndexOf("!=", place + 2, StringComparison.Ordinal) >= 0) return null;

        return LocalFix.ReplaceLine(Id, "Write \"is not equal to\" as ~=",
            "Lua writes \"is not equal to\" as ~=. It has no !=, so it stopped reading at the ! - it has no ! at all.",
            at.Source.Path, at.Number, at.Line[..place] + "~=" + at.Line[(place + 2)..]);
    }
}

/// <summary>
/// <c>total += 5</c>, <c>count++</c>: Lua has no shorthand for changing a variable by an amount - it stops at the operator,
/// "syntax error near '+'" - so the change is written out in full: <c>total = total + 5</c>. (count-- is not one of them:
/// to Lua, -- starts a comment.)
/// </summary>
public sealed partial class LuaCompoundAssignment : ILocalFixRule
{
    public string Id => "lua-compound-assignment";

    [GeneratedRegex(@"^syntax error near '(?<operator>[-+*/%^]|\.\.)'$")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?<indent>\s*)(?<name>[A-Za-z_][\w.]*)\s*(?:(?<operator>[-+*/%^]|\.\.)=\s*(?<value>\S.*?)|(?<step>\+\+))\s*$")]
    private static partial Regex Shorthand();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!LuaCode.IsSyntaxError(context) || Message().Match(context.Error.Message ?? "") is not { Success: true } said) return null;
        if (LuaCode.Locate(context) is not { } at) return null;

        var (code, tail) = CodeText.SplitComment(at.Line, Syntax.Lua);
        if (Shorthand().Match(code) is not { Success: true } shorthand) return null;

        var name = shorthand.Groups["name"].Value;
        var (written, value) = shorthand.Groups["step"].Success
            ? ("+", "1")
            : (shorthand.Groups["operator"].Value, shorthand.Groups["value"].Value);
        if (!written.StartsWith(said.Groups["operator"].Value[..1], StringComparison.Ordinal)) return null;

        var shown = shorthand.Groups["step"].Success ? $"{name}{shorthand.Groups["step"].Value}" : $"{name} {written}= ...";

        return LocalFix.ReplaceLine(Id, $"Write it out in full: {name} = {name} {written} {value}",
            $"Lua has no {shown} shorthand, so it stopped reading at the {said.Groups["operator"].Value}. The change is written out in full: " +
            $"{name} is given its own value {written} {value}.",
            at.Source.Path, at.Number, $"{shorthand.Groups["indent"].Value}{name} = {name} {written} {value}{tail}");
    }
}

/// <summary>
/// "'then' expected near 'print'": an if or elseif line without then, so Lua read on into the next line looking for it.
/// The same for a for or while line without do. The word goes at the end of the line that needs it.
/// </summary>
public sealed partial class LuaMissingThenOrDo : ILocalFixRule
{
    public string Id => "lua-missing-then-or-do";

    /// <summary>How far above the line Lua names the line that needs the word is looked for.</summary>
    private const int LinesAboveLooked = 4;

    [GeneratedRegex(@"^'(?<word>then|do)' expected near '(?<next>[^']+)'$")]
    private static partial Regex Message();

    [GeneratedRegex(@"^\s*(?<opening>if|elseif|for|while)\b")]
    private static partial Regex Opening();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!LuaCode.IsSyntaxError(context) || Message().Match(context.Error.Message ?? "") is not { Success: true } said) return null;
        if (LuaCode.Locate(context) is not { } at) return null;

        var word = said.Groups["word"].Value;
        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.Lua);

        // Lua noticed at the start of the line it names: the line before it is the one that went without the word.
        if (!masked[at.Number - 1].TrimStart().StartsWith(said.Groups["next"].Value, StringComparison.Ordinal)) return null;

        for (var index = at.Number - 2; index >= Math.Max(0, at.Number - 1 - LinesAboveLooked); index--)
        {
            if (masked[index].Trim().Length == 0) continue;
            if (Opening().Match(masked[index]) is not { Success: true } opening) return null;

            var needs = opening.Groups["opening"].Value is "if" or "elseif" ? "then" : "do";
            if (needs != word || Regex.IsMatch(masked[index], $@"\b{word}\b")) return null;

            var (code, tail) = CodeText.SplitComment(at.Source.Lines[index], Syntax.Lua);

            return LocalFix.ReplaceLine(Id, $"End the {opening.Groups["opening"].Value} line with {word}",
                $"In Lua, {(word == "then" ? "an if's condition ends with then" : "a loop's heading ends with do")}. Without it, Lua read on into " +
                $"the next line looking for {word}, and stopped there, at {said.Groups["next"].Value}.",
                at.Source.Path, index + 1, code.TrimEnd() + " " + word + tail);
        }

        return null;
    }
}

/// <summary>
/// "attempt to call a nil value (global 'averge')": a name Lua knows no value for - in Lua a name never given one is nil -
/// within two letters of one the file does give a value to, and of only one, becomes that one on the line Lua names.
/// </summary>
public sealed partial class LuaNearestName : ILocalFixRule
{
    public string Id => "lua-nearest-name";

    [GeneratedRegex(@"^attempt to (?:perform arithmetic on|call|index|concatenate|compare \w+ with|compare) a nil value \(global '(?<name>\w+)'\)$")]
    private static partial Regex Message();

    /// <summary>A name the code gives a value to: a local, an assignment, a function and its parameters, a loop's variables.</summary>
    [GeneratedRegex(@"\blocal\s+(?!function\b)(?<names>[A-Za-z_]\w*(?:\s*,\s*[A-Za-z_]\w*)*)|^\s*(?<assigned>[A-Za-z_]\w*)\s*=(?!=)|\bfunction\s+(?<function>[A-Za-z_][\w.:]*)\s*\((?<parameters>[^)]*)\)|\bfor\s+(?<loop>[A-Za-z_]\w*(?:\s*,\s*[A-Za-z_]\w*)*)\s*(?:=|in\b)", RegexOptions.Multiline)]
    private static partial Regex GivenAValue();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!LuaCode.IsRuntimeError(context) || Message().Match(context.Error.Message ?? "") is not { Success: true } said) return null;
        if (LuaCode.Locate(context) is not { } at) return null;

        var wrong = said.Groups["name"].Value;
        var named = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match given in GivenAValue().Matches(string.Join("\n", CodeText.MaskAll(at.Source.Lines, Syntax.Lua))))
        {
            foreach (var group in new[] { "names", "parameters", "loop" })
                foreach (var name in given.Groups[group].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    named.Add(name);

            if (given.Groups["assigned"].Success) named.Add(given.Groups["assigned"].Value);
            if (given.Groups["function"].Success && !given.Groups["function"].Value.Contains('.') && !given.Groups["function"].Value.Contains(':'))
                named.Add(given.Groups["function"].Value);
        }

        if (CodeText.Nearest(wrong, named) is not { } right) return null;
        if (CodeText.ReplaceWord(at.Line, wrong, right, Syntax.Lua) is not { } corrected) return null;

        return LocalFix.ReplaceLine(Id, $"Change {wrong} to {right}",
            $"{wrong} is given no value anywhere, so to Lua it is nil - and nil cannot be used as a value here. {right}, a letter or two " +
            "apart, is the name the file gives a value to.",
            at.Source.Path, at.Number, corrected);
    }
}

/// <summary>
/// "attempt to add a 'string' with a 'number'": + only adds numbers in Lua, and text is joined with .. - the one + on the
/// line Lua names becomes .. .
/// </summary>
public sealed partial class LuaJoinText : ILocalFixRule
{
    public string Id => "lua-join-text";

    [GeneratedRegex(@"^attempt to add a '(?:string|number)' with a '(?:string|number)'$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!LuaCode.IsRuntimeError(context) || !Message().IsMatch(context.Error.Message ?? "") || !context.Error.Message!.Contains("'string'")) return null;
        if (LuaCode.Locate(context) is not { } at) return null;

        var masked = CodeText.Mask(at.Line, Syntax.Lua);
        var place = masked.IndexOf('+');
        if (place < 0 || masked.IndexOf('+', place + 1) >= 0) return null;

        return LocalFix.ReplaceLine(Id, "Join the text with ..",
            "In Lua, + only adds numbers, and .. joins text - turning a number into text as it does. Lua would not add text that is not " +
            "a number, so it stopped.",
            at.Source.Path, at.Number, at.Line[..place].TrimEnd() + " .. " + at.Line[(place + 1)..].TrimStart());
    }
}

/// <summary>
/// "attempt to divide by zero" from //, or "attempt to perform 'n%0'" from %: whole numbers divided by zero. The one such
/// division on the line Lua names is given an answer for when the divisor is zero.
/// </summary>
public sealed partial class LuaDivisionGuard : ILocalFixRule
{
    public string Id => "lua-division-guard";

    [GeneratedRegex(@"^attempt to (?:divide by zero|perform 'n%%?0')$")]
    private static partial Regex Message();

    [GeneratedRegex(@"(?<dividend>[A-Za-z_][\w.]*(?:\([^()]*\))?|\d+|\([^()]*\)|#[A-Za-z_][\w.]*)\s*(?<operator>//|%)\s*(?<divisor>\([^()]*\)|#[A-Za-z_][\w.]*|[A-Za-z_][\w.]*(?:\([^()]*\))?)")]
    private static partial Regex Division();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!LuaCode.IsRuntimeError(context) || !Message().IsMatch(context.Error.Message ?? "") || LuaCode.Locate(context) is not { } at) return null;
        if (Division().Matches(CodeText.Mask(at.Line, Syntax.Lua)) is not [var division]) return null;

        var divisor = at.Line.Substring(division.Groups["divisor"].Index, division.Groups["divisor"].Length);
        var expression = at.Line.Substring(division.Index, division.Length);
        var corrected = at.Line[..division.Index] + $"({divisor} == 0 and 0 or {expression})" + at.Line[(division.Index + division.Length)..];

        return LocalFix.ReplaceLine(Id, "Give an answer for when the divisor is zero",
            $"A whole number divided by zero with {division.Groups["operator"].Value} has no answer, so Lua stopped the program. The divisor is " +
            "checked first, and 0 given when it is zero - Lua counts 0 as true, so `and 0 or` gives the 0. Change it to whatever an empty " +
            "case should give.",
            at.Source.Path, at.Number, corrected);
    }
}

/// <summary>
/// <c>if mark = 40 then</c>: = stores a value, and Lua will not store one in a condition - it stops at the =, saying what
/// it expected there: "'then' expected near '='". The one = on the line Lua names becomes ==, which compares.
/// </summary>
public sealed partial class LuaCompareWithDoubleEquals : ILocalFixRule
{
    public string Id => "lua-compare-with-double-equals";

    [GeneratedRegex(@"^'(?:then|do)' expected near '='$")]
    private static partial Regex Message();

    /// <summary>An = on its own: not ==, ~=, &lt;= or &gt;=.</summary>
    [GeneratedRegex(@"(?<![=~<>])=(?!=)")]
    private static partial Regex SingleEquals();

    [GeneratedRegex(@"^\s*(?:if|elseif|while)\b")]
    private static partial Regex Condition();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!LuaCode.IsSyntaxError(context) || !Message().IsMatch(context.Error.Message ?? "") || LuaCode.Locate(context) is not { } at) return null;

        var masked = CodeText.Mask(at.Line, Syntax.Lua);
        if (!Condition().IsMatch(masked) || SingleEquals().Matches(masked) is not [var place]) return null;

        return LocalFix.ReplaceLine(Id, "Compare with ==",
            "== compares two values; = stores one, and Lua will not store a value in a condition, so it stopped at the =.",
            at.Source.Path, at.Number, at.Line[..place.Index] + "==" + at.Line[(place.Index + 1)..]);
    }
}
