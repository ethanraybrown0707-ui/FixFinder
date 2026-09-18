using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes.Rules;

// The C#, JavaScript and Go mistakes of a computer science degree past the first weeks: generic interfaces, structs in
// collections, iterators, records, threads and tasks; Node's callback APIs and promises; goroutines, channels and JSON.
// Each rule proposes one change.

// ======================================================================= C#

/// <summary><c>'Student' does not implement interface member 'IComparable.CompareTo(object?)'</c> - for a class that already has <c>CompareTo(Student)</c>.</summary>
public sealed partial class CSharpGenericInterface : ILocalFixRule
{
    public string Id => "csharp-generic-interface";

    [GeneratedRegex(@"^'(?<class>\w+)' does not implement interface member '(?<interface>IComparable|IEquatable|IComparer)\.(?<member>CompareTo|Equals|Compare)\((?:object\??|T\??)(?:, ?(?:object\??|T\??))?\)'$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context, "CS0535") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (Cs.Locate(context) is not { } at) return null;

        var (source, number, line, _) = at;
        var interfaceName = message.Groups["interface"].Value;
        var member = message.Groups["member"].Value;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        if (Regex.Matches(masked[number - 1], $@"(?<![\w.]){interfaceName}(?!\s*[<\w])").ToList() is not [var mention]) return null;

        var depths = CCode.DepthAtStart(masked);
        var outer = depths[number - 1];
        var end = number;
        while (end < masked.Count && depths[end + 1] > outer) end++;

        var parameter = member == "Compare"
            ? $@"\bCompare\s*\(\s*(?<type>[A-Z]\w*\??)\s+\w+\s*,\s*\k<type>\s+\w+\s*\)"
            : $@"\b{member}\s*\(\s*(?<type>[A-Z]\w*\??)\s+\w+\s*\)";

        var types = Enumerable.Range(number - 1, Math.Max(0, end - number + 1))
            .Select(i => Regex.Match(masked[i], parameter))
            .Where(m => m.Success)
            .Select(m => m.Groups["type"].Value.TrimEnd('?'))
            .Where(t => t is not ("Object" or "object"))
            .Distinct()
            .ToList();

        if (types is not [var type]) return null;

        return LocalFix.ReplaceLine(
            Id, $"Say what it compares: {interfaceName}<{type}>",
            $"`{interfaceName}` without a type is the old interface whose `{member}` takes `object`, so the `{member}` taking `{type}` that is " +
            $"already here does not count. `{interfaceName}<{type}>` is the generic one, and that method is exactly what it asks for - " +
            "`List.Sort()` and the other collections use it.",
            source.Path, number, line[..mention.Index] + $"{interfaceName}<{type}>" + line[(mention.Index + mention.Length)..]);
    }
}

/// <summary><c>points[0].X = 5</c> - <c>Cannot modify the return value ... because it is not a variable</c>, for a struct in a list.</summary>
public sealed partial class CSharpStructInCollection : ILocalFixRule
{
    public string Id => "csharp-struct-in-collection";

    [GeneratedRegex(@"^Cannot modify the return value of '(?<container>[^']+)\.this\[[^\]]*\]' because it is not a variable$")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?<lead>\s*)(?<list>[A-Za-z_][\w.]*)\[(?<index>[^\[\]]+)\]\.(?<member>[A-Za-z_]\w*)\s*(?<assignment>(?:[+\-*/%]?=)\s*[^;]+|\+\+|--)\s*;(?<tail>\s*(?://.*)?)$")]
    private static partial Regex Statement();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context, "CS1612") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (Cs.Locate(context) is not { } at || Statement().Match(at.Line) is not { Success: true } statement) return null;

        var (source, number, _, _) = at;
        var list = statement.Groups["list"].Value;
        var index = statement.Groups["index"].Value.Trim();
        if (Regex.IsMatch(index, @"\(|\+\+|--")) return null;

        var simple = list.Split('.')[^1];
        var name = simple.Length > 1 && simple.EndsWith('s') ? simple[..^1] : "item";
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (masked.Any(l => Regex.IsMatch(l, $@"(?<![\w.]){Regex.Escape(name)}(?!\w)"))) name = "copy";
        if (masked.Any(l => Regex.IsMatch(l, $@"(?<![\w.]){Regex.Escape(name)}(?!\w)"))) return null;

        var lead = statement.Groups["lead"].Value;
        var assignment = statement.Groups["assignment"].Value.Trim();
        var member = statement.Groups["member"].Value;
        var change = assignment is "++" or "--" ? $"{name}.{member}{assignment};" : $"{name}.{member} {assignment};";

        return new LocalFix
        {
            RuleId = Id,
            Title = $"Change a copy of the struct, then store it back in {list}",
            Explanation =
                $"The items of `{list}` are structs, and a struct is a value: `{list}[{index}]` hands back a copy, so changing `{member}` on it " +
                "would change a copy that is thrown away straight after - C# refuses to let that silently do nothing. The copy is taken, " +
                $"changed, and put back. (If the type were a `class` instead, `{list}[{index}].{member}` would change the item itself.)",
            File = source.Path, StartLine = number, RemoveCount = 1,
            NewLines = [$"{lead}var {name} = {list}[{index}];", $"{lead}{change}", $"{lead}{list}[{index}] = {name};{statement.Groups["tail"].Value}"],
        };
    }
}

/// <summary><c>yield return</c> in a method declared to return <c>List&lt;int&gt;</c> - an iterator returns <c>IEnumerable&lt;int&gt;</c>.</summary>
public sealed partial class CSharpIteratorReturnType : ILocalFixRule
{
    public string Id => "csharp-iterator-return-type";

    [GeneratedRegex(@"^The body of '[^']+' cannot be an iterator block because '(?<type>[^']+)' is not an iterator interface type$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context, "CS1624") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (Cs.Locate(context) is not { } at) return null;

        var (source, number, line, _) = at;
        var type = message.Groups["type"].Value;

        var element = Regex.Match(type, @"^(?:System\.Collections\.Generic\.)?(?:List|IList|ICollection|IReadOnlyList|IReadOnlyCollection|Collection|HashSet|Queue|Stack)<(?<element>.+)>$") is { Success: true } generic
            ? generic.Groups["element"].Value
            : Regex.Match(type, @"^(?<element>[\w.<>]+)\[\]$") is { Success: true } array ? array.Groups["element"].Value : null;

        if (element is null) return null;

        var written = Regex.Escape(type.Replace("System.Collections.Generic.", "", StringComparison.Ordinal)).Replace(@"\ ", @"\s*", StringComparison.Ordinal);
        if (Regex.Matches(line, $@"(?<![\w.]){written}(?=\s+\w+\s*[(<])").ToList() is not [var declared]) return null;

        return LocalFix.ReplaceLine(
            Id, $"Return IEnumerable<{element}> from an iterator",
            $"`yield return` makes the method an iterator: it hands items out one at a time, as they are asked for, rather than building a " +
            $"`{type}`. An iterator's return type has to say that - `IEnumerable<{element}>`. A caller that needs a list can call `.ToList()` on it.",
            source.Path, number, line[..declared.Index] + $"IEnumerable<{element}>" + line[(declared.Index + declared.Length)..]);
    }
}

/// <summary><c>p.X = 5</c> on a record - init-only, where a changed copy is made with <c>with</c>.</summary>
public sealed partial class CSharpRecordWith : ILocalFixRule
{
    public string Id => "csharp-record-with";

    [GeneratedRegex(@"^Init-only property or indexer '(?<type>\w+)\.(?<property>\w+)' can only be assigned")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?<lead>\s*)(?<variable>[a-z_]\w*)\.(?<property>[A-Z]\w*)\s*=\s*(?<value>[^;]+?)\s*;(?<tail>\s*(?://.*)?)$")]
    private static partial Regex Assignment();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context, "CS8852") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (Cs.Locate(context) is not { } at || Assignment().Match(at.Line) is not { Success: true } assignment) return null;

        var (source, number, _, _) = at;
        var type = message.Groups["type"].Value;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        if (!masked.Any(l => Regex.IsMatch(l, $@"\brecord\s+(?:class\s+|struct\s+)?{Regex.Escape(type)}\b"))) return null;

        var variable = assignment.Groups["variable"].Value;
        var property = assignment.Groups["property"].Value;

        // A local that can be given a new value - not a readonly field or a parameter declared in.
        if (!masked.Take(number - 1).Any(l => Regex.IsMatch(l, $@"(?:\bvar|\b{Regex.Escape(type)}\??)\s+{Regex.Escape(variable)}\s*=")) ) return null;

        return LocalFix.ReplaceLine(
            Id, $"Make a changed copy: {variable} = {variable} with {{ {property} = ... }}",
            $"`{type}` is a record, and its properties can only be set when it is made - that is what keeps a record's value from changing " +
            $"underneath the code holding it. `with` makes a copy with `{property}` changed, and `{variable}` then holds the copy.",
            source.Path, number,
            $"{assignment.Groups["lead"].Value}{variable} = {variable} with {{ {property} = {assignment.Groups["value"].Value} }};{assignment.Groups["tail"].Value}");
    }
}

/// <summary><c>new Thread(Work())</c> - calling the method, where the thread wanted the method to call.</summary>
public sealed partial class CSharpDelegateCalled : ILocalFixRule
{
    public string Id => "csharp-delegate-called";

    [GeneratedRegex(@"^Argument \d+: cannot convert from 'void' to '(?:System\.Threading\.)?(?:ThreadStart|ParameterizedThreadStart)'|^Argument \d+: cannot convert from 'void' to 'System\.(?:Action|Func<[^']*>)'")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?<name>[A-Za-z_][\w.]*)\s*\((?<arguments>[^()]*)\)")]
    private static partial Regex Call();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context, "CS1503") || !Message().IsMatch(context.Error.Message ?? "")) return null;
        if (Cs.Locate(context) is not { Index: >= 0 } at) return null;

        var (source, number, line, index) = at;
        if (Call().Match(line[index..]) is not { Success: true } call) return null;

        var name = call.Groups["name"].Value;
        var arguments = call.Groups["arguments"].Value.Trim();
        var replacement = arguments.Length == 0 ? name : $"() => {name}({arguments})";

        return LocalFix.ReplaceLine(
            Id, arguments.Length == 0 ? $"Pass the method itself: {name}" : $"Pass a lambda that calls it: () => {name}(...)",
            $"`{name}(...)` runs the method straight away, on this thread, and passes on what it returns - nothing. The thread needs the " +
            $"method to run later, on its own: {(arguments.Length == 0 ? $"`{name}` without brackets" : "a lambda that makes the call")}.",
            source.Path, number, line[..index] + replacement + line[(index + call.Length)..]);
    }
}

/// <summary><c>static async void Main</c> - <c>A void or int returning entry point cannot be async</c>.</summary>
public sealed partial class CSharpAsyncMain : ILocalFixRule
{
    public string Id => "csharp-async-main";

    [GeneratedRegex(@"\basync\s+(?<type>void|int)\s+Main\b")]
    private static partial Regex Main();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context, "CS4009") || Cs.Locate(context) is not { } at) return null;

        var (source, number, line, _) = at;
        if (Main().Match(line) is not { Success: true } main) return null;

        var type = main.Groups["type"];
        var task = type.Value == "void" ? "Task" : "Task<int>";

        return LocalFix.ReplaceLine(
            Id, $"Let an async Main return {task}",
            $"An `async void` method returns the moment it reaches its first `await`, and for `Main` that would end the program before the " +
            $"work is done. `async {task} Main` gives the runtime something to wait for.",
            source.Path, number, line[..type.Index] + task + line[(type.Index + type.Length)..]);
    }
}

/// <summary><c>string text = File.ReadAllTextAsync(path)</c> - a <c>Task&lt;string&gt;</c> where the string was meant.</summary>
public sealed partial class CSharpTaskNotAwaited : ILocalFixRule
{
    public string Id => "csharp-task-not-awaited";

    [GeneratedRegex(@"^Cannot implicitly convert type 'System\.Threading\.Tasks\.Task<(?<inner>.+)>' to '(?<target>.+)'$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context, "CS0029") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (message.Groups["inner"].Value != message.Groups["target"].Value) return null;
        if (Cs.Locate(context) is not { Index: >= 0 } at) return null;

        var (source, number, line, index) = at;
        if (line[index..].StartsWith("await", StringComparison.Ordinal)) return null;

        // Inside an async method, or top-level statements, where await can be written.
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var depths = CCode.DepthAtStart(masked);
        var header = number - 1;
        while (header > 0 && !(depths[header] < depths[number - 1] && Regex.IsMatch(masked[header], @"\)\s*\{?\s*$") && Regex.IsMatch(masked[header], @"\w+\s*\(") && !Regex.IsMatch(masked[header], @"^\s*(?:if|for|foreach|while|using|lock|switch|catch)\b"))) header--;

        var topLevel = !masked.Any(l => Regex.IsMatch(l, @"\bstatic\s+(?:async\s+)?\w+(?:<[^>]*>)?\s+Main\s*\("));
        if (!topLevel && !Regex.IsMatch(masked[header], @"\basync\b")) return null;

        return LocalFix.ReplaceLine(
            Id, "Wait for the result: await",
            $"A method ending in `Async` starts the work and returns straight away with a `Task<{message.Groups["inner"].Value}>` - a promise of " +
            $"the {message.Groups["inner"].Value} it will produce. `await` waits for it to finish and gives back the {message.Groups["inner"].Value} itself.",
            source.Path, number, line[..index] + "await " + line[index..]);
    }
}

// ======================================================================= JavaScript

/// <summary><c>fs is not defined</c> - a built-in module used without being loaded.</summary>
public sealed partial class JsBuiltinNotLoaded : ILocalFixRule
{
    public string Id => "js-builtin-not-loaded";

    [GeneratedRegex(@"^(?<name>[A-Za-z_]\w*) is not defined$")]
    private static partial Regex Message();

    private static readonly Dictionary<string, (string Module, bool Named)> Known = new(StringComparer.Ordinal)
    {
        ["fs"] = ("fs", false), ["path"] = ("path", false), ["os"] = ("os", false), ["http"] = ("http", false), ["https"] = ("https", false),
        ["crypto"] = ("crypto", false), ["readline"] = ("readline", false), ["util"] = ("util", false), ["events"] = ("events", false),
        ["child_process"] = ("child_process", false), ["net"] = ("net", false), ["url"] = ("url", false), ["assert"] = ("assert", false),
        ["zlib"] = ("zlib", false), ["EventEmitter"] = ("events", true), ["promisify"] = ("util", true), ["createServer"] = ("http", true),
        ["readFileSync"] = ("fs", true), ["writeFileSync"] = ("fs", true), ["existsSync"] = ("fs", true), ["execSync"] = ("child_process", true),
    };

    public LocalFix? Propose(LocalFixContext context)
    {
        if (Js.Error(context.Error, "ReferenceError", Message()) is not { } message || context.Read(context.Frame?.File) is not { } source) return null;
        if (!Js.IsJs(source)) return null;

        var name = message.Groups["name"].Value;
        if (!Known.TryGetValue(name, out var known)) return null;

        var lines = source.Lines;
        var masked = CodeText.MaskAll(lines, Syntax.CLike);
        var n = Regex.Escape(name);
        var declares = new Regex($@"\b(?:(?:const|let|var)\s+(?:{n}|\{{[^}}]*\b{n}\b[^}}]*\}})\s*=|function\s+{n}\b|class\s+{n}\b|import\s+(?:{n}\b|\{{[^}}]*\b{n}\b[^}}]*\}}|\*\s+as\s+{n}\b))");
        if (masked.Any(l => declares.IsMatch(l))) return null;

        var module = source.Path.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase) || masked.Any(l => Regex.IsMatch(l, @"^\s*(?:import\s|export\s)"));

        var statement = (module, known.Named) switch
        {
            (true, false) => $"import {name} from \"node:{known.Module}\";",
            (true, true) => $"import {{ {name} }} from \"node:{known.Module}\";",
            (false, false) => $"const {name} = require(\"{known.Module}\");",
            (false, true) => name == "EventEmitter" ? "const EventEmitter = require(\"events\");" : $"const {{ {name} }} = require(\"{known.Module}\");",
        };

        // After a shebang, "use strict" and the loads already at the top.
        var at = 0;
        while (at < lines.Count && (lines[at].StartsWith("#!", StringComparison.Ordinal) || Regex.IsMatch(lines[at], @"^\s*[""']use strict[""'];?\s*$"))) at++;
        while (at < lines.Count && Regex.IsMatch(masked[at], @"^\s*(?:(?:const|let|var)\s+[^=]+=\s*require\s*\(.*\)\s*;?|import\s.*from\s.*;?|import\s+[""'].*)\s*$")) at++;

        return LocalFix.Insert(
            Id, $"Load {name}: {statement.TrimEnd(';')}",
            $"`{name}` comes from Node's built-in `{known.Module}` module, and a module is only available in a file that loads it - " +
            $"{(module ? "with `import`, since this file is an ES module" : "with `require`")}.",
            source.Path, at + 1, [statement]);
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
        if (Js.Locate(context) is not { } at) return null;

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
        if (Js.Locate(context) is not { } at) return null;

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
        if (Js.Error(context.Error, "TypeError", Message()) is null || Js.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.CLike);
        if (Call().Matches(masked).ToList() is not [var call]) return null;

        var open = call.Index + call.Length - 1;
        if (CCode.Matching(masked, open) is not { } close) return null;

        var inside = line[(open + 1)..close].Trim();
        if (inside.Length == 0 || inside.StartsWith('[')) return null;

        var parts = Cpp.SplitTopLevel(masked, open + 1, close, ',');
        if (parts.Count < 2 && Regex.IsMatch(inside, @"^[A-Za-z_$][\w$]*$")) return null;

        return LocalFix.ReplaceLine(
            Id, $"Give Promise.{call.Groups["method"].Value} one array: [{inside}]",
            $"`Promise.{call.Groups["method"].Value}` takes a single array of promises and waits on them together. Given them one by one, " +
            "it treats the first as that array, and a promise is not something it can step through. Square brackets make them one array.",
            source.Path, number, line[..(open + 1)] + $"[{inside}]" + line[close..]);
    }
}

// ======================================================================= Go

/// <summary>A deadlock at <c>wg.Wait()</c> - a <c>sync.WaitGroup</c> passed by value, so each goroutine marks its own copy done.</summary>
public sealed partial class GoWaitGroupByValue : ILocalFixRule
{
    public string Id => "go-waitgroup-by-value";

    [GeneratedRegex(@"^all goroutines are asleep - deadlock!$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.Runtime(context.Error, Message()) is null || !context.Error.RawText.Contains("WaitGroup", StringComparison.Ordinal)) return null;

        foreach (var source in GoCode.Files(context))
        {
            var lines = source.Lines;
            var masked = CodeText.MaskAll(lines, Syntax.CLike);
            var definitions = Enumerable.Range(0, masked.Count)
                .Select(i => (Index: i, Match: Regex.Match(masked[i], @"^func\s+(?<function>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)")))
                .Where(x => x.Match.Success && Regex.IsMatch(x.Match.Groups["parameters"].Value, @"\b\w+\s+sync\.WaitGroup\b"))
                .ToList();

            if (definitions.Count == 0) continue;
            if (definitions is not [var definition]) return null;

            var function = definition.Match.Groups["function"].Value;
            var parameters = definition.Match.Groups["parameters"].Value.Split(',').Select(p => p.Trim()).ToList();
            var position = parameters.FindIndex(p => Regex.IsMatch(p, @"^\w+\s+sync\.WaitGroup$"));
            if (position < 0) return null;

            var calls = Enumerable.Range(0, masked.Count)
                .Where(i => i != definition.Index && Regex.IsMatch(masked[i], $@"(?<![\w.]){Regex.Escape(function)}\s*\("))
                .ToList();

            if (calls.Count == 0) return null;

            var changed = new Dictionary<int, string>
            {
                [definition.Index] = Regex.Replace(lines[definition.Index], @"(?<=\b\w+\s+)sync\.WaitGroup\b", "*sync.WaitGroup"),
            };

            foreach (var call in calls)
            {
                var text = masked[call];
                var at = Regex.Match(text, $@"(?<![\w.]){Regex.Escape(function)}\s*\(");
                var open = at.Index + at.Length - 1;
                if (CCode.Matching(text, open) is not { } close) return null;

                var arguments = Cpp.SplitTopLevel(text, open + 1, close, ',');
                if (arguments.Count != parameters.Count) return null;

                var (start, end) = arguments[position];
                var argument = lines[call][start..end];
                var trimmed = argument.Trim();
                if (!Regex.IsMatch(trimmed, @"^[A-Za-z_]\w*$")) return null;

                var offset = start + argument.IndexOf(trimmed, StringComparison.Ordinal);
                changed[call] = lines[call][..offset] + "&" + lines[call][offset..];
            }

            var first = changed.Keys.Min();
            var last = changed.Keys.Max();

            return new LocalFix
            {
                RuleId = Id,
                Title = $"Share one WaitGroup: pass &wg, take *sync.WaitGroup",
                Explanation =
                    $"`{function}` takes its `sync.WaitGroup` by value, so every goroutine gets a copy and calls `Done` on the copy. The " +
                    "WaitGroup that `Wait` is watching never hears about it and waits forever - Go notices nothing else can run and stops " +
                    "with a deadlock. A pointer shares the one WaitGroup.",
                File = source.Path, StartLine = first + 1, RemoveCount = last - first + 1,
                NewLines = Enumerable.Range(first, last - first + 1).Select(i => changed.TryGetValue(i, out var line) ? line : lines[i]).ToList(),
            };
        }

        return null;
    }
}

/// <summary><c>json: Unmarshal(non-pointer main.Config)</c> - decoding into a copy, which the result can never reach.</summary>
public sealed partial class GoUnmarshalPointer : ILocalFixRule
{
    public string Id => "go-unmarshal-pointer";

    [GeneratedRegex(@"^(?:json|xml|yaml): Unmarshal\(non-pointer [\w.\[\]*]+\)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "go" } error || !Message().IsMatch(error.Message ?? "")) return null;
        if (GoCode.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (GoCode.Function(masked, at.Number - 1) is not { } function) return null;

        var found = Enumerable.Range(function.Header, at.Number - function.Header)
            .Select(i => (Index: i, Match: Regex.Match(masked[i], @"\b(?:json|xml)\.Unmarshal\s*\((?<data>(?:[^()]|\([^()]*\))*?),\s*(?<target>[A-Za-z_]\w*)\s*\)")))
            .Where(x => x.Match.Success)
            .ToList();

        if (found is not [var call]) return null;

        var target = call.Match.Groups["target"];
        var line = source.Lines[call.Index];

        return LocalFix.ReplaceLine(
            Id, $"Decode into {target.Value} itself: &{target.Value}",
            $"Go passes `{target.Value}` by value, so `Unmarshal` would get a copy and fill in the copy - and `{target.Value}` would stay empty. " +
            $"It refuses instead. `&{target.Value}` passes a pointer, so it fills in the variable itself.",
            source.Path, call.Index + 1, line[..target.Index] + "&" + line[target.Index..]);
    }
}

/// <summary>A deadlock at <c>for v := range ch</c> - the goroutine sending on the channel never closes it, so the loop never ends.</summary>
public sealed partial class GoCloseChannel : ILocalFixRule
{
    public string Id => "go-close-channel";

    [GeneratedRegex(@"^all goroutines are asleep - deadlock!$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.Runtime(context.Error, Message()) is null || !context.Error.RawText.Contains("[chan receive]", StringComparison.Ordinal)) return null;
        if (GoCode.Locate(context) is not { } at) return null;

        var source = at.Source;
        var lines = source.Lines;
        var masked = CodeText.MaskAll(lines, Syntax.CLike);

        if (Regex.Match(masked[at.Number - 1], @"\bfor\s+(?:[\w\s,]*:?=\s*)?range\s+(?<channel>[A-Za-z_]\w*)\s*\{") is not { Success: true } loop) return null;
        if (GoCode.Function(masked, at.Number - 1) is not { } function) return null;

        var channel = loop.Groups["channel"].Value;

        // The one goroutine started with this channel in the function before the loop.
        var starts = Enumerable.Range(function.Header, at.Number - 1 - function.Header)
            .Select(i => Regex.Match(masked[i], $@"^\s*go\s+(?<function>[A-Za-z_]\w*)\s*\((?<arguments>[^)]*)\)"))
            .Where(m => m.Success && Regex.IsMatch(m.Groups["arguments"].Value, $@"(?<![\w.]){Regex.Escape(channel)}(?!\w)"))
            .ToList();

        if (starts is not [var start]) return null;

        var arguments = start.Groups["arguments"].Value.Split(',').Select(a => a.Trim()).ToList();
        var position = arguments.IndexOf(channel);
        if (position < 0) return null;

        var sender = start.Groups["function"].Value;
        var headers = Enumerable.Range(0, masked.Count).Where(i => Regex.IsMatch(masked[i], $@"^func\s+{Regex.Escape(sender)}\s*\(")).ToList();
        if (headers is not [var header] || GoCode.Function(masked, header) is not { } body) return null;

        var parameters = Regex.Match(masked[header], @"\((?<parameters>[^)]*)\)").Groups["parameters"].Value.Split(',').Select(p => p.Trim()).ToList();
        if (parameters.Count != arguments.Count || Regex.Match(parameters[position], @"^(?<name>\w+)\s+(?:chan|chan<-)\s") is not { Success: true } parameter) return null;

        var name = parameter.Groups["name"].Value;
        var inside = Enumerable.Range(header + 1, body.Close - header - 1).ToList();

        if (!inside.Any(i => Regex.IsMatch(masked[i], $@"(?<![\w.]){Regex.Escape(name)}\s*<-"))) return null;
        if (inside.Any(i => Regex.IsMatch(masked[i], $@"\bclose\s*\(\s*{Regex.Escape(name)}\s*\)|\breturn\b"))) return null;

        var indent = inside.Select(i => lines[i]).FirstOrDefault(l => l.Trim().Length > 0) is { } first ? CodeText.Indentation(first) : "\t";

        return LocalFix.Insert(
            Id, $"Close {name} when {sender} has sent everything",
            $"`for ... range {channel}` keeps receiving until the channel is closed, and `{sender}` sends its values and then returns " +
            $"without closing it - so the loop waits for a value that never comes, and Go stops the program as a deadlock. `close({name})` " +
            "at the end of the sender tells the loop there is nothing more.",
            source.Path, body.Close + 1, [$"{indent}close({name})"]);
    }
}
