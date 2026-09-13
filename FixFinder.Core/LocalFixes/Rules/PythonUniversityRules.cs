using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>Finding the function, class or loop a Python line belongs to - which in Python is indentation.</summary>
internal static partial class PyScope
{
    [GeneratedRegex(@"^\s*(?:async\s+)?def\s+(?<name>[A-Za-z_]\w*)\s*\((?<parameters>.*)\)\s*(?:->[^:]*)?:")]
    public static partial Regex Def();

    [GeneratedRegex(@"^\s*(?:async\s+)?def\s")]
    public static partial Regex AnyDef();

    /// <summary>The nearest line above <paramref name="index"/>, less indented than it, that matches <paramref name="header"/>.</summary>
    public static int Enclosing(IReadOnlyList<string> masked, int index, Regex header)
    {
        var indent = CodeText.Indentation(masked[index]).Length;

        for (var i = index - 1; i >= 0 && indent > 0; i--)
        {
            if (masked[i].Trim().Length == 0) continue;

            var own = CodeText.Indentation(masked[i]).Length;
            if (own >= indent) continue;

            if (header.IsMatch(masked[i])) return i;
            indent = own;
        }

        return -1;
    }

    /// <summary>The first line of a block's body, and the line after its last non-blank one.</summary>
    public static (int First, int End) Body(IReadOnlyList<string> lines, int header)
    {
        var indent = CodeText.Indentation(lines[header]).Length;
        var end = header + 1;

        while (end < lines.Count && (lines[end].Trim().Length == 0 || CodeText.Indentation(lines[end]).Length > indent)) end++;
        while (end > header + 1 && lines[end - 1].Trim().Length == 0) end--;

        return (header + 1, end);
    }

    /// <summary>Where a new first statement goes: after the docstring, which has to stay first.</summary>
    public static int FirstStatement(IReadOnlyList<string> lines, int first, int end)
    {
        while (first < end && lines[first].Trim().Length == 0) first++;
        if (first >= end) return first;

        var opening = lines[first].TrimStart();
        var quote = opening.StartsWith("\"\"\"", StringComparison.Ordinal) ? "\"\"\"" : opening.StartsWith("'''", StringComparison.Ordinal) ? "'''" : null;

        if (quote is null) return first;
        if (opening.Length > 3 && opening[3..].Contains(quote, StringComparison.Ordinal)) return first + 1;

        var close = first + 1;
        while (close < end && !lines[close].Contains(quote, StringComparison.Ordinal)) close++;

        return close + 1;
    }

    /// <summary>The names of a def's parameters, without annotations, defaults or stars.</summary>
    public static List<string> Parameters(string parameters) => parameters
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(p => Regex.Match(p, @"^\*{0,2}(?<name>[A-Za-z_]\w*)").Groups["name"].Value)
        .Where(name => name.Length > 0)
        .ToList();

    /// <summary>Where <paramref name="op"/> first appears outside brackets, or -1.</summary>
    public static int TopLevel(string masked, string op)
    {
        var depth = 0;

        for (var i = 0; i + op.Length <= masked.Length; i++)
        {
            var c = masked[i];

            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') depth--;
            else if (depth == 0 && string.CompareOrdinal(masked, i, op, 0, op.Length) == 0) return i;
        }

        return -1;
    }

    /// <summary>The lines that assign to a name: <c>name = ...</c>, not <c>name == ...</c>.</summary>
    public static List<int> Assignments(IReadOnlyList<string> masked, string name) =>
        Enumerable.Range(0, masked.Count)
            .Where(i => Regex.IsMatch(masked[i], $@"^\s*{Regex.Escape(name)}\s*(?::[^=]*)?=(?!=)"))
            .ToList();
}

// ======================================================================= classes and objects

/// <summary><c>'float' object is not callable</c> from brackets after a <c>@property</c>.</summary>
public sealed partial class PythonPropertyCalled : ILocalFixRule
{
    public string Id => "python-property-called";

    [GeneratedRegex(@"^'(?<type>[\w.]+)' object is not callable$")]
    private static partial Regex Message();

    [GeneratedRegex(@"\.(?<name>[A-Za-z_]\w*)(?<brackets>\s*\(\s*\))$")]
    private static partial Regex Call();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "TypeError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (Py.UnderlineSpan(context.Error, number, line) is not { } span) return null;
        if (Call().Match(line[span.Start..span.End]) is not { Success: true } call) return null;

        var name = call.Groups["name"].Value;
        var masked = CodeText.MaskAll(source.Lines, Syntax.Python);

        var property = Enumerable.Range(1, Math.Max(0, masked.Count - 1)).Any(i =>
            Regex.IsMatch(masked[i], $@"^\s*def\s+{Regex.Escape(name)}\s*\(\s*self\s*\)") &&
            Regex.IsMatch(masked[i - 1], @"^\s*@property\b"));

        if (!property) return null;

        var from = span.Start + call.Groups["brackets"].Index;

        return LocalFix.ReplaceLine(
            Id, $"Read {name} without brackets",
            $"`{name}` is a `@property`, so it is read like an attribute: reading it already runs the method and gives its value - " +
            $"here a {message.Groups["type"].Value}. The brackets then try to call that value.",
            source.Path, number, line[..from] + line[(from + call.Groups["brackets"].Length)..]);
    }
}

/// <summary><c>'Student' object has no attribute 'name'</c> when <c>__init__</c> said <c>name = name</c>.</summary>
public sealed partial class PythonMissingSelfAttribute : ILocalFixRule
{
    public string Id => "python-missing-self-attribute";

    [GeneratedRegex(@"^'(?<cls>[A-Za-z_]\w*)' object has no attribute '(?<attr>[A-Za-z_]\w*)'")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "AttributeError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (context.Read(context.Frame?.File) is not { } source) return null;

        var lines = source.Lines;
        var masked = CodeText.MaskAll(lines, Syntax.Python);
        var cls = Regex.Escape(message.Groups["cls"].Value);
        var attr = message.Groups["attr"].Value;

        var classes = Enumerable.Range(0, masked.Count).Where(i => Regex.IsMatch(masked[i], $@"^\s*class\s+{cls}\b")).ToList();
        if (classes is not [var cl]) return null;

        var (classFirst, classEnd) = PyScope.Body(lines, cl);

        // A class that stores it somewhere already has a different problem.
        if (Enumerable.Range(classFirst, classEnd - classFirst).Any(i => Regex.IsMatch(masked[i], $@"\bself\.{Regex.Escape(attr)}\s*(?::[^=]*)?=(?!=)"))) return null;

        var inits = Enumerable.Range(classFirst, classEnd - classFirst).Where(i => Regex.IsMatch(masked[i], @"^\s+def\s+__init__\s*\(\s*self\b")).ToList();
        if (inits is not [var init]) return null;

        var (first, end) = PyScope.Body(lines, init);
        var bare = Enumerable.Range(first, end - first).Where(i => Regex.IsMatch(masked[i], $@"^\s+{Regex.Escape(attr)}\s*(?::[^=]*)?=(?!=)")).ToList();
        if (bare is not [var assignment]) return null;

        var indent = CodeText.Indentation(lines[assignment]).Length;

        return LocalFix.ReplaceLine(
            Id, $"Store {attr} on the object: self.{attr}",
            $"In `__init__`, `{attr} = ...` makes a local variable that is gone as soon as `__init__` returns. Only `self.{attr} = ...` " +
            $"stores it on the object, which is where `self.{attr}` looks for it later.",
            source.Path, assignment + 1, lines[assignment][..indent] + "self." + lines[assignment][indent..]);
    }
}

/// <summary><c>Animal.__init__() missing 1 required positional argument: 'name'</c> from a bare <c>super().__init__()</c>.</summary>
public sealed partial class PythonSuperArguments : ILocalFixRule
{
    public string Id => "python-super-arguments";

    [GeneratedRegex(@"^(?<callee>[\w.]+)\.__init__\(\) missing \d+ required positional arguments?: (?<names>.+)$")]
    private static partial Regex Message();

    [GeneratedRegex(@"\bsuper\(\s*\)\.__init__\((?<inside>\s*)\)")]
    private static partial Regex BareSuper();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "TypeError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.Python);
        if (BareSuper().Match(masked[number - 1]) is not { Success: true } call) return null;

        var missing = Regex.Matches(message.Groups["names"].Value, @"'(?<name>[A-Za-z_]\w*)'").Select(m => m.Groups["name"].Value).ToList();
        var def = PyScope.Enclosing(masked, number - 1, PyScope.AnyDef());
        if (missing.Count == 0 || def < 0 || PyScope.Def().Match(masked[def]) is not { Success: true } header) return null;

        var parameters = PyScope.Parameters(header.Groups["parameters"].Value);
        if (!missing.All(parameters.Contains)) return null;

        var inside = call.Groups["inside"];
        var names = string.Join(", ", missing);
        var said = string.Join(" and ", missing.Select(n => $"`{n}`"));

        return LocalFix.ReplaceLine(
            Id, $"Pass {string.Join(" and ", missing)} on to {message.Groups["callee"].Value}.__init__",
            $"`{message.Groups["callee"].Value}.__init__` needs {said}, and `super().__init__()` passes nothing. This `__init__` was given " +
            $"{said} itself, so it passes {(missing.Count == 1 ? "it" : "them")} on.",
            source.Path, number, line[..inside.Index] + names + line[(inside.Index + inside.Length)..]);
    }
}

// ======================================================================= syntax from other languages

/// <summary><c>except ValueError e:</c> - Python names the exception with <c>as</c>.</summary>
public sealed partial class PythonExceptAs : ILocalFixRule
{
    public string Id => "python-except-as";

    [GeneratedRegex(@"^(?<lead>\s*except\s+)(?<types>[A-Za-z_][\w.]*|\([^()]*\))\s+(?<name>[A-Za-z_]\w*)\s*:(?<tail>.*)$")]
    private static partial Regex Clause();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "SyntaxError") || Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (Clause().Match(line) is not { Success: true } clause || clause.Groups["name"].Value == "as") return null;

        var name = clause.Groups["name"].Value;

        return LocalFix.ReplaceLine(
            Id, $"Name the exception with as {name}",
            "Python names the exception it caught with `as`: `except ValueError as e:`. With only a space between them, as in Java or C#, it is not Python.",
            source.Path, number, $"{clause.Groups["lead"].Value}{clause.Groups["types"].Value} as {name}:{clause.Groups["tail"].Value}");
    }
}

/// <summary><c>[n for n in xs if n &gt; 0 else 0]</c> - a conditional value goes before the <c>for</c>.</summary>
public sealed partial class PythonComprehensionCondition : ILocalFixRule
{
    public string Id => "python-comprehension-condition";

    [GeneratedRegex(@"^\s*(?<expr>.+?)\s+for\s+(?<target>.+?)\s+in\s+(?<iterable>.+?)\s+if\s+(?<condition>.+?)\s+else\s+(?<alternative>.+?)\s*$")]
    private static partial Regex Misplaced();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "SyntaxError") || Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.Python);

        for (var open = masked.IndexOf('['); open >= 0; open = masked.IndexOf('[', open + 1))
        {
            var close = Py.MatchForward(masked, open);
            if (close < 0) return null;

            var inside = masked[(open + 1)..close];
            if (Misplaced().Match(inside) is not { Success: true } m) continue;

            string Part(string group) => line.Substring(open + 1 + m.Groups[group].Index, m.Groups[group].Length);

            var rewritten = $"{Part("expr")} if {Part("condition")} else {Part("alternative")} for {Part("target")} in {Part("iterable")}";

            return LocalFix.ReplaceLine(
                Id, "Put the if/else before the for",
                "In a comprehension, an `if` after the `for` filters elements out and cannot have an `else`. To pick a value for every element, " +
                "the `a if condition else b` comes first: `[a if condition else b for x in items]`.",
                source.Path, number, line[..(open + 1)] + rewritten + line[close..]);
        }

        return null;
    }
}

// ======================================================================= async

/// <summary><c>'await' outside async function</c> - the function has to be <c>async def</c>.</summary>
public sealed partial class PythonAwaitOutsideAsync : ILocalFixRule
{
    public string Id => "python-await-outside-async";

    [GeneratedRegex(@"^(?<lead>\s*)def\s")]
    private static partial Regex PlainDef();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "SyntaxError") || context.Error.Message?.Trim() != "'await' outside async function") return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, _) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.Python);

        var def = PyScope.Enclosing(masked, number - 1, PyScope.AnyDef());
        if (def < 0 || PlainDef().Match(source.Lines[def]) is not { Success: true } plain) return null;

        var name = PyScope.Def().Match(masked[def]).Groups["name"].Value;
        var lead = plain.Groups["lead"].Length;

        return LocalFix.ReplaceLine(
            Id, $"Make {name} async",
            $"`await` only works inside an `async def`, so `{name}` has to be one. Whatever calls it then has to `await` it as well, " +
            $"or start it with `asyncio.run({name}())`.",
            source.Path, def + 1, source.Lines[def][..lead] + "async " + source.Lines[def][lead..]);
    }
}

/// <summary><c>a coroutine was expected, got &lt;function main&gt;</c> - <c>asyncio.run(main)</c> without the call.</summary>
public sealed partial class PythonCoroutineNotCalled : ILocalFixRule
{
    public string Id => "python-coroutine-not-called";

    [GeneratedRegex(@"^a coroutine was expected, got <function (?<name>[A-Za-z_]\w*) at ")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "ValueError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var name = Regex.Escape(message.Groups["name"].Value);
        var masked = CodeText.MaskAll(source.Lines, Syntax.Python);

        if (!masked.Any(text => Regex.IsMatch(text, $@"^\s*async\s+def\s+{name}\s*\("))) return null;

        var hits = Regex.Matches(masked[number - 1], $@"\(\s*(?<name>{name})\s*[,)]");
        if (hits.Count != 1) return null;

        var end = hits[0].Groups["name"].Index + hits[0].Groups["name"].Length;

        return LocalFix.ReplaceLine(
            Id, $"Call {message.Groups["name"].Value}() to make the coroutine",
            "An `async def` does nothing until it is called: calling it makes the coroutine `asyncio.run` expects. Without the brackets it " +
            "is the function itself.",
            source.Path, number, line[..end] + "()" + line[end..]);
    }
}

// ======================================================================= modules and imports

/// <summary><c>'module' object is not callable. Did you mean: 'pprint.pprint(...)'?</c></summary>
public sealed partial class PythonModuleCalled : ILocalFixRule
{
    public string Id => "python-module-called";

    [GeneratedRegex(@"^'module' object is not callable\. Did you mean: '(?<module>[A-Za-z_]\w*)\.(?<attr>[A-Za-z_]\w*)\(\.\.\.\)'\?$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "TypeError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var module = message.Groups["module"].Value;
        var attr = message.Groups["attr"].Value;

        var hits = Regex.Matches(CodeText.Mask(line, Syntax.Python), $@"(?<![\w.]){Regex.Escape(module)}(?=\s*\()");
        if (hits.Count == 0) return null;

        return LocalFix.ReplaceLine(
            Id, $"Call {module}.{attr}",
            $"`{module}` is the module, and the function inside it is `{module}.{attr}` - Python said so. (`from {module} import {attr}` " +
            "at the top would let the short name work instead.)",
            source.Path, number, CCode.Replace(line, hits, _ => $"{module}.{attr}"));
    }
}

/// <summary><c>name 'defaultdict' is not defined</c> for a standard name that lives inside a module.</summary>
public sealed partial class PythonMissingFromImport : ILocalFixRule
{
    public string Id => "python-missing-from-import";

    [GeneratedRegex(@"^name '(?<name>[A-Za-z_]\w*)' is not defined$")]
    private static partial Regex Message();

    private static readonly Dictionary<string, string> Homes = new(StringComparer.Ordinal)
    {
        ["dataclass"] = "dataclasses", ["field"] = "dataclasses",
        ["defaultdict"] = "collections", ["Counter"] = "collections", ["deque"] = "collections", ["namedtuple"] = "collections", ["OrderedDict"] = "collections",
        ["Optional"] = "typing", ["Union"] = "typing", ["Any"] = "typing", ["Callable"] = "typing", ["Iterable"] = "typing", ["TypeVar"] = "typing",
        ["randint"] = "random", ["choice"] = "random", ["shuffle"] = "random", ["uniform"] = "random", ["sample"] = "random",
        ["sleep"] = "time", ["sqrt"] = "math", ["floor"] = "math", ["ceil"] = "math",
        ["reduce"] = "functools", ["partial"] = "functools", ["lru_cache"] = "functools", ["wraps"] = "functools", ["cache"] = "functools",
        ["ABC"] = "abc", ["abstractmethod"] = "abc", ["Enum"] = "enum", ["auto"] = "enum", ["deepcopy"] = "copy", ["Path"] = "pathlib",
        ["pprint"] = "pprint", ["Fraction"] = "fractions", ["Decimal"] = "decimal", ["timedelta"] = "datetime",
        ["permutations"] = "itertools", ["combinations"] = "itertools", ["product"] = "itertools", ["chain"] = "itertools",
    };

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "NameError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (context.Read(context.Frame?.File) is not { } source) return null;

        var name = message.Groups["name"].Value;
        if (!Homes.TryGetValue(name, out var module)) return null;

        var lines = source.Lines;
        var existing = Enumerable.Range(0, lines.Count)
            .Select(i => (Index: i, Match: Regex.Match(lines[i], $@"^from\s+{Regex.Escape(module)}\s+import\s+(?<names>[A-Za-z_][\w\s,]*?)\s*$")))
            .FirstOrDefault(x => x.Match.Success);

        var explanation = $"`{name}` is not built in - it lives in the `{module}` module, which has to be imported first.";

        if (existing.Match is { Success: true })
            return LocalFix.ReplaceLine(Id, $"Import {name} from {module}", explanation, source.Path, existing.Index + 1, lines[existing.Index].TrimEnd() + ", " + name);

        return LocalFix.Insert(
            Id, $"Add from {module} import {name}", explanation,
            source.Path, PythonLayout.ImportInsertionLine(lines), [$"from {module} import {name}"]);
    }
}

// ======================================================================= scope

/// <summary><c>cannot access local variable 'count'</c> in a function nested inside the one that owns <c>count</c>.</summary>
public sealed partial class PythonNonlocal : ILocalFixRule
{
    public string Id => "python-nonlocal";

    [GeneratedRegex(@"(?:cannot access local variable|local variable) '(?<name>[A-Za-z_]\w*)'")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "UnboundLocalError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (context.Frame is not { Line: { } number, Symbol: { Length: > 0 } symbol } frame || context.Read(frame.File) is not { } source) return null;

        var function = Regex.Escape(symbol[(symbol.LastIndexOf('.') + 1)..]);
        var name = message.Groups["name"].Value;
        var word = Regex.Escape(name);
        var lines = source.Lines;
        var masked = CodeText.MaskAll(lines, Syntax.Python);

        var inner = Enumerable.Range(0, masked.Count).Where(i => Regex.IsMatch(masked[i], $@"^\s+(?:async\s+)?def\s+{function}\s*\(")).ToList();
        if (inner is not [var def]) return null;

        var outer = PyScope.Enclosing(masked, def, PyScope.AnyDef());
        if (outer < 0) return null;

        var (first, end) = PyScope.Body(lines, def);
        if (number - 1 < first || number - 1 >= end) return null;
        if (Enumerable.Range(first, end - first).Any(i => Regex.IsMatch(masked[i], $@"^\s*(?:global|nonlocal)\b.*\b{word}\b"))) return null;

        // The outer function has to own it: an assignment in its body, outside this inner function.
        var (outerFirst, outerEnd) = PyScope.Body(lines, outer);
        var owned = Enumerable.Range(outerFirst, outerEnd - outerFirst)
            .Where(i => i < def || i >= end)
            .Any(i => Regex.IsMatch(masked[i], $@"^\s+{word}\s*(?::[^=]*)?=(?!=)"));

        if (!owned) return null;

        var at = PyScope.FirstStatement(lines, first, end);
        if (at >= end) return null;

        var outerName = PyScope.Def().Match(masked[outer]).Groups["name"].Value;
        var innerName = PyScope.Def().Match(masked[def]).Groups["name"].Value;

        return LocalFix.Insert(
            Id, $"Add nonlocal {name}",
            $"`{name}` belongs to `{outerName}`, the function around this one. Assigning to it inside `{innerName}` - `{name} += 1` counts - " +
            $"makes it a new local variable of `{innerName}`, which has no value yet. `nonlocal {name}` says to use `{outerName}`'s.",
            source.Path, at + 1, [CodeText.Indentation(lines[at]) + "nonlocal " + name]);
    }
}

/// <summary><c>unsupported operand type(s) for +: 'NoneType' and 'int'</c> from a function that works a value out and never returns it.</summary>
public sealed partial class PythonMissingReturn : ILocalFixRule
{
    public string Id => "python-missing-return";

    [GeneratedRegex(@"^unsupported operand type\(s\) for (?<op>\S+): '(?<left>[\w.]+)' and '(?<right>[\w.]+)'$")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?<func>[A-Za-z_]\w*)\(.*\)$")]
    private static partial Regex Call();

    [GeneratedRegex(@"^(?<indent>\s+)(?<var>[A-Za-z_]\w*)\s*(?:[+\-*/%]|//|\*\*)?=(?!=)")]
    private static partial Regex Assignment();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "TypeError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;

        var leftIsNone = message.Groups["left"].Value == "NoneType";
        if (leftIsNone == (message.Groups["right"].Value == "NoneType")) return null;

        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (Py.UnderlineSpan(context.Error, number, line) is not { } span) return null;

        var op = message.Groups["op"].Value;
        var text = line[span.Start..span.End];
        var position = PyScope.TopLevel(CodeText.Mask(text, Syntax.Python), op);
        if (position < 0) return null;

        var operand = (leftIsNone ? text[..position] : text[(position + op.Length)..]).Trim();
        if (Call().Match(operand) is not { Success: true } call) return null;

        var func = call.Groups["func"].Value;
        var lines = source.Lines;
        var masked = CodeText.MaskAll(lines, Syntax.Python);

        var defs = Enumerable.Range(0, masked.Count).Where(i => Regex.IsMatch(masked[i], $@"^def\s+{Regex.Escape(func)}\s*\(")).ToList();
        if (defs is not [var def]) return null;

        var (first, end) = PyScope.Body(lines, def);
        if (end <= first || Enumerable.Range(first, end - first).Any(i => Regex.IsMatch(masked[i], @"\breturn\b|\byield\b"))) return null;

        var last = end - 1;
        if (Assignment().Match(masked[last]) is not { Success: true } assigned) return null;

        var variable = assigned.Groups["var"].Value;

        return LocalFix.Insert(
            Id, $"Return {variable} from {func}",
            $"`{func}` works out `{variable}` but never returns it, and a function with no `return` gives back `None` - which is what the " +
            $"`{op}` then met. `return {variable}` hands the result back.",
            source.Path, last + 2, [assigned.Groups["indent"].Value + "return " + variable]);
    }
}

// ======================================================================= collections

/// <summary><c>list indices must be integers or slices, not str</c> from <c>names[name]</c> inside <c>for name in names</c>.</summary>
public sealed partial class PythonIndexWithElement : ILocalFixRule
{
    public string Id => "python-index-with-element";

    [GeneratedRegex(@"^(?:list|tuple) indices must be integers(?: or slices)?, not \w+$")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?<seq>[A-Za-z_][\w.]*)\[(?<var>[A-Za-z_]\w*)\]$")]
    private static partial Regex Subscript();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "TypeError") || !Message().IsMatch(context.Error.Message ?? "")) return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (Py.UnderlineSpan(context.Error, number, line) is not { } span) return null;
        if (Subscript().Match(line[span.Start..span.End]) is not { Success: true } subscript) return null;

        var seq = subscript.Groups["seq"].Value;
        var variable = subscript.Groups["var"].Value;
        var masked = CodeText.MaskAll(source.Lines, Syntax.Python);

        var loop = PyScope.Enclosing(masked, number - 1, new Regex($@"^\s*for\s+{Regex.Escape(variable)}\s+in\s+{Regex.Escape(seq)}\s*:"));
        if (loop < 0) return null;

        return LocalFix.ReplaceLine(
            Id, $"Use {variable} itself",
            $"`for {variable} in {seq}` already hands over each element - `{variable}` is the element, not its position - so `{seq}[{variable}]` " +
            $"looks an element up by itself. `{variable}` alone is the value; `for i, {variable} in enumerate({seq})` gives the position as well.",
            source.Path, number, line[..span.Start] + variable + line[span.End..]);
    }
}

/// <summary><c>'tuple' object does not support item assignment</c> - the tuple should have been a list.</summary>
public sealed partial class PythonTupleToList : ILocalFixRule
{
    public string Id => "python-tuple-to-list";

    [GeneratedRegex(@"^(?<name>[A-Za-z_]\w*)\[")]
    private static partial Regex Target();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "TypeError") || context.Error.Message != "'tuple' object does not support item assignment") return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (Py.UnderlineSpan(context.Error, number, line) is not { } span || Target().Match(line[span.Start..span.End]) is not { Success: true } target) return null;

        var name = target.Groups["name"].Value;
        var masked = CodeText.MaskAll(source.Lines, Syntax.Python);
        if (PyScope.Assignments(masked, name) is not [var index]) return null;

        var assignment = Regex.Match(masked[index], $@"^\s*{Regex.Escape(name)}\s*=\s*(?<value>.+?)\s*$");
        if (!assignment.Success) return null;

        var value = assignment.Groups["value"];
        var original = source.Lines[index];
        var start = value.Index;
        var end = value.Index + value.Length;
        string corrected;

        var row = masked[index];

        if (row[start] == '(' && Py.MatchForward(row, start) == end - 1 && PyScope.TopLevel(row[(start + 1)..(end - 1)], ",") >= 0)
            corrected = original[..start] + "[" + original[(start + 1)..(end - 1)] + "]" + original[end..];
        else if (PyScope.TopLevel(value.Value, ",") >= 0)
            corrected = original[..start] + "[" + original[start..end] + "]" + original[end..];
        else
            return null;

        return LocalFix.ReplaceLine(
            Id, $"Make {name} a list",
            $"`{name}` is a tuple, and a tuple cannot be changed once it is made. A list holds the same values and can be changed - square " +
            "brackets instead of round ones.",
            source.Path, index + 1, corrected);
    }
}

/// <summary><c>'str' object does not support item assignment</c> - a new string has to be built.</summary>
public sealed partial class PythonStringItemAssignment : ILocalFixRule
{
    public string Id => "python-string-item-assignment";

    [GeneratedRegex(@"^(?<indent>\s*)(?<name>[A-Za-z_]\w*)\[(?<index>[^\[\]:]+)\]\s*=(?!=)\s*(?<value>.+?)\s*$")]
    private static partial Regex Assignment();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "TypeError") || context.Error.Message != "'str' object does not support item assignment") return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var (code, tail) = CodeText.SplitComment(line, Syntax.Python);
        if (Assignment().Match(code) is not { Success: true } m) return null;

        var name = m.Groups["name"].Value;
        var index = m.Groups["index"].Value.Trim();
        var value = m.Groups["value"].Value;

        var rebuilt = index switch
        {
            "0" => $"{name} = {value} + {name}[1:]",
            "-1" => $"{name} = {name}[:-1] + {value}",
            _ => $"{name} = {name}[:{index}] + {value} + {name}[{index} + 1:]",
        };

        return LocalFix.ReplaceLine(
            Id, $"Build a new string for {name}",
            "A string cannot be changed in place. To change one character, build a new string from the pieces around it and assign that.",
            source.Path, number, m.Groups["indent"].Value + rebuilt + tail);
    }
}

/// <summary><c>'map' object is not subscriptable</c>, and the same for zip, filter, generators and dictionary views.</summary>
public sealed partial class PythonNotSubscriptable : ILocalFixRule
{
    public string Id => "python-not-subscriptable";

    [GeneratedRegex(@"^'(?<type>map|filter|zip|generator|dict_keys|dict_values|dict_items|reversed|enumerate)' object is not subscriptable$")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?:map|filter|zip|reversed|enumerate)\s*\(|\.(?:keys|values|items)\s*\(\s*\)$")]
    private static partial Regex Lazy();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "TypeError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (Py.UnderlineSpan(context.Error, number, line) is not { } span) return null;

        var text = line[span.Start..span.End];
        var maskedText = CodeText.Mask(text, Syntax.Python);
        if (!maskedText.EndsWith(']')) return null;

        var bracket = Py.MatchBack(maskedText, maskedText.Length - 1);
        if (bracket <= 0) return null;

        var receiver = text[..bracket].Trim();
        var type = message.Groups["type"].Value;
        var explanation = type == "generator"
            ? "A generator makes its values one at a time, as they are asked for, so there is no position to look up. A list comprehension - " +
              "square brackets - makes them all at once, and can be indexed."
            : $"A `{type}` hands its values out one at a time and cannot be indexed. `list(...)` collects them into a list, which can.";

        if (receiver.EndsWith(')') && Lazy().IsMatch(receiver))
        {
            return LocalFix.ReplaceLine(
                Id, $"Make it a list first: list({receiver})", explanation,
                source.Path, number, line[..span.Start] + $"list({receiver})" + line[(span.Start + bracket)..]);
        }

        if (!Regex.IsMatch(receiver, @"^[A-Za-z_]\w*$")) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.Python);
        if (PyScope.Assignments(masked, receiver) is not [var index]) return null;

        var assignment = Regex.Match(masked[index], $@"^\s*{Regex.Escape(receiver)}\s*=\s*(?<value>.+?)\s*$");
        if (!assignment.Success) return null;

        var value = assignment.Groups["value"];
        var original = source.Lines[index];
        var start = value.Index;
        var end = start + value.Length;

        if (type == "generator")
        {
            var row = masked[index];
            if (row[start] != '(' || Py.MatchForward(row, start) != end - 1 || !Regex.IsMatch(value.Value, @"\sfor\s")) return null;

            return LocalFix.ReplaceLine(
                Id, $"Make {receiver} a list comprehension", explanation,
                source.Path, index + 1, original[..start] + "[" + original[(start + 1)..(end - 1)] + "]" + original[end..]);
        }

        if (!Lazy().IsMatch(value.Value) || !value.Value.EndsWith(')')) return null;

        return LocalFix.ReplaceLine(
            Id, $"Make {receiver} a list", explanation,
            source.Path, index + 1, original[..start] + "list(" + original[start..end] + ")" + original[end..]);
    }
}

/// <summary>Two names unpacked from each element of something that does not have two: a dict, or a list of numbers.</summary>
public sealed partial class PythonLoopUnpack : ILocalFixRule
{
    public string Id => "python-loop-unpack";

    [GeneratedRegex(@"^(?:too many values to unpack \(expected 2\)|not enough values to unpack \(expected 2, got \d+\))$")]
    private static partial Regex FromDict();

    [GeneratedRegex(@"^cannot unpack non-iterable (?:int|float|bool|NoneType) object$")]
    private static partial Regex FromScalars();

    [GeneratedRegex(@"^(?<head>\s*(?:async\s+)?for\s+\(?\s*[A-Za-z_]\w*\s*,\s*[A-Za-z_]\w*\s*\)?\s+in\s+)(?<iterable>[A-Za-z_][\w.]*)(?<tail>\s*:.*)$")]
    private static partial Regex Loop();

    public LocalFix? Propose(LocalFixContext context)
    {
        var message = context.Error.Message ?? "";
        var dict = Py.Is(context, "ValueError") && FromDict().IsMatch(message);
        var scalars = Py.Is(context, "TypeError") && FromScalars().IsMatch(message);

        if (!dict && !scalars || Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (Loop().Match(line) is not { Success: true } loop) return null;

        var iterable = loop.Groups["iterable"].Value;
        string replacement, explanation;

        if (dict)
        {
            var masked = CodeText.MaskAll(source.Lines, Syntax.Python);
            if (PyScope.Assignments(masked, iterable) is not [var index]) return null;
            if (!Regex.IsMatch(masked[index], $@"^\s*{Regex.Escape(iterable)}\s*=\s*(?:\{{.*:.*\}}|dict\s*\()")) return null;

            replacement = $"{iterable}.items()";
            explanation = $"Looping over a dictionary gives its keys, one at a time - not keys and values. `{iterable}.items()` gives the pairs.";
        }
        else
        {
            replacement = $"enumerate({iterable})";
            explanation = $"Each element of `{iterable}` is a single value, so there is nothing to split into two names. " +
                          $"`enumerate({iterable})` gives each position together with its element.";
        }

        return LocalFix.ReplaceLine(
            Id, $"Loop over {replacement}", explanation,
            source.Path, number, loop.Groups["head"].Value + replacement + loop.Groups["tail"].Value);
    }
}

/// <summary><c>unhashable type: 'list'</c> - a list added to a set.</summary>
public sealed partial class PythonUnhashableList : ILocalFixRule
{
    public string Id => "python-unhashable-list";

    [GeneratedRegex(@"\.add\(\s*(?<open>\[)")]
    private static partial Regex AddList();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "TypeError") || context.Error.Message != "unhashable type: 'list'") return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.Python);
        if (AddList().Matches(masked) is not { Count: 1 } hits) return null;

        var open = hits[0].Groups["open"].Index;
        var close = Py.MatchForward(masked, open);
        if (close < 0 || !masked[(close + 1)..].TrimStart().StartsWith(')')) return null;

        var inside = line[(open + 1)..close];
        var tuple = PyScope.TopLevel(masked[(open + 1)..close], ",") < 0 && inside.Trim().Length > 0 ? $"({inside.Trim()},)" : $"({inside})";

        return LocalFix.ReplaceLine(
            Id, "Add a tuple instead of a list",
            "A set - like a dictionary's keys - can only hold values that never change, and a list can change. A tuple holds the same values and cannot.",
            source.Path, number, line[..open] + tuple + line[(close + 1)..]);
    }
}

// ======================================================================= numbers and formatting

/// <summary><c>Unknown format code 'f' for object of type 'str'</c> - a number format applied to text.</summary>
public sealed partial class PythonFormatCodeOnText : ILocalFixRule
{
    public string Id => "python-format-code-on-text";

    [GeneratedRegex(@"^Unknown format code '(?<code>.)' for object of type 'str'$")]
    private static partial Regex Message();

    [GeneratedRegex(@"^\{(?<expr>[^{}:!]+?)(?:![rsa])?:[^{}]*\}$")]
    private static partial Regex Field();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "ValueError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (Py.UnderlineSpan(context.Error, number, line) is not { } span) return null;
        if (Field().Match(line[span.Start..span.End]) is not { Success: true } field) return null;

        var code = message.Groups["code"].Value;
        var convert = "dnbcoxX".Contains(code) ? "int" : "eEfFgG%".Contains(code) ? "float" : null;
        if (convert is null) return null;

        var expr = field.Groups["expr"];
        var start = span.Start + expr.Index;
        var text = expr.Value.Trim();

        return LocalFix.ReplaceLine(
            Id, $"Convert {text} with {convert}()",
            $"`:{code}` formats a number, but `{text}` is text - probably read from input or a file. `{convert}({text})` turns it into a number to format.",
            source.Path, number, line[..start] + $"{convert}({text})" + line[(start + expr.Length)..]);
    }
}

/// <summary><c>can't multiply sequence by non-int of type 'float'</c> - a <c>/</c> that should have been <c>//</c>.</summary>
public sealed class PythonSequenceTimesFloat : ILocalFixRule
{
    public string Id => "python-sequence-times-float";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "TypeError") || context.Error.Message != "can't multiply sequence by non-int of type 'float'") return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (Py.UnderlineSpan(context.Error, number, line) is not { } span) return null;

        var masked = CodeText.Mask(line[span.Start..span.End], Syntax.Python);
        var slashes = Enumerable.Range(0, masked.Length)
            .Where(i => masked[i] == '/' && (i + 1 >= masked.Length || masked[i + 1] != '/') && (i == 0 || masked[i - 1] != '/'))
            .ToList();

        if (slashes is not [var slash]) return null;

        var at_ = span.Start + slash;

        return LocalFix.ReplaceLine(
            Id, "Divide with // to keep a whole number",
            "A string or a list can only be repeated a whole number of times. `/` always gives a float - `9 / 2` is `4.5` - and `//` gives the whole number, `4`.",
            source.Path, number, line[..at_] + "//" + line[(at_ + 1)..]);
    }
}
