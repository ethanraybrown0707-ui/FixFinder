using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes.Rules;

// The Python mistakes of a computer science degree past the first weeks: data structures and algorithms, files and JSON,
// databases, sockets, threads and processes, hashing, asyncio and pattern matching. Each rule reads what Python said and
// proposes one change.

internal static partial class PyCourse
{
    /// <summary>
    /// A change to one line that needs a name imported: the line alone when <paramref name="name"/> is already imported, and
    /// otherwise one run of lines from the import to the change, so both land together or not at all.
    /// </summary>
    /// <returns>The fix, and how the name is written at the change - <c>cmp_to_key</c> or <c>functools.cmp_to_key</c>.</returns>
    public static LocalFix? WithImport(
        string rule, string title, string explanation, SourceFile source, int number, string module, string name, Func<string, string> change)
    {
        var lines = source.Lines;

        if (lines.Any(l => Regex.IsMatch(l, $@"^import\s+{Regex.Escape(module)}\s*(?:#.*)?$")))
            return LocalFix.ReplaceLine(rule, title, explanation, source.Path, number, change($"{module}.{name}"));

        var from = Enumerable.Range(0, number - 1)
            .Select(i => (Index: i, Match: Regex.Match(lines[i], $@"^from\s+{Regex.Escape(module)}\s+import\s+(?<names>[A-Za-z_][\w\s,]*?)\s*(?:#.*)?$")))
            .FirstOrDefault(x => x.Match.Success);

        if (from.Match is { Success: true } existing)
        {
            var changed = change(name);
            if (existing.Groups["names"].Value.Split(',').Any(n => n.Trim() == name))
                return LocalFix.ReplaceLine(rule, title, explanation, source.Path, number, changed);

            var start = from.Index + 1;
            return Span(rule, title, explanation, source, start, number, [lines[from.Index].TrimEnd() + ", " + name], changed);
        }

        var insertAt = PythonLayout.ImportInsertionLine(lines);
        if (insertAt > number) return null;

        return Span(rule, title, explanation, source, insertAt, number, [$"from {module} import {name}"], change(name), insert: true);
    }

    private static LocalFix Span(
        string rule, string title, string explanation, SourceFile source, int start, int number, IReadOnlyList<string> head, string changed, bool insert = false)
    {
        var lines = source.Lines;
        var middle = lines.Skip(start - 1 + (insert ? 0 : 1)).Take(number - start - (insert ? 0 : 1));

        return new LocalFix
        {
            RuleId = rule, Title = title, Explanation = explanation, File = source.Path,
            StartLine = start, RemoveCount = number - start + 1, NewLines = [.. head, .. middle, changed],
        };
    }

    /// <summary>The top-level arguments of the call whose opening bracket is at <paramref name="open"/>, with where each starts.</summary>
    public static List<(int Start, string Text)>? Arguments(string line, string masked, int open)
    {
        var close = Py.MatchForward(masked, open);
        if (close < 0) return null;

        var result = new List<(int, string)>();
        var depth = 0;
        var start = open + 1;

        for (var i = open + 1; i <= close; i++)
        {
            var c = masked[i];

            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}' && i < close) depth--;

            if ((c == ',' && depth == 0) || i == close)
            {
                var text = line[start..i];
                if (text.Trim().Length > 0) result.Add((start + (text.Length - text.TrimStart().Length), text.Trim()));
                start = i + 1;
            }
        }

        return result;
    }

    /// <summary>A plain name, attribute chain, call or subscript - something <c>.encode()</c> can follow without brackets.</summary>
    public static bool IsPrimary(string expression) =>
        Regex.IsMatch(expression, @"^[A-Za-z_][\w.]*(?:\([^()]*\)|\[[^\[\]]*\])*$");
}

/// <summary><c>'str' object cannot be interpreted as an integer</c> for a number read with <c>input()</c>.</summary>
public sealed partial class PythonInputNotNumber : ILocalFixRule
{
    public string Id => "python-input-not-number";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "TypeError") || context.Error.Message != "'str' object cannot be interpreted as an integer") return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.Python);
        var names = Regex.Matches(masked[number - 1], @"(?<![\w.])[A-Za-z_]\w*(?![\w(])").Select(m => m.Value).Distinct().ToList();

        var found = names
            .SelectMany(name => Enumerable.Range(0, number - 1)
                .Where(i => Regex.IsMatch(masked[i], $@"^\s*{Regex.Escape(name)}\s*=\s*input\s*\(.*\)\s*$"))
                .Select(i => (Name: name, Index: i)))
            .ToList();

        if (found is not [var only]) return null;

        var assignment = source.Lines[only.Index];
        var equals = assignment.IndexOf('=');
        var value = assignment[(equals + 1)..].Trim();

        return LocalFix.ReplaceLine(
            Id, $"Read {only.Name} as a whole number: int(input(...))",
            $"`input()` always returns text, even when what was typed is a number, and `{only.Name}` is used where a whole number is " +
            "needed. `int()` reads the number out of the text.",
            source.Path, only.Index + 1, assignment[..(equals + 1)] + " " + $"int({value})");
    }
}

/// <summary><c>json.loads(f)</c> given a file, or <c>json.load(text)</c> given text - one letter apart.</summary>
public sealed partial class PythonJsonLoadOrLoads : ILocalFixRule
{
    public string Id => "python-json-load-loads";

    [GeneratedRegex(@"^the JSON object must be str, bytes or bytearray, not (?:TextIOWrapper|BufferedReader)$")]
    private static partial Regex GivenAFile();

    [GeneratedRegex(@"^'str' object has no attribute 'read'$")]
    private static partial Regex GivenText();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "TypeError") && !Py.Is(context, "AttributeError")) return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var message = context.Error.Message ?? "";
        var masked = CodeText.Mask(line, Syntax.Python);

        if (GivenAFile().IsMatch(message))
        {
            if (Regex.Matches(masked, @"\bjson\.loads\s*\(").ToList() is not [var call]) return null;

            return LocalFix.ReplaceLine(
                Id, "Read JSON from a file with json.load",
                "`json.loads` - with an s, for string - reads JSON from text, and it was given an open file. `json.load` reads it " +
                "from the file.",
                source.Path, number, line[..call.Index] + "json.load(" + line[(call.Index + call.Length)..]);
        }

        if (!GivenText().IsMatch(message) || Regex.Matches(masked, @"\bjson\.load\s*\(\s*(?<name>[A-Za-z_]\w*)\s*\)").ToList() is not [var load]) return null;

        // Text for certain: read from a file, or a JSON literal - not a variable holding a file name, which wants opening.
        var name = load.Groups["name"].Value;
        var all = CodeText.MaskAll(source.Lines, Syntax.Python);
        var assigned = Enumerable.Range(0, number - 1).Where(i => Regex.IsMatch(all[i], $@"^\s*{Regex.Escape(name)}\s*=")).ToList();

        if (assigned is not [var index] || !Regex.IsMatch(source.Lines[index], $@"=\s*(?:[\w.]+\.read\(\)|['""]\s*[\[{{])")) return null;

        return LocalFix.ReplaceLine(
            Id, "Read JSON from text with json.loads",
            $"`json.load` reads JSON from an open file, and `{name}` is text that has already been read. `json.loads` - with an s, for " +
            "string - reads it from text.",
            source.Path, number, line[..load.Index] + "json.loads(" + line[(load.Index + "json.load(".Length)..]);
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
        if (!Py.Is(context, "TypeError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (context.Read(context.Frame?.File) is not { } source) return null;

        var name = message.Groups["class"].Value;
        var methods = Regex.Matches(message.Groups["methods"].Value, @"'?(?<m>[A-Za-z_]\w*)'?").Select(m => m.Groups["m"].Value).ToList();
        if (methods.Count == 0) return null;

        var lines = source.Lines;
        var masked = CodeText.MaskAll(lines, Syntax.Python);

        var headers = Enumerable.Range(0, lines.Count).Where(i => Regex.IsMatch(masked[i], $@"^class\s+{Regex.Escape(name)}\s*[(:]")).ToList();
        if (headers is not [var header]) return null;

        var (first, end) = PyScope.Body(lines, header);
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
        if (!Py.Is(context, "ValueError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (context.Read(context.Frame?.File) is not { } source) return null;

        var field = message.Groups["field"].Value;
        var type = message.Groups["type"].Value;
        var masked = CodeText.MaskAll(source.Lines, Syntax.Python);
        var empty = type switch { "list" => @"\[\s*\]|list\(\s*\)", "dict" => @"\{\s*\}|dict\(\s*\)", _ => @"set\(\s*\)" };
        var declaration = new Regex($@"^(?<head>\s*{Regex.Escape(field)}\s*:[^=]*=\s*)(?:{empty})\s*$");

        if (Enumerable.Range(0, masked.Count).Where(i => declaration.IsMatch(masked[i])).ToList() is not [var index]) return null;

        var head = declaration.Match(masked[index]).Groups["head"].Value;
        var (_, tail) = CodeText.SplitComment(source.Lines[index], Syntax.Python);

        return PyCourse.WithImport(
            Id, $"Give each object its own {type}: field(default_factory={type})",
            $"A default is made once, when the class is defined, so every object would share one `{type}` - adding to one object's " +
            $"`{field}` would add to all of them. Dataclasses refuse it for that reason. `field(default_factory={type})` makes a new, " +
            "empty one for each object.",
            source, index + 1, "dataclasses", "field", written => $"{source.Lines[index][..head.Length]}{written}(default_factory={type}){tail}");
    }
}

/// <summary><c>deque.pop() takes no arguments (1 given)</c> - <c>pop(0)</c> from a list, where a deque has <c>popleft()</c>.</summary>
public sealed partial class PythonDequePopLeft : ILocalFixRule
{
    public string Id => "python-deque-popleft";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "TypeError") || context.Error.Message != "deque.pop() takes no arguments (1 given)") return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (Regex.Matches(CodeText.Mask(line, Syntax.Python), @"\.pop\(\s*0\s*\)").ToList() is not [var pop]) return null;

        return LocalFix.ReplaceLine(
            Id, "Take from the front of a deque with popleft()",
            "`pop(0)` takes from the front of a list, but a `deque` takes no position: `pop()` takes from the back and `popleft()` from " +
            "the front. Taking from the front is what a queue in a breadth-first search needs, and it is what `deque` makes fast.",
            source.Path, number, line[..pop.Index] + ".popleft()" + line[(pop.Index + pop.Length)..]);
    }
}

/// <summary><c>'set' object has no attribute 'append'</c> - a list's method on a set, which calls it <c>add</c>.</summary>
public sealed partial class PythonSetMethod : ILocalFixRule
{
    public string Id => "python-set-method";

    [GeneratedRegex(@"^'set' object has no attribute '(?<method>append|push|extend)'$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "AttributeError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var method = message.Groups["method"].Value;
        var replacement = method == "extend" ? "update" : "add";

        if (Regex.Matches(CodeText.Mask(line, Syntax.Python), $@"\.{method}\s*\(").ToList() is not [var call]) return null;

        return LocalFix.ReplaceLine(
            Id, $"Use a set's own method: {replacement}",
            $"`{method}` is a list's method. A set has no order to add to the end of, so it calls it `{replacement}`.",
            source.Path, number, line[..call.Index] + $".{replacement}(" + line[(call.Index + call.Length)..]);
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
        if (!Py.Is(context, "TypeError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (context.Read(context.Frame?.File) is not { } source) return null;

        var name = message.Groups["class"].Value;
        var lines = source.Lines;
        var masked = CodeText.MaskAll(lines, Syntax.Python);

        if (Enumerable.Range(0, lines.Count).Where(i => Regex.IsMatch(masked[i], $@"^class\s+{Regex.Escape(name)}\s*[(:]")).ToList() is not [var header]) return null;

        var (first, end) = PyScope.Body(lines, header);
        var body = Enumerable.Range(first, end - first).ToList();

        if (body.Any(i => Regex.IsMatch(masked[i], @"^\s*(?:def\s+__hash__|__hash__\s*=)"))) return null;
        if (body.Where(i => Regex.IsMatch(masked[i], @"^\s*def\s+__eq__\s*\(")).ToList() is not [var eq]) return null;

        var (eqFirst, eqEnd) = PyScope.Body(lines, eq);
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

/// <summary><c>sort() got an unexpected keyword argument 'cmp'</c> - Python 2's comparison function, which Python 3 takes as a key.</summary>
public sealed partial class PythonSortCmp : ILocalFixRule
{
    public string Id => "python-sort-cmp";

    [GeneratedRegex(@"^(?:sort\(\) got an unexpected keyword argument 'cmp'|'cmp' is an invalid keyword argument for sort\(\)|sorted\(\) got an unexpected keyword argument 'cmp')$")]
    private static partial Regex Message();

    [GeneratedRegex(@"\bcmp\s*=\s*(?<function>[A-Za-z_][\w.]*)(?=\s*[,)])")]
    private static partial Regex Cmp();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "TypeError") || !Message().IsMatch(context.Error.Message ?? "")) return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (Cmp().Matches(CodeText.Mask(line, Syntax.Python)).ToList() is not [var cmp]) return null;

        var function = cmp.Groups["function"].Value;

        return PyCourse.WithImport(
            Id, $"Sort with {function} as a key: cmp_to_key({function})",
            $"Python 3 removed `cmp=`, the comparison function Python 2 took. `functools.cmp_to_key` turns a comparison function like " +
            $"`{function}` into the `key=` that sorting takes now, so the order comes out the same.",
            source, number, "functools", "cmp_to_key",
            written => line[..cmp.Index] + $"key={written}({function})" + line[(cmp.Index + cmp.Length)..]);
    }
}

/// <summary><c>worker() argument after * must be an iterable, not int</c> - <c>args=(5)</c>, which is 5 in brackets, not a tuple.</summary>
/// <remarks>
/// Raised inside <c>threading</c> or <c>multiprocessing</c>, so the traceback never names the program's own file. The call is
/// found by the function the message names, in the Python files of the program's folder, and must be the only one.
/// </remarks>
public sealed partial class PythonArgsTuple : ILocalFixRule
{
    public string Id => "python-args-tuple";

    [GeneratedRegex(@"^(?:__main__\.|__mp_main__\.)?(?<function>[A-Za-z_]\w*)\(\) argument after \* must be an iterable, not (?<type>\w+)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "TypeError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (context.SourceRoot is not { } root || !Directory.Exists(root)) return null;

        var function = message.Groups["function"].Value;
        var call = new Regex($@"\btarget\s*=\s*{Regex.Escape(function)}\b.*?\bargs\s*=\s*(?<args>\((?<inner>[^(),]*(?:\([^()]*\))?[^(),]*)\)|(?<bare>[A-Za-z_]\w*|\d+|""[^""]*""|'[^']*'))(?=\s*[,)])");

        var hits = new List<(SourceFile Source, int Index, Match Match)>();

        foreach (var path in Directory.EnumerateFiles(root, "*.py").Take(200))
        {
            if (SourceFile.Read(path) is not { } source) continue;
            var masked = CodeText.MaskAll(source.Lines, Syntax.Python);

            for (var i = 0; i < masked.Count; i++)
                if (call.Match(masked[i]) is { Success: true } m) hits.Add((source, i, m));
        }

        if (hits is not [var hit]) return null;

        var args = hit.Match.Groups["args"];
        var line = hit.Source.Lines[hit.Index];
        var value = hit.Match.Groups["inner"].Success ? line.Substring(hit.Match.Groups["inner"].Index, hit.Match.Groups["inner"].Length).Trim() : line.Substring(args.Index, args.Length).Trim();

        // A name holding a list or tuple already is iterable; only a single value in brackets is the mistake read here.
        if (value.Length == 0 || (hit.Match.Groups["bare"].Success && message.Groups["type"].Value is "list" or "tuple")) return null;

        return LocalFix.ReplaceLine(
            Id, $"Pass the argument as a tuple: args=({value},)",
            $"`args` has to be a tuple of the arguments for `{function}`, and `({value})` is not a tuple - brackets around one value are " +
            "just brackets. The comma is what makes a tuple of one: `(" + value + ",)`.",
            hit.Source.Path, hit.Index + 1, line[..args.Index] + $"({value},)" + line[(args.Index + args.Length)..]);
    }
}

/// <summary><c>Incorrect number of bindings supplied. The current statement uses 1, and there are 3 supplied.</c></summary>
/// <remarks><c>(name)</c> is the string itself, and sqlite3 binds a string one character at a time.</remarks>
public sealed partial class PythonSqlParameterTuple : ILocalFixRule
{
    public string Id => "python-sql-parameter-tuple";

    [GeneratedRegex(@"^Incorrect number of bindings supplied\. The current statement uses 1, and there are (?<n>\d+) supplied\.$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "python" } || !(context.Error.ExceptionType ?? "").EndsWith("ProgrammingError", StringComparison.Ordinal)) return null;
        if (Message().Match(context.Error.Message ?? "") is not { Success: true } || Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.Python);
        if (Regex.Matches(masked, @"\.execute(?:many)?\s*\(").ToList() is not [var execute]) return null;

        if (PyCourse.Arguments(line, masked, execute.Index + execute.Length - 1) is not [_, var parameters]) return null;

        var text = parameters.Text;
        string corrected;

        if (text.StartsWith('(') && Py.MatchForward(CodeText.Mask(text, Syntax.Python), 0) == text.Length - 1 && !CodeText.Mask(text, Syntax.Python)[1..^1].Contains(','))
            corrected = text[..^1].TrimEnd() + ",)";
        else if (Regex.IsMatch(text, @"^(?:[A-Za-z_][\w.]*|""[^""]*""|'[^']*')$"))
            corrected = $"({text},)";
        else
            return null;

        return LocalFix.ReplaceLine(
            Id, $"Pass the one parameter as a tuple: {corrected}",
            "The statement has one `?`, so it needs a sequence holding one value. Brackets around a single value are only brackets, so " +
            "sqlite3 was given the text itself, and a string is a sequence of its characters - one binding per letter. The comma makes " +
            "it a tuple of one.",
            source.Path, number, line[..parameters.Start] + corrected + line[(parameters.Start + text.Length)..]);
    }
}

/// <summary><c>socket.bind() takes exactly one argument (2 given)</c> - the host and port given separately, where the address is one pair.</summary>
public sealed partial class PythonSocketAddress : ILocalFixRule
{
    public string Id => "python-socket-address";

    [GeneratedRegex(@"^socket\.(?<method>bind|connect|connect_ex)\(\) takes exactly one argument \(2 given\)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "TypeError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var method = message.Groups["method"].Value;
        var masked = CodeText.Mask(line, Syntax.Python);

        if (Regex.Matches(masked, $@"\.{method}\s*\(").ToList() is not [var call]) return null;
        if (PyCourse.Arguments(line, masked, call.Index + call.Length - 1) is not [var host, var port]) return null;

        var start = host.Start;
        var end = port.Start + port.Text.Length;

        return LocalFix.ReplaceLine(
            Id, $"Pass the address as one pair: {method}(({host.Text}, {port.Text}))",
            $"A socket address is one value - a tuple of host and port - so `{method}` takes a single argument. The two go inside one more " +
            "pair of brackets.",
            source.Path, number, line[..start] + $"({host.Text}, {port.Text})" + line[end..]);
    }
}

/// <summary><c>a bytes-like object is required, not 'str'</c>, and <c>Strings must be encoded before hashing</c> - text where bytes go.</summary>
public sealed partial class PythonTextToBytes : ILocalFixRule
{
    public string Id => "python-text-to-bytes";

    [GeneratedRegex(@"^(?:a bytes-like object is required, not 'str'|Strings must be encoded before hashing)$")]
    private static partial Regex Message();

    [GeneratedRegex(@"(?:\.(?:sendall|send|sendto|write|update)|\bhashlib\.(?:md5|sha1|sha224|sha256|sha384|sha512|sha3_256|sha3_512|blake2b|blake2s)|\bhashlib\.new\s*\(\s*['""]\w+['""]\s*,)\s*\(?")]
    private static partial Regex Call();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "TypeError") || !Message().IsMatch(context.Error.Message ?? "")) return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.Python);
        var hashing = context.Error.Message!.StartsWith("Strings", StringComparison.Ordinal);

        var calls = Call().Matches(masked).Where(m => hashing == m.Value.Contains("hashlib", StringComparison.Ordinal) || (hashing && m.Value.StartsWith(".update", StringComparison.Ordinal))).ToList();
        if (calls is not [var call]) return null;

        var open = masked.LastIndexOf('(', call.Index + call.Length - 1);
        if (call.Value.Contains("hashlib.new", StringComparison.Ordinal)) open = masked.IndexOf('(', call.Index);
        if (open < 0 || PyCourse.Arguments(line, masked, open) is not { Count: > 0 } arguments) return null;

        var target = call.Value.Contains("hashlib.new", StringComparison.Ordinal) ? arguments.ElementAtOrDefault(1) : arguments[0];
        if (target.Text is null || target.Text.Contains('=')) return null;

        var text = target.Text;
        string corrected;

        if (Regex.IsMatch(text, @"^(?:""[\x20-\x7E]*""|'[\x20-\x7E]*')$") && !text.Contains('\\'))
            corrected = "b" + text;
        else if (PyCourse.IsPrimary(text) || Regex.IsMatch(text, @"^[fF]?(?:""[^""]*""|'[^']*')$"))
            corrected = text + ".encode()";
        else
            corrected = $"({text}).encode()";

        return LocalFix.ReplaceLine(
            Id, $"Turn the text into bytes: {corrected}",
            hashing
                ? "A hash is worked out over bytes, not text, because the same text can be stored as different bytes. `.encode()` turns " +
                  "text into bytes as UTF-8; a `b\"...\"` literal is bytes already."
                : "Sockets and binary files carry bytes, not text. `.encode()` turns text into bytes as UTF-8, and a `b\"...\"` literal is " +
                  "bytes already. The other end turns them back with `.decode()`.",
            source.Path, number, line[..target.Start] + corrected + line[(target.Start + text.Length)..]);
    }
}

/// <summary>The <c>bootstrapping phase</c> error: starting a process at the top level of a script, which Windows runs again in the child.</summary>
public sealed partial class PythonMainGuard : ILocalFixRule
{
    public string Id => "python-main-guard";

    [GeneratedRegex(@"^(?:@|def\s|async\s+def\s|class\s|import\s|from\s|#|if\s+__name__\s*==)")]
    private static partial Regex Setup();

    public LocalFix? Propose(LocalFixContext context)
    {
        // The explanation comes after the traceback, over several lines, so it is looked for in everything that was printed.
        if (!Py.Is(context, "RuntimeError")) return null;
        if (!context.Error.RawText.Contains("bootstrapping phase", StringComparison.Ordinal) &&
            !context.Output.Any(l => l.Text.Contains("bootstrapping phase", StringComparison.Ordinal))) return null;
        if (context.Read(context.Frame?.File) is not { } source) return null;

        var lines = source.Lines;
        var masked = CodeText.MaskAll(lines, Syntax.Python);
        if (masked.Any(l => Regex.IsMatch(l, @"^if\s+__name__\s*=="))) return null;

        // Statements at the top level that are not setup - they are what runs on import, and what the guard goes around.
        var statements = Enumerable.Range(0, lines.Count)
            .Where(i => masked[i].Trim().Length > 0 && CodeText.Indentation(masked[i]).Length == 0 && !Setup().IsMatch(masked[i]))
            .ToList();
        if (statements.Count == 0) return null;

        var first = statements[0];

        // Only a script whose statements come after everything it defines: a def or class below them would move into the guard.
        if (Enumerable.Range(first, lines.Count - first).Any(i => CodeText.Indentation(masked[i]).Length == 0 && Regex.IsMatch(masked[i], @"^(?:@|def\s|async\s+def\s|class\s)"))) return null;

        var end = lines.Count;
        while (end > first && lines[end - 1].Trim().Length == 0) end--;

        var unit = lines.Select(CodeText.Indentation).FirstOrDefault(i => i.Length > 0) ?? "    ";
        var wrapped = new List<string> { "if __name__ == \"__main__\":" };
        wrapped.AddRange(Enumerable.Range(first, end - first).Select(i => lines[i].Trim().Length == 0 ? "" : unit + lines[i]));

        return new LocalFix
        {
            RuleId = Id,
            Title = "Start processes only when run directly: if __name__ == \"__main__\":",
            Explanation =
                "On Windows a new process starts by importing this file again, so code at the top level runs again inside every child - " +
                "which starts another process before the first has finished starting, and Python stops it. Code under " +
                "`if __name__ == \"__main__\":` runs only in the process that was started directly, not on those imports.",
            File = source.Path, StartLine = first + 1, RemoveCount = end - first, NewLines = wrapped,
        };
    }
}

/// <summary><c>asyncio.gather([a(), b()])</c> - gather takes the awaitables themselves, not a list of them.</summary>
public sealed partial class PythonGatherList : ILocalFixRule
{
    public string Id => "python-gather-list";

    [GeneratedRegex(@"^(?:unhashable type: 'list'|An asyncio\.Future, a coroutine or an awaitable is required)$")]
    private static partial Regex Message();

    [GeneratedRegex(@"\bgather\s*\(\s*(?=[\[A-Za-z_])")]
    private static partial Regex Gather();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "TypeError") || !Message().IsMatch(context.Error.Message ?? "")) return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.Python);
        if (Gather().Matches(masked).ToList() is not [var gather]) return null;

        var open = masked.IndexOf('(', gather.Index);
        if (PyCourse.Arguments(line, masked, open) is not [var only]) return null;

        string corrected;

        if (only.Text.StartsWith('[') && Py.MatchForward(CodeText.Mask(only.Text, Syntax.Python), 0) == only.Text.Length - 1)
            corrected = only.Text[1..^1].Trim();
        else if (Regex.IsMatch(only.Text, @"^[A-Za-z_]\w*$"))
            corrected = "*" + only.Text;
        else
            return null;

        return LocalFix.ReplaceLine(
            Id, "Give gather the awaitables themselves",
            "`asyncio.gather` takes each coroutine as its own argument and waits for them all, returning their results in a list. Given " +
            "one list instead, it tries to await the list. The items go in directly - or `*` spreads a list that is built elsewhere.",
            source.Path, number, line[..only.Start] + corrected + line[(only.Start + only.Text.Length)..]);
    }
}

/// <summary><c>unsupported operand type(s) for +: 'coroutine' and 'int'</c> - an <c>async def</c> called without <c>await</c>.</summary>
public sealed partial class PythonCoroutineNotAwaited : ILocalFixRule
{
    public string Id => "python-coroutine-not-awaited";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "python" } error || error.ExceptionType is not ("TypeError" or "AttributeError")) return null;
        if (!(error.Message ?? "").Contains("'coroutine'", StringComparison.Ordinal) || Py.Locate(context) is not { } at) return null;

        var (source, number, _) = at;
        var lines = source.Lines;
        var masked = CodeText.MaskAll(lines, Syntax.Python);

        var asyncs = masked.Select(l => Regex.Match(l, @"^\s*async\s+def\s+(?<name>[A-Za-z_]\w*)\s*\(")).Where(m => m.Success).Select(m => m.Groups["name"].Value).ToHashSet();
        if (asyncs.Count == 0) return null;

        var function = PyScope.Enclosing(masked, number - 1, PyScope.AnyDef());
        if (function < 0 || !masked[function].TrimStart().StartsWith("async", StringComparison.Ordinal)) return null;

        // The call on the error line itself, or the variable it was stored in a few lines above.
        var direct = Regex.Matches(masked[number - 1], @"(?<![\w.])(?<!await\s)(?<name>[A-Za-z_]\w*)\s*\(").Where(m => asyncs.Contains(m.Groups["name"].Value)).ToList();

        if (direct is [var call])
        {
            var text = lines[number - 1];
            var close = Py.MatchForward(masked[number - 1], masked[number - 1].IndexOf('(', call.Index));
            if (close < 0) return null;

            var expression = text[call.Index..(close + 1)];
            var standalone = masked[number - 1].Trim() == expression.Trim();

            return Fix(source, number, text[..call.Index] + (standalone ? $"await {expression}" : $"(await {expression})") + text[(close + 1)..], call.Groups["name"].Value);
        }

        var used = Regex.Matches(masked[number - 1], @"(?<![\w.])[A-Za-z_]\w*(?![\w(])").Select(m => m.Value).ToHashSet();

        var stored = Enumerable.Range(function + 1, number - 2 - function)
            .Select(i => (Index: i, Match: Regex.Match(masked[i], @"^(?<lead>\s*(?<name>[A-Za-z_]\w*)\s*=\s*)(?<call>(?<function>[A-Za-z_]\w*)\s*\()")))
            .Where(x => x.Match.Success && used.Contains(x.Match.Groups["name"].Value) && asyncs.Contains(x.Match.Groups["function"].Value))
            .ToList();

        if (stored is not [var assignment]) return null;

        var original = lines[assignment.Index];
        var lead = assignment.Match.Groups["lead"].Length;

        return Fix(source, assignment.Index + 1, original[..lead] + "await " + original[lead..], assignment.Match.Groups["function"].Value);
    }

    private LocalFix Fix(SourceFile source, int number, string corrected, string function) => LocalFix.ReplaceLine(
        Id, $"Wait for {function}: await",
        $"`{function}` is an `async def`, so calling it does not run it - it makes a coroutine, a promise of the result. `await` runs it " +
        "and gives back what it returns.",
        source.Path, number, corrected);
}

/// <summary><c>Point() accepts 0 positional sub-patterns (2 given)</c> - a class pattern with positions, for a class that never said what they are.</summary>
public sealed partial class PythonMatchArgs : ILocalFixRule
{
    public string Id => "python-match-args";

    [GeneratedRegex(@"^(?<class>[A-Za-z_]\w*)\(\) accepts 0 positional sub-patterns \((?<given>\d+) given\)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "TypeError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (context.Read(context.Frame?.File) is not { } source) return null;

        var name = message.Groups["class"].Value;
        var given = int.Parse(message.Groups["given"].Value);
        var lines = source.Lines;
        var masked = CodeText.MaskAll(lines, Syntax.Python);

        if (Enumerable.Range(0, lines.Count).Where(i => Regex.IsMatch(masked[i], $@"^\s*class\s+{Regex.Escape(name)}\s*[(:]")).ToList() is not [var header]) return null;

        var (first, end) = PyScope.Body(lines, header);
        if (Enumerable.Range(first, end - first).Where(i => PyScope.Def().Match(masked[i]) is { Success: true } d && d.Groups["name"].Value == "__init__").ToList() is not [var init]) return null;

        var parameters = PyScope.Parameters(PyScope.Def().Match(masked[init]).Groups["parameters"].Value).Skip(1).ToList();
        var (initFirst, initEnd) = PyScope.Body(lines, init);
        var body = string.Join("\n", Enumerable.Range(initFirst, initEnd - initFirst).Select(i => masked[i]));

        // Each parameter kept under its own name, in order - the positions a pattern would mean.
        var kept = parameters.TakeWhile(p => Regex.IsMatch(body, $@"\bself\.{Regex.Escape(p)}\s*=\s*{Regex.Escape(p)}\b")).ToList();
        if (kept.Count < given) return null;

        var at = PyScope.FirstStatement(lines, first, end);
        var indent = CodeText.Indentation(lines[at < end ? at : first]);
        var names = string.Join(", ", kept.Select(k => $"\"{k}\"")) + (kept.Count == 1 ? "," : "");

        return LocalFix.Insert(
            Id, $"Say which attributes {name}'s positions are: __match_args__",
            $"`case {name}(...)` with values in positions has to know which attribute each position is, and an ordinary class does not " +
            $"say - a dataclass does it automatically. `__match_args__` lists them, in the order `__init__` takes them.",
            source.Path, at + 1, [$"{indent}__match_args__ = ({names})", ""]);
    }
}

/// <summary><c>object of type 'generator' has no len()</c>.</summary>
public sealed partial class PythonGeneratorLen : ILocalFixRule
{
    public string Id => "python-generator-len";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "TypeError") || context.Error.Message is not ("object of type 'generator' has no len()" or "object of type 'map' has no len()" or "object of type 'filter' has no len()" or "object of type 'zip' has no len()")) return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.Python);
        if (Regex.Matches(masked, @"(?<![\w.])len\s*\(").ToList() is not [var len]) return null;

        var open = masked.IndexOf('(', len.Index);
        var close = Py.MatchForward(masked, open);
        if (close < 0) return null;

        var inner = line[(open + 1)..close];
        var kind = context.Error.Message!.Split('\'')[1];

        return LocalFix.ReplaceLine(
            Id, "Count the items by making a list of them: len(list(...))",
            $"A {kind} hands its items out one at a time and does not know how many there are until it has run out, so it has no length. " +
            "`list(...)` collects them all first, and a list can be counted.",
            source.Path, number, line[..(open + 1)] + $"list({inner})" + line[close..]);
    }
}

/// <summary><c>Object of type set is not JSON serializable</c>.</summary>
public sealed partial class PythonJsonSet : ILocalFixRule
{
    public string Id => "python-json-set";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "TypeError") || context.Error.Message != "Object of type set is not JSON serializable") return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.Python);
        if (Regex.Matches(masked[number - 1], @"\bjson\.dumps?\s*\(\s*(?<name>[A-Za-z_]\w*)\s*[,)]").ToList() is not [var dump]) return null;

        var name = dump.Groups["name"];
        var assigned = Enumerable.Range(0, number - 1).Where(i => Regex.IsMatch(masked[i], $@"^\s*{Regex.Escape(name.Value)}\s*=")).ToList();

        if (assigned is not [var index] || !Regex.IsMatch(masked[index], $@"=\s*(?:set\s*\(|\{{[^:{{}}]*\}}\s*$)")) return null;

        return LocalFix.ReplaceLine(
            Id, $"Save the set as a list: list({name.Value})",
            "JSON has lists but no sets, so a set cannot be written as JSON directly. `list(...)` writes its items as a JSON list; reading " +
            "it back gives a list, which `set(...)` turns back into a set.",
            source.Path, number, line[..name.Index] + $"list({name.Value})" + line[(name.Index + name.Length)..]);
    }
}
