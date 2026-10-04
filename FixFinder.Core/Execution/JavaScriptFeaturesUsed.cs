using System.Collections.Concurrent;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution.Versions;

namespace FixFinder.Core.Execution;

/// <summary>
/// The Node.js a program's own JavaScript needs, read from how it is written: the newest part of the language or of its
/// built-in objects it uses - ?. and ?? are Node.js 14's, ??= 15's, structuredClone 17's, an array's toSorted 20's,
/// Object.groupBy 21's, Promise.withResolvers 22's, RegExp.escape 24's - so it runs with a Node.js that has it, and how it
/// ran says which and why.
/// </summary>
/// <remarks>
/// Which Node.js added each is the version_added MDN's browser compatibility data gives Node.js for it. The code is read
/// with FixFinder's own JavaScript reader, so text, comments and regular expressions are never taken for code. Only forms
/// that cannot be anything else are counted: Object.groupBy where the program has no Object of its own, fetch where it has
/// no fetch of its own, and methods by names only arrays have - but not on what the program imports, as lodash's
/// _.findLast is not an array's - so the Node.js said to be needed is never more than the code needs. Nothing older than
/// Node.js 14 is looked for.
/// </remarks>
public static class JavaScriptFeaturesUsed
{
    private static LanguageVersion Node(int major, int minor = -1) => new(major, minor);

    /// <summary>Built-in functions called through their object - Object.groupBy - and the Node.js that added each.</summary>
    private static readonly Dictionary<(string Owner, string Name), LanguageVersion> StaticFunctions = new()
    {
        [("RegExp", "escape")] = Node(24),
        [("Error", "isError")] = Node(24),
        [("Promise", "try")] = Node(23),
        [("Promise", "withResolvers")] = Node(22),
        [("Array", "fromAsync")] = Node(22),
        [("Iterator", "from")] = Node(22),
        [("Object", "groupBy")] = Node(21),
        [("Map", "groupBy")] = Node(21),
        [("Object", "hasOwn")] = Node(16, 9),
    };

    /// <summary>Methods by names only arrays - and typed arrays, which gained them in the same Node.js - have.</summary>
    private static readonly Dictionary<string, LanguageVersion> ArrayMethods = new(StringComparer.Ordinal)
    {
        ["toReversed"] = Node(20),
        ["toSorted"] = Node(20),
        ["toSpliced"] = Node(20),
        ["findLast"] = Node(18),
        ["findLastIndex"] = Node(18),
    };

    /// <summary>Global functions, and the Node.js that added each.</summary>
    private static readonly Dictionary<string, LanguageVersion> GlobalFunctions = new(StringComparer.Ordinal)
    {
        ["fetch"] = Node(18),
        ["structuredClone"] = Node(17),
    };

    /// <summary>Operators, and the Node.js that added each.</summary>
    private static readonly Dictionary<string, LanguageVersion> Operators = new(StringComparer.Ordinal)
    {
        ["??="] = Node(15),
        ["||="] = Node(15),
        ["&&="] = Node(15),
        ["?."] = Node(14),
        ["??"] = Node(14),
    };

    private static readonly ConcurrentDictionary<string, (DateTime Written, IReadOnlyList<(LanguageVersion Since, string Because)> Needs)> Remembered =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The newest Node.js the program this file is part of needs - the file and those it imports - and why; or null when nothing was found.</summary>
    public static ToolchainChoice.AtLeast? For(string javaScriptFile)
    {
        IEnumerable<string> files;

        try
        {
            files = ProgramFiles.Of(javaScriptFile).Prepend(javaScriptFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            files = [javaScriptFile];
        }

        return Of(files.Where(IsJavaScript).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    public static ToolchainChoice.AtLeast? Of(IEnumerable<string> javaScriptFiles)
    {
        var newest = javaScriptFiles.SelectMany(NeedsOf).OrderByDescending(need => need.Since).FirstOrDefault();
        return newest.Because is null ? null : new ToolchainChoice.AtLeast(newest.Since, newest.Because);
    }

    private static bool IsJavaScript(string file) => Path.GetExtension(file).ToLowerInvariant() is ".js" or ".mjs" or ".cjs";

    private static IReadOnlyList<(LanguageVersion Since, string Because)> NeedsOf(string file)
    {
        try
        {
            var written = File.GetLastWriteTimeUtc(file);
            if (Remembered.TryGetValue(file, out var known) && known.Written == written) return known.Needs;

            var tokens = new JsLexer(File.ReadAllText(file)).Tokens();
            var needs = NeedsIn(tokens, Path.GetFileName(file));

            Remembered[file] = (written, needs);
            return needs;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static List<(LanguageVersion, string)> NeedsIn(IReadOnlyList<JsToken> tokens, string name)
    {
        var needs = new List<(LanguageVersion, string)>();
        var imported = ImportedNames(tokens);

        void Add(LanguageVersion since, string uses, JsToken at)
        {
            if (needs.All(need => need.Item1 < since)) needs.Add((since, $"{name} {uses} at line {at.Line}, which Node.js {since} added"));
        }

        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            var next = index + 1 < tokens.Count ? tokens[index + 1] : null;
            var afterNext = index + 2 < tokens.Count ? tokens[index + 2] : null;
            var before = index > 0 ? tokens[index - 1] : null;

            if (token.Kind == JsTokenKind.Punctuator && Operators.TryGetValue(token.Text, out var operatorSince))
            {
                Add(operatorSince, token.Text == "?." ? "uses optional chaining, ?." : $"uses {token.Text}", token);
                continue;
            }

            if (token.Kind != JsTokenKind.Identifier) continue;

            // Object.groupBy: the object, a dot, the function, and a call - with no Object of the program's own. A property can
            // have a keyword's name, as Promise.try does.
            if (before is not { Text: "." } && next is { Text: "." } && afterNext is { Kind: JsTokenKind.Identifier or JsTokenKind.Keyword } member &&
                index + 3 < tokens.Count && tokens[index + 3].Text == "(" &&
                StaticFunctions.TryGetValue((token.Text, member.Text), out var staticSince) && !HasItsOwn(tokens, token.Text))
            {
                Add(staticSince, $"calls {token.Text}.{member.Text}", token);
                continue;
            }

            // items.toSorted(...): a method only arrays have - but not on what the program imports.
            if (before is { Text: "." } && next is { Text: "(" } && ArrayMethods.TryGetValue(token.Text, out var methodSince) &&
                !(index >= 2 && tokens[index - 2] is { Kind: JsTokenKind.Identifier } receiver && imported.Contains(receiver.Text)))
            {
                Add(methodSince, $"calls an array's {token.Text}", token);
                continue;
            }

            // fetch(...): a global function called, where the program has none of its own by the name.
            if (before is not { Text: "." } && next is { Text: "(" } && GlobalFunctions.TryGetValue(token.Text, out var globalSince) &&
                !HasItsOwn(tokens, token.Text))
            {
                Add(globalSince, $"calls {token.Text}", token);
            }
        }

        return needs;
    }

    /// <summary>
    /// Whether the program has something of its own by the name - declared, imported, a parameter, a method - so what the
    /// name calls may be that: any use of it other than as an object followed by a dot, or as a function called, counts.
    /// </summary>
    private static bool HasItsOwn(IReadOnlyList<JsToken> tokens, string name)
    {
        for (var index = 0; index < tokens.Count; index++)
        {
            if (tokens[index] is not { Kind: JsTokenKind.Identifier } token || token.Text != name) continue;

            var before = index > 0 ? tokens[index - 1].Text : "";
            if (before == ".") continue;
            if (before is "function" or "class") return true;

            var after = index + 1 < tokens.Count ? tokens[index + 1].Text : "";
            if (after == ".") continue;
            if (after != "(") return true;

            // name(...) { - a method or a function of its own being declared, not called.
            if (ClosingOf(tokens, index + 1) is { } closing && closing + 1 < tokens.Count && tokens[closing + 1].Text == "{") return true;
        }

        return false;
    }

    /// <summary>The names the file binds to what it imports: import x, * as x and { a as x } from ..., and const x = require(...), destructured or not.</summary>
    private static HashSet<string> ImportedNames(IReadOnlyList<JsToken> tokens)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < tokens.Count; index++)
        {
            if (tokens[index] is { Kind: JsTokenKind.Keyword, Text: "import" } && index + 1 < tokens.Count && tokens[index + 1].Text != "(")
            {
                for (var named = index + 1; named < tokens.Count && tokens[named].Text is not ("from" or ";") && tokens[named].Kind != JsTokenKind.Text; named++)
                {
                    if (tokens[named].Kind == JsTokenKind.Identifier) names.Add(tokens[named].Text);
                }
            }

            if (tokens[index] is { Kind: JsTokenKind.Keyword, Text: "const" or "let" or "var" })
            {
                var equals = index + 1;
                while (equals < tokens.Count && tokens[equals].Text is not ("=" or ";")) equals++;

                if (equals + 2 < tokens.Count && tokens[equals].Text == "=" && tokens[equals + 1].Text == "require" && tokens[equals + 2].Text == "(")
                {
                    for (var bound = index + 1; bound < equals; bound++)
                    {
                        if (tokens[bound].Kind == JsTokenKind.Identifier) names.Add(tokens[bound].Text);
                    }
                }
            }
        }

        return names;
    }

    /// <summary>The index of the bracket that closes the one at <paramref name="opening"/>, or null when it is never closed.</summary>
    private static int? ClosingOf(IReadOnlyList<JsToken> tokens, int opening)
    {
        var depth = 0;

        for (var index = opening; index < tokens.Count; index++)
        {
            if (tokens[index].Kind != JsTokenKind.Punctuator) continue;

            if (tokens[index].Text is "(" or "[" or "{") depth++;
            else if (tokens[index].Text is ")" or "]" or "}" && --depth == 0) return index;
        }

        return null;
    }
}
