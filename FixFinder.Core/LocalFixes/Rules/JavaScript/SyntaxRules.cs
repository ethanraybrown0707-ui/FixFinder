using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary><c>'It's here'</c> - an apostrophe ending a single-quoted string early.</summary>
public sealed partial class JsApostrophe : ILocalFixRule
{
    public string Id => "js-apostrophe";

    [GeneratedRegex(@"'(?<first>[^'""\\\r\n]*[A-Za-z])'(?<rest>[A-Za-z][^'""\\\r\n]*)'")]
    private static partial Regex Split();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "node", ExceptionType: "SyntaxError" } || JavaScriptCode.Locate(context) is not { } at) return null;

        var hits = Split().Matches(at.Line).ToList();
        if (hits is not [var hit]) return null;

        var text = hit.Groups["first"].Value + "'" + hit.Groups["rest"].Value;

        return LocalFix.ReplaceLine(
            Id, "Put the text in double quotes",
            $"The apostrophe in {text} ends the single-quoted string early, so the rest of the line is read as code. Double quotes " +
            "around the text leave the apostrophe inside it.",
            at.Source.Path, at.Number, at.Line[..hit.Index] + "\"" + text + "\"" + at.Line[(hit.Index + hit.Length)..]);
    }
}

/// <summary><c>missing ) after argument list</c> - a call left open at the end of the statement.</summary>
public sealed partial class JsMissingClosingParen : ILocalFixRule
{
    public string Id => "js-missing-closing-paren";

    [GeneratedRegex(@"^missing \) after argument list$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaScriptCode.ErrorMessage(context.Error, "SyntaxError", Message()) is null || JavaScriptCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var (code, _) = CodeText.SplitComment(CodeText.Mask(line, Syntax.CLike), Syntax.CLike);
        var trimmed = code.TrimEnd();

        if (trimmed.Count(c => c == '(') - trimmed.Count(c => c == ')') != 1) return null;

        var at_ = trimmed.EndsWith(';') ? trimmed.Length - 1 : trimmed.Length;

        return LocalFix.ReplaceLine(
            Id, "Add the missing closing bracket",
            $"A `(` on line {number} is never closed - the statement ends first.",
            source.Path, number, line[..at_] + ")" + line[at_..]);
    }
}

/// <summary><c>Invalid or unexpected token</c> from a string with no closing quote.</summary>
public sealed partial class JsUnclosedString : ILocalFixRule
{
    public string Id => "js-unclosed-string";

    [GeneratedRegex(@"^Invalid or unexpected token$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaScriptCode.ErrorMessage(context.Error, "SyntaxError", Message()) is null || JavaScriptCode.Locate(context) is not { } at) return null;
        if (BraceRules.CloseString(at.Line, Syntax.CLike) is not { } corrected) return null;

        return LocalFix.ReplaceLine(Id, "Close the string", BraceRules.CloseStringExplanation, at.Source.Path, at.Number, corrected);
    }
}

/// <summary><c>Unexpected end of input</c> - a block never closed.</summary>
/// <remarks>
/// The brace goes where the indentation says the block ended - before the first line that is back at the opener's own
/// level - so a call that was written after the function stays after it.
/// </remarks>
public sealed partial class JsMissingClosingBrace : ILocalFixRule
{
    public string Id => "js-missing-closing-brace";

    [GeneratedRegex(@"^Unexpected end of input$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaScriptCode.ErrorMessage(context.Error, "SyntaxError", Message()) is null) return null;
        if (context.Read(context.Frame?.File) is not { } source || !JavaScriptCode.IsJavaScript(source)) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var unclosed = new Stack<int>();

        for (var i = 0; i < masked.Count; i++)
        {
            foreach (var c in masked[i])
            {
                if (c == '{') unclosed.Push(i);
                else if (c == '}' && !unclosed.TryPop(out _)) return null;
            }
        }

        if (unclosed.Count != 1) return null;

        var opener = unclosed.Pop();
        var indent = CodeText.Indentation(source.Lines[opener]);

        var back = Enumerable.Range(opener + 1, Math.Max(0, source.Count - opener - 1))
            .FirstOrDefault(i => masked[i].Trim().Length > 0 && CodeText.Indentation(source.Lines[i]).Length <= indent.Length, -1);

        var last = (back < 0 ? source.Count : back) - 1;
        while (last > opener && source.Lines[last].Trim().Length == 0) last--;

        return LocalFix.Insert(
            Id, $"Close the block opened on line {opener + 1}",
            $"The `{{` on line {opener + 1} is never closed, so the file ends inside it. The closing brace goes where its indentation " +
            "ends - after the last line indented inside it.",
            source.Path, last + 2, [indent + "}"]);
    }
}

/// <summary><c>Unexpected token '}'</c> - one closing brace too many.</summary>
public sealed partial class JsExtraClosingBrace : ILocalFixRule
{
    public string Id => "js-extra-closing-brace";

    [GeneratedRegex(@"^Unexpected token '\}'$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaScriptCode.ErrorMessage(context.Error, "SyntaxError", Message()) is null) return null;
        if (context.Read(context.Frame?.File) is not { } source || !JavaScriptCode.IsJavaScript(source)) return null;

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

        // A block closed too soon strands the lines after it, and the last brace is then the one with nothing to close.
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

        var remove = early >= 0 ? early : extra;

        return CCode.RemoveLine(
            Id, $"Remove the extra }} on line {remove + 1}",
            early >= 0
                ? $"The `}}` on line {remove + 1} closes its block too soon, and the last `}}` then has nothing to close."
                : $"The file has one more `}}` than `{{`. The one on line {remove + 1} closes nothing - every block is already closed before it.",
            source.Path, remove + 1);
    }
}

/// <summary><c>} elif (x) {</c>, which JavaScript writes <c>else if</c>.</summary>
public sealed partial class JsElif : ILocalFixRule
{
    public string Id => "js-elif";

    [GeneratedRegex(@"(?<![\w$.])elif(?=\s*\()")]
    private static partial Regex Elif();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "node", ExceptionType: "SyntaxError" } || JavaScriptCode.Locate(context) is not { } at) return null;

        var hits = Elif().Matches(CodeText.Mask(at.Line, Syntax.CLike));
        if (hits.Count == 0) return null;

        return LocalFix.ReplaceLine(
            Id, "Write elif as else if", "`elif` is Python. JavaScript writes it `else if`.",
            at.Source.Path, at.Number, CCode.ReplaceEach(at.Line, hits, _ => "else if"));
    }
}

/// <summary><c>if x &gt; 3 {</c> - the condition without its brackets.</summary>
public sealed partial class JsConditionParentheses : ILocalFixRule
{
    public string Id => "js-condition-parentheses";

    [GeneratedRegex(@"^(?<lead>\s*(?:\}\s*)?(?:else\s+)?)(?<keyword>if|while|switch)\s+(?<condition>[^({\s][^{;]*?)\s*(?<brace>\{?)$")]
    private static partial Regex Condition();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "node", ExceptionType: "SyntaxError" } || JavaScriptCode.Locate(context) is not { } at) return null;

        var (code, tail) = CodeText.SplitComment(at.Line, Syntax.CLike);
        if (Condition().Match(code) is not { Success: true } match) return null;

        return LocalFix.ReplaceLine(
            Id, $"Put the {match.Groups["keyword"].Value} condition in brackets",
            "In JavaScript the condition of `if`, `while` and `switch` always goes in brackets.",
            at.Source.Path, at.Number,
            $"{match.Groups["lead"].Value}{match.Groups["keyword"].Value} ({match.Groups["condition"].Value}){(match.Groups["brace"].Length > 0 ? " {" : "")}{tail}");
    }
}

/// <summary><c>for item in items {</c> from Python and <c>for (const item : items)</c> from Java - both <c>for...of</c>.</summary>
public sealed partial class JsForOf : ILocalFixRule
{
    public string Id => "js-for-of";

    [GeneratedRegex(@"^(?<lead>\s*)for\s+(?<var>[A-Za-z_$][\w$]*)\s+in\s+(?<iter>[^{]+?)\s*(?<brace>\{?)\s*$")]
    private static partial Regex Python();

    [GeneratedRegex(@"\bfor\s*\(\s*(?<decl>const|let|var)\s+(?<var>[A-Za-z_$][\w$]*)\s*(?<colon>:)\s*(?<iter>[^)]+?)\s*\)")]
    private static partial Regex Java();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "node", ExceptionType: "SyntaxError" } || JavaScriptCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.CLike);

        if (Python().Match(masked) is { Success: true } python)
        {
            var iter = line.Substring(python.Groups["iter"].Index, python.Groups["iter"].Length);

            return LocalFix.ReplaceLine(
                Id, "Write the loop as for...of",
                "`for item in items` is Python. JavaScript loops over the values of a list with `for (const item of items)` - with " +
                "brackets, and `of`; `in` would give the positions instead.",
                source.Path, number,
                $"{python.Groups["lead"].Value}for (const {python.Groups["var"].Value} of {iter}){(python.Groups["brace"].Length > 0 ? " {" : "")}");
        }

        if (Java().Matches(masked).ToList() is [var java])
        {
            var colon = java.Groups["colon"].Index;
            var before = line[..colon].TrimEnd();

            return LocalFix.ReplaceLine(
                Id, "Write the loop as for...of",
                "`for (item : items)` is Java. JavaScript loops over the values of a list with `for (const item of items)`.",
                source.Path, number, before + " of " + line[(colon + 1)..].TrimStart());
        }

        return null;
    }
}

/// <summary><c>(x) -&gt; x * 2</c> - Java's thin arrow, which JavaScript writes <c>=&gt;</c>.</summary>
public sealed partial class JsThinArrow : ILocalFixRule
{
    public string Id => "js-thin-arrow";

    [GeneratedRegex(@"(?<=[\w$)]\s*)->")]
    private static partial Regex Arrow();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "node", ExceptionType: "SyntaxError", Message: "Unexpected token '>'" } || JavaScriptCode.Locate(context) is not { } at) return null;

        var hits = Arrow().Matches(CodeText.Mask(at.Line, Syntax.CLike));
        if (hits.Count != 1) return null;

        return LocalFix.ReplaceLine(
            Id, "Write the arrow as =>", "`->` is how Java writes a lambda. JavaScript's arrow function is `=>`.",
            at.Source.Path, at.Number, CCode.ReplaceEach(at.Line, hits, _ => "=>"));
    }
}

/// <summary><c>fucntion greet()</c>, <c>Let count</c>, <c>def greet()</c> - the first word of a statement misspelt or borrowed.</summary>
public sealed partial class JsKeywordTypo : ILocalFixRule
{
    public string Id => "js-keyword-typo";

    [GeneratedRegex(@"^Unexpected identifier '(?<next>[\w$]+)'$")]
    private static partial Regex Message();

    private static readonly string[] Starters = ["function", "let", "const", "var", "class", "return", "async", "await", "else", "while"];

    private static readonly Dictionary<string, string> Borrowed = new(StringComparer.Ordinal)
    {
        ["def"] = "function", ["func"] = "function", ["fn"] = "function", ["fun"] = "function", ["Function"] = "function",
    };

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaScriptCode.ErrorMessage(context.Error, "SyntaxError", Message()) is not { } message || JavaScriptCode.Locate(context) is not { } at) return null;

        var next = Regex.Escape(message.Groups["next"].Value);
        var start = Regex.Match(CodeText.Mask(at.Line, Syntax.CLike), $@"^(?<lead>\s*)(?<word>[A-Za-z_$][\w$]*)\s+{next}\b");
        if (!start.Success) return null;

        var word = start.Groups["word"].Value;
        if (JavaScriptCode.Keywords.Contains(word)) return null;

        var right = Borrowed.GetValueOrDefault(word) ?? CodeText.Nearest(word, Starters);
        if (right is null) return null;

        var index = start.Groups["word"].Index;

        return LocalFix.ReplaceLine(
            Id, $"Change {word} to {right}",
            Borrowed.ContainsKey(word)
                ? $"`{word}` is how another language starts a function. JavaScript writes `{right}`."
                : $"`{word}` is not a JavaScript word, so the name after it made no sense. `{right}` is the keyword within a letter or two of it.",
            at.Source.Path, at.Number, at.Line[..index] + right + at.Line[(index + word.Length)..]);
    }
}

/// <summary><c>Unexpected identifier 'age'</c> on a property whose line above has no comma.</summary>
public sealed partial class JsMissingComma : ILocalFixRule
{
    public string Id => "js-missing-comma";

    [GeneratedRegex(@"^Unexpected (?:identifier|string|number|token) '?(?<token>[^']*)'?$")]
    private static partial Regex Message();

    [GeneratedRegex(@"^\s*(?:[A-Za-z_$][\w$]*|""[^""]*""|'[^']*')\s*:")]
    private static partial Regex Property();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaScriptCode.ErrorMessage(context.Error, "SyntaxError", Message()) is null || JavaScriptCode.Locate(context) is not { } at) return null;

        var (source, number, _) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (!Property().IsMatch(masked[number - 1])) return null;

        var k = number - 2;
        while (k >= 0 && masked[k].Trim().Length == 0) k--;
        if (k < 0) return null;

        var previous = masked[k].TrimEnd();
        if (!Property().IsMatch(previous) || previous.EndsWith(',') || previous.EndsWith('{') || previous.EndsWith('[') || previous.EndsWith('(')) return null;

        var (code, tail) = CodeText.SplitComment(source.Lines[k], Syntax.CLike);
        var trimmed = code.TrimEnd();

        return LocalFix.ReplaceLine(
            Id, "Add the comma between the properties",
            $"Properties in an object are separated by commas, and the one on line {k + 1} has none after it - so the next one reads as " +
            "part of the same value.",
            source.Path, k + 1, trimmed + "," + code[trimmed.Length..] + tail);
    }
}

/// <summary><c>Identifier 'x' has already been declared</c> - a second <c>let</c> where an assignment was meant.</summary>
public sealed partial class JsRedeclared : ILocalFixRule
{
    public string Id => "js-redeclared";

    [GeneratedRegex(@"^Identifier '(?<name>[\w$]+)' has already been declared$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaScriptCode.ErrorMessage(context.Error, "SyntaxError", Message()) is not { } message || JavaScriptCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var name = Regex.Escape(message.Groups["name"].Value);
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        var redeclared = Regex.Match(line, $@"^(?<lead>\s*)(?:let|var)\s+(?<name>{name})\s*=\s*(?<value>[^;]+?)\s*;?(?<tail>\s*(?://.*)?)$");
        if (!redeclared.Success) return null;

        var earlier = Enumerable.Range(0, number - 1).LastOrDefault(i => Regex.IsMatch(masked[i], $@"\b(?:let|var|const)\s+{name}\b"), -1);
        if (earlier < 0 || Regex.IsMatch(masked[earlier], $@"\bconst\s+{name}\b")) return null;

        return LocalFix.ReplaceLine(
            Id, $"Assign to {message.Groups["name"].Value} instead of declaring it again",
            $"`{message.Groups["name"].Value}` is already declared on line {earlier + 1}. `let` again declares a second one with the same " +
            "name, which one block cannot hold; without it, the line gives the existing one a new value.",
            source.Path, number,
            $"{redeclared.Groups["lead"].Value}{message.Groups["name"].Value} = {redeclared.Groups["value"].Value};{redeclared.Groups["tail"].Value}");
    }
}

/// <summary><c>await is only valid in async functions</c> - the function around it never said <c>async</c>.</summary>
public sealed partial class JsAwaitOutsideAsync : ILocalFixRule
{
    public string Id => "js-await-outside-async";

    [GeneratedRegex(@"^await is only valid in async functions and the top level bodies of modules$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaScriptCode.ErrorMessage(context.Error, "SyntaxError", Message()) is null || JavaScriptCode.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (JavaScriptCode.EnclosingFunction(masked, at.Number - 1) is not { IsAsync: false } function) return null;

        var original = source.Lines[function.Line];

        return LocalFix.ReplaceLine(
            Id, "Mark the function async",
            "`await` waits for a promise, and only a function marked `async` is allowed to wait - its callers get a promise back " +
            "instead of a value. `async` in front of the function that holds the `await` says so.",
            source.Path, function.Line + 1, original[..function.AsyncAt] + "async " + original[function.AsyncAt..]);
    }
}

/// <summary><c>print</c>, <c>System.out.println</c>, <c>Console.WriteLine</c> - how other languages print.</summary>
public sealed partial class JsForeignPrint : ILocalFixRule
{
    public string Id => "js-foreign-print";

    [GeneratedRegex(@"^(?<name>print|System|Console|puts|println) is not defined$")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?<lead>\s*)(?<call>System\s*\.\s*out\s*\.\s*(?:println|print)|Console\s*\.\s*(?:WriteLine|Write)|print|puts|println)\s*\((?<args>.*)\)\s*;?(?<tail>\s*)$")]
    private static partial Regex Statement();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaScriptCode.ErrorMessage(context.Error, "ReferenceError", Message()) is not { } message || JavaScriptCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.CLike);
        if (Statement().Match(masked) is not { Success: true } statement) return null;

        var args = statement.Groups["args"];
        var call = statement.Groups["call"].Value;

        // A comma in C#'s Console.WriteLine means a format string, which console.log does not read.
        if (call.StartsWith("Console", StringComparison.Ordinal) && CppCode.SplitTopLevel(masked, args.Index, args.Index + args.Length, ',').Count > 1) return null;

        var language = message.Groups["name"].Value switch
        {
            "System" => "Java",
            "Console" => "C#",
            "puts" => "Ruby",
            "println" => "Kotlin",
            _ => "Python",
        };

        return LocalFix.ReplaceLine(
            Id, "Print with console.log",
            $"`{call.Split('(')[0].Trim()}` is {language}. JavaScript prints with `console.log`.",
            source.Path, number, $"{statement.Groups["lead"].Value}console.log({line.Substring(args.Index, args.Length)});{line[statement.Groups["tail"].Index..]}");
    }
}

/// <summary><c>True</c>, <c>None</c>, <c>nil</c> - other languages' words for JavaScript's <c>true</c> and <c>null</c>.</summary>
public sealed partial class JsForeignWord : ILocalFixRule
{
    public string Id => "js-foreign-word";

    [GeneratedRegex(@"^(?<name>True|False|None|nil|NULL|Nothing) is not defined$")]
    private static partial Regex Message();

    private static readonly Dictionary<string, (string Right, string From)> Words = new(StringComparer.Ordinal)
    {
        ["True"] = ("true", "Python"), ["False"] = ("false", "Python"), ["None"] = ("null", "Python"),
        ["nil"] = ("null", "Ruby, Lua and Go"), ["NULL"] = ("null", "C"), ["Nothing"] = ("null", "Visual Basic"),
    };

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaScriptCode.ErrorMessage(context.Error, "ReferenceError", Message()) is not { } message || JavaScriptCode.Locate(context) is not { } at) return null;

        var word = message.Groups["name"].Value;
        var (right, from) = Words[word];
        var hits = JavaScriptCode.UnqualifiedUses(CodeText.Mask(at.Line, Syntax.CLike), word);
        if (hits.Count == 0) return null;

        var corrected = at.Line;
        foreach (var index in Enumerable.Reverse(hits)) corrected = corrected[..index] + right + corrected[(index + word.Length)..];

        return LocalFix.ReplaceLine(Id, $"Write {word} as {right}", $"`{word}` is {from}. In JavaScript it is `{right}`.", at.Source.Path, at.Number, corrected);
    }
}
