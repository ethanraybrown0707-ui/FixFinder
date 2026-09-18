using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary><c>Dog.bark() takes 0 positional arguments but 1 was given</c> - a method written without self.</summary>
public sealed partial class PythonMissingSelf : ILocalFixRule
{
    public string Id => "python-missing-self";

    [GeneratedRegex(@"^(?<cls>[A-Za-z_]\w*)\.(?<method>[A-Za-z_]\w*)\(\) takes (?<n>\d+) positional arguments? but (?<m>\d+) (?:were|was) given$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "TypeError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (int.Parse(message.Groups["m"].Value) != int.Parse(message.Groups["n"].Value) + 1) return null;
        if (context.Read(context.Frame?.File) is not { } source) return null;

        var cls = Regex.Escape(message.Groups["cls"].Value);
        var method = Regex.Escape(message.Groups["method"].Value);
        var masked = CodeText.MaskAll(source.Lines, Syntax.Python);

        var classes = Enumerable.Range(0, masked.Count).Where(i => Regex.IsMatch(masked[i], $@"^\s*class\s+{cls}\b")).ToList();
        if (classes.Count != 1) return null;

        var indent = CodeText.Indentation(masked[classes[0]]).Length;
        var defs = new List<int>();

        for (var i = classes[0] + 1; i < masked.Count; i++)
        {
            if (masked[i].Trim().Length == 0) continue;
            if (CodeText.Indentation(masked[i]).Length <= indent) break;
            if (Regex.IsMatch(masked[i], $@"^\s+(?:async\s+)?def\s+{method}\s*\(")) defs.Add(i);
        }

        if (defs.Count != 1) return null;

        var index = defs[0];
        if (index > 0 && Regex.IsMatch(masked[index - 1], @"^\s*@(?:staticmethod|classmethod)\b")) return null;

        var paren = masked[index].IndexOf('(');
        var close = Brackets.Closing(masked[index], paren);
        if (close < 0) return null;

        var parameters = masked[index][(paren + 1)..close].Trim();
        if (parameters.StartsWith("self", StringComparison.Ordinal) || parameters.StartsWith("cls", StringComparison.Ordinal)) return null;

        var original = source.Lines[index];
        var corrected = original[..(paren + 1)] + (parameters.Length == 0 ? "self" : "self, ") + original[(paren + 1)..].TrimStart();

        if (parameters.Length == 0) corrected = original[..(paren + 1)] + "self" + original[(paren + 1)..];

        return LocalFix.ReplaceLine(
            Id, $"Give {message.Groups["method"].Value} a self parameter",
            "Calling a method on an object passes the object in as the first argument. This method has no parameter " +
            "for it, so Python had one argument more than the method could take - that first parameter is `self`.",
            source.Path, index + 1, corrected);
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
        if (!PythonCode.Raised(context, "AttributeError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (context.Read(context.Frame?.File) is not { } source) return null;

        var lines = source.Lines;
        var masked = CodeText.MaskAll(lines, Syntax.Python);
        var cls = Regex.Escape(message.Groups["cls"].Value);
        var attr = message.Groups["attr"].Value;

        var classes = Enumerable.Range(0, masked.Count).Where(i => Regex.IsMatch(masked[i], $@"^\s*class\s+{cls}\b")).ToList();
        if (classes is not [var cl]) return null;

        var (classFirst, classEnd) = PythonCode.BlockBody(lines, cl);

        // A class that stores it somewhere already has a different problem.
        if (Enumerable.Range(classFirst, classEnd - classFirst).Any(i => Regex.IsMatch(masked[i], $@"\bself\.{Regex.Escape(attr)}\s*(?::[^=]*)?=(?!=)"))) return null;

        var inits = Enumerable.Range(classFirst, classEnd - classFirst).Where(i => Regex.IsMatch(masked[i], @"^\s+def\s+__init__\s*\(\s*self\b")).ToList();
        if (inits is not [var init]) return null;

        var (first, end) = PythonCode.BlockBody(lines, init);
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

/// <summary><c>super.__init__()</c> - super has to be called before anything can be looked up on it.</summary>
public sealed partial class PythonSuperCall : ILocalFixRule
{
    public string Id => "python-super-call";

    [GeneratedRegex(@"^descriptor '\w+' (?:(?:of|for) 'super' object needs an argument|requires a 'super' object but received an? '\w+')$")]
    private static partial Regex Message();

    [GeneratedRegex(@"(?<![\w.])super\.")]
    private static partial Regex SuperDot();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "python", ExceptionType: "TypeError" } error) return null;
        if (!Message().IsMatch(error.Message ?? "")) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (source.Line(number) is not { } line) return null;

        var hits = SuperDot().Matches(CodeText.Mask(line, Syntax.Python));
        if (hits.Count != 1) return null;

        var at = hits[0].Index + "super".Length;

        return LocalFix.ReplaceLine(
            Id, "Call super() before using it",
            "`super` on its own is the type; `super()` is the parent-class object whose `__init__` you want.",
            source.Path, number, line[..at] + "()" + line[at..]);
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
        if (!PythonCode.Raised(context, "TypeError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.Python);
        if (BareSuper().Match(masked[number - 1]) is not { Success: true } call) return null;

        var missing = Regex.Matches(message.Groups["names"].Value, @"'(?<name>[A-Za-z_]\w*)'").Select(m => m.Groups["name"].Value).ToList();
        var def = PythonCode.EnclosingHeader(masked, number - 1, PythonCode.FunctionKeyword());
        if (missing.Count == 0 || def < 0 || PythonCode.FunctionHeader().Match(masked[def]) is not { Success: true } header) return null;

        var parameters = PythonCode.SplitParameters(header.Groups["parameters"].Value);
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

/// <summary><c>Dog() takes no arguments</c>, from a class whose __init__ is misspelt.</summary>
public sealed partial class PythonInitTypo : ILocalFixRule
{
    public string Id => "python-init-typo";

    [GeneratedRegex(@"^(?<cls>[A-Za-z_]\w*)\(\) takes no arguments$")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?<lead>\s+def\s+)(?<name>[A-Za-z_]\w*)\s*\(")]
    private static partial Regex Def();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "python", ExceptionType: "TypeError" } error) return null;
        if (Message().Match(error.Message ?? "") is not { Success: true } message) return null;
        if (context.Read(context.Frame?.File) is not { } source) return null;

        var cls = message.Groups["cls"].Value;
        var masked = CodeText.MaskAll(source.Lines, Syntax.Python);

        var starts = Enumerable.Range(0, masked.Count).Where(i => Regex.IsMatch(masked[i], $@"^\s*class\s+{Regex.Escape(cls)}\b")).ToList();
        if (starts.Count != 1) return null;

        var indent = CodeText.Indentation(masked[starts[0]]).Length;
        var defs = new List<(int Line, Match Match)>();

        for (var i = starts[0] + 1; i < masked.Count; i++)
        {
            if (masked[i].Trim().Length == 0) continue;
            if (CodeText.Indentation(masked[i]).Length <= indent) break;
            if (Def().Match(masked[i]) is { Success: true } def) defs.Add((i, def));
        }

        if (defs.Any(d => d.Match.Groups["name"].Value == "__init__")) return null;

        var near = defs.Where(d => CodeText.Distance(d.Match.Groups["name"].Value, "__init__") <= 2).ToList();
        if (near.Count != 1) return null;

        var (index, match) = near[0];
        var name = match.Groups["name"];
        var original = source.Lines[index];

        return LocalFix.ReplaceLine(
            Id, $"Rename {name.Value} to __init__",
            $"Python runs a method only if it is called exactly `__init__` when an object is made. `{cls}` has none - " +
            $"it has `{name.Value}` - so the arguments given to `{cls}(...)` had nowhere to go.",
            source.Path, index + 1, original[..name.Index] + "__init__" + original[(name.Index + name.Length)..]);
    }
}

/// <summary><c>__str__ returned non-string</c> - the method has to return text.</summary>
public sealed partial class PythonDunderStrReturn : ILocalFixRule
{
    public string Id => "python-dunder-str-return";

    [GeneratedRegex(@"^__(?<method>str|repr)__ returned non-string")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?<lead>\s*return\s+)(?<expr>.+?)\s*$")]
    private static partial Regex Return();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "TypeError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (context.Read(context.Frame?.File) is not { } source) return null;

        var method = message.Groups["method"].Value;
        var masked = CodeText.MaskAll(source.Lines, Syntax.Python);

        var defs = Enumerable.Range(0, masked.Count).Where(i => Regex.IsMatch(masked[i], $@"^\s+def\s+__{method}__\s*\(\s*self")).ToList();
        if (defs.Count != 1) return null;

        var indent = CodeText.Indentation(masked[defs[0]]).Length;
        var returns = new List<int>();

        for (var i = defs[0] + 1; i < masked.Count; i++)
        {
            if (masked[i].Trim().Length == 0) continue;
            if (CodeText.Indentation(masked[i]).Length <= indent) break;
            if (Return().IsMatch(masked[i])) returns.Add(i);
        }

        if (returns.Count != 1) return null;

        var (code, tail) = CodeText.SplitComment(source.Lines[returns[0]], Syntax.Python);
        if (Return().Match(code) is not { Success: true } match) return null;

        var expression = match.Groups["expr"].Value;
        if (expression.StartsWith("str(", StringComparison.Ordinal) || PythonCode.StringLiteral().IsMatch(expression)) return null;

        return LocalFix.ReplaceLine(
            Id, $"Return text from __{method}__",
            $"`__{method}__` is how Python turns the object into text, so it must return a str. `str(...)` makes the text form of what it returned.",
            source.Path, returns[0] + 1, $"{match.Groups["lead"].Value}str({expression}){tail}");
    }
}

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
        if (!PythonCode.Raised(context, "TypeError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (PythonCode.UnderlineSpan(context.Error, number, line) is not { } span) return null;
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

/// <summary><c>Can't instantiate abstract class Square without an implementation for abstract method 'area'</c>.</summary>
/// <remarks>
/// The same answer the C#, Java and C++ rules give: the method is written, with the signature the base class declared, and a
/// body that raises until it is filled in. Refused when the class named is itself the abstract one, since creating it was
/// the mistake there.
/// </remarks>
public sealed partial class PythonUnwrittenAbstractMethod : ILocalFixRule
{
    public string Id => "python-unwritten-abstract-method";

    [GeneratedRegex(@"^Can't instantiate abstract class (?<class>\w+) (?:without an implementation for|with) abstract methods? (?<methods>.+)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "TypeError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (context.Read(context.Frame?.File) is not { } source) return null;

        var name = message.Groups["class"].Value;
        var methods = Regex.Matches(message.Groups["methods"].Value, @"'?(?<m>[A-Za-z_]\w*)'?").Select(m => m.Groups["m"].Value).ToList();
        if (methods.Count == 0) return null;

        var lines = source.Lines;
        var masked = CodeText.MaskAll(lines, Syntax.Python);

        var headers = Enumerable.Range(0, lines.Count).Where(i => Regex.IsMatch(masked[i], $@"^class\s+{Regex.Escape(name)}\s*[(:]")).ToList();
        if (headers is not [var header]) return null;

        var (first, end) = PythonCode.BlockBody(lines, header);
        var body = Enumerable.Range(first, end - first).ToList();

        // A class that declares abstract methods of its own is the abstract one, and is not for creating.
        if (body.Any(i => masked[i].TrimStart().StartsWith("@abstractmethod", StringComparison.Ordinal) ||
                          masked[i].TrimStart().StartsWith("@abc.abstractmethod", StringComparison.Ordinal))) return null;

        var indent = body.Select(i => lines[i]).FirstOrDefault(l => l.Trim().Length > 0) is { } bodyLine
            ? CodeText.Indentation(bodyLine)
            : CodeText.Indentation(lines[header]) + "    ";
        var deeper = indent + (indent.Contains('\t') ? "\t" : "    ");

        var added = new List<string>();

        foreach (var method in methods)
        {
            // The declaration in a base class: the def after an @abstractmethod.
            var declaration = Enumerable.Range(1, lines.Count - 1)
                .Where(i => Regex.IsMatch(masked[i], $@"^\s*(?:async\s+)?def\s+{Regex.Escape(method)}\s*\(") &&
                            Enumerable.Range(Math.Max(0, i - 3), i - Math.Max(0, i - 3)).Any(k => masked[k].Contains("abstractmethod", StringComparison.Ordinal)))
                .ToList();

            if (declaration is not [var at]) return null;

            var signature = lines[at].Trim();
            if (!signature.EndsWith(':')) return null;

            added.AddRange(["", indent + signature, $"{deeper}raise NotImplementedError(\"{method} is not written yet\")"]);
        }

        var list = string.Join(", ", methods);

        return LocalFix.Insert(
            Id, $"Write {list} in {name}",
            $"`{name}` inherits abstract {(methods.Count == 1 ? "method" : "methods")} {string.Join(", ", methods.Select(m => $"`{m}`"))} and never " +
            $"writes {(methods.Count == 1 ? "it" : "them")}, and Python will not create an object with anything left abstract. " +
            $"{(methods.Count == 1 ? "It is" : "They are")} added with the signature the base class declared, raising `NotImplementedError` " +
            "until the real body is written.",
            source.Path, end + 1, added);
    }
}

/// <summary><c>mutable default &lt;class 'list'&gt; for field items is not allowed: use default_factory</c>.</summary>
public sealed partial class PythonDataclassDefaultFactory : ILocalFixRule
{
    public string Id => "python-dataclass-default-factory";

    [GeneratedRegex(@"^mutable default <class '(?<type>list|dict|set)'> for field (?<field>\w+) is not allowed: use default_factory$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "ValueError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (context.Read(context.Frame?.File) is not { } source) return null;

        var field = message.Groups["field"].Value;
        var type = message.Groups["type"].Value;
        var masked = CodeText.MaskAll(source.Lines, Syntax.Python);
        var empty = type switch { "list" => @"\[\s*\]|list\(\s*\)", "dict" => @"\{\s*\}|dict\(\s*\)", _ => @"set\(\s*\)" };
        var declaration = new Regex($@"^(?<head>\s*{Regex.Escape(field)}\s*:[^=]*=\s*)(?:{empty})\s*$");

        if (Enumerable.Range(0, masked.Count).Where(i => declaration.IsMatch(masked[i])).ToList() is not [var index]) return null;

        var head = declaration.Match(masked[index]).Groups["head"].Value;
        var (_, tail) = CodeText.SplitComment(source.Lines[index], Syntax.Python);

        return PythonCode.WithImport(
            Id, $"Give each object its own {type}: field(default_factory={type})",
            $"A default is made once, when the class is defined, so every object would share one `{type}` - adding to one object's " +
            $"`{field}` would add to all of them. Dataclasses refuse it for that reason. `field(default_factory={type})` makes a new, " +
            "empty one for each object.",
            source, index + 1, "dataclasses", "field", written => $"{source.Lines[index][..head.Length]}{written}(default_factory={type}){tail}");
    }
}

/// <summary><c>unhashable type: 'Point'</c> for a class that defines <c>__eq__</c> - which removes the <c>__hash__</c> it had.</summary>
public sealed partial class PythonHashWithEq : ILocalFixRule
{
    public string Id => "python-hash-with-eq";

    [GeneratedRegex(@"^unhashable type: '(?<class>[A-Z]\w*)'$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "TypeError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (context.Read(context.Frame?.File) is not { } source) return null;

        var name = message.Groups["class"].Value;
        var lines = source.Lines;
        var masked = CodeText.MaskAll(lines, Syntax.Python);

        if (Enumerable.Range(0, lines.Count).Where(i => Regex.IsMatch(masked[i], $@"^class\s+{Regex.Escape(name)}\s*[(:]")).ToList() is not [var header]) return null;

        var (first, end) = PythonCode.BlockBody(lines, header);
        var body = Enumerable.Range(first, end - first).ToList();

        if (body.Any(i => Regex.IsMatch(masked[i], @"^\s*(?:def\s+__hash__|__hash__\s*=)"))) return null;
        if (body.Where(i => Regex.IsMatch(masked[i], @"^\s*def\s+__eq__\s*\(")).ToList() is not [var eq]) return null;

        var (eqFirst, eqEnd) = PythonCode.BlockBody(lines, eq);
        var compared = string.Join("\n", Enumerable.Range(eqFirst, eqEnd - eqFirst).Select(i => masked[i]));

        // The fields __eq__ compares, in the order it compares them - equal objects must hash the same.
        var fields = Regex.Matches(compared, @"\bself\.(?<field>[A-Za-z_]\w*)\b(?!\s*\()").Select(m => m.Groups["field"].Value).Where(f => !f.StartsWith("__", StringComparison.Ordinal)).Distinct().ToList();
        if (fields.Count == 0 || !Regex.IsMatch(compared, @"\bother\.")) return null;

        var indent = CodeText.Indentation(lines[eq]);
        var deeper = CodeText.Indentation(lines[eqFirst]);
        var value = fields.Count == 1 ? $"self.{fields[0]}" : "(" + string.Join(", ", fields.Select(f => "self." + f)) + ")";

        return LocalFix.Insert(
            Id, $"Let {name} be hashed by what it compares: __hash__",
            $"Defining `__eq__` makes Python remove the `__hash__` a class otherwise has, because two objects that are equal must hash " +
            $"the same and the old hash did not know about `{string.Join("`, `", fields)}`. Without one, `{name}` cannot go in a set or be " +
            "a dictionary key. The new `__hash__` hashes exactly what `__eq__` compares; those fields should not change once the object " +
            "is in a set.",
            source.Path, eqEnd + 1, ["", $"{indent}def __hash__(self):", $"{deeper}return hash({value})"]);
    }
}

/// <summary><c>Point() accepts 0 positional sub-patterns (2 given)</c> - a class pattern with positions, for a class that never said what they are.</summary>
public sealed partial class PythonMatchArgs : ILocalFixRule
{
    public string Id => "python-match-args";

    [GeneratedRegex(@"^(?<class>[A-Za-z_]\w*)\(\) accepts 0 positional sub-patterns \((?<given>\d+) given\)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "TypeError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (context.Read(context.Frame?.File) is not { } source) return null;

        var name = message.Groups["class"].Value;
        var given = int.Parse(message.Groups["given"].Value);
        var lines = source.Lines;
        var masked = CodeText.MaskAll(lines, Syntax.Python);

        if (Enumerable.Range(0, lines.Count).Where(i => Regex.IsMatch(masked[i], $@"^\s*class\s+{Regex.Escape(name)}\s*[(:]")).ToList() is not [var header]) return null;

        var (first, end) = PythonCode.BlockBody(lines, header);
        if (Enumerable.Range(first, end - first).Where(i => PythonCode.FunctionHeader().Match(masked[i]) is { Success: true } d && d.Groups["name"].Value == "__init__").ToList() is not [var init]) return null;

        var parameters = PythonCode.SplitParameters(PythonCode.FunctionHeader().Match(masked[init]).Groups["parameters"].Value).Skip(1).ToList();
        var (initFirst, initEnd) = PythonCode.BlockBody(lines, init);
        var body = string.Join("\n", Enumerable.Range(initFirst, initEnd - initFirst).Select(i => masked[i]));

        // Each parameter kept under its own name, in order - the positions a pattern would mean.
        var kept = parameters.TakeWhile(p => Regex.IsMatch(body, $@"\bself\.{Regex.Escape(p)}\s*=\s*{Regex.Escape(p)}\b")).ToList();
        if (kept.Count < given) return null;

        var at = PythonCode.FirstStatement(lines, first, end);
        var indent = CodeText.Indentation(lines[at < end ? at : first]);
        var names = string.Join(", ", kept.Select(k => $"\"{k}\"")) + (kept.Count == 1 ? "," : "");

        return LocalFix.Insert(
            Id, $"Say which attributes {name}'s positions are: __match_args__",
            $"`case {name}(...)` with values in positions has to know which attribute each position is, and an ordinary class does not " +
            $"say - a dataclass does it automatically. `__match_args__` lists them, in the order `__init__` takes them.",
            source.Path, at + 1, [$"{indent}__match_args__ = ({names})", ""]);
    }
}
