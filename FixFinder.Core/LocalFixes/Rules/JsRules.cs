using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>What the JavaScript rules share: the file being JavaScript, the words of the language, and declarations.</summary>
/// <remarks>
/// Node names the error's kind and message exactly - <c>TypeError: items.append is not a function</c> - and points at the
/// line with a caret. What it never says is what was meant, so these rules read the declaration of the value involved
/// (<c>= []</c>, <c>= "text"</c>, <c>new Set()</c>) and the class around the line, and change nothing when those do not
/// pin down one edit.
/// </remarks>
internal static partial class Js
{
    public static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "break", "case", "catch", "class", "const", "continue", "debugger", "default", "delete", "do", "else", "export",
        "extends", "finally", "for", "function", "if", "import", "in", "instanceof", "let", "new", "return", "super",
        "switch", "this", "throw", "try", "typeof", "var", "void", "while", "with", "yield", "async", "await", "of",
        "static", "get", "set", "true", "false", "null", "undefined", "constructor",
    };

    /// <summary>Globals a misspelt name might have meant.</summary>
    public static readonly string[] Globals =
    [
        "Array", "Object", "String", "Number", "Boolean", "Math", "JSON", "Date", "Promise", "Map", "Set", "WeakMap",
        "WeakSet", "Symbol", "BigInt", "Error", "TypeError", "RangeError", "parseInt", "parseFloat", "isNaN", "isFinite",
        "console", "process", "require", "module", "exports", "setTimeout", "setInterval", "clearTimeout",
        "clearInterval", "structuredClone", "globalThis", "Buffer", "URL", "fetch",
    ];

    public static bool IsJs(SourceFile source) =>
        Path.GetExtension(source.Path).ToLowerInvariant() is ".js" or ".mjs" or ".cjs";

    public static Match? Error(ParsedError error, string type, Regex message) =>
        error.LanguageId == "node" && error.ExceptionType == type && message.Match(error.Message ?? "") is { Success: true } m ? m : null;

    public static (SourceFile Source, int Number, string Line)? Locate(LocalFixContext context)
    {
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (!IsJs(source) || source.Line(number) is not { } line) return null;

        return (source, number, line);
    }

    /// <summary>Where a word stands alone on a masked line, not a property of something.</summary>
    public static List<int> Unqualified(string masked, string word) =>
        Regex.Matches(masked, $@"(?<![\w$.]){Regex.Escape(word)}(?![\w$])").Select(m => m.Index).ToList();

    /// <summary>What a name was given where it was declared: array, string, set, map, object, number - or null.</summary>
    public static string? KindOf(IReadOnlyList<string> masked, IReadOnlyList<string> lines, string name)
    {
        var declaration = new Regex($@"\b(?:const|let|var)\s+{Regex.Escape(name)}\s*=\s*(?<value>\S.*)$");

        for (var i = 0; i < masked.Count; i++)
        {
            if (declaration.Match(masked[i]) is not { Success: true } found) continue;

            var value = lines[i][found.Groups["value"].Index..];

            if (value.StartsWith('[') || Regex.IsMatch(value, @"^new\s+Array\b")) return "array";
            if (value[0] is '"' or '\'' or '`') return "string";
            if (Regex.IsMatch(value, @"^new\s+Set\b")) return "set";
            if (Regex.IsMatch(value, @"^new\s+Map\b")) return "map";
            if (value.StartsWith('{')) return "object";
            if (Regex.IsMatch(value, @"^-?\d")) return "number";
            return null;
        }

        return null;
    }

    /// <summary>The class a name was made from - <c>const d = new Dog(...)</c> gives <c>Dog</c>.</summary>
    public static string? ClassOf(IReadOnlyList<string> masked, string name) =>
        masked.Select(text => Regex.Match(text, $@"\b(?:const|let|var)\s+{Regex.Escape(name)}\s*=\s*new\s+(?<class>[A-Z][\w$]*)\s*\("))
            .FirstOrDefault(m => m.Success)?.Groups["class"].Value;

    /// <summary>The lines of a class's body - its opening line and closing line - by name, or null.</summary>
    public static (int Header, int Close)? Class(IReadOnlyList<string> masked, string name)
    {
        for (var i = 0; i < masked.Count; i++)
        {
            if (!Regex.IsMatch(masked[i], $@"\bclass\s+{Regex.Escape(name)}\b")) continue;
            return BlockFrom(masked, i) is { } close ? (i, close) : null;
        }

        return null;
    }

    /// <summary>The innermost class whose body holds a line.</summary>
    public static (string Name, int Header, int Close)? EnclosingClass(IReadOnlyList<string> masked, int index)
    {
        for (var i = index; i >= 0; i--)
        {
            if (Regex.Match(masked[i], @"\bclass\s+(?<name>[A-Za-z_$][\w$]*)") is not { Success: true } cls) continue;
            if (BlockFrom(masked, i) is { } close && close >= index) return (cls.Groups["name"].Value, i, close);
        }

        return null;
    }

    /// <summary>The line holding the brace that closes the first block opened on or after a line.</summary>
    public static int? BlockFrom(IReadOnlyList<string> masked, int start)
    {
        var depth = 0;
        var opened = false;

        for (var i = start; i < masked.Count; i++)
        {
            foreach (var c in masked[i])
            {
                if (c == '{')
                {
                    depth++;
                    opened = true;
                }
                else if (c == '}' && opened && --depth == 0)
                {
                    return i;
                }
            }

            if (!opened && i > start + 1) return null;
        }

        return null;
    }

    /// <remarks>
    /// A class method may carry <c>static</c>, <c>async</c>, <c>get</c> or <c>set</c> in front of its name, and all of them are the
    /// method's own - <c>set name(value) {</c> is a method called <c>name</c>.
    /// </remarks>
    [GeneratedRegex(@"(?<async>\basync\s+)?(?:(?<keyword>\bfunction\b)\s*[\w$]*\s*\([^()]*\)|(?<arrow>\([^()]*\)|[A-Za-z_$][\w$]*)\s*=>|^\s*(?:static\s+)?(?<methodAsync>async\s+)?(?:(?:get|set)\s+)?(?<method>(?!(?:if|for|while|switch|catch|with|function)\b)[A-Za-z_$][\w$]*)\s*\([^()]*\))\s*\{")]
    private static partial Regex FunctionOpener();

    /// <summary>The innermost function around a line: where it opens, and where the word <c>async</c> would go.</summary>
    public static (int Line, int AsyncAt, bool IsAsync, Match Opener)? EnclosingFunction(IReadOnlyList<string> masked, int index)
    {
        for (var i = index; i >= 0; i--)
        {
            foreach (var opener in FunctionOpener().Matches(masked[i]).Reverse())
            {
                if (BlockFrom(masked, i) is not { } close || close < index) continue;

                var at = opener.Groups["keyword"].Success ? opener.Groups["keyword"].Index
                    : opener.Groups["arrow"].Success ? opener.Groups["arrow"].Index
                    : opener.Groups["method"].Index;

                return (i, at, opener.Groups["async"].Success || opener.Groups["methodAsync"].Success, opener);
            }
        }

        return null;
    }
}

/// <summary>Built-in names asked of Node itself, so they are right for the Node that ran the program.</summary>
/// <remarks>
/// Only fixed expressions are ever evaluated - <c>String.prototype</c>, <c>Math</c>, a core module's exports - and never
/// anything from the program, which is the rule every check here keeps: nothing the user wrote is run.
/// </remarks>
internal static partial class JsRuntime
{
    private static readonly Dictionary<string, IReadOnlyList<string>> Cache = new(StringComparer.Ordinal);

    [GeneratedRegex(@"^[A-Za-z]+(?:\.prototype)?$")]
    private static partial Regex Expression();

    [GeneratedRegex(@"^(?:node:)?[a-z_][a-z0-9_/]*$")]
    private static partial Regex ModuleName();

    /// <summary>Every property name along the prototype chain of a built-in: <c>String.prototype</c>, <c>Math</c>.</summary>
    public static IReadOnlyList<string> Members(string expression) =>
        !Expression().IsMatch(expression)
            ? []
            : Ask($"members:{expression}",
                $"let o = {expression}; const names = new Set(); while (o) {{ Object.getOwnPropertyNames(o).forEach(n => names.add(n)); o = Object.getPrototypeOf(o); }} console.log([...names].join(' '))");

    public static IReadOnlyList<string> BuiltinModules() =>
        Ask("builtins", "console.log(require('module').builtinModules.join(' '))");

    /// <summary>What a core module exports. Anything that is not a core module is never loaded.</summary>
    public static IReadOnlyList<string> Exports(string module)
    {
        var bare = module.StartsWith("node:", StringComparison.Ordinal) ? module[5..] : module;
        if (!ModuleName().IsMatch(module) || !BuiltinModules().Contains(bare)) return [];

        return Ask($"exports:{bare}", $"console.log(Object.keys(require('{bare}')).join(' '))");
    }

    private static IReadOnlyList<string> Ask(string key, string script)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var known)) return known;
        }

        if (TargetFactory.FindOnPath("node") is not { } node) return [];

        var output = Run(node, script) ?? Run(node, script);
        var names = (output ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        // Only a real answer is remembered; a Node that was slow to start once is asked again next time.
        if (names.Length == 0) return names;

        lock (Cache)
        {
            Cache[key] = names;
        }

        return names;
    }

    private static string? Run(string node, string script)
    {
        try
        {
            var start = new ProcessStartInfo(node)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
            };

            start.ArgumentList.Add("-e");
            start.ArgumentList.Add(script);

            using var process = Process.Start(start);
            if (process is null) return null;

            process.StandardInput.Close();
            process.ErrorDataReceived += (_, _) => { };
            process.BeginErrorReadLine();

            var output = process.StandardOutput.ReadToEndAsync();

            if (!process.WaitForExit(20_000))
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }

                return null;
            }

            return process.ExitCode == 0 && output.Wait(5_000) ? output.Result : null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}

// ====================================================================== syntax

/// <summary><c>'It's here'</c> - an apostrophe ending a single-quoted string early.</summary>
public sealed partial class JsApostrophe : ILocalFixRule
{
    public string Id => "js-apostrophe";

    [GeneratedRegex(@"'(?<first>[^'""\\\r\n]*[A-Za-z])'(?<rest>[A-Za-z][^'""\\\r\n]*)'")]
    private static partial Regex Split();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "node", ExceptionType: "SyntaxError" } || Js.Locate(context) is not { } at) return null;

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
        if (Js.Error(context.Error, "SyntaxError", Message()) is null || Js.Locate(context) is not { } at) return null;

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
        if (Js.Error(context.Error, "SyntaxError", Message()) is null || Js.Locate(context) is not { } at) return null;
        if (Blocks.CloseString(at.Line, Syntax.CLike) is not { } corrected) return null;

        return LocalFix.ReplaceLine(Id, "Close the string", Blocks.CloseStringExplanation, at.Source.Path, at.Number, corrected);
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
        if (Js.Error(context.Error, "SyntaxError", Message()) is null) return null;
        if (context.Read(context.Frame?.File) is not { } source || !Js.IsJs(source)) return null;

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
        if (Js.Error(context.Error, "SyntaxError", Message()) is null) return null;
        if (context.Read(context.Frame?.File) is not { } source || !Js.IsJs(source)) return null;

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
        var depths = CCode.DepthAtStart(masked);
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
        if (context.Error is not { LanguageId: "node", ExceptionType: "SyntaxError" } || Js.Locate(context) is not { } at) return null;

        var hits = Elif().Matches(CodeText.Mask(at.Line, Syntax.CLike));
        if (hits.Count == 0) return null;

        return LocalFix.ReplaceLine(
            Id, "Write elif as else if", "`elif` is Python. JavaScript writes it `else if`.",
            at.Source.Path, at.Number, CCode.Replace(at.Line, hits, _ => "else if"));
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
        if (context.Error is not { LanguageId: "node", ExceptionType: "SyntaxError" } || Js.Locate(context) is not { } at) return null;

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
        if (context.Error is not { LanguageId: "node", ExceptionType: "SyntaxError" } || Js.Locate(context) is not { } at) return null;

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
        if (context.Error is not { LanguageId: "node", ExceptionType: "SyntaxError", Message: "Unexpected token '>'" } || Js.Locate(context) is not { } at) return null;

        var hits = Arrow().Matches(CodeText.Mask(at.Line, Syntax.CLike));
        if (hits.Count != 1) return null;

        return LocalFix.ReplaceLine(
            Id, "Write the arrow as =>", "`->` is how Java writes a lambda. JavaScript's arrow function is `=>`.",
            at.Source.Path, at.Number, CCode.Replace(at.Line, hits, _ => "=>"));
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
        if (Js.Error(context.Error, "SyntaxError", Message()) is not { } message || Js.Locate(context) is not { } at) return null;

        var next = Regex.Escape(message.Groups["next"].Value);
        var start = Regex.Match(CodeText.Mask(at.Line, Syntax.CLike), $@"^(?<lead>\s*)(?<word>[A-Za-z_$][\w$]*)\s+{next}\b");
        if (!start.Success) return null;

        var word = start.Groups["word"].Value;
        if (Js.Keywords.Contains(word)) return null;

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
        if (Js.Error(context.Error, "SyntaxError", Message()) is null || Js.Locate(context) is not { } at) return null;

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
        if (Js.Error(context.Error, "SyntaxError", Message()) is not { } message || Js.Locate(context) is not { } at) return null;

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
        if (Js.Error(context.Error, "SyntaxError", Message()) is null || Js.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (Js.EnclosingFunction(masked, at.Number - 1) is not { IsAsync: false } function) return null;

        var original = source.Lines[function.Line];

        return LocalFix.ReplaceLine(
            Id, "Mark the function async",
            "`await` waits for a promise, and only a function marked `async` is allowed to wait - its callers get a promise back " +
            "instead of a value. `async` in front of the function that holds the `await` says so.",
            source.Path, function.Line + 1, original[..function.AsyncAt] + "async " + original[function.AsyncAt..]);
    }
}

// ====================================================================== modules

/// <summary><c>require is not defined in ES module scope</c> - CommonJS <c>require</c> in a module.</summary>
public sealed partial class JsRequireInModule : ILocalFixRule
{
    public string Id => "js-require-in-module";

    [GeneratedRegex(@"^require is not defined in ES module scope")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?<lead>\s*)(?:const|let|var)\s+(?<binding>[A-Za-z_$][\w$]*|\{[^:}]*\})\s*=\s*require\(\s*(?<quote>[""'])(?<module>[^""']+)\k<quote>\s*\)\s*;?(?<tail>\s*)$")]
    private static partial Regex Require();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (Js.Error(context.Error, "ReferenceError", Message()) is null || Js.Locate(context) is not { } at) return null;
        if (Require().Match(at.Line) is not { Success: true } require) return null;

        var binding = require.Groups["binding"].Value;
        var module = require.Groups["module"].Value;

        return LocalFix.ReplaceLine(
            Id, $"Import {module} instead of requiring it",
            "This file is an ES module - a `.mjs` file, or one in a package that says `\"type\": \"module\"` - and modules have no " +
            "`require`. They load other modules with `import`.",
            at.Source.Path, at.Number, $"{require.Groups["lead"].Value}import {binding} from \"{module}\";{require.Groups["tail"].Value}");
    }
}

/// <summary><c>Cannot find module 'fss'</c> a letter from a core module - a typo, not a package to install.</summary>
public sealed partial class JsCoreModuleTypo : ILocalFixRule
{
    public string Id => "js-core-module-typo";

    [GeneratedRegex(@"^Cannot find module '(?<name>[a-z_][a-z0-9_/]*)'")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (Js.Error(context.Error, "Error", Message()) is not { } message) return null;

        var name = message.Groups["name"].Value;
        var builtins = JsRuntime.BuiltinModules();
        if (builtins.Count == 0 || builtins.Contains(name)) return null;
        if (CodeText.Nearest(name, builtins.Where(b => !b.StartsWith('_'))) is not { } right) return null;

        foreach (var frame in context.Error.Frames)
        {
            if (context.Read(frame.File) is not { } source || !Js.IsJs(source)) continue;

            var loading = new Regex($@"(?:require\(\s*|\bfrom\s+|\bimport\s+)(?<quote>[""'])(?<name>{Regex.Escape(name)})\k<quote>");
            var lines = Enumerable.Range(0, source.Count).Where(i => loading.IsMatch(source.Lines[i])).ToList();
            if (lines is not [var index]) return null;

            var hit = loading.Match(source.Lines[index]).Groups["name"];

            return LocalFix.ReplaceLine(
                Id, $"Change '{name}' to '{right}'",
                $"There is no module called `{name}`, and `{right}` - one of Node's own - is a letter away. Installing a package called " +
                $"`{name}` would fetch a stranger's code to fix a spelling mistake.",
                source.Path, index + 1, source.Lines[index][..hit.Index] + right + source.Lines[index][(hit.Index + hit.Length)..]);
        }

        return null;
    }
}

/// <summary><c>does not provide an export named 'readFileSnyc'</c> - a misspelt import from a core module.</summary>
public sealed partial class JsNamedExportTypo : ILocalFixRule
{
    public string Id => "js-named-export-typo";

    [GeneratedRegex(@"^The requested module '(?<module>[^']+)' does not provide an export named '(?<name>[\w$]+)'$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (Js.Error(context.Error, "SyntaxError", Message()) is not { } message || Js.Locate(context) is not { } at) return null;

        var name = message.Groups["name"].Value;
        var exports = JsRuntime.Exports(message.Groups["module"].Value);
        if (CodeText.Nearest(name, exports) is not { } right) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var word = new Regex($@"(?<![\w$.]){Regex.Escape(name)}(?![\w$])");
        var used = Enumerable.Range(0, masked.Count).Where(i => word.IsMatch(masked[i])).ToList();

        // Every use is renamed with the import, so they have to sit close enough to be one change.
        if (used.Count == 0 || used[^1] - used[0] > 30) return null;

        var lines = Enumerable.Range(used[0], used[^1] - used[0] + 1)
            .Select(i => CCode.Replace(source.Lines[i], word.Matches(masked[i]), _ => right))
            .ToList();

        return new LocalFix
        {
            RuleId = Id,
            Title = $"Change {name} to {right}",
            Explanation =
                $"`{message.Groups["module"].Value}` has no export called `{name}`. `{right}` is the only thing it exports within a letter or two - " +
                "read from the module itself - and every use of the name is changed with the import.",
            File = source.Path,
            StartLine = used[0] + 1,
            RemoveCount = lines.Count,
            NewLines = lines,
        };
    }
}

// ====================================================================== names

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
        if (Js.Error(context.Error, "ReferenceError", Message()) is not { } message || Js.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.CLike);
        if (Statement().Match(masked) is not { Success: true } statement) return null;

        var args = statement.Groups["args"];
        var call = statement.Groups["call"].Value;

        // A comma in C#'s Console.WriteLine means a format string, which console.log does not read.
        if (call.StartsWith("Console", StringComparison.Ordinal) && Cpp.SplitTopLevel(masked, args.Index, args.Index + args.Length, ',').Count > 1) return null;

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
        if (Js.Error(context.Error, "ReferenceError", Message()) is not { } message || Js.Locate(context) is not { } at) return null;

        var word = message.Groups["name"].Value;
        var (right, from) = Words[word];
        var hits = Js.Unqualified(CodeText.Mask(at.Line, Syntax.CLike), word);
        if (hits.Count == 0) return null;

        var corrected = at.Line;
        foreach (var index in Enumerable.Reverse(hits)) corrected = corrected[..index] + right + corrected[(index + word.Length)..];

        return LocalFix.ReplaceLine(Id, $"Write {word} as {right}", $"`{word}` is {from}. In JavaScript it is `{right}`.", at.Source.Path, at.Number, corrected);
    }
}

/// <summary><c>len(items)</c> from Python, which JavaScript writes <c>items.length</c>.</summary>
public sealed partial class JsLen : ILocalFixRule
{
    public string Id => "js-len";

    [GeneratedRegex(@"^len is not defined$")]
    private static partial Regex Message();

    [GeneratedRegex(@"(?<![\w$.])len\s*\(\s*(?<arg>[A-Za-z_$][\w$]*(?:\.[A-Za-z_$][\w$]*)*)\s*\)")]
    private static partial Regex Call();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (Js.Error(context.Error, "ReferenceError", Message()) is null || Js.Locate(context) is not { } at) return null;

        var hits = Call().Matches(CodeText.Mask(at.Line, Syntax.CLike));
        if (hits.Count == 0) return null;

        return LocalFix.ReplaceLine(
            Id, "Use .length", "`len(...)` is Python. In JavaScript a list or a string carries its own length: `items.length`.",
            at.Source.Path, at.Number, CCode.Replace(at.Line, hits, hit => hit.Groups["arg"].Value + ".length"));
    }
}

/// <summary><c>count is not defined</c> inside a class that has <c>this.count</c> - a member used without <c>this</c>.</summary>
public sealed partial class JsMissingThis : ILocalFixRule
{
    public string Id => "js-missing-this";

    [GeneratedRegex(@"^(?<name>[A-Za-z_$][\w$]*) is not defined$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (Js.Error(context.Error, "ReferenceError", Message()) is not { } message || Js.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var name = message.Groups["name"].Value;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        if (Js.EnclosingClass(masked, number - 1) is not { } cls) return null;

        var escaped = Regex.Escape(name);
        var body = Enumerable.Range(cls.Header, cls.Close - cls.Header + 1).Select(i => masked[i]).ToList();

        var field = body.Any(text => Regex.IsMatch(text, $@"\bthis\.{escaped}\s*(?:=(?!=)|\+\+|--|[-+*/]=)"));
        var method = body.Any(text => Regex.IsMatch(text, $@"^\s*(?:async\s+)?(?<!static\s+){escaped}\s*\([^()]*\)\s*\{{") && !Regex.IsMatch(text, @"^\s*static\b"));

        if (!field && !method) return null;

        // Inside a static method there is no object for `this` to be.
        if (Js.EnclosingFunction(masked, number - 1) is { } function && Regex.IsMatch(masked[function.Line], @"^\s*static\b")) return null;

        var hits = Js.Unqualified(masked[number - 1], name);
        if (hits.Count == 0) return null;

        var corrected = line;
        foreach (var index in Enumerable.Reverse(hits)) corrected = corrected[..index] + "this." + corrected[index..];

        return LocalFix.ReplaceLine(
            Id, $"Write this.{name}",
            $"`{name}` belongs to the object - {(field ? $"it is set as `this.{name}`" : $"it is a method of `{cls.Name}`")} - and inside a " +
            $"class a member is always reached through `this`. Without it, JavaScript looks for a variable called `{name}`, and there is none.",
            source.Path, number, corrected);
    }
}

/// <summary><c>totl is not defined</c> - the one name within a letter or two, from the file or JavaScript's globals.</summary>
public sealed partial class JsNearestName : ILocalFixRule
{
    public string Id => "js-nearest-name";

    [GeneratedRegex(@"^(?<name>[A-Za-z_$][\w$]*) is not defined$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (Js.Error(context.Error, "ReferenceError", Message()) is not { } message || Js.Locate(context) is not { } at) return null;

        var wrong = message.Groups["name"].Value;
        var candidates = CodeText.Identifiers(CodeText.MaskAll(at.Source.Lines, Syntax.CLike))
            .Where(word => !Js.Keywords.Contains(word))
            .Concat(Js.Globals);

        if (CodeText.Nearest(wrong, candidates.Where(c => c != wrong)) is not { } right) return null;

        // Every use on the line, or the copy still fails on the one Node did not point at.
        var hits = Js.Unqualified(CodeText.Mask(at.Line, Syntax.CLike), wrong);
        if (hits.Count == 0) return null;

        var corrected = at.Line;
        foreach (var index in Enumerable.Reverse(hits)) corrected = corrected[..index] + right + corrected[(index + wrong.Length)..];

        return LocalFix.ReplaceLine(
            Id, $"Change {wrong} to {right}",
            $"Nothing called `{wrong}` exists. `{right}` is the only name in this file or JavaScript's globals within a letter or two of it.",
            at.Source.Path, at.Number, corrected);
    }
}

// ====================================================================== calls

/// <summary><c>Class constructor Dog cannot be invoked without 'new'</c>.</summary>
public sealed partial class JsClassWithoutNew : ILocalFixRule
{
    public string Id => "js-class-without-new";

    [GeneratedRegex(@"^Class constructor (?<name>[\w$]+) cannot be invoked without 'new'$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (Js.Error(context.Error, "TypeError", Message()) is not { } message || Js.Locate(context) is not { } at) return null;

        var name = message.Groups["name"].Value;
        var hits = Regex.Matches(CodeText.Mask(at.Line, Syntax.CLike), $@"(?<![\w$.])(?<!\bnew\s+){Regex.Escape(name)}\s*\(").ToList();
        if (hits is not [var hit]) return null;

        return LocalFix.ReplaceLine(
            Id, $"Create the {name} with new",
            $"`{name}` is a class, and a class makes an object only with `new` - `new {name}(...)`. Called like a function, it refuses.",
            at.Source.Path, at.Number, at.Line[..hit.Index] + "new " + at.Line[hit.Index..]);
    }
}

/// <summary><c>Assignment to constant variable.</c> - a <c>const</c> that the program goes on to change.</summary>
public sealed partial class JsConstReassigned : ILocalFixRule
{
    public string Id => "js-const-reassigned";

    [GeneratedRegex(@"^Assignment to constant variable\.$")]
    private static partial Regex Message();

    [GeneratedRegex(@"(?<![\w$.])(?<name>[A-Za-z_$][\w$]*)\s*(?:[-+*/%]?=(?![=>])|\+\+|--)|(?:\+\+|--)\s*(?<name>[A-Za-z_$][\w$]*)")]
    private static partial Regex Assigned();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (Js.Error(context.Error, "TypeError", Message()) is null || Js.Locate(context) is not { } at) return null;

        var (source, number, _) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        var found = new List<(int Line, int Index, string Name)>();

        foreach (var name in Assigned().Matches(masked[number - 1]).Select(m => m.Groups["name"].Value).Distinct())
        {
            var declaration = new Regex($@"\bconst\s+(?={Regex.Escape(name)}\b)");

            for (var i = number - 1; i >= 0; i--)
            {
                if (declaration.Match(masked[i]) is not { Success: true } d) continue;
                found.Add((i, d.Index, name));
                break;
            }
        }

        if (found is not [var (line, index, variable)]) return null;

        var original = source.Lines[line];

        return LocalFix.ReplaceLine(
            Id, $"Declare {variable} with let",
            $"`const` promises `{variable}` will never be given another value, and line {number} gives it one. `let` declares a variable " +
            "that is allowed to change.",
            source.Path, line + 1, original[..index] + "let" + original[(index + "const".Length)..]);
    }
}

/// <summary><c>(intermediate value).area is not a function</c> on a getter - <c>get area()</c> is read, not called.</summary>
public sealed partial class JsGetterCalled : ILocalFixRule
{
    public string Id => "js-getter-called";

    [GeneratedRegex(@"^(?<object>.+)\.(?<member>[\w$]+) is not a function$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (Js.Error(context.Error, "TypeError", Message()) is not { } message || Js.Locate(context) is not { } at) return null;

        var member = Regex.Escape(message.Groups["member"].Value);
        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.CLike);
        if (!masked.Any(text => Regex.IsMatch(text, $@"^\s*(?:static\s+)?get\s+{member}\s*\(\s*\)\s*\{{"))) return null;

        var hits = Regex.Matches(masked[at.Number - 1], $@"\.\s*{member}(?<call>\s*\(\s*\))").ToList();
        if (hits is not [var hit]) return null;

        var call = hit.Groups["call"];

        return LocalFix.ReplaceLine(
            Id, $"Read {message.Groups["member"].Value} without brackets",
            $"`{message.Groups["member"].Value}` is a getter - `get {message.Groups["member"].Value}()` - so it is read like a property and gives " +
            "its value straight away. Brackets try to call that value as a function.",
            at.Source.Path, at.Number, at.Line[..call.Index] + at.Line[(call.Index + call.Length)..]);
    }
}

/// <summary><c>d.create is not a function</c> where <c>create</c> is static - it belongs to the class, not the object.</summary>
public sealed partial class JsStaticOnInstance : ILocalFixRule
{
    public string Id => "js-static-on-instance";

    [GeneratedRegex(@"^(?<object>[A-Za-z_$][\w$]*)\.(?<member>[\w$]+) is not a function$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (Js.Error(context.Error, "TypeError", Message()) is not { } message || Js.Locate(context) is not { } at) return null;

        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.CLike);
        var name = message.Groups["object"].Value;
        var member = message.Groups["member"].Value;

        if (Js.ClassOf(masked, name) is not { } cls || Js.Class(masked, cls) is not { } body) return null;

        var isStatic = Enumerable.Range(body.Header, body.Close - body.Header + 1)
            .Any(i => Regex.IsMatch(masked[i], $@"^\s*static\s+(?:async\s+)?{Regex.Escape(member)}\s*\("));
        if (!isStatic) return null;

        var hits = Regex.Matches(masked[at.Number - 1], $@"(?<![\w$.]){Regex.Escape(name)}(?=\s*\.\s*{Regex.Escape(member)}\s*\()").ToList();
        if (hits is not [var hit]) return null;

        return LocalFix.ReplaceLine(
            Id, $"Call {member} on {cls}",
            $"`{member}` is `static`: it belongs to the class `{cls}`, not to each object made from it, so it is called as `{cls}.{member}(...)`.",
            at.Source.Path, at.Number, at.Line[..hit.Index] + cls + at.Line[(hit.Index + name.Length)..]);
    }
}

/// <summary><c>module.exports.add is not a function</c> after <c>module.export = ...</c> - one letter short.</summary>
public sealed partial class JsModuleExportsTypo : ILocalFixRule
{
    public string Id => "js-module-exports-typo";

    [GeneratedRegex(@"^module\.exports\.[\w$]+ is not a function$")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?<lead>\s*module\s*\.\s*)export(?<rest>\s*=[^=].*)$")]
    private static partial Regex Export();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (Js.Error(context.Error, "TypeError", Message()) is null || Js.Locate(context) is not { } at) return null;

        var lines = Enumerable.Range(0, at.Source.Count).Where(i => Export().IsMatch(at.Source.Lines[i])).ToList();
        if (lines is not [var index]) return null;

        var match = Export().Match(at.Source.Lines[index]);

        return LocalFix.ReplaceLine(
            Id, "Write module.exports",
            $"Line {index + 1} sets `module.export`, which is just a new property nobody reads. What a file hands to `require` is " +
            "`module.exports` - with an s.",
            at.Source.Path, index + 1, match.Groups["lead"].Value + "exports" + match.Groups["rest"].Value);
    }
}

/// <summary><c>items.map is not a function</c> where <c>items</c> is the promise an async function returned.</summary>
public sealed partial class JsPromiseNotAwaited : ILocalFixRule
{
    public string Id => "js-promise-not-awaited";

    [GeneratedRegex(@"^(?<object>[A-Za-z_$][\w$]*)\.[\w$]+ is not a function$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (Js.Error(context.Error, "TypeError", Message()) is not { } message || Js.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var name = Regex.Escape(message.Groups["object"].Value);

        for (var i = at.Number - 1; i >= 0; i--)
        {
            var declared = Regex.Match(masked[i], $@"\b(?:const|let|var)\s+{name}\s*=\s*(?<call>(?<function>[A-Za-z_$][\w$]*)\s*\()");
            if (!declared.Success) continue;

            var function = Regex.Escape(declared.Groups["function"].Value);
            var isAsync = masked.Any(text => Regex.IsMatch(text, $@"\basync\s+function\s+{function}\s*\(|\b{function}\s*=\s*async\b"));
            if (!isAsync || Js.EnclosingFunction(masked, i) is not { IsAsync: true }) return null;

            var call = declared.Groups["call"].Index;

            return LocalFix.ReplaceLine(
                Id, $"Wait for {declared.Groups["function"].Value}() with await",
                $"`{declared.Groups["function"].Value}` is `async`, so calling it gives back a promise of its result, not the result - and a " +
                $"promise has no `.{context.Error.Message!.Split('.')[1].Split(' ')[0]}`. `await` waits for the promise and gives the value inside.",
                source.Path, i + 1, source.Lines[i][..call] + "await " + source.Lines[i][call..]);
        }

        return null;
    }
}

/// <summary><c>items is not a function</c> for a list called with round brackets - <c>items(0)</c> for <c>items[0]</c>.</summary>
public sealed partial class JsArrayCalled : ILocalFixRule
{
    public string Id => "js-array-called";

    [GeneratedRegex(@"^(?<name>[A-Za-z_$][\w$]*) is not a function$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (Js.Error(context.Error, "TypeError", Message()) is not { } message || Js.Locate(context) is not { } at) return null;

        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.CLike);
        var name = message.Groups["name"].Value;
        if (Js.KindOf(masked, at.Source.Lines, name) is not ("array" or "string")) return null;

        var hits = Regex.Matches(masked[at.Number - 1], $@"(?<![\w$.]){Regex.Escape(name)}\s*(?<open>\()(?<arg>[^()]+)(?<close>\))").ToList();
        if (hits is not [var hit]) return null;

        var open = hit.Groups["open"].Index;
        var close = hit.Groups["close"].Index;

        return LocalFix.ReplaceLine(
            Id, $"Index {name} with square brackets",
            $"`{name}` is a list, and an item is reached with square brackets - `{name}[0]`. Round brackets try to call it as a function.",
            at.Source.Path, at.Number, at.Line[..open] + "[" + at.Line[(open + 1)..close] + "]" + at.Line[(close + 1)..]);
    }
}

/// <summary>
/// <c>items.append is not a function</c>, <c>seen.push</c> on a Set, <c>text.contains</c>, <c>Math.squareRoot</c> - a method the value
/// does not have, answered from what kind of value it was declared as.
/// </summary>
public sealed partial class JsMemberNotFunction : ILocalFixRule
{
    public string Id => "js-member-not-function";

    [GeneratedRegex(@"^(?<object>[A-Za-z_$][\w$]*)\.(?<member>[\w$]+) is not a function$")]
    private static partial Regex Message();

    private static readonly Dictionary<string, (string Prototype, string Shown, Dictionary<string, string> Foreign)> Kinds = new(StringComparer.Ordinal)
    {
        ["array"] = ("Array.prototype", "a list", new(StringComparer.Ordinal)
        {
            ["size"] = "length", ["count"] = "length", ["length"] = "length", ["len"] = "length", ["append"] = "push",
            ["add"] = "push", ["contains"] = "includes", ["Add"] = "push", ["Count"] = "length",
        }),
        ["string"] = ("String.prototype", "a string", new(StringComparer.Ordinal)
        {
            ["size"] = "length", ["count"] = "length", ["length"] = "length", ["len"] = "length", ["contains"] = "includes",
            ["upper"] = "toUpperCase", ["lower"] = "toLowerCase", ["strip"] = "trim",
        }),
        ["set"] = ("Set.prototype", "a Set", new(StringComparer.Ordinal)
        {
            ["push"] = "add", ["append"] = "add", ["contains"] = "has", ["includes"] = "has", ["remove"] = "delete",
            ["length"] = "size", ["size"] = "size", ["count"] = "size",
        }),
        ["map"] = ("Map.prototype", "a Map", new(StringComparer.Ordinal)
        {
            ["put"] = "set", ["containsKey"] = "has", ["contains"] = "has", ["remove"] = "delete", ["length"] = "size",
            ["size"] = "size", ["count"] = "size",
        }),
    };

    private static readonly Dictionary<string, string> MathWords = new(StringComparer.Ordinal)
    {
        ["squareRoot"] = "sqrt", ["power"] = "pow", ["absolute"] = "abs", ["maximum"] = "max", ["minimum"] = "min",
    };

    private static readonly HashSet<string> Properties = new(StringComparer.Ordinal) { "length", "size" };

    private static readonly HashSet<string> BuiltIns = new(StringComparer.Ordinal) { "Math", "console", "JSON", "Object", "Number", "Array", "String", "Promise" };

    public LocalFix? Propose(LocalFixContext context)
    {
        if (Js.Error(context.Error, "TypeError", Message()) is not { } message || Js.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var name = message.Groups["object"].Value;
        var member = message.Groups["member"].Value;

        string? right;
        string shown;

        if (BuiltIns.Contains(name))
        {
            right = (name == "Math" ? MathWords.GetValueOrDefault(member) : null) ?? CodeText.Nearest(member, JsRuntime.Members(name));
            shown = $"`{name}`";
        }
        else if (Js.KindOf(masked, source.Lines, name) is { } kind && Kinds.TryGetValue(kind, out var entry))
        {
            right = entry.Foreign.GetValueOrDefault(member) ?? CodeText.Nearest(member, JsRuntime.Members(entry.Prototype));
            shown = entry.Shown;
        }
        else if (Js.KindOf(masked, source.Lines, name) == "object" && member == "get")
        {
            right = "[]";
            shown = "a plain object";
        }
        else
        {
            return null;
        }

        if (right is null || right == member && !Properties.Contains(right)) return null;

        var hits = Regex.Matches(masked[number - 1], $@"(?<![\w$.]){Regex.Escape(name)}\s*\.\s*(?<member>{Regex.Escape(member)})\b(?<call>\s*\((?<args>[^()]*)\))?").ToList();
        if (hits is not [var hit]) return null;

        var memberGroup = hit.Groups["member"];
        var callGroup = hit.Groups["call"];
        var args = hit.Groups["args"].Success ? line.Substring(hit.Groups["args"].Index, hit.Groups["args"].Length).Trim() : "";

        string corrected;
        string title;

        if (right == "[]")
        {
            if (!callGroup.Success || args.Length == 0 || args.Contains(',')) return null;

            var dot = masked[number - 1].LastIndexOf('.', memberGroup.Index);
            corrected = line[..dot] + "[" + args + "]" + line[(callGroup.Index + callGroup.Length)..];
            title = $"Read {name}[{args}]";
        }
        else if (Properties.Contains(right))
        {
            // length and size are read, not called - brackets with nothing in them go.
            if (callGroup.Success && args.Length > 0) return null;

            var end = callGroup.Success ? callGroup.Index + callGroup.Length : memberGroup.Index + memberGroup.Length;
            corrected = line[..memberGroup.Index] + right + line[end..];
            title = $"Read {name}.{right}";
        }
        else
        {
            corrected = line[..memberGroup.Index] + right + line[(memberGroup.Index + memberGroup.Length)..];
            title = $"Change {member} to {right}";
        }

        var explanation = right switch
        {
            "[]" => $"`{name}` is {shown}, and a plain object has no `get` - that is a Map's. A property is read with square brackets.",
            _ when Properties.Contains(right) => $"`{name}` is {shown}, and how many it holds is its `{right}` - a property, read without brackets.",
            _ => $"`{name}` is {shown}, which has no `{member}`. The one it has for that is `{right}`" +
                 (member.Equals(right, StringComparison.OrdinalIgnoreCase) || CodeText.Distance(member, right) <= 2 ? " - the name within a letter or two, read from Node itself." : "."),
        };

        return LocalFix.ReplaceLine(Id, title, explanation, source.Path, number, corrected);
    }
}

// ====================================================================== values that were not there

/// <summary><c>Reduce of empty array with no initial value</c> - a sum with nothing to start from.</summary>
public sealed partial class JsReduceWithoutInitial : ILocalFixRule
{
    public string Id => "js-reduce-without-initial";

    [GeneratedRegex(@"^Reduce of empty array with no initial value$")]
    private static partial Regex Message();

    [GeneratedRegex(@"\.\s*reduce\s*(?<open>\()")]
    private static partial Regex Reduce();

    [GeneratedRegex(@"^\(?\s*(?<a>[A-Za-z_$][\w$]*)\s*,\s*(?<b>[A-Za-z_$][\w$]*)\s*\)?\s*=>\s*\k<a>\s*\+\s*\k<b>\s*$")]
    private static partial Regex Sum();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (Js.Error(context.Error, "TypeError", Message()) is null || Js.Locate(context) is not { } at) return null;

        var masked = CodeText.Mask(at.Line, Syntax.CLike);
        if (Reduce().Matches(masked).ToList() is not [var reduce]) return null;

        var open = reduce.Groups["open"].Index;
        if (CCode.Matching(masked, open) is not { } close) return null;
        if (Cpp.SplitTopLevel(masked, open + 1, close, ',').Count != 1) return null;
        if (!Sum().IsMatch(at.Line[(open + 1)..close].Trim())) return null;

        return LocalFix.ReplaceLine(
            Id, "Start the sum at 0",
            "`reduce` with no starting value uses the first item as one - and an empty list has none, so it throws. `0` as the second " +
            "argument is where a sum starts, and an empty list then adds up to 0.",
            at.Source.Path, at.Number, at.Line[..close] + ", 0" + at.Line[close..]);
    }
}

/// <summary><c>Cannot read properties of undefined</c> reading from the result of <c>forEach</c>, which returns nothing.</summary>
public sealed partial class JsForEachResult : ILocalFixRule
{
    public string Id => "js-foreach-result";

    [GeneratedRegex(@"^Cannot read properties of undefined \(reading '(?<property>[^']+)'\)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (Js.Error(context.Error, "TypeError", Message()) is not { } message || Js.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var property = Regex.Escape(message.Groups["property"].Value);

        var found = new List<(int Line, int Index)>();

        foreach (Match read in Regex.Matches(masked[at.Number - 1], $@"(?<![\w$.])(?<name>[A-Za-z_$][\w$]*)\s*\.\s*{property}\b"))
        {
            var declaration = new Regex($@"\b(?:const|let|var)\s+{Regex.Escape(read.Groups["name"].Value)}\s*=\s*.+(?<each>\.\s*forEach)\s*\(");

            for (var i = at.Number - 1; i >= 0; i--)
            {
                if (declaration.Match(masked[i]) is not { Success: true } d) continue;
                found.Add((i, d.Groups["each"].Index));
                break;
            }
        }

        if (found is not [var (line, index)]) return null;

        var original = source.Lines[line];
        var at_ = original.IndexOf("forEach", index, StringComparison.Ordinal);

        return LocalFix.ReplaceLine(
            Id, "Use map to collect the results",
            "`forEach` runs a function for every item and gives back nothing, so the variable holds `undefined`. `map` runs the same " +
            "function and gives back a new list of what it returned.",
            source.Path, line + 1, original[..at_] + "map" + original[(at_ + "forEach".Length)..]);
    }
}

/// <summary><c>ages["Ada"] = 36</c> on a Map, which <c>ages.get("Ada")</c> then cannot see.</summary>
public sealed partial class JsMapBracket : ILocalFixRule
{
    public string Id => "js-map-bracket";

    [GeneratedRegex(@"^Cannot read properties of undefined \(reading '[^']+'\)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (Js.Error(context.Error, "TypeError", Message()) is null || Js.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        var reads = Regex.Matches(masked[at.Number - 1], @"(?<![\w$.])(?<map>[A-Za-z_$][\w$]*)\s*\.\s*get\s*\(")
            .Select(m => m.Groups["map"].Value)
            .Where(map => Js.KindOf(masked, source.Lines, map) == "map")
            .Distinct()
            .ToList();

        if (reads is not [var name]) return null;

        var assignment = new Regex($@"^(?<lead>\s*){Regex.Escape(name)}\s*\[\s*(?<key>[^\]]+?)\s*\]\s*=(?!=)\s*(?<value>[^;]+?)\s*;?\s*$");
        var lines = Enumerable.Range(0, source.Count).Where(i => assignment.IsMatch(source.Lines[i])).ToList();
        if (lines is not [var index]) return null;

        var match = assignment.Match(source.Lines[index]);

        return LocalFix.ReplaceLine(
            Id, $"Store it with {name}.set",
            $"`{name}` is a Map, and square brackets on a Map set an ordinary property of the object - one `get` never looks at. A Map " +
            "stores its entries with `set`.",
            source.Path, index + 1, $"{match.Groups["lead"].Value}{name}.set({match.Groups["key"].Value}, {match.Groups["value"].Value});");
    }
}

/// <summary><c>name = name</c> in a constructor, so <c>d.name</c> is undefined later.</summary>
public sealed partial class JsConstructorWithoutThis : ILocalFixRule
{
    public string Id => "js-constructor-without-this";

    [GeneratedRegex(@"^Cannot read properties of undefined \(reading '(?<property>[^']+)'\)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (Js.Error(context.Error, "TypeError", Message()) is not { } message || Js.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var property = Regex.Escape(message.Groups["property"].Value);

        var reads = Regex.Matches(masked[at.Number - 1], $@"(?<![\w$.])(?<object>[A-Za-z_$][\w$]*)\s*\.\s*(?<field>[A-Za-z_$][\w$]*)\s*\.\s*{property}\b").ToList();
        if (reads is not [var read]) return null;

        if (Js.ClassOf(masked, read.Groups["object"].Value) is not { } cls || Js.Class(masked, cls) is not { } body) return null;

        var field = Regex.Escape(read.Groups["field"].Value);
        var assignment = new Regex($@"^(?<lead>\s*){field}\s*=(?!=)\s*(?<value>[^;]+?)\s*;?\s*$");

        var lines = Enumerable.Range(body.Header, body.Close - body.Header + 1).Where(i => assignment.IsMatch(masked[i])).ToList();
        if (lines is not [var index]) return null;

        var match = assignment.Match(source.Lines[index]);

        return LocalFix.ReplaceLine(
            Id, $"Store it as this.{read.Groups["field"].Value}",
            $"`{read.Groups["field"].Value} = {match.Groups["value"].Value}` inside the constructor only gives the parameter its own value back. " +
            $"The object keeps it only as `this.{read.Groups["field"].Value}`, which is what `{read.Groups["object"].Value}.{read.Groups["field"].Value}` reads.",
            source.Path, index + 1, $"{match.Groups["lead"].Value}this.{read.Groups["field"].Value} = {match.Groups["value"].Value};");
    }
}

/// <summary><c>this.ticks</c> inside <c>function () {...}</c> passed as a callback - where <c>this</c> is not the object.</summary>
public sealed partial class JsThisInCallback : ILocalFixRule
{
    public string Id => "js-this-in-callback";

    [GeneratedRegex(@"^Cannot read properties of undefined \(reading '(?<property>[^']+)'\)$")]
    private static partial Regex Message();

    [GeneratedRegex(@"(?<=[(,]\s*)function\s*\((?<params>[^()]*)\)\s*\{")]
    private static partial Regex Callback();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (Js.Error(context.Error, "TypeError", Message()) is not { } message || Js.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (!Regex.IsMatch(masked[at.Number - 1], $@"\bthis\s*\.\s*{Regex.Escape(message.Groups["property"].Value)}\b")) return null;

        if (Js.EnclosingFunction(masked, at.Number - 1) is not { } function || !function.Opener.Groups["keyword"].Success) return null;
        if (Callback().Matches(masked[function.Line]).ToList() is not [var callback]) return null;

        var original = source.Lines[function.Line];

        return LocalFix.ReplaceLine(
            Id, "Use an arrow function so this stays the object",
            "A `function` passed as a callback gets its own `this`, which here is `undefined` - not the object whose method passed it. An " +
            "arrow function has no `this` of its own, so inside it `this` is still the object.",
            source.Path, function.Line + 1,
            original[..callback.Index] + $"({callback.Groups["params"].Value}) => {{" + original[(callback.Index + callback.Length)..]);
    }
}

/// <summary><c>const inc = c.increment;</c> then <c>inc()</c> - a method taken off its object, so <c>this</c> is undefined inside.</summary>
public sealed partial class JsDetachedMethod : ILocalFixRule
{
    public string Id => "js-detached-method";

    [GeneratedRegex(@"^Cannot read properties of undefined \(reading '(?<property>[^']+)'\)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (Js.Error(context.Error, "TypeError", Message()) is not { } message || Js.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (!Regex.IsMatch(masked[at.Number - 1], $@"\bthis\s*\.\s*{Regex.Escape(message.Groups["property"].Value)}\b")) return null;

        if (Js.EnclosingFunction(masked, at.Number - 1) is not { } function || !function.Opener.Groups["method"].Success) return null;

        var method = Regex.Escape(function.Opener.Groups["method"].Value);
        var detached = new Regex($@"^(?<lead>\s*(?:const|let|var)\s+[A-Za-z_$][\w$]*\s*=\s*)(?<object>[A-Za-z_$][\w$]*)\s*\.\s*{method}\s*;\s*$");

        var lines = Enumerable.Range(0, masked.Count).Where(i => detached.IsMatch(masked[i])).ToList();
        if (lines is not [var index]) return null;

        var match = detached.Match(source.Lines[index]);
        var obj = match.Groups["object"].Value;

        return LocalFix.ReplaceLine(
            Id, $"Bind {function.Opener.Groups["method"].Value} to {obj}",
            $"Taking `{obj}.{function.Opener.Groups["method"].Value}` off the object and calling it on its own loses `{obj}` - inside, `this` is " +
            $"`undefined`. `.bind({obj})` makes a function that always runs with `this` as `{obj}`.",
            source.Path, index + 1, $"{match.Groups["lead"].Value}{obj}.{function.Opener.Groups["method"].Value}.bind({obj});");
    }
}

/// <summary><c>Must call super constructor in derived class</c> - a subclass constructor using <c>this</c> before <c>super</c>.</summary>
public sealed partial class JsSuperMissing : ILocalFixRule
{
    public string Id => "js-super-missing";

    [GeneratedRegex(@"^Must call super constructor in derived class before accessing 'this'")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?<indent>\s*)constructor\s*\((?<params>[^()]*)\)\s*\{\s*$")]
    private static partial Regex Constructor();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (Js.Error(context.Error, "ReferenceError", Message()) is null || Js.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        if (Js.EnclosingClass(masked, at.Number - 1) is not { } cls) return null;
        if (Regex.Match(masked[cls.Header], @"\bextends\s+(?<base>[A-Za-z_$][\w$]*)") is not { Success: true } extends) return null;

        var header = Enumerable.Range(cls.Header, at.Number - cls.Header).LastOrDefault(i => Constructor().IsMatch(masked[i]), -1);
        if (header < 0) return null;

        var derived = Names(Constructor().Match(masked[header]).Groups["params"].Value);

        IReadOnlyList<string> needed = [];
        if (Js.Class(masked, extends.Groups["base"].Value) is { } parent)
        {
            var baseHeader = Enumerable.Range(parent.Header, parent.Close - parent.Header + 1).FirstOrDefault(i => Constructor().IsMatch(masked[i]), -1);
            if (baseHeader >= 0) needed = Names(Constructor().Match(masked[baseHeader]).Groups["params"].Value);
        }
        else
        {
            return null;
        }

        if (needed.Any(name => !derived.Contains(name))) return null;

        var indent = header + 1 < source.Count && source.Lines[header + 1].Trim().Length > 0
            ? CodeText.Indentation(source.Lines[header + 1])
            : CodeText.Indentation(source.Lines[header]) + "  ";

        return LocalFix.Insert(
            Id, $"Call super({string.Join(", ", needed)}) first",
            $"A class that `extends {extends.Groups["base"].Value}` has to let `{extends.Groups["base"].Value}` build the object before it can use " +
            $"`this` - that is what `super(...)` does, and it comes first in the constructor.",
            source.Path, header + 2, [$"{indent}super({string.Join(", ", needed)});"]);
    }

    private static IReadOnlyList<string> Names(string parameters) =>
        parameters.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => Regex.Match(p, @"^[A-Za-z_$][\w$]*").Value)
            .ToList();
}

/// <summary><c>Maximum call stack size exceeded</c> from a setter that assigns to its own property.</summary>
public sealed partial class JsSetterRecursion : ILocalFixRule
{
    public string Id => "js-setter-recursion";

    [GeneratedRegex(@"^Maximum call stack size exceeded$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (Js.Error(context.Error, "RangeError", Message()) is null || Js.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        if (Js.EnclosingFunction(masked, at.Number - 1) is not { } function) return null;

        var setter = Regex.Match(masked[function.Line], @"^\s*set\s+(?<name>[A-Za-z_$][\w$]*)\s*\(");
        if (!setter.Success) return null;

        var name = Regex.Escape(setter.Groups["name"].Value);
        if (masked.Any(text => Regex.IsMatch(text, $@"^\s*get\s+{name}\s*\("))) return null;

        var hits = Regex.Matches(masked[at.Number - 1], $@"\bthis\s*\.\s*(?<name>{name})\s*=(?!=)").ToList();
        if (hits is not [var hit]) return null;

        var index = hit.Groups["name"].Index;

        return LocalFix.ReplaceLine(
            Id, $"Store the value as this._{setter.Groups["name"].Value}",
            $"Assigning `this.{setter.Groups["name"].Value}` inside `set {setter.Groups["name"].Value}` calls the same setter again, which assigns " +
            $"again, until the stack runs out. The value has to be kept under another name - `this._{setter.Groups["name"].Value}` is the usual one.",
            source.Path, at.Number, at.Line[..index] + "_" + at.Line[index..]);
    }
}
