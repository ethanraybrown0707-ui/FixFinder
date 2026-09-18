using System.Text.RegularExpressions;
using FixFinder.Core.Logic;
using FixFinder.Core.Parsing.Parsers;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary><c>C2146 / C2143: syntax error: missing ';' before ...</c></summary>
/// <remarks>
/// MSVC reports a missing semicolon on the line where it noticed - the start of the next statement -
/// so the semicolon goes at the end of the code line before it.
/// </remarks>
public sealed partial class CMissingSemicolon : ILocalFixRule
{
    public string Id => "c-missing-semicolon";

    [GeneratedRegex(@"^syntax error: missing ';' before (?:identifier )?'(?<token>[^']+)'$")]
    private static partial Regex Message();

    /// <summary>MSVC's C++ front end: <c>C2760 syntax error: 'int' was unexpected here; expected ';'</c>.</summary>
    [GeneratedRegex(@"^syntax error: '(?<token>[^']+)' was unexpected here; expected ';'$")]
    private static partial Regex Unexpected();

    /// <summary>gcc: <c>expected ',' or ';' before 'printf'</c>, and <c>expected ';' before '}' token</c>.</summary>
    [GeneratedRegex(@"^expected (?:'[^']+' or )*';'(?: or '[^']+')* before '(?<token>[^']+)'(?: token)?$")]
    private static partial Regex GnuMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        var message = error.LanguageId switch
        {
            "msvc" when error.ErrorCode is "C2146" or "C2143" => Message().Match(error.Message ?? ""),
            "msvc" when error.ErrorCode == "C2760" => Unexpected().Match(error.Message ?? ""),
            "gcc" => GnuMessage().Match(error.Message ?? ""),
            _ => null,
        };

        if (message is not { Success: true }) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (number < 1 || number > masked.Count) return null;

        var token = message.Groups["token"].Value;
        if (!masked[number - 1].TrimStart().StartsWith(token, StringComparison.Ordinal)) return null;

        for (var k = number - 2; k >= 0; k--)
        {
            var code = masked[k].Trim();
            if (code.Length == 0) continue;

            if (code.StartsWith('#') || code.EndsWith(';') || code.EndsWith('{') || code.EndsWith('}') ||
                code.EndsWith(',') || code.EndsWith('(') || code.EndsWith('\\'))
                return null;

            var (lineCode, tail) = CodeText.SplitComment(source.Lines[k], Syntax.CLike);

            return LocalFix.ReplaceLine(
                Id, "Add the missing semicolon",
                $"The compiler reports the missing semicolon at line {number}, where it noticed - it belongs at the end of the " +
                $"statement on line {k + 1}.",
                source.Path, k + 1, lineCode + ";" + tail);
        }

        return null;
    }
}

/// <summary><c>C1075: '{': no matching token found</c> - the file ends before the block closes.</summary>
public sealed partial class CMissingClosingBrace : ILocalFixRule
{
    public string Id => "c-missing-closing-brace";

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        // MSVC names the brace that was never closed. gcc names the end of the file, where it gave
        // up - so the opener is found by matching braces, which serves both.
        var recognised = error.LanguageId switch
        {
            "msvc" => error.ErrorCode == "C1075" &&
                      (error.Message ?? "").StartsWith("'{': no matching token found", StringComparison.Ordinal),
            "gcc" => error.Message is "expected declaration or statement at end of input" or "expected '}' at end of input",
            _ => false,
        };

        if (!recognised || context.Read(context.Frame?.File) is not { } source) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var unclosed = new Stack<int>();
        var stray = 0;

        for (var i = 0; i < masked.Count; i++)
        {
            foreach (var c in masked[i])
            {
                if (c == '{') unclosed.Push(i);
                else if (c == '}' && !unclosed.TryPop(out _)) stray++;
            }
        }

        // Exactly one short. More than that, and where each one goes is a real question.
        if (stray > 0 || unclosed.Count != 1) return null;

        var opener = unclosed.Pop() + 1;
        var last = source.Count - 1;
        while (last > 0 && source.Lines[last].Trim().Length == 0) last--;

        return LocalFix.Insert(
            Id, $"Close the block opened on line {opener}",
            $"The `{{` on line {opener} is never closed - the file ends first. The closing brace goes at the end; if " +
            "code at the bottom belongs outside the block, move the brace up to where the block really ends.",
            source.Path, last + 2, [CodeText.Indentation(source.Lines[opener - 1]) + "}"]);
    }
}

/// <summary><c>elif</c> from Python, which C writes <c>else if</c>.</summary>
public sealed partial class CElif : ILocalFixRule
{
    public string Id => "c-elif";

    [GeneratedRegex(@"(?<![\w.])elif(?=\s*\()")]
    private static partial Regex Elif();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CCode.IsCompiler(context.Error) || CCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var hits = Elif().Matches(CodeText.Mask(line, Syntax.CLike));
        if (hits.Count == 0) return null;

        return LocalFix.ReplaceLine(
            Id, "Write elif as else if", "C has no `elif` - that is Python. In C it is `else if`.",
            source.Path, number, CCode.ReplaceEach(line, hits, _ => "else if"));
    }
}

/// <summary><c>and</c>, <c>or</c> and <c>not</c> from Python, which C writes <c>&amp;&amp;</c>, <c>||</c> and <c>!</c>.</summary>
/// <remarks>C only: in C++ the words are real alternative spellings of the operators.</remarks>
public sealed partial class CWordOperators : ILocalFixRule
{
    public string Id => "c-word-operators";

    [GeneratedRegex(@"'(?:and|or|not)'")]
    private static partial Regex Named();

    [GeneratedRegex(@"(?<![\w.])(?<word>and|or|not)(?!\w)(?<space>(?<=not)\s*)?")]
    private static partial Regex Word();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CCode.IsCompiler(context.Error) || !Named().IsMatch(context.Error.Message ?? "")) return null;
        if (CCode.Locate(context) is not { } at || !CCode.IsC(at.Source) || CCode.Includes(at.Source, "iso646.h")) return null;

        var (source, number, line) = at;
        var hits = Word().Matches(CodeText.Mask(line, Syntax.CLike));
        if (hits.Count == 0) return null;

        var corrected = CCode.ReplaceEach(line, hits, hit => hit.Groups["word"].Value switch
        {
            "and" => "&&",
            "or" => "||",
            _ => "!",
        });

        return LocalFix.ReplaceLine(
            Id, "Use &&, || and ! for and, or and not",
            "`and`, `or` and `not` are Python. C writes them `&&`, `||` and `!`.",
            source.Path, number, corrected);
    }
}

/// <summary><c>if x &gt; 1 {</c> - in C the condition needs its brackets.</summary>
public sealed partial class CConditionParentheses : ILocalFixRule
{
    public string Id => "c-condition-parentheses";

    [GeneratedRegex(@"^(?<lead>\s*(?:\}\s*)?(?:else\s+)?)(?<keyword>if|while|switch)\s+(?<condition>[^({\s][^{;]*?)\s*(?<brace>\{?)$")]
    private static partial Regex Condition();

    [GeneratedRegex(@"^expected '\(' before")]
    private static partial Regex GccMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        var recognised = CCode.IsMsvc(error, "C2061", "C2059", "C2143", "C2146") || CCode.GccMessage(error, GccMessage()) is not null;

        if (!recognised || CCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var (code, tail) = CodeText.SplitComment(line, Syntax.CLike);

        if (Condition().Match(code) is not { Success: true } match) return null;

        var brace = match.Groups["brace"].Length > 0 ? " {" : "";

        return LocalFix.ReplaceLine(
            Id, $"Put the {match.Groups["keyword"].Value} condition in brackets",
            "In C the condition of `if`, `while` and `switch` always goes in brackets.",
            source.Path, number,
            $"{match.Groups["lead"].Value}{match.Groups["keyword"].Value} ({match.Groups["condition"].Value}){brace}{tail}");
    }
}

/// <summary><c>C2001: newline in string literal</c> / <c>missing terminating " character</c>.</summary>
/// <remarks>
/// The closing quote goes before whatever finishes the statement - one <c>)</c> for each bracket
/// still open before the string, then the semicolon. When the end of the line is not shaped like
/// that, where the string was meant to end is a guess, and nothing is offered.
/// </remarks>
public sealed partial class CUnterminatedString : ILocalFixRule
{
    public string Id => "c-unterminated-string";

    [GeneratedRegex(@"^missing terminating "" character$")]
    private static partial Regex GccMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        var recognised = CCode.IsMsvc(error, "C2001") || CCode.GccMessage(error, GccMessage()) is not null;

        if (!recognised || CCode.Locate(context) is not { } at) return null;
        if (BraceRules.CloseString(at.Line, Syntax.CLike) is not { } corrected) return null;

        return LocalFix.ReplaceLine(Id, "Close the string", BraceRules.CloseStringExplanation, at.Source.Path, at.Number, corrected);
    }
}

/// <summary><c>C2143: missing ')' before ';'</c> - a call or condition left open at the end of the statement.</summary>
public sealed partial class CMissingClosingParenthesis : ILocalFixRule
{
    public string Id => "c-missing-closing-parenthesis";

    [GeneratedRegex(@"^syntax error: missing '\)' before ';'$")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"^expected '\)' before ';' token$")]
    private static partial Regex GccMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        var recognised = CCode.MsvcMessage(error, "C2143", MsvcMessage()) is not null || CCode.GccMessage(error, GccMessage()) is not null;

        if (!recognised || CCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.CLike);
        var (code, _) = CodeText.SplitComment(masked, Syntax.CLike);

        if (!code.TrimEnd().EndsWith(';')) return null;
        if (code.Count(c => c == '(') - code.Count(c => c == ')') != 1) return null;

        var semicolon = code.LastIndexOf(';');

        return LocalFix.ReplaceLine(
            Id, "Add the missing closing bracket",
            $"A `(` on line {number} is never closed - the statement ends with the semicolon first.",
            source.Path, number, line[..semicolon] + ")" + line[semicolon..]);
    }
}

/// <summary><c>C2059: syntax error: '}'</c> - one more closing brace than opening ones.</summary>
public sealed partial class CExtraClosingBrace : ILocalFixRule
{
    public string Id => "c-extra-closing-brace";

    [GeneratedRegex(@"^expected (?:identifier or '\(' |declaration or statement |declaration |unqualified-id )?before '(?:\}|\w+)'(?: token)?$")]
    private static partial Regex GccMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        var recognised = (CCode.IsMsvc(error, "C2059") && Regex.IsMatch(error.Message ?? "", @"^syntax error: '(?:\}|return|if|for|while|else|do|switch)'$")) ||
                         CCode.GccMessage(error, GccMessage()) is not null;

        if (!recognised || context.Read(context.Frame?.File) is not { } source || !CCode.IsNative(source)) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var depth = 0;
        var stray = new List<int>();

        for (var i = 0; i < masked.Count; i++)
        {
            foreach (var c in masked[i])
            {
                if (c == '{') depth++;
                else if (c == '}' && depth == 0) stray.Add(i);
                else if (c == '}') depth--;
            }
        }

        if (depth != 0 || stray is not [var extra] || masked[extra].Trim() != "}") return null;

        // A block closed too soon leaves the lines after it outside every function, and then the brace at the bottom is the one that
        // closes nothing. The brace to remove is the early one: indented deeper than the bottom one, with code stranded below it.
        var depths = Brackets.BraceDepths(masked);
        var early = -1;

        for (var i = extra - 1; i >= 0; i--)
        {
            if (masked[i].Trim().Length == 0) continue;

            if (masked[i].Trim() == "}" && depths[i + 1] == 0)
            {
                if (i < extra - 1 && CodeText.Indentation(source.Lines[i]).Length > CodeText.Indentation(source.Lines[extra]).Length) early = i;
                break;
            }

            if (depths[i] != 0) break;
        }

        if (early >= 0)
        {
            return CCode.RemoveLine(
                Id, $"Remove the extra }} on line {early + 1}",
                $"The `}}` on line {early + 1} closes its block too soon: the lines after it, down to line {extra + 1}, are left outside it, " +
                "and the last `}` then has nothing to close.",
                source.Path, early + 1);
        }

        return CCode.RemoveLine(
            Id, $"Remove the extra }} on line {extra + 1}",
            $"The file has one more `}}` than `{{`. The one on line {extra + 1} closes nothing - every block is already closed before it.",
            source.Path, extra + 1);
    }
}

/// <summary><c>C2628 ... (did you forget a ';'?)</c> - a struct definition without the semicolon after its brace.</summary>
public sealed partial class CStructSemicolon : ILocalFixRule
{
    public string Id => "c-struct-semicolon";

    [GeneratedRegex(@"^'\w+' followed by '\w+' is illegal \(did you forget a ';'\?\)$")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"^expected ';', identifier or '\(' before ")]
    private static partial Regex GccMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        var recognised = CCode.MsvcMessage(error, "C2628", MsvcMessage()) is not null || CCode.GccMessage(error, GccMessage()) is not null;

        if (!recognised || CCode.Locate(context) is not { } at) return null;

        var (source, number, _) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        var k = masked[number - 1].TrimEnd().EndsWith('}') ? number - 1 : number - 2;
        while (k >= 0 && masked[k].Trim().Length == 0) k--;
        if (k < 0 || !masked[k].TrimEnd().EndsWith('}')) return null;

        // Walk back to the brace this one closes, and check it opened a struct, union or enum.
        var depth = 0;
        var opener = -1;

        for (var i = k; i >= 0 && opener < 0; i--)
        {
            var row = masked[i];
            for (var c = (i == k ? row.LastIndexOf('}') : row.Length - 1); c >= 0; c--)
            {
                if (row[c] == '}') depth++;
                else if (row[c] == '{' && --depth == 0)
                {
                    opener = i;
                    break;
                }
            }
        }

        if (opener < 0 || Regex.Match(masked[opener], @"\b(?<keyword>struct|union|enum|class)\b") is not { Success: true } kind) return null;

        var keyword = kind.Groups["keyword"].Value;
        var (code, tail) = CodeText.SplitComment(source.Lines[k], Syntax.CLike);
        var trimmed = code.TrimEnd();

        return LocalFix.ReplaceLine(
            Id, $"Add the semicolon after the {keyword}",
            $"A {keyword} definition ends with a semicolon after its closing brace, and the one ending on line {k + 1} has none - " +
            "so the compiler reads the next line as part of it.",
            source.Path, k + 1, trimmed + ";" + code[trimmed.Length..] + tail);
    }
}

/// <summary><c>#define SIZE 5;</c> - the semicolon is pasted in wherever the macro is used.</summary>
/// <remarks>
/// gcc reports the error at the #define itself, MSVC where the macro was used. Either way the edit is
/// the same one, on the #define.
/// </remarks>
public sealed partial class CDefineSemicolon : ILocalFixRule
{
    public string Id => "c-define-semicolon";

    [GeneratedRegex(@"^\s*#\s*define\s+(?<name>[A-Za-z_]\w*)(?:\([^)]*\))?\s+.*?(?<semicolon>;)\s*$")]
    private static partial Regex Define();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CCode.IsCompiler(context.Error) || CCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var lines = source.Lines;
        int index;
        Match define;

        if (Define().Match(line) is { Success: true } here)
        {
            (index, define) = (number - 1, here);
        }
        else
        {
            var used = CodeText.Identifiers([CodeText.Mask(line, Syntax.CLike)]);
            var macros = Enumerable.Range(0, lines.Count)
                .Select(i => (Index: i, Match: Define().Match(lines[i])))
                .Where(x => x.Match.Success && used.Contains(x.Match.Groups["name"].Value))
                .ToList();

            if (macros is not [var only]) return null;
            (index, define) = (only.Index, only.Match);
        }

        var name = define.Groups["name"].Value;
        var semicolon = define.Groups["semicolon"].Index;

        return LocalFix.ReplaceLine(
            Id, $"Remove the semicolon from #define {name}",
            $"`#define` pastes its text in wherever `{name}` is written - the semicolon too - so inside `[...]` or an expression `{name}` " +
            "leaves a stray `;` behind. A #define does not end with a semicolon.",
            source.Path, index + 1, lines[index][..semicolon] + lines[index][(semicolon + 1)..]);
    }
}

/// <summary><c>'else' without a previous 'if'</c> / <c>C2181</c> - from <c>if (x); {</c>.</summary>
public sealed class CIfSemicolon : ILocalFixRule
{
    public string Id => "c-if-semicolon";

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        var recognised = error.LanguageId == "gcc" && error.Message == "'else' without a previous 'if'" || CCode.IsMsvc(error, "C2181");

        if (!recognised || CCode.Locate(context) is not { } at) return null;

        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.CLike);
        if (BraceRules.IfSemicolon(at.Source.Lines, masked, at.Number - 1) is not { } fix) return null;

        return LocalFix.ReplaceLine(Id, BraceRules.IfSemicolonTitle, BraceRules.IfSemicolonExplanation, at.Source.Path, fix.Line + 1, fix.Corrected);
    }
}

/// <summary>gcc's <c>stray '\342' in program</c> - the first byte of a curly quote.</summary>
public sealed class CSmartQuotes : ILocalFixRule
{
    public string Id => "c-smart-quotes";

    public LocalFix? Propose(LocalFixContext context) =>
        context.Error.LanguageId == "gcc" && (context.Error.Message ?? "").StartsWith("stray ", StringComparison.Ordinal) &&
        context.Frame is { Line: { } number } frame && context.Read(frame.File) is { } source
            ? Guards.StraightenFix(Id, source, number)
            : null;
}

/// <summary><c>parcel p;</c> for a <c>struct parcel</c> - C needs the <c>struct</c> keyword.</summary>
public sealed partial class CStructKeyword : ILocalFixRule
{
    public string Id => "c-struct-keyword";

    [GeneratedRegex(@"^'(?<name>\w+)': undeclared identifier$")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"^unknown type name '(?<name>\w+)'")]
    private static partial Regex GccMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        var message = CCode.MsvcMessage(error, "C2065", MsvcMessage()) ?? CCode.GccMessage(error, GccMessage());

        if (message is null || CCode.Locate(context) is not { } at || !CCode.IsC(at.Source)) return null;

        var (source, number, line) = at;
        var name = Regex.Escape(message.Groups["name"].Value);
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        if (!masked.Any(text => Regex.IsMatch(text, $@"\bstruct\s+{name}\s*\{{"))) return null;
        if (masked.Any(text => Regex.IsMatch(text, $@"\btypedef\b.*\b{name}\s*;") || Regex.IsMatch(text, $@"\}}\s*{name}\s*;"))) return null;

        var use = Regex.Match(masked[number - 1], $@"^(?<lead>\s*(?:(?:const|static)\s+)*)(?<name>{name})(?=\s*\**\s*[A-Za-z_]\w*\s*[=;\[,)])");
        if (!use.Success) return null;

        var at_ = use.Groups["name"].Index;

        return LocalFix.ReplaceLine(
            Id, $"Write struct {message.Groups["name"].Value}",
            $"In C a struct's name is only a type together with the word `struct` - `struct {message.Groups["name"].Value}`. " +
            "(C++ drops that rule, which is why it often looks right.)",
            source.Path, number, line[..at_] + "struct " + line[at_..]);
    }
}

/// <summary><c>string name = "Ethan";</c> in C, which has no string type.</summary>
public sealed partial class CStringType : ILocalFixRule
{
    public string Id => "c-string-type";

    [GeneratedRegex(@"^'string': undeclared identifier$")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"^unknown type name 'string'")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^(?<lead>\s*)string\s+(?<name>[A-Za-z_]\w*)\s*=\s*(?<value>.+?)\s*;(?<tail>.*)$")]
    private static partial Regex Declaration();

    [GeneratedRegex(@"^""(?:[^""\\]|\\.)*""$")]
    private static partial Regex Literal();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        var recognised = CCode.MsvcMessage(error, "C2065", MsvcMessage()) is not null || CCode.GccMessage(error, GccMessage()) is not null;

        if (!recognised || CCode.Locate(context) is not { } at || !CCode.IsC(at.Source)) return null;

        var (source, number, line) = at;
        if (!Regex.IsMatch(CodeText.Mask(line, Syntax.CLike), @"^\s*string\s+\w")) return null;
        if (Declaration().Match(line) is not { Success: true } declaration) return null;

        var name = declaration.Groups["name"].Value;
        var value = declaration.Groups["value"].Value;
        var lead = declaration.Groups["lead"].Value;
        var tail = declaration.Groups["tail"].Value;

        var literal = Literal().IsMatch(value);
        var corrected = literal ? $"{lead}char {name}[] = {value};{tail}" : $"{lead}const char *{name} = {value};{tail}";

        return LocalFix.ReplaceLine(
            Id, literal ? $"Declare {name} as a char array" : $"Declare {name} as const char *",
            "C has no `string` type - that is C++ or C#. Text in C is an array of `char`" +
            (literal ? ", and the string it starts with sets how big the array is." : ", usually handled through a `const char *`."),
            source.Path, number, corrected);
    }
}

/// <summary><c>int x = 2;</c> a second time in the same block, where an assignment was meant.</summary>
public sealed partial class CRedefinition : ILocalFixRule
{
    public string Id => "c-redefinition";

    [GeneratedRegex(@"^'(?<name>\w+)': redefinition; multiple initialization$")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"^(?:redefinition|redeclaration) of '(?:[^']*?[\s*&])?(?<name>\w+)'$")]
    private static partial Regex GccMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        var message = CCode.MsvcMessage(error, "C2374", MsvcMessage()) ?? CCode.GccMessage(error, GccMessage());

        if (message is null || CCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var name = Regex.Escape(message.Groups["name"].Value);

        var redeclared = Regex.Match(line, $@"^(?<lead>\s*)(?<type>(?!return\b)[A-Za-z_][\w ]*?)\s*(?<stars>\**)\s*(?<name>{name})\s*=\s*(?<value>[^;]+?)\s*;(?<tail>.*)$");
        if (!redeclared.Success || redeclared.Groups["type"].Value.Contains("const")) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var (first, _) = CCode.EnclosingFunction(masked, number - 1);
        var type = Regex.Replace(redeclared.Groups["type"].Value.Trim(), @"\s+", @"\s+");
        var earlier = new Regex($@"(?<![\w]){type}\s*{Regex.Escape(redeclared.Groups["stars"].Value)}\s*{name}\s*[=;,\[]");

        var original = Enumerable.Range(first, number - 1 - first).LastOrDefault(i => earlier.IsMatch(masked[i]), -1);
        if (original < 0) return null;

        return LocalFix.ReplaceLine(
            Id, $"Assign to {message.Groups["name"].Value} instead of declaring it again",
            $"`{message.Groups["name"].Value}` is already declared on line {original + 1}. Writing the type again declares a " +
            "second variable with the same name, which one block cannot hold; without the type, the line gives the existing one a new value.",
            source.Path, number,
            $"{redeclared.Groups["lead"].Value}{message.Groups["name"].Value} = {redeclared.Groups["value"].Value};{redeclared.Groups["tail"].Value}");
    }
}

/// <summary><c>redefinition of 'struct point'</c> in a header included twice - a header with no include guard.</summary>
public sealed partial class CHeaderGuard : ILocalFixRule
{
    public string Id => "c-header-guard";

    // A type defined twice is an error before C23; from C23 an identical one is allowed, and a variable defined twice is what
    // still fails - so both are read.
    [GeneratedRegex(@"^redefinition of '(?:(?:struct|union|enum|class) )?(?<name>\w+)'$")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^'(?<name>\w+)': (?:'(?:struct|union|enum|class)' type redefinition|redefinition(?:; multiple initialization)?)$")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"^\s*#\s*(?:pragma\s+once|ifndef\s+\w+)")]
    private static partial Regex Guard();

    public LocalFix? Propose(LocalFixContext context)
    {
        if ((CCode.GccMessage(context.Error, GccMessage()) ?? CCode.MsvcMessage(context.Error, "C2011", MsvcMessage()) ??
             CCode.MsvcMessage(context.Error, "C2374", MsvcMessage()) ?? CCode.MsvcMessage(context.Error, "C2086", MsvcMessage())) is null) return null;
        if (context.Frame?.File is not { } file || context.Read(file) is not { } source) return null;

        if (Path.GetExtension(source.Path).ToLowerInvariant() is not (".h" or ".hpp" or ".hh" or ".hxx")) return null;
        if (!source.EndsWithNewline || source.Lines.FirstOrDefault(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith("//", StringComparison.Ordinal)) is not { } first || Guard().IsMatch(first)) return null;

        var guard = Regex.Replace(Path.GetFileName(source.Path).ToUpperInvariant(), @"[^A-Z0-9]", "_");
        if (char.IsDigit(guard[0])) guard = "_" + guard;

        var body = source.Lines.ToList();
        while (body.Count > 0 && body[^1].Trim().Length == 0) body.RemoveAt(body.Count - 1);

        return new LocalFix
        {
            RuleId = Id,
            Title = $"Guard {Path.GetFileName(source.Path)} against being included twice",
            Explanation =
                $"`{Path.GetFileName(source.Path)}` is included more than once in the same file - directly, and again through another header - " +
                "so everything in it is defined twice, and a second definition of the same type is an error. An include guard skips the " +
                $"header's contents the second time: `{guard}` is defined the first time through, and `#ifndef` sees it after that.",
            File = source.Path, StartLine = 1, RemoveCount = source.Count,
            NewLines = [$"#ifndef {guard}", $"#define {guard}", "", .. body, "", $"#endif"],
        };
    }
}
