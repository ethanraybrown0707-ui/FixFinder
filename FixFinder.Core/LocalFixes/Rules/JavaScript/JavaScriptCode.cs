using System.Diagnostics;
using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

internal static partial class JavaScriptCode
{
    public static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "break", "case", "catch", "class", "const", "continue", "debugger", "default", "delete", "do", "else", "export",
        "extends", "finally", "for", "function", "if", "import", "in", "instanceof", "let", "new", "return", "super",
        "switch", "this", "throw", "try", "typeof", "var", "void", "while", "with", "yield", "async", "await", "of",
        "static", "get", "set", "true", "false", "null", "undefined", "constructor",
    };

    public static readonly string[] Globals =
    [
        "Array", "Object", "String", "Number", "Boolean", "Math", "JSON", "Date", "Promise", "Map", "Set", "WeakMap",
        "WeakSet", "Symbol", "BigInt", "Error", "TypeError", "RangeError", "parseInt", "parseFloat", "isNaN", "isFinite",
        "console", "process", "require", "module", "exports", "setTimeout", "setInterval", "clearTimeout",
        "clearInterval", "structuredClone", "globalThis", "Buffer", "URL", "fetch",
    ];

    public static bool IsJavaScript(SourceFile source) =>
        Path.GetExtension(source.Path).ToLowerInvariant() is ".js" or ".mjs" or ".cjs";

    public static Match? ErrorMessage(ParsedError error, string type, Regex message) =>
        error.LanguageId == "node" && error.ExceptionType == type && message.Match(error.Message ?? "") is { Success: true } m ? m : null;

    public static (SourceFile Source, int Number, string Line)? Locate(LocalFixContext context)
    {
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (!IsJavaScript(source) || source.Line(number) is not { } line) return null;

        return (source, number, line);
    }

    public static List<int> UnqualifiedUses(string masked, string word) =>
        Regex.Matches(masked, $@"(?<![\w$.]){Regex.Escape(word)}(?![\w$])").Select(m => m.Index).ToList();

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

    public static string? ClassOf(IReadOnlyList<string> masked, string name) =>
        masked.Select(text => Regex.Match(text, $@"\b(?:const|let|var)\s+{Regex.Escape(name)}\s*=\s*new\s+(?<class>[A-Z][\w$]*)\s*\("))
            .FirstOrDefault(m => m.Success)?.Groups["class"].Value;

    public static (int Header, int Close)? ClassBlock(IReadOnlyList<string> masked, string name)
    {
        for (var i = 0; i < masked.Count; i++)
        {
            if (!Regex.IsMatch(masked[i], $@"\bclass\s+{Regex.Escape(name)}\b")) continue;
            return Brackets.FirstBlockEnd(masked, i) is { } close ? (i, close) : null;
        }

        return null;
    }

    public static (string Name, int Header, int Close)? EnclosingClass(IReadOnlyList<string> masked, int index)
    {
        for (var i = index; i >= 0; i--)
        {
            if (Regex.Match(masked[i], @"\bclass\s+(?<name>[A-Za-z_$][\w$]*)") is not { Success: true } cls) continue;
            if (Brackets.FirstBlockEnd(masked, i) is { } close && close >= index) return (cls.Groups["name"].Value, i, close);
        }

        return null;
    }

    [GeneratedRegex(@"(?<async>\basync\s+)?(?:(?<keyword>\bfunction\b)\s*[\w$]*\s*\([^()]*\)|(?<arrow>\([^()]*\)|[A-Za-z_$][\w$]*)\s*=>|^\s*(?:static\s+)?(?<methodAsync>async\s+)?(?:(?:get|set)\s+)?(?<method>(?!(?:if|for|while|switch|catch|with|function)\b)[A-Za-z_$][\w$]*)\s*\([^()]*\))\s*\{")]
    private static partial Regex FunctionOpener();

    public static (int Line, int AsyncAt, bool IsAsync, Match Opener)? EnclosingFunction(IReadOnlyList<string> masked, int index)
    {
        for (var i = index; i >= 0; i--)
        {
            foreach (var opener in FunctionOpener().Matches(masked[i]).Reverse())
            {
                if (Brackets.FirstBlockEnd(masked, i) is not { } close || close < index) continue;

                var at = opener.Groups["keyword"].Success ? opener.Groups["keyword"].Index
                    : opener.Groups["arrow"].Success ? opener.Groups["arrow"].Index
                    : opener.Groups["method"].Index;

                return (i, at, opener.Groups["async"].Success || opener.Groups["methodAsync"].Success, opener);
            }
        }

        return null;
    }
}
