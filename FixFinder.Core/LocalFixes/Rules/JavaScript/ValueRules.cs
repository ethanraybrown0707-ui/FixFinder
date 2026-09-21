using System.Diagnostics;
using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary><c>items.map is not a function</c> where <c>items</c> is the promise an async function returned.</summary>
public sealed partial class JsPromiseNotAwaited : ILocalFixRule
{
    public string Id => "js-promise-not-awaited";

    [GeneratedRegex(@"^(?<object>[A-Za-z_$][\w$]*)\.[\w$]+ is not a function$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaScriptCode.ErrorMessage(context.Error, "TypeError", Message()) is not { } message || JavaScriptCode.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var name = Regex.Escape(message.Groups["object"].Value);

        for (var i = at.Number - 1; i >= 0; i--)
        {
            var declared = Regex.Match(masked[i], $@"\b(?:const|let|var)\s+{name}\s*=\s*(?<call>(?<function>[A-Za-z_$][\w$]*)\s*\()");
            if (!declared.Success) continue;

            var function = Regex.Escape(declared.Groups["function"].Value);
            var isAsync = masked.Any(text => Regex.IsMatch(text, $@"\basync\s+function\s+{function}\s*\(|\b{function}\s*=\s*async\b"));
            if (!isAsync || JavaScriptCode.EnclosingFunction(masked, i) is not { IsAsync: true }) return null;

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
        if (JavaScriptCode.ErrorMessage(context.Error, "TypeError", Message()) is not { } message || JavaScriptCode.Locate(context) is not { } at) return null;

        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.CLike);
        var name = message.Groups["name"].Value;
        if (JavaScriptCode.KindOf(masked, at.Source.Lines, name) is not ("array" or "string")) return null;

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

/// <summary><c>items.append is not a function</c>, <c>seen.push</c> on a Set, <c>text.contains</c>, <c>Math.squareRoot</c> - a
/// method the value does not have, answered from what kind of value it was declared as.</summary>
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
        if (JavaScriptCode.ErrorMessage(context.Error, "TypeError", Message()) is not { } message || JavaScriptCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var name = message.Groups["object"].Value;
        var member = message.Groups["member"].Value;

        string? right;
        string shown;

        if (BuiltIns.Contains(name))
        {
            right = (name == "Math" ? MathWords.GetValueOrDefault(member) : null) ?? CodeText.Nearest(member, NodeRuntime.Members(name));
            shown = $"`{name}`";
        }
        else if (JavaScriptCode.KindOf(masked, source.Lines, name) is { } kind && Kinds.TryGetValue(kind, out var entry))
        {
            right = entry.Foreign.GetValueOrDefault(member) ?? CodeText.Nearest(member, NodeRuntime.Members(entry.Prototype));
            shown = entry.Shown;
        }
        else if (JavaScriptCode.KindOf(masked, source.Lines, name) == "object" && member == "get")
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
        if (JavaScriptCode.ErrorMessage(context.Error, "TypeError", Message()) is null || JavaScriptCode.Locate(context) is not { } at) return null;

        var masked = CodeText.Mask(at.Line, Syntax.CLike);
        if (Reduce().Matches(masked).ToList() is not [var reduce]) return null;

        var open = reduce.Groups["open"].Index;
        if (Brackets.ClosingParenthesis(masked, open) is not { } close) return null;
        if (CppCode.SplitTopLevel(masked, open + 1, close, ',').Count != 1) return null;
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
        if (JavaScriptCode.ErrorMessage(context.Error, "TypeError", Message()) is not { } message || JavaScriptCode.Locate(context) is not { } at) return null;

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
        if (JavaScriptCode.ErrorMessage(context.Error, "TypeError", Message()) is null || JavaScriptCode.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        var reads = Regex.Matches(masked[at.Number - 1], @"(?<![\w$.])(?<map>[A-Za-z_$][\w$]*)\s*\.\s*get\s*\(")
            .Select(m => m.Groups["map"].Value)
            .Where(map => JavaScriptCode.KindOf(masked, source.Lines, map) == "map")
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

/// <summary><c>fs.readFile(path, "utf8")</c> with no callback, used as if it returned the contents.</summary>
public sealed partial class JsCallbackApiUsedForValue : ILocalFixRule
{
    public string Id => "js-callback-api-used-for-value";

    [GeneratedRegex(@"^The ""cb"" argument must be of type function\. Received")]
    private static partial Regex Message();

    [GeneratedRegex(@"\bfs\.(?<method>readFile|writeFile|appendFile|readdir|mkdir|stat|lstat|unlink|rename|copyFile|access|rm|rmdir|exists)\s*\(")]
    private static partial Regex Call();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "node", ExceptionType: "TypeError", ErrorCode: "ERR_INVALID_ARG_TYPE" } || !Message().IsMatch(context.Error.Message ?? "")) return null;
        if (JavaScriptCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.CLike);
        if (Call().Matches(masked).ToList() is not [var call] || Regex.IsMatch(masked[..call.Index], @"\bawait\s*$")) return null;

        var method = call.Groups["method"];

        return LocalFix.ReplaceLine(
            Id, $"Use the version that returns the result: fs.{method.Value}Sync",
            $"`fs.{method.Value}` does its work in the background and hands the result to a callback when it is done - it returns nothing " +
            $"itself, and without a callback Node refuses the call. `fs.{method.Value}Sync` waits and returns the result, which is what this " +
            "line uses it for. (In a program that must keep responding while it waits, `await fs.promises." + method.Value + "(...)` does the same without blocking.)",
            source.Path, number, line[..(method.Index + method.Length)] + "Sync" + line[(method.Index + method.Length)..]);
    }
}

/// <summary><c>setTimeout(tick(), 100)</c> - calling the function now, where it should be called later.</summary>
public sealed partial class JsCallbackCalledTooSoon : ILocalFixRule
{
    public string Id => "js-callback-called-too-soon";

    [GeneratedRegex(@"^The ""(?:callback|listener)"" argument must be of type function\. Received")]
    private static partial Regex Message();

    [GeneratedRegex(@"(?:\b(?:setTimeout|setInterval|setImmediate|process\.nextTick|queueMicrotask)\s*\(\s*|\.(?:on|once|addListener|prependListener)\s*\(\s*(?:""[^""]*""|'[^']*'|`[^`]*`|[\w.]+)\s*,\s*)(?<name>[A-Za-z_$][\w$.]*)\s*\((?<arguments>[^()]*)\)")]
    private static partial Regex Call();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "node", ExceptionType: "TypeError", ErrorCode: "ERR_INVALID_ARG_TYPE" } || !Message().IsMatch(context.Error.Message ?? "")) return null;
        if (JavaScriptCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (Call().Matches(line).ToList() is not [var call]) return null;

        var name = call.Groups["name"];
        var arguments = call.Groups["arguments"].Value.Trim();
        var end = call.Index + call.Length;
        var replacement = arguments.Length == 0 ? name.Value : $"() => {name.Value}({arguments})";

        return LocalFix.ReplaceLine(
            Id, arguments.Length == 0 ? $"Pass {name.Value} itself, to be called later" : $"Pass a function that calls {name.Value} later",
            $"`{name.Value}(...)` runs straight away and passes on what it returns - `undefined` - so there is nothing left to call when " +
            $"the time comes. {(arguments.Length == 0 ? $"`{name.Value}` without brackets is the function itself" : "An arrow function makes the call when it is run")}.",
            source.Path, number, line[..name.Index] + replacement + line[end..]);
    }
}

/// <summary><c>Promise.all(a, b)</c> - <c>object is not iterable</c>, because all takes one array of promises.</summary>
public sealed partial class JsPromiseCombinatorArray : ILocalFixRule
{
    public string Id => "js-promise-combinator-array";

    [GeneratedRegex(@"is not iterable")]
    private static partial Regex Message();

    [GeneratedRegex(@"\bPromise\.(?<method>all|allSettled|race|any)\s*\(")]
    private static partial Regex Call();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaScriptCode.ErrorMessage(context.Error, "TypeError", Message()) is null || JavaScriptCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.CLike);
        if (Call().Matches(masked).ToList() is not [var call]) return null;

        var open = call.Index + call.Length - 1;
        if (Brackets.ClosingParenthesis(masked, open) is not { } close) return null;

        var inside = line[(open + 1)..close].Trim();
        if (inside.Length == 0 || inside.StartsWith('[')) return null;

        var parts = CppCode.SplitTopLevel(masked, open + 1, close, ',');
        if (parts.Count < 2 && Regex.IsMatch(inside, @"^[A-Za-z_$][\w$]*$")) return null;

        return LocalFix.ReplaceLine(
            Id, $"Give Promise.{call.Groups["method"].Value} one array: [{inside}]",
            $"`Promise.{call.Groups["method"].Value}` takes a single array of promises and waits on them together. Given them one by one, " +
            "it treats the first as that array, and a promise is not something it can step through. Square brackets make them one array.",
            source.Path, number, line[..(open + 1)] + $"[{inside}]" + line[close..]);
    }
}
