using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>What the C construct rules share: which compiler said it, the line it is about, and blocks.</summary>
/// <remarks>
/// MSVC reports a line and no column; gcc reports both, and ASan reports the line a crash happened
/// on. So these rules find their place from the line and the shape of the code on it, and change
/// nothing when that shape does not pin down one edit.
/// </remarks>
internal static partial class CCode
{
    public static bool IsCompiler(ParsedError error) =>
        error.LanguageId is "msvc" or "gcc" && error.ErrorCode?.StartsWith("CS", StringComparison.Ordinal) != true;

    public static bool IsMsvc(ParsedError error, params string[] codes) =>
        error.LanguageId == "msvc" && codes.Contains(error.ErrorCode);

    public static Match? MsvcMessage(ParsedError error, string code, Regex message) =>
        error.LanguageId == "msvc" && error.ErrorCode == code && message.Match(error.Message ?? "") is { Success: true } m ? m : null;

    public static Match? GccMessage(ParsedError error, Regex message) =>
        error.LanguageId == "gcc" && message.Match(error.Message ?? "") is { Success: true } m ? m : null;

    public static bool IsNative(SourceFile source) =>
        Path.GetExtension(source.Path).ToLowerInvariant() is ".c" or ".cpp" or ".cc" or ".cxx" or ".c++" or ".h" or ".hpp";

    public static (SourceFile Source, int Number, string Line)? Locate(LocalFixContext context)
    {
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (!IsNative(source) || source.Line(number) is not { } line) return null;

        return (source, number, line);
    }

    public static bool Includes(SourceFile source, string header) =>
        source.Lines.Any(line => Msvc.Include().Match(line) is { Success: true } m &&
                                 m.Groups["header"].Value.Equals(header, StringComparison.OrdinalIgnoreCase));

    /// <summary>Brace depth at the start of each masked line; one extra entry for the end of the file.</summary>
    public static int[] DepthAtStart(IReadOnlyList<string> masked)
    {
        var depths = new int[masked.Count + 1];
        var depth = 0;

        for (var i = 0; i < masked.Count; i++)
        {
            depths[i] = depth;

            foreach (var c in masked[i])
            {
                if (c == '{') depth++;
                else if (c == '}') depth--;
            }
        }

        depths[masked.Count] = depth;
        return depths;
    }

    /// <summary>The header line and closing line (0-based) of the top-level function around a line.</summary>
    public static (int Header, int End) EnclosingFunction(IReadOnlyList<string> masked, int index)
    {
        var depths = DepthAtStart(masked);

        var start = index;
        while (start > 0 && depths[start] > 0) start--;
        if (masked[start].Trim() == "{" && start > 0) start--;

        var end = index;
        while (end + 1 < masked.Count && depths[end + 1] > 0) end++;

        return (start, end);
    }

    /// <summary>The index of the bracket closing the one at <paramref name="open"/> on the same line.</summary>
    public static int? Matching(string masked, int open)
    {
        var depth = 0;

        for (var i = open; i < masked.Length; i++)
        {
            if (masked[i] == '(') depth++;
            else if (masked[i] == ')' && --depth == 0) return i;
        }

        return null;
    }

    public static string Replace(string line, IEnumerable<Match> hits, Func<Match, string> replacement)
    {
        foreach (var hit in hits.OrderByDescending(h => h.Index))
            line = line[..hit.Index] + replacement(hit) + line[(hit.Index + hit.Length)..];

        return line;
    }

    public static LocalFix RemoveLine(string ruleId, string title, string explanation, string file, int line) => new()
    {
        RuleId = ruleId, Title = title, Explanation = explanation, File = file,
        StartLine = line, RemoveCount = 1, NewLines = [],
    };
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
            source.Path, number, CCode.Replace(line, hits, _ => "else if"));
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
        if (CCode.Locate(context) is not { } at || !Msvc.IsC(at.Source) || CCode.Includes(at.Source, "iso646.h")) return null;

        var (source, number, line) = at;
        var hits = Word().Matches(CodeText.Mask(line, Syntax.CLike));
        if (hits.Count == 0) return null;

        var corrected = CCode.Replace(line, hits, hit => hit.Groups["word"].Value switch
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

        if (!recognised || CCode.Locate(context) is not { } at || OpenQuote(at.Line) is not { } quote) return null;

        var (source, number, line) = at;
        var before = CodeText.Mask(line[..quote], Syntax.CLike);
        var open = before.Count(c => c == '(') - before.Count(c => c == ')');
        if (open < 0) return null;

        var rest = line[(quote + 1)..].TrimEnd();
        var cut = rest.Length;
        var semicolon = cut > 0 && rest[cut - 1] == ';';
        if (semicolon) cut--;
        if (!semicolon && open == 0) return null;

        for (var k = 0; k < open; k++)
        {
            while (cut > 0 && rest[cut - 1] == ' ') cut--;
            if (cut == 0 || rest[cut - 1] != ')') return null;
            cut--;
        }

        return LocalFix.ReplaceLine(
            Id, "Close the string",
            $"The string on line {number} has no closing `\"`, so C reads the rest of the line as part of it.",
            source.Path, number, line[..(quote + 1)] + rest[..cut] + "\"" + rest[cut..]);
    }

    /// <summary>Where the one string on the line that never closes begins.</summary>
    private static int? OpenQuote(string line)
    {
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '/' && i + 1 < line.Length && line[i + 1] is '/' or '*') return null;
            if (c is not ('"' or '\'')) continue;

            var start = i;
            for (i++; i < line.Length && line[i] != c; i++)
                if (line[i] == '\\') i++;

            if (i >= line.Length) return c == '"' ? start : null;
        }

        return null;
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

    [GeneratedRegex(@"^expected (?:identifier or '\(' |declaration or statement )?before '\}' token$")]
    private static partial Regex GccMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        var recognised = (CCode.IsMsvc(error, "C2059") && error.Message == "syntax error: '}'") || CCode.GccMessage(error, GccMessage()) is not null;

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

        return CCode.RemoveLine(
            Id, $"Remove the extra }} on line {extra + 1}",
            $"The file has one more `}}` than `{{`. The one on line {extra + 1} closes nothing - every block is already closed before it.",
            source.Path, extra + 1);
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

        if (!recognised || CCode.Locate(context) is not { } at || !Msvc.IsC(at.Source)) return null;

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

        if (message is null || CCode.Locate(context) is not { } at || !Msvc.IsC(at.Source)) return null;

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

/// <summary><c>-&gt;</c> on a struct, or <c>.</c> on a pointer to one.</summary>
public sealed partial class CMemberOperator : ILocalFixRule
{
    public string Id => "c-member-operator";

    [GeneratedRegex(@"^'->(?<member>\w+)': left operand has '(?:struct|union|class)' type, use '\.'$")]
    private static partial Regex ArrowOnValue();

    [GeneratedRegex(@"^'\.(?<member>\w+)': left operand points to '(?:struct|union|class)', use '->'$")]
    private static partial Regex DotOnPointer();

    [GeneratedRegex(@"^invalid type argument of '->' \(have '[^']+'\)$")]
    private static partial Regex GccArrow();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if (CCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.CLike);

        string wrong, right;
        MatchCollection hits;

        if ((CCode.MsvcMessage(error, "C2232", ArrowOnValue()) ?? CCode.GccMessage(error, GccArrow())) is { } arrow)
        {
            var member = arrow.Groups["member"].Success ? Regex.Escape(arrow.Groups["member"].Value) : @"\w+";
            hits = Regex.Matches(masked, $@"->(?=\s*{member}\b)");
            (wrong, right) = ("->", ".");
        }
        else if (CCode.MsvcMessage(error, "C2231", DotOnPointer()) is { } dot)
        {
            hits = Regex.Matches(masked, $@"(?<=[\w\])])\.(?=\s*{Regex.Escape(dot.Groups["member"].Value)}\b)");
            (wrong, right) = (".", "->");
        }
        else
        {
            return null;
        }

        if (hits.Count != 1) return null;

        return LocalFix.ReplaceLine(
            Id, $"Use {right} instead of {wrong}",
            wrong == "->"
                ? "`->` reaches a member through a pointer. This is the struct itself, not a pointer to it, so it is `.`."
                : "This is a pointer to a struct, and a member is reached through a pointer with `->`.",
            source.Path, number, CCode.Replace(line, hits, _ => right));
    }
}

/// <summary><c>for (i = 0; ...)</c> with <c>i</c> never declared.</summary>
/// <remarks>Declared in the loop only when nothing outside the loop uses it - otherwise it has to live longer.</remarks>
public sealed partial class CForCounter : ILocalFixRule
{
    public string Id => "c-for-counter";

    [GeneratedRegex(@"^'(?<name>\w+)': undeclared identifier$")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"^'(?<name>\w+)' undeclared")]
    private static partial Regex GccMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        var message = CCode.MsvcMessage(error, "C2065", MsvcMessage()) ?? CCode.GccMessage(error, GccMessage());

        if (message is null || CCode.Locate(context) is not { } at) return null;

        var (source, number, _) = at;
        var name = message.Groups["name"].Value;
        var escaped = Regex.Escape(name);
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var header = new Regex($@"\bfor\s*(?<open>\()\s*(?<name>{escaped})\s*=(?!=)");

        for (var k = number - 1; k >= Math.Max(0, number - 16); k--)
        {
            if (header.Match(masked[k]) is not { Success: true } loop) continue;
            if (CCode.Matching(masked[k], loop.Groups["open"].Index) is not { } close) return null;
            if (BodyEnd(masked, k, close + 1) is not { } end || number - 1 > end) return null;

            var (first, last) = CCode.EnclosingFunction(masked, k);
            var word = new Regex($@"(?<![\w.]){escaped}(?!\w)");

            for (var i = first + 1; i <= last; i++)
                if ((i < k || i > end) && word.IsMatch(masked[i])) return null;

            var column = loop.Groups["name"].Index;

            return LocalFix.ReplaceLine(
                Id, $"Declare {name} in the loop",
                $"`{name}` is the loop counter but is never declared. Nothing outside the loop uses it, so declaring it in the loop with `int` is all it needs.",
                source.Path, k + 1, source.Lines[k][..column] + "int " + source.Lines[k][column..]);
        }

        return null;
    }

    private static int? BodyEnd(IReadOnlyList<string> masked, int line, int after)
    {
        var rest = masked[line][after..];
        var brace = rest.IndexOf('{');

        if (brace < 0) return rest.Trim().Length > 0 ? line : line + 1 < masked.Count ? line + 1 : null;

        var depth = 0;

        for (var i = line; i < masked.Count; i++)
        {
            for (var c = i == line ? after + brace : 0; c < masked[i].Length; c++)
            {
                if (masked[i][c] == '{') depth++;
                else if (masked[i][c] == '}' && --depth == 0) return i;
            }
        }

        return null;
    }
}

/// <summary>A function called above its definition: <c>C2371 redefinition; different basic types</c> / <c>conflicting types</c>.</summary>
/// <remarks>gcc 14 and later stop earlier, at <c>implicit declaration of function</c> on the call itself.</remarks>
public sealed partial class CFunctionPrototype : ILocalFixRule
{
    public string Id => "c-function-prototype";

    [GeneratedRegex(@"^'(?<name>\w+)': redefinition; different basic types$")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"^conflicting types for '(?<name>\w+)'")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^implicit declaration of function '(?<name>\w+)'")]
    private static partial Regex GccImplicit();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        var message = CCode.MsvcMessage(error, "C2371", MsvcMessage()) ?? CCode.GccMessage(error, GccMessage()) ??
                      CCode.GccMessage(error, GccImplicit());

        if (message is null || context.Read(context.Frame?.File) is not { } source || !CCode.IsNative(source)) return null;

        var name = message.Groups["name"].Value;
        var escaped = Regex.Escape(name);
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var depths = CCode.DepthAtStart(masked);

        var signature = new Regex($@"^\s*(?<sig>(?:(?:static|inline)\s+)*[A-Za-z_][\w\s]*?[\s*]{escaped}\s*\([^()]*\))\s*\{{?\s*$");
        var definition = -1;
        Match? found = null;

        for (var i = 0; i < masked.Count && definition < 0; i++)
        {
            if (depths[i] != 0 || signature.Match(masked[i]) is not { Success: true } m) continue;

            var opensHere = masked[i].TrimEnd().EndsWith('{');
            var opensNext = i + 1 < masked.Count && masked[i + 1].Trim().StartsWith('{');
            if (!opensHere && !opensNext) continue;

            (definition, found) = (i, m);
        }

        if (found is null) return null;

        var call = new Regex($@"(?<![\w.>]){escaped}\s*\(");
        var use = Enumerable.Range(0, definition).FirstOrDefault(i => depths[i] > 0 && call.IsMatch(masked[i]), -1);
        if (use < 0) return null;

        var (header, _) = CCode.EnclosingFunction(masked, use);
        var group = found.Groups["sig"];
        var prototype = source.Lines[definition].Substring(group.Index, group.Length).Trim() + ";";

        return LocalFix.Insert(
            Id, $"Declare {name} before it is used",
            $"`{name}` is called on line {use + 1} but only defined on line {definition + 1}. C reads from the top, so at the call " +
            $"it had to guess what `{name}` returns - and the real definition then contradicts the guess. A declaration above " +
            "the first use tells it the truth in time.",
            source.Path, header + 1, [prototype]);
    }
}

/// <summary><c>name = "Ethan";</c> for a char array, which C cannot assign to.</summary>
public sealed partial class CArrayAssignString : ILocalFixRule
{
    public string Id => "c-array-assign-string";

    [GeneratedRegex(@"^'=': left operand must be l-value$")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"^assignment to expression with array type$")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^(?<lead>\s*)(?<name>[A-Za-z_]\w*)\s*=\s*(?<value>""(?:[^""\\]|\\.)*"")\s*;(?<tail>.*)$")]
    private static partial Regex Assignment();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        var recognised = CCode.MsvcMessage(error, "C2106", MsvcMessage()) is not null || CCode.GccMessage(error, GccMessage()) is not null;

        if (!recognised || CCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (Assignment().Match(line) is not { Success: true } assignment) return null;

        // strcpy without its header is a second problem. With the header already there, this is one edit.
        if (!CCode.Includes(source, "string.h")) return null;

        var name = assignment.Groups["name"].Value;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var declaration = masked.Select(text => Regex.Match(text, $@"\bchar\s+{Regex.Escape(name)}\s*\[\s*(?<size>\d+)\s*\]")).FirstOrDefault(m => m.Success);
        if (declaration is null) return null;

        var value = assignment.Groups["value"].Value;
        var length = Regex.Replace(value[1..^1], @"\\.", "x").Length;
        var size = int.Parse(declaration.Groups["size"].Value);

        // A copy that does not fit would turn a compile error into a buffer overflow.
        if (length + 1 > size) return null;

        return LocalFix.ReplaceLine(
            Id, $"Copy the text into {name} with strcpy",
            $"A C array cannot be assigned to - `=` only works where it is declared. `strcpy` copies the text in instead, and " +
            $"{value} with its terminating zero is {length + 1} characters, which fits the {size} that `{name}` has.",
            source.Path, number, $"{assignment.Groups["lead"].Value}strcpy({name}, {value});{assignment.Groups["tail"].Value}");
    }
}

/// <summary><c>int x = 2;</c> a second time in the same block, where an assignment was meant.</summary>
public sealed partial class CRedefinition : ILocalFixRule
{
    public string Id => "c-redefinition";

    [GeneratedRegex(@"^'(?<name>\w+)': redefinition; multiple initialization$")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"^redefinition of '(?<name>\w+)'$")]
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

/// <summary><c>#include &lt;iostream&gt;</c> in a C file.</summary>
public sealed partial class CIostreamInC : ILocalFixRule
{
    public string Id => "c-iostream-in-c";

    [GeneratedRegex(@"^iostream: No such file or directory$|^'iostream' file not found$")]
    private static partial Regex GccMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        var recognised =
            (CCode.IsMsvc(error, "C1189") && (error.Message ?? "").Contains("expected C++ compiler", StringComparison.Ordinal)) ||
            (CCode.IsMsvc(error, "C1083") && (error.Message ?? "").StartsWith("Cannot open include file: 'iostream'", StringComparison.Ordinal)) ||
            CCode.GccMessage(error, GccMessage()) is not null;

        if (!recognised) return null;

        // MSVC reports this one from inside its own C++ headers, so the file that included them is
        // not in the error. It is the one C file in the project that includes <iostream>.
        var source = context.Read(context.Frame?.File) is { } framed && Msvc.IsC(framed)
            ? framed
            : OnlyCFileIncludingIostream(context.SourceRoot);

        if (source is null || !Msvc.IsC(source)) return null;

        var index = source.Lines.ToList().FindIndex(l => Msvc.Include().Match(l) is { Success: true } m && m.Groups["header"].Value == "iostream");
        if (index < 0) return null;

        const string explanation = "<iostream> is C++ - a C compiler cannot use it. In C, `printf` and the rest of console " +
                                   "input and output come from <stdio.h>.";

        return CCode.Includes(source, "stdio.h")
            ? CCode.RemoveLine(Id, "Remove #include <iostream>", explanation, source.Path, index + 1)
            : LocalFix.ReplaceLine(Id, "Include <stdio.h> instead of <iostream>", explanation, source.Path, index + 1, "#include <stdio.h>");
    }

    private static SourceFile? OnlyCFileIncludingIostream(string? root)
    {
        if (root is null || !Directory.Exists(root)) return null;

        var matches = Directory.EnumerateFiles(root, "*.c", SearchOption.TopDirectoryOnly)
            .Take(200)
            .Select(SourceFile.Read)
            .OfType<SourceFile>()
            .Where(file => CCode.Includes(file, "iostream"))
            .Take(2)
            .ToList();

        return matches is [var only] ? only : null;
    }
}

/// <summary><c>cout &lt;&lt; "hi";</c> in a C file, which only has printf.</summary>
public sealed partial class CCoutInC : ILocalFixRule
{
    public string Id => "c-cout-in-c";

    [GeneratedRegex(@"^'cout': undeclared identifier$")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"^'cout' undeclared")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^(?<lead>\s*)(?:std::)?cout\s*<<(?<parts>.+);(?<tail>\s*(?://.*)?)$")]
    private static partial Regex Statement();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        var recognised = CCode.MsvcMessage(error, "C2065", MsvcMessage()) is not null || CCode.GccMessage(error, GccMessage()) is not null;

        if (!recognised || CCode.Locate(context) is not { } at || !Msvc.IsC(at.Source)) return null;

        var (source, number, line) = at;
        if (Statement().Match(line) is not { Success: true } statement) return null;

        var parts = statement.Groups["parts"];
        var masked = CodeText.Mask(line, Syntax.CLike).Substring(parts.Index, parts.Length);
        var text = new System.Text.StringBuilder();
        var start = 0;

        foreach (var piece in masked.Split("<<").Select(p => (Masked: p, Start: 0)))
        {
            var raw = line.Substring(parts.Index + start, piece.Masked.Length).Trim();
            start += piece.Masked.Length + 2;

            if (raw is "endl" or "std::endl" or "'\\n'") text.Append("\\n");
            else if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"') text.Append(raw[1..^1].Replace("%", "%%"));
            else return null;
        }

        return LocalFix.ReplaceLine(
            Id, "Print it with printf",
            "`cout` is C++. A C program prints with `printf` from <stdio.h>.",
            source.Path, number, $"{statement.Groups["lead"].Value}printf(\"{text}\");{statement.Groups["tail"].Value}");
    }
}

/// <summary>AddressSanitizer's <c>attempting double-free</c>, where the same pointer is freed twice in a row.</summary>
public sealed partial class CDoubleFree : ILocalFixRule
{
    public string Id => "c-double-free";

    [GeneratedRegex(@"^\s*free\s*\(\s*(?<pointer>[A-Za-z_][\w.]*(?:->\w+)*)\s*\)\s*;\s*$")]
    private static partial Regex Free();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "gcc", ExceptionType: "attempting double-free" }) return null;
        if (CCode.Locate(context) is not { } at) return null;

        var (source, number, _) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (Free().Match(masked[number - 1]) is not { Success: true } second) return null;

        var pointer = second.Groups["pointer"].Value;
        var escaped = Regex.Escape(pointer);
        var depths = CCode.DepthAtStart(masked);
        var (first, _) = CCode.EnclosingFunction(masked, number - 1);

        for (var k = number - 2; k > first; k--)
        {
            // Below the second free's block level means another branch - the two frees may never both run.
            if (depths[k] < depths[number - 1] || masked[k].Contains('}')) return null;
            if (Regex.IsMatch(masked[k], $@"(?<![\w.>]){escaped}\s*=(?!=)")) return null;

            if (Free().Match(masked[k]) is { Success: true } earlier && earlier.Groups["pointer"].Value == pointer)
            {
                return CCode.RemoveLine(
                    Id, $"Remove the second free({pointer})",
                    $"`{pointer}` is freed on line {k + 1} and then again on line {number}. Memory can only be given back once - " +
                    "the second `free` corrupts the heap, which is what AddressSanitizer caught.",
                    source.Path, number);
            }
        }

        return null;
    }
}

/// <summary>AddressSanitizer's <c>stack-buffer-overflow</c> from a loop that runs past the end of an array.</summary>
public sealed partial class CArrayBoundLoop : ILocalFixRule
{
    public string Id => "c-array-bound-loop";

    [GeneratedRegex(@"(?<array>[A-Za-z_]\w*)\s*\[\s*(?<var>[A-Za-z_]\w*)\s*\]")]
    private static partial Regex IndexUse();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "gcc", ExceptionType: "stack-buffer-overflow" or "global-buffer-overflow" }) return null;
        if (CCode.Locate(context) is not { } at) return null;

        var (source, number, _) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        foreach (Match use in IndexUse().Matches(masked[number - 1]))
        {
            var array = Regex.Escape(use.Groups["array"].Value);
            var variable = Regex.Escape(use.Groups["var"].Value);

            var declaration = masked
                .Select(text => Regex.Match(text, $@"^\s*(?:(?:static|const)\s+)*[A-Za-z_][\w ]*?\s\**{array}\s*\[\s*(?<size>\d+|[A-Z_][A-Z0-9_]*)\s*\]"))
                .FirstOrDefault(m => m.Success);

            if (declaration is null) continue;

            var size = declaration.Groups["size"].Value;
            var loop = new Regex($@"\bfor\s*\(\s*(?:(?:int|long|short|unsigned|size_t)\s+)*{variable}\s*=\s*0\s*;\s*{variable}\s*(?<op><=|<)\s*(?<bound>[^;]+?)\s*;");

            for (var k = number - 1; k >= Math.Max(0, number - 16); k--)
            {
                if (loop.Match(masked[k]) is not { Success: true } header) continue;

                var op = header.Groups["op"];
                var bound = header.Groups["bound"];
                if (op.Value == "<" && bound.Value == size) return null;

                var original = source.Lines[k];

                return LocalFix.ReplaceLine(
                    Id, $"Stop the loop at the end of {use.Groups["array"].Value}: < {size}",
                    $"`{use.Groups["array"].Value}` has {size} elements, so its indexes run from 0 to one less than {size}. The loop on " +
                    $"line {k + 1} runs while {use.Groups["var"].Value} {op.Value} {bound.Value}, which writes past the end of the " +
                    "array into whatever memory sits next to it.",
                    source.Path, k + 1, original[..op.Index] + "< " + size + original[(bound.Index + bound.Length)..]);
            }
        }

        return null;
    }
}

/// <summary><c>C4477</c>: a printf conversion that does not match the argument - <c>%s</c> given an int.</summary>
/// <remarks>
/// A warning, but the kind that crashes: printf reads an int as an address and follows it. gcc
/// reports the same thing with a fix-it of its own; MSVC names the argument and its type, which is
/// enough to pick the one conversion that fits.
/// </remarks>
public sealed partial class CFormatSpecifier : ILocalFixRule
{
    public string Id => "c-format-specifier";

    [GeneratedRegex(@"^'(?<func>\w+)' : format string '(?<spec>[^']+)' requires an argument of type '(?<want>[^']+)', but variadic argument (?<arg>\d+) has type '(?<have>[^']+)'")]
    private static partial Regex Message();

    [GeneratedRegex(@"%(?:%|(?<flags>[-+ #0]*)(?<width>\*|\d+)?(?:\.(?<precision>\*|\d+))?(?<length>hh|h|ll|l|L|z|j|t|I64|I32|I)?(?<conversion>[diouxXeEfFgGaAcspn]))")]
    private static partial Regex Conversion();

    private static readonly Dictionary<string, string> Conversions = new(StringComparer.Ordinal)
    {
        ["int"] = "d", ["unsigned int"] = "u", ["long"] = "ld", ["unsigned long"] = "lu", ["__int64"] = "lld",
        ["unsigned __int64"] = "llu", ["long long"] = "lld", ["unsigned long long"] = "llu", ["double"] = "f",
        ["char *"] = "s", ["const char *"] = "s",
    };

    private static readonly Dictionary<string, int> FormatPosition = new(StringComparer.Ordinal)
    {
        ["printf"] = 0, ["printf_s"] = 0, ["fprintf"] = 1, ["fprintf_s"] = 1, ["sprintf"] = 1, ["sprintf_s"] = 2, ["snprintf"] = 2,
    };

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if (CCode.MsvcMessage(error, "C4477", Message()) is not { } message || CCode.Locate(context) is not { } at) return null;
        if (!Conversions.TryGetValue(message.Groups["have"].Value, out var fits)) return null;
        if (!FormatPosition.TryGetValue(message.Groups["func"].Value, out var position)) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.CLike);

        var calls = Regex.Matches(masked, $@"(?<![\w.]){Regex.Escape(message.Groups["func"].Value)}\s*(?<open>\()");
        if (calls.Count != 1) return null;

        var open = calls[0].Groups["open"].Index;
        if (CCode.Matching(masked, open) is not { } close) return null;

        var arguments = Arguments(masked, open + 1, close);
        if (position >= arguments.Count) return null;

        var (argumentStart, argumentEnd) = arguments[position];
        var literal = line[argumentStart..argumentEnd].Trim();
        if (literal.Length < 2 || literal[0] != '"' || literal[^1] != '"') return null;

        var literalStart = line.IndexOf('"', argumentStart);
        var conversions = Conversion().Matches(literal[1..^1]).Where(c => c.Value != "%%").ToList();

        // A * width or precision takes an argument of its own, and the count would be off by it.
        if (conversions.Any(c => c.Groups["width"].Value == "*" || c.Groups["precision"].Value == "*")) return null;

        var index = int.Parse(message.Groups["arg"].Value) - 1;
        if (index < 0 || index >= conversions.Count || conversions[index].Value != message.Groups["spec"].Value) return null;

        var wrong = conversions[index];
        var keepPrecision = fits is "f" or "s" && wrong.Groups["precision"].Success;
        var replacement = "%" + wrong.Groups["flags"].Value + wrong.Groups["width"].Value +
                          (keepPrecision ? "." + wrong.Groups["precision"].Value : "") + fits;

        var from = literalStart + 1 + wrong.Index;

        return LocalFix.ReplaceLine(
            Id, $"Change {wrong.Value} to {replacement}",
            $"`{wrong.Value}` tells {message.Groups["func"].Value} to expect a {message.Groups["want"].Value}, but the argument is a " +
            $"{message.Groups["have"].Value}. printf cannot tell - it reads the value as the wrong type, and for `%s` that means " +
            "following a number as if it were an address, which crashes.",
            source.Path, number, line[..from] + replacement + line[(from + wrong.Length)..]) with
        {
            ResolvesWarning = error.Message,
        };
    }

    private static List<(int Start, int End)> Arguments(string masked, int from, int to)
    {
        var arguments = new List<(int, int)>();
        var depth = 0;
        var start = from;

        for (var i = from; i < to; i++)
        {
            if (masked[i] is '(' or '[' or '{') depth++;
            else if (masked[i] is ')' or ']' or '}') depth--;
            else if (masked[i] == ',' && depth == 0)
            {
                arguments.Add((start, i));
                start = i + 1;
            }
        }

        arguments.Add((start, to));
        return arguments;
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

        if (opener < 0 || !Regex.IsMatch(masked[opener], @"\b(?:struct|union|enum)\b")) return null;

        var (code, tail) = CodeText.SplitComment(source.Lines[k], Syntax.CLike);
        var trimmed = code.TrimEnd();

        return LocalFix.ReplaceLine(
            Id, "Add the semicolon after the struct",
            $"A struct definition ends with a semicolon after its closing brace, and the one ending on line {k + 1} has none - " +
            "so C reads the next line as part of it.",
            source.Path, k + 1, trimmed + ";" + code[trimmed.Length..] + tail);
    }
}
