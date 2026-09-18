using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>A name Python could not find, and did not suggest a replacement for: <c>name 'nmae' is not defined</c>, or <c>'Dog'
/// object has no attribute 'nmae'</c> on a class in this file.</summary>
public sealed partial class PythonNearestName : ILocalFixRule
{
    public string Id => "python-nearest-name";

    [GeneratedRegex(@"^name '(?<name>[A-Za-z_]\w*)' is not defined$")]
    private static partial Regex NameMessage();

    [GeneratedRegex(@"^'(?<type>[A-Za-z_]\w*)' object has no attribute '(?<name>[A-Za-z_]\w*)'$")]
    private static partial Regex AttributeMessage();

    [GeneratedRegex(@"^\s*(?:async\s+)?def\s+(?<name>[A-Za-z_]\w*)\s*\((?<params>[^)]*)\)")]
    private static partial Regex Def();

    [GeneratedRegex(@"^\s*class\s+(?<name>[A-Za-z_]\w*)")]
    private static partial Regex Class();

    [GeneratedRegex(@"^\s*(?<targets>[A-Za-z_][\w\s,]*?)\s*(?::[^=]+)?=(?!=)")]
    private static partial Regex Assignment();

    [GeneratedRegex(@"\bfor\s+(?<targets>[\w\s,()]+?)\s+in\b")]
    private static partial Regex ForTargets();

    [GeneratedRegex(@"\bas\s+(?<name>[A-Za-z_]\w*)")]
    private static partial Regex AsName();

    [GeneratedRegex(@"^\s*import\s+(?<modules>[\w.,\s]+)$")]
    private static partial Regex Import();

    [GeneratedRegex(@"^\s*from\s+[\w.]+\s+import\s+(?<names>.+)$")]
    private static partial Regex FromImport();

    [GeneratedRegex(@"\blambda\s+(?<params>[^:]*):")]
    private static partial Regex Lambda();

    [GeneratedRegex(@"[A-Za-z_]\w*")]
    private static partial Regex Word();

    private static readonly string[] Builtins =
        ("print input len range int str float list dict set tuple bool True False None sum min max abs round sorted " +
         "reversed enumerate zip map filter any all open type isinstance issubclass hasattr getattr setattr super object " +
         "Exception ValueError TypeError KeyError IndexError AttributeError NameError ZeroDivisionError FileNotFoundError " +
         "RuntimeError StopIteration iter next id hash hex oct bin chr ord format repr divmod pow callable vars dir " +
         "globals locals help exit quit bytes bytearray frozenset complex property staticmethod classmethod slice").Split(' ');

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        if (error.LanguageId != "python") return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (source.Line(number) is not { } line) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.Python);

        if (error.ExceptionType == "NameError" && NameMessage().Match(error.Message ?? "") is { Success: true } name)
        {
            var wrong = name.Groups["name"].Value;
            var candidates = BoundNames(masked).Concat(Builtins).Where(n => n != wrong);

            if (CodeText.Nearest(wrong, candidates) is not { } right) return null;
            if (Replace(error, number, line, $@"(?<![\w.]){Regex.Escape(wrong)}(?!\w)", wrong, right) is not { } corrected) return null;

            return LocalFix.ReplaceLine(
                Id, $"Change {wrong} to {right}",
                $"Nothing called `{wrong}` exists here, and `{right}` is the only name this file defines - or Python " +
                "provides - within a letter or two of it. Python itself did not suggest it, because it counts two swapped " +
                "letters as two mistakes.",
                source.Path, number, corrected);
        }

        if (error.ExceptionType == "AttributeError" && AttributeMessage().Match(error.Message ?? "") is { Success: true } attribute)
        {
            var type = attribute.Groups["type"].Value;
            var wrong = attribute.Groups["name"].Value;

            if (ClassMembers(masked, type) is not { Count: > 0 } members) return null;
            if (CodeText.Nearest(wrong, members.Where(m => m != wrong)) is not { } right) return null;
            if (Replace(error, number, line, $@"\.\s*{Regex.Escape(wrong)}(?!\w)", wrong, right) is not { } corrected) return null;

            return LocalFix.ReplaceLine(
                Id, $"Change {wrong} to {right}",
                $"`{type}` has nothing called `{wrong}`. `{right}` is the only thing the class defines within a letter or two of it.",
                source.Path, number, corrected);
        }

        return null;
    }

    private static string? Replace(ParsedError error, int number, string line, string pattern, string wrong, string right)
    {
        var hits = Regex.Matches(CodeText.Mask(line, Syntax.Python), pattern)
            .Select(m => m.Index + m.Length - wrong.Length)
            .ToList();

        int at;

        if (hits.Count == 1)
        {
            at = hits[0];
        }
        else
        {
            if (PythonCode.UnderlineColumn(error, number, line) is not { } column) return null;

            var raw = Regex.Matches(line, pattern).Select(m => m.Index + m.Length - wrong.Length).ToList();
            var underlined = raw.Where(h => h >= column && h <= column + 1).ToList();

            if (underlined.Count != 1) return null;
            at = underlined[0];
        }

        return line[..at] + right + line[(at + wrong.Length)..];
    }

    private static HashSet<string> BoundNames(IReadOnlyList<string> masked)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        void AddWords(string text)
        {
            foreach (Match word in Word().Matches(text)) names.Add(word.Value);
        }

        void AddParameters(string parameters)
        {
            foreach (var part in parameters.Split(','))
                if (Word().Match(part.Split(':')[0].Split('=')[0]) is { Success: true } word) names.Add(word.Value);
        }

        foreach (var line in masked)
        {
            if (Def().Match(line) is { Success: true } def)
            {
                names.Add(def.Groups["name"].Value);
                AddParameters(def.Groups["params"].Value);
            }

            if (Class().Match(line) is { Success: true } cls) names.Add(cls.Groups["name"].Value);
            if (Assignment().Match(line) is { Success: true } assignment) AddWords(assignment.Groups["targets"].Value);

            foreach (Match target in ForTargets().Matches(line)) AddWords(target.Groups["targets"].Value);
            foreach (Match alias in AsName().Matches(line)) names.Add(alias.Groups["name"].Value);
            foreach (Match lambda in Lambda().Matches(line)) AddParameters(lambda.Groups["params"].Value);

            if (Import().Match(line) is { Success: true } import)
                foreach (var module in import.Groups["modules"].Value.Split(','))
                    if (Word().Match(module) is { Success: true } top) names.Add(top.Value);

            if (FromImport().Match(line) is { Success: true } from) AddWords(from.Groups["names"].Value.Replace(" as ", ","));
        }

        names.ExceptWith(["import", "from", "as", "in", "for", "if", "else", "and", "or", "not", "is", "def", "class", "return", "lambda"]);

        return names;
    }

    private static HashSet<string>? ClassMembers(IReadOnlyList<string> masked, string type)
    {
        var start = -1;

        for (var i = 0; i < masked.Count && start < 0; i++)
            if (Regex.IsMatch(masked[i], $@"^\s*class\s+{Regex.Escape(type)}\b")) start = i;

        if (start < 0) return null;

        var indent = CodeText.Indentation(masked[start]).Length;
        var members = new HashSet<string>(StringComparer.Ordinal);

        for (var i = start + 1; i < masked.Count; i++)
        {
            var text = masked[i];
            if (text.Trim().Length == 0) continue;
            if (CodeText.Indentation(text).Length <= indent) break;

            if (Def().Match(text) is { Success: true } def) members.Add(def.Groups["name"].Value);

            foreach (Match assigned in Regex.Matches(text, @"\bself\.(?<name>[A-Za-z_]\w*)\s*(?::[^=]+)?=(?!=)"))
                members.Add(assigned.Groups["name"].Value);

            if (Regex.Match(text, @"^\s+(?<name>[A-Za-z_]\w*)\s*(?::[^=]+)?=(?!=)") is { Success: true } field)
                members.Add(field.Groups["name"].Value);
        }

        return members;
    }
}

/// <summary><c>NameError: name 'math' is not defined.</summary>
public sealed partial class PythonForgottenImport : ILocalFixRule
{
    public string Id => "python-forgotten-import";

    [GeneratedRegex(@"Did you forget to import '(?<module>[A-Za-z_][A-Za-z0-9_]*)'\?")]
    private static partial Regex HintPattern();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        if (error.LanguageId != "python" || error.ExceptionType != "NameError") return null;
        if (HintPattern().Match(error.Message ?? "") is not { Success: true } hint) return null;
        if (context.Read(context.Frame?.File) is not { } source) return null;

        var module = hint.Groups["module"].Value;

        if (module is "this" or "antigravity") return null;

        return LocalFix.Insert(
            Id,
            $"Add import {module}",
            $"Python said so itself: `{module}` is a standard module that was used without being imported. " +
            "The import goes at the top of the file, with any others.",
            source.Path,
            PythonCode.ImportInsertionLine(source.Lines),
            [$"import {module}"]);
    }
}

/// <summary><c>this.name</c> in a method, where Python's word is <c>self</c>.</summary>
public sealed partial class PythonThisForSelf : ILocalFixRule
{
    public string Id => "python-this-for-self";

    [GeneratedRegex(@"(?<![\w.])this\.")]
    private static partial Regex ThisDot();

    [GeneratedRegex(@"^\s*(?:async\s+)?def\s+\w+\s*\(\s*self\b")]
    private static partial Regex MethodWithSelf();

    [GeneratedRegex(@"^\s*(?:async\s+)?def\b")]
    private static partial Regex AnyDef();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "NameError") || context.Error.Message?.StartsWith("name 'this' is not defined", StringComparison.Ordinal) != true) return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.Python);
        var indent = CodeText.Indentation(line).Length;
        var inMethod = false;

        for (var k = number - 2; k >= 0; k--)
        {
            if (masked[k].Trim().Length == 0 || CodeText.Indentation(masked[k]).Length >= indent) continue;

            if (AnyDef().IsMatch(masked[k]))
            {
                inMethod = MethodWithSelf().IsMatch(masked[k]);
                break;
            }

            indent = CodeText.Indentation(masked[k]).Length;
            if (indent == 0) break;
        }

        if (!inMethod) return null;

        var hits = ThisDot().Matches(CodeText.Mask(line, Syntax.Python));
        if (hits.Count == 0) return null;

        var corrected = line;
        foreach (var hit in hits.Reverse()) corrected = corrected[..hit.Index] + "self." + corrected[(hit.Index + hit.Length)..];

        return LocalFix.ReplaceLine(
            Id, "Write this as self",
            "Python has no `this`. Inside a method the object is the first parameter, which this method calls `self`. " +
            "(Python's hint about importing `this` is about an unrelated module that prints a poem.)",
            source.Path, number, corrected);
    }
}

/// <summary><c>null</c>, <c>nil</c> and friends, which Python spells <c>None</c>.</summary>
public sealed partial class PythonNullToNone : ILocalFixRule
{
    public string Id => "python-null";

    [GeneratedRegex(@"^name '(?<name>null|nil|NULL|Null|nullptr)' is not defined")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "NameError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var name = message.Groups["name"].Value;
        var hits = Regex.Matches(CodeText.Mask(line, Syntax.Python), $@"(?<![\w.]){name}(?!\w)");

        if (hits.Count == 0) return null;

        var corrected = line;
        foreach (var hit in hits.Reverse()) corrected = corrected[..hit.Index] + "None" + corrected[(hit.Index + hit.Length)..];

        return LocalFix.ReplaceLine(
            Id, $"Write {name} as None",
            "Python's word for no value is `None`, with a capital N.",
            source.Path, number, corrected);
    }
}

/// <summary>Names that Python 3 renamed: <c>raw_input</c>, <c>unicode</c>, <c>long</c> and the rest.</summary>
public sealed partial class PythonTwoToThreeName : ILocalFixRule
{
    public string Id => "python-2-to-3-name";

    [GeneratedRegex(@"^name '(?<name>raw_input|unicode|basestring|long|unichr|file)' is not defined")]
    private static partial Regex Message();

    private static readonly Dictionary<string, string> Renamed = new(StringComparer.Ordinal)
    {
        ["raw_input"] = "input",
        ["unicode"] = "str",
        ["basestring"] = "str",
        ["long"] = "int",
        ["unichr"] = "chr",
        ["file"] = "open",
    };

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "NameError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var name = message.Groups["name"].Value;
        var right = Renamed[name];

        var hits = Regex.Matches(CodeText.Mask(line, Syntax.Python), $@"(?<![\w.]){name}(?!\w)");
        if (hits.Count == 0) return null;

        var corrected = line;
        foreach (var hit in hits.Reverse()) corrected = corrected[..hit.Index] + right + corrected[(hit.Index + hit.Length)..];

        return LocalFix.ReplaceLine(
            Id, $"Write {name} as {right}",
            $"`{name}` is Python 2. Python 3 calls it `{right}`.",
            source.Path, number, corrected);
    }
}

/// <summary><c>No module named 'maths'</c>, when the file plainly means the standard <c>math</c>.</summary>
public sealed partial class PythonStdlibModuleTypo : ILocalFixRule
{
    public string Id => "python-stdlib-module-typo";

    [GeneratedRegex(@"^No module named '(?<name>[A-Za-z_]\w*)'$")]
    private static partial Regex Message();

    private const int MaxSpan = 25;

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "python", ExceptionType: "ModuleNotFoundError" } error) return null;
        if (Message().Match(error.Message ?? "") is not { Success: true } message) return null;
        if (context.Read(context.Frame?.File) is not { } source) return null;

        var wrong = message.Groups["name"].Value;
        if (PythonStandardLibrary.TypoOf(context.PythonInterpreter, wrong, source.Lines) is not { } right) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.Python);
        var pattern = new Regex($@"(?<![\w.]){Regex.Escape(wrong)}(?!\w)");

        var touched = Enumerable.Range(0, masked.Count).Where(i => pattern.IsMatch(masked[i])).ToList();
        if (touched.Count == 0 || touched[^1] - touched[0] > MaxSpan) return null;

        var lines = new List<string>();

        for (var i = touched[0]; i <= touched[^1]; i++)
        {
            var line = source.Lines[i];

            foreach (var hit in pattern.Matches(masked[i]).Reverse())
                line = line[..hit.Index] + right + line[(hit.Index + hit.Length)..];

            lines.Add(line);
        }

        var used = PythonStandardLibrary.UsedNames(wrong, source.Lines);

        return new LocalFix
        {
            RuleId = Id,
            Title = $"Change {wrong} to {right}",
            Explanation =
                $"There is no module called `{wrong}`, but the standard library has `{right}` - and everything this file " +
                $"uses from it ({string.Join(", ", used)}) is in `{right}`. Installing a package called `{wrong}` would " +
                "download somebody else's code to fix a spelling mistake.",
            File = source.Path,
            StartLine = touched[0] + 1,
            RemoveCount = touched[^1] - touched[0] + 1,
            NewLines = lines,
        };
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
        if (!PythonCode.Raised(context, "NameError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
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
            source.Path, PythonCode.ImportInsertionLine(lines), [$"from {module} import {name}"]);
    }
}

/// <summary><c>'module' object is not callable.</summary>
public sealed partial class PythonModuleCalled : ILocalFixRule
{
    public string Id => "python-module-called";

    [GeneratedRegex(@"^'module' object is not callable\. Did you mean: '(?<module>[A-Za-z_]\w*)\.(?<attr>[A-Za-z_]\w*)\(\.\.\.\)'\?$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "TypeError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var module = message.Groups["module"].Value;
        var attr = message.Groups["attr"].Value;

        var hits = Regex.Matches(CodeText.Mask(line, Syntax.Python), $@"(?<![\w.]){Regex.Escape(module)}(?=\s*\()");
        if (hits.Count == 0) return null;

        return LocalFix.ReplaceLine(
            Id, $"Call {module}.{attr}",
            $"`{module}` is the module, and the function inside it is `{module}.{attr}` - Python said so. (`from {module} import {attr}` " +
            "at the top would let the short name work instead.)",
            source.Path, number, CCode.ReplaceEach(line, hits, _ => $"{module}.{attr}"));
    }
}

/// <summary><c>UnboundLocalError: cannot access local variable 'count' where it is not associated with a value</c></summary>
public sealed partial class PythonUnboundGlobal : ILocalFixRule
{
    public string Id => "python-unbound-global";

    [GeneratedRegex(@"(?:cannot access local variable|local variable) '(?<name>[A-Za-z_]\w*)'")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        if (error.LanguageId != "python" || error.ExceptionType != "UnboundLocalError") return null;
        if (Message().Match(error.Message ?? "") is not { Success: true } message) return null;
        if (context.Frame is not { Line: { } number, Symbol: { Length: > 0 } function } frame) return null;
        if (context.Read(frame.File) is not { } source) return null;

        var name = message.Groups["name"].Value;
        var lines = source.Lines;
        var masked = CodeText.MaskAll(lines, Syntax.Python);

        var definitions = Enumerable.Range(0, lines.Count)
            .Where(i => Regex.IsMatch(masked[i], $@"^def\s+{Regex.Escape(function)}\s*\(.*\)\s*(->[^:]*)?:\s*$"))
            .ToList();

        if (definitions.Count != 1) return null;

        var def = definitions[0];
        var bodyEnd = def + 1;

        while (bodyEnd < lines.Count && (lines[bodyEnd].Trim().Length == 0 || char.IsWhiteSpace(lines[bodyEnd][0]))) bodyEnd++;

        if (number - 1 <= def || number - 1 >= bodyEnd) return null;

        var body = Enumerable.Range(def + 1, bodyEnd - def - 1).Select(i => masked[i]).ToList();
        var word = Regex.Escape(name);

        if (body.Any(text => Regex.IsMatch(text, $@"^\s*(?:global|nonlocal)\b.*\b{word}\b"))) return null;

        if (body.Any(text => Regex.IsMatch(text, $@"^\s*{word}\s*=(?!=)") && Regex.Matches(text, $@"\b{word}\b").Count == 1)) return null;

        var moduleLevel = Enumerable.Range(0, lines.Count)
            .Where(i => i < def || i >= bodyEnd)
            .Any(i => Regex.IsMatch(masked[i], $@"^{word}\s*(?::[^=]*)?=(?!=)"));

        if (!moduleLevel) return null;

        var first = def + 1;
        while (first < bodyEnd && lines[first].Trim().Length == 0) first++;
        if (first >= bodyEnd) return null;

        var indent = CodeText.Indentation(lines[first]);
        var insertAt = first;

        var opening = lines[first].TrimStart();
        var quote = opening.StartsWith("\"\"\"", StringComparison.Ordinal) ? "\"\"\"" : opening.StartsWith("'''", StringComparison.Ordinal) ? "'''" : null;

        if (quote is not null)
        {
            var closesOnSameLine = opening.Length > 3 && opening[3..].Contains(quote, StringComparison.Ordinal);
            insertAt = first + 1;

            if (!closesOnSameLine)
            {
                while (insertAt < bodyEnd && !lines[insertAt].Contains(quote, StringComparison.Ordinal)) insertAt++;
                insertAt++;
            }
        }

        return LocalFix.Insert(
            Id,
            $"Declare {name} as global in {function}",
            $"{function} assigns to `{name}`, and assigning anywhere in a function makes the name local to all of it - " +
            $"so line {number} reads a local that was never set. There is a `{name}` at module level, and " +
            $"`global {name}` makes the function use that one.",
            source.Path, insertAt + 1, [$"{indent}global {name}"]);
    }
}

/// <summary><c>module 'datetime' has no attribute 'now'</c> - now lives on the class inside the module.</summary>
public sealed partial class PythonDatetimeClass : ILocalFixRule
{
    public string Id => "python-datetime-class";

    [GeneratedRegex(@"^module 'datetime' has no attribute '(?<name>now|today|utcnow|fromtimestamp|fromisoformat|strptime|combine)'$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "python", ExceptionType: "AttributeError" } error) return null;
        if (Message().Match(error.Message ?? "") is not { Success: true } message) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (source.Line(number) is not { } line) return null;

        var name = message.Groups["name"].Value;
        var hits = Regex.Matches(CodeText.Mask(line, Syntax.Python), $@"(?<![\w.])datetime\.{name}\b");
        if (hits.Count != 1) return null;

        var at = hits[0].Index;

        return LocalFix.ReplaceLine(
            Id, $"Call {name} on the datetime class: datetime.datetime.{name}()",
            $"`import datetime` gives you the module, and `{name}` belongs to the class of the same name inside it - " +
            $"`datetime.datetime.{name}()`. Writing `from datetime import datetime` at the top is the other common way.",
            source.Path, number, line[..at] + "datetime." + line[at..]);
    }
}

/// <summary>Method names from other languages on Python's own types: <c>.length</c>, <c>.push</c>, <c>.toUpperCase()</c>,
/// <c>.equals()</c>, <c>.contains()</c>, and Python 2's <c>has_key</c>.</summary>
public sealed partial class PythonForeignMethod : ILocalFixRule
{
    public string Id => "python-foreign-method";

    [GeneratedRegex(@"^'(?<type>str|list|dict|tuple|set)' object has no attribute '(?<name>\w+)'$")]
    private static partial Regex Message();

    private static readonly Dictionary<(string Type, string Name), string> Renames = new()
    {
        [("list", "push")] = "append", [("list", "add")] = "append", [("list", "addAll")] = "extend",
        [("list", "indexOf")] = "index", [("list", "removeAt")] = "pop", [("set", "push")] = "add",
        [("str", "toUpperCase")] = "upper", [("str", "toLowerCase")] = "lower", [("str", "trim")] = "strip",
        [("str", "trimStart")] = "lstrip", [("str", "trimLeft")] = "lstrip", [("str", "trimEnd")] = "rstrip",
        [("str", "trimRight")] = "rstrip", [("str", "startsWith")] = "startswith", [("str", "endsWith")] = "endswith",
        [("str", "indexOf")] = "find", [("str", "replaceAll")] = "replace", [("str", "toUpper")] = "upper",
        [("str", "toLower")] = "lower",
        [("dict", "iteritems")] = "items", [("dict", "itervalues")] = "values", [("dict", "iterkeys")] = "keys",
        [("dict", "getOrDefault")] = "get", [("dict", "keySet")] = "keys", [("dict", "entrySet")] = "items",
    };

    private static readonly HashSet<string> Lengths = ["length", "size", "len", "count"];
    private static readonly HashSet<string> Membership = ["contains", "includes", "has_key", "containsKey", "has"];

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "AttributeError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var type = message.Groups["type"].Value;
        var name = message.Groups["name"].Value;

        var masked = CodeText.Mask(line, Syntax.Python);
        var hits = Regex.Matches(masked, $@"\.{Regex.Escape(name)}(?!\w)");
        if (hits.Count != 1) return null;

        var dot = hits[0].Index;
        var nameEnd = dot + 1 + name.Length;
        var isCall = nameEnd < masked.Length && masked[nameEnd] == '(';
        var close = isCall ? Brackets.Closing(masked, nameEnd) : -1;

        if (isCall && close < 0) return null;

        var arguments = isCall ? line[(nameEnd + 1)..close].Trim() : "";
        var spanEnd = isCall ? close + 1 : nameEnd;

        if (Renames.TryGetValue((type, name), out var renamed) && isCall)
        {
            return LocalFix.ReplaceLine(
                Id, $"Call {renamed} instead of {name}",
                $"`{name}` is another language's name for it. On a Python {type} it is `{renamed}`.",
                source.Path, number, line[..(dot + 1)] + renamed + line[nameEnd..]);
        }

        var start = PythonCode.ReceiverStart(masked, dot);
        if (start < 0) return null;

        var receiver = line[start..dot];

        string replacement;
        string title;
        string explanation;

        if (Lengths.Contains(name) && (!isCall || arguments.Length == 0) && !(name == "count" && type is "str" or "list" or "tuple"))
        {
            replacement = $"len({receiver})";
            title = $"Use len({receiver})";
            explanation = $"A Python {type} has no `{name}`. The length of anything is `len(...)`.";
        }
        else if (isCall && arguments.Length > 0 &&
                 ((type == "dict" && name is "has_key" or "containsKey" or "has") ||
                  (type != "dict" && name is "contains" or "includes")))
        {
            replacement = $"{arguments} in {receiver}";
            title = $"Test membership with in";
            explanation = $"Python tests whether something is in a {type} with the `in` operator, not a method.";
        }
        else if (name == "equals" && isCall && arguments.Length > 0)
        {
            replacement = $"{receiver} == {arguments}";
            title = "Compare with ==";
            explanation = "Python compares values with `==`; there is no `equals` method.";
        }
        else if (name == "toString" && isCall && arguments.Length == 0)
        {
            replacement = $"str({receiver})";
            title = $"Use str({receiver})";
            explanation = "Python turns a value into text with `str(...)`.";
        }
        else
        {
            return null;
        }

        if (replacement.Contains(" in ", StringComparison.Ordinal) || replacement.Contains(" == ", StringComparison.Ordinal))
        {
            if (PythonCode.NeedsParentheses(line, start, spanEnd)) replacement = $"({replacement})";
        }

        return LocalFix.ReplaceLine(Id, title, explanation, source.Path, number, line[..start] + replacement + line[spanEnd..]);
    }
}
