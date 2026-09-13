using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>What the Python construct rules share: finding the line, and reading an expression back from a dot or a bracket.</summary>
internal static partial class Py
{
    public static bool Is(LocalFixContext context, string type) =>
        context.Error.LanguageId == "python" && context.Error.ExceptionType == type;

    /// <summary>The file, the 1-based line number and the text of the line the error happened on.</summary>
    public static (SourceFile Source, int Number, string Line)? Locate(LocalFixContext context)
    {
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        return source.Line(number) is { } line ? (source, number, line) : null;
    }

    /// <summary>The index of the bracket that opens the one closing at <paramref name="close"/>, or -1.</summary>
    public static int MatchBack(string text, int close)
    {
        var depth = 0;

        for (var i = close; i >= 0; i--)
        {
            if (text[i] is ')' or ']' or '}') depth++;
            else if (text[i] is '(' or '[' or '{' && --depth == 0) return i;
        }

        return -1;
    }

    /// <summary>The index of the bracket that closes the one opening at <paramref name="open"/>, or -1.</summary>
    public static int MatchForward(string text, int open)
    {
        var depth = 0;

        for (var i = open; i < text.Length; i++)
        {
            if (text[i] is '(' or '[' or '{') depth++;
            else if (text[i] is ')' or ']' or '}' && --depth == 0) return i;
        }

        return -1;
    }

    /// <summary>
    /// Where the expression ending just before a dot starts: a name, a dotted chain, a call or a
    /// subscript, or a string literal. -1 when there is nothing recognisable there.
    /// </summary>
    public static int ReceiverStart(string masked, int dot)
    {
        var i = dot - 1;

        while (i >= 0)
        {
            var c = masked[i];

            if (c is ')' or ']')
            {
                i = MatchBack(masked, i);
                if (i < 0) return -1;
                i--;
                continue;
            }

            if (c is '"' or '\'')
            {
                var open = i > 0 ? masked.LastIndexOf(c, i - 1) : -1;
                if (open < 0) return -1;
                i = open - 1;
                continue;
            }

            if (CodeText.IsWordChar(c) || c == '.')
            {
                i--;
                continue;
            }

            break;
        }

        return i + 1 < dot ? i + 1 : -1;
    }

    private static readonly string[] BareBefore = ["if", "elif", "while", "return", "=", "(", ",", "not", "and", "or", "["];
    private static readonly string[] BareAfter = [":", ")", ",", "]", "and ", "or ", "if ", "else "];

    /// <summary>
    /// True when an <c>in</c> or <c>==</c> expression put in place of this span needs brackets to
    /// keep meaning what it meant - anywhere except alone in a condition, an assignment or an argument.
    /// </summary>
    public static bool NeedsParentheses(string line, int start, int end)
    {
        var before = line[..start].TrimEnd();
        var after = line[end..].TrimStart();

        var bareBefore = before.Length == 0 || BareBefore.Any(token => before.EndsWith(token, StringComparison.Ordinal));
        var bareAfter = after.Length == 0 || after.StartsWith('#') || BareAfter.Any(token => after.StartsWith(token, StringComparison.Ordinal));

        return !(bareBefore && bareAfter);
    }

    [GeneratedRegex(@"^\s+File\s+"".+"",\s+line\s+(?<line>\d+)")]
    private static partial Regex FileLine();

    [GeneratedRegex(@"^\s*[~^]+\s*$")]
    private static partial Regex Underline();

    /// <summary>The stretch of the line Python underlined, as start and end columns, or null.</summary>
    public static (int Start, int End)? UnderlineSpan(ParsedError error, int number, string line)
    {
        var raw = error.RawText.Split('\n').Select(text => text.TrimEnd('\r')).ToList();
        var at = raw.FindLastIndex(text => FileLine().Match(text) is { Success: true } m && m.Groups["line"].Value == number.ToString());

        if (at < 0 || at + 2 >= raw.Count || !Underline().IsMatch(raw[at + 2])) return null;

        var echo = raw[at + 1];
        var echoed = echo.Trim();
        var offset = line.IndexOf(echoed, StringComparison.Ordinal);

        if (echoed.Length == 0 || offset < 0) return null;

        var underline = raw[at + 2].TrimEnd();
        var first = underline.Length - underline.TrimStart().Length;
        var echoStart = echo.Length - echo.TrimStart().Length;

        var start = offset + (first - echoStart);
        var end = offset + (underline.Length - echoStart);

        return start >= 0 && end <= line.Length && end > start ? (start, end) : null;
    }

    [GeneratedRegex(@"^-?\d+(?<fraction>\.\d*)?$")]
    public static partial Regex Number();

    [GeneratedRegex(@"^[rRbBuUfF]{0,2}[""']")]
    public static partial Regex StringLiteral();
}

// ======================================================================= habits from other languages

/// <summary><c>this.name</c> in a method, where Python's word is <c>self</c>.</summary>
/// <remarks>
/// Python's own hint for this one is <c>Did you forget to import 'this'?</c>, because a standard
/// module called <c>this</c> exists - it prints the Zen of Python. Following the hint would add a
/// joke import and change nothing, so this rule runs before the one that reads import hints.
/// </remarks>
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
        if (!Py.Is(context, "NameError") || context.Error.Message?.StartsWith("name 'this' is not defined", StringComparison.Ordinal) != true) return null;
        if (Py.Locate(context) is not { } at) return null;

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
        if (!Py.Is(context, "NameError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (Py.Locate(context) is not { } at) return null;

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

/// <summary>
/// Syntax carried over from JavaScript, Java or C: <c>&amp;&amp;</c>, <c>||</c>, <c>!</c>,
/// <c>x++</c>, <c>let</c>/<c>var</c>/<c>const</c>, <c>new</c> and <c>catch</c>.
/// </summary>
/// <remarks>
/// Every one of these is a plain token swap with one Python spelling, and they often come together
/// on one line - <c>if (x &gt; 1 &amp;&amp; !done)</c> - so everything recognised on the line is
/// rewritten at once. Inside strings and comments nothing is touched.
/// </remarks>
public sealed class PythonForeignSyntax : ILocalFixRule
{
    public string Id => "python-foreign-syntax";

    private sealed record Change(Regex Pattern, Func<Match, string> Replacement, string Explanation);

    private static readonly Change[] Changes =
    [
        new(new Regex(@"\s*&&\s*"), _ => " and ", "`&&` is `and`"),
        new(new Regex(@"\s*\|\|\s*"), _ => " or ", "`||` is `or`"),
        new(new Regex(@"(?<![!=<>])!(?!=)\s*(?=[A-Za-z_(])"), _ => "not ", "`!` is `not`"),
        new(new Regex(@"^(?<indent>\s*)(?<target>[A-Za-z_][\w.]*(?:\[[^\]]*\])?)\s*\+\+\s*$"),
            m => $"{m.Groups["indent"].Value}{m.Groups["target"].Value} += 1", "there is no `++` - `+= 1` adds one"),
        new(new Regex(@"^(?<indent>\s*)(?<target>[A-Za-z_][\w.]*(?:\[[^\]]*\])?)\s*--\s*$"),
            m => $"{m.Groups["indent"].Value}{m.Groups["target"].Value} -= 1", "there is no `--` - `-= 1` takes one away"),
        new(new Regex(@"^(?<indent>\s*)(?:let|var|const)\s+(?=[A-Za-z_])"),
            m => m.Groups["indent"].Value, "a variable needs no `let`, `var` or `const` - assigning to it is enough"),
        new(new Regex(@"(?<![\w.])new\s+(?=[A-Za-z_][\w.]*\s*\()"), _ => "", "objects are made by calling the class, without `new`"),
        new(new Regex(@"^(?<indent>\s*)catch\b"), m => m.Groups["indent"].Value + "except", "the handler after `try` is `except`, not `catch`"),
    ];

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "SyntaxError") || Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var (code, tail) = CodeText.SplitComment(line, Syntax.Python);
        var applied = new List<string>();

        foreach (var change in Changes)
        {
            var hits = change.Pattern.Matches(CodeText.Mask(code, Syntax.Python)).Reverse().ToList();
            if (hits.Count == 0) continue;

            foreach (var hit in hits) code = code[..hit.Index] + change.Replacement(hit) + code[(hit.Index + hit.Length)..];

            applied.Add(change.Explanation);
        }

        if (applied.Count == 0) return null;

        return LocalFix.ReplaceLine(
            Id, "Write this line the way Python writes it",
            $"This is how another language says it. In Python, {string.Join("; ", applied)}.",
            source.Path, number, code + tail);
    }
}

/// <summary><c>except ValueError, e:</c> - Python 2's way of naming the exception.</summary>
public sealed partial class PythonExceptComma : ILocalFixRule
{
    public string Id => "python-except-comma";

    [GeneratedRegex(@"^(?<head>\s*except\s+)(?<type>[\w.]+)\s*,\s*(?<name>[A-Za-z_]\w*)\s*:(?<rest>.*)$")]
    private static partial Regex ExceptComma();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "SyntaxError") || context.Error.Message != "multiple exception types must be parenthesized") return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (ExceptComma().Match(line) is not { Success: true } match) return null;

        var type = match.Groups["type"].Value;
        var name = match.Groups["name"].Value;

        // `except ValueError, TypeError:` meant two types; `except ValueError, e:` meant a name.
        var isType = char.IsUpper(name[0]) || name.EndsWith("Error", StringComparison.Ordinal) || name.EndsWith("Exception", StringComparison.Ordinal);

        var corrected = isType
            ? $"{match.Groups["head"].Value}({type}, {name}):{match.Groups["rest"].Value}"
            : $"{match.Groups["head"].Value}{type} as {name}:{match.Groups["rest"].Value}";

        return LocalFix.ReplaceLine(
            Id, isType ? "Put the exception types in brackets" : $"Name the exception with as {name}",
            isType
                ? "Catching more than one exception type needs them in brackets, as a tuple."
                : $"`except {type}, {name}:` is Python 2. Python 3 names the caught exception with `as`.",
            source.Path, number, corrected);
    }
}

/// <summary><c>raise "something went wrong"</c> - only an exception can be raised, not a string.</summary>
public sealed partial class PythonRaiseString : ILocalFixRule
{
    public string Id => "python-raise-string";

    [GeneratedRegex(@"^(?<indent>\s*)raise\s+(?<value>[rRbBuUfF]{0,2}(?<q>[""']).*\k<q>)$")]
    private static partial Regex RaiseString();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "TypeError") || context.Error.Message != "exceptions must derive from BaseException") return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var (code, tail) = CodeText.SplitComment(line, Syntax.Python);

        if (RaiseString().Match(code) is not { Success: true } match) return null;

        return LocalFix.ReplaceLine(
            Id, "Raise an Exception carrying the message",
            "`raise` needs an exception object. A string on its own was allowed in very old Python; now the message goes inside one.",
            source.Path, number, $"{match.Groups["indent"].Value}raise Exception({match.Groups["value"].Value}){tail}");
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
        if (!Py.Is(context, "NameError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (Py.Locate(context) is not { } at) return null;

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

/// <summary><c>print &gt;&gt;sys.stderr, "oops"</c> - Python 2's way of printing somewhere else.</summary>
public sealed partial class PythonPrintRedirect : ILocalFixRule
{
    public string Id => "python-print-redirect";

    [GeneratedRegex(@"^(?<indent>\s*)print\s*>>\s*(?<stream>[\w.]+)\s*,\s*(?<args>.+?)$")]
    private static partial Regex Redirect();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "TypeError") ||
            context.Error.Message?.StartsWith("unsupported operand type(s) for >>: 'builtin_function_or_method'", StringComparison.Ordinal) != true)
            return null;

        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var (code, tail) = CodeText.SplitComment(line, Syntax.Python);

        if (Redirect().Match(code) is not { Success: true } match) return null;

        return LocalFix.ReplaceLine(
            Id, "Print to the stream with file=",
            "`print >>stream, text` is Python 2. In Python 3 print is a function, and where it writes is its `file` argument.",
            source.Path, number,
            $"{match.Groups["indent"].Value}print({match.Groups["args"].Value}, file={match.Groups["stream"].Value}){tail}");
    }
}

/// <summary>
/// Method names from other languages on Python's own types: <c>.length</c>, <c>.push</c>,
/// <c>.toUpperCase()</c>, <c>.equals()</c>, <c>.contains()</c>, and Python 2's <c>has_key</c>.
/// </summary>
/// <remarks>
/// Only for the built-in types, where what exists is fixed and known, and only when Python itself
/// did not suggest a name. Some are renames - <c>push</c> is <c>append</c> - and some are a
/// different shape entirely: a length is <c>len(x)</c>, and membership is <c>item in x</c>.
/// </remarks>
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
        if (!Py.Is(context, "AttributeError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var type = message.Groups["type"].Value;
        var name = message.Groups["name"].Value;

        var masked = CodeText.Mask(line, Syntax.Python);
        var hits = Regex.Matches(masked, $@"\.{Regex.Escape(name)}(?!\w)");
        if (hits.Count != 1) return null;

        var dot = hits[0].Index;
        var nameEnd = dot + 1 + name.Length;
        var isCall = nameEnd < masked.Length && masked[nameEnd] == '(';
        var close = isCall ? Py.MatchForward(masked, nameEnd) : -1;

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

        var start = Py.ReceiverStart(masked, dot);
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
            if (Py.NeedsParentheses(line, start, spanEnd)) replacement = $"({replacement})";
        }

        return LocalFix.ReplaceLine(Id, title, explanation, source.Path, number, line[..start] + replacement + line[spanEnd..]);
    }
}

// ======================================================================= classes and functions

/// <summary><c>Dog.bark() takes 0 positional arguments but 1 was given</c> - a method written without self.</summary>
public sealed partial class PythonMissingSelf : ILocalFixRule
{
    public string Id => "python-missing-self";

    [GeneratedRegex(@"^(?<cls>[A-Za-z_]\w*)\.(?<method>[A-Za-z_]\w*)\(\) takes (?<n>\d+) positional arguments? but (?<m>\d+) (?:were|was) given$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "TypeError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
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
        var close = Py.MatchForward(masked[index], paren);
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
        if (!Py.Is(context, "TypeError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
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
        if (expression.StartsWith("str(", StringComparison.Ordinal) || Py.StringLiteral().IsMatch(expression)) return null;

        return LocalFix.ReplaceLine(
            Id, $"Return text from __{method}__",
            $"`__{method}__` is how Python turns the object into text, so it must return a str. `str(...)` makes the text form of what it returned.",
            source.Path, returns[0] + 1, $"{match.Groups["lead"].Value}str({expression}){tail}");
    }
}

/// <summary><c>def greet:</c> - a function needs its brackets even when it takes nothing.</summary>
public sealed partial class PythonDefWithoutParentheses : ILocalFixRule
{
    public string Id => "python-def-without-parentheses";

    [GeneratedRegex(@"^(?<head>\s*(?:async\s+)?def\s+[A-Za-z_]\w*)\s*:(?<rest>.*)$")]
    private static partial Regex Def();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "SyntaxError") || context.Error.Message != "expected '('") return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (Def().Match(line) is not { Success: true } match) return null;

        return LocalFix.ReplaceLine(
            Id, "Add the brackets after the function name",
            "A function definition always has brackets for its parameters, even when there are none.",
            source.Path, number, $"{match.Groups["head"].Value}():{match.Groups["rest"].Value}");
    }
}

/// <summary><c>global count = 0</c> - global declares a name; it cannot assign in the same statement.</summary>
public sealed partial class PythonGlobalAssignment : ILocalFixRule
{
    public string Id => "python-global-assignment";

    [GeneratedRegex(@"^(?<indent>\s*)global\s+(?<name>[A-Za-z_]\w*)\s*=\s*(?<value>.+?)$")]
    private static partial Regex GlobalAssign();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "SyntaxError") || Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var (code, tail) = CodeText.SplitComment(line, Syntax.Python);

        if (GlobalAssign().Match(code) is not { Success: true } match) return null;

        var indent = match.Groups["indent"].Value;
        var name = match.Groups["name"].Value;
        var assignment = $"{indent}{name} = {match.Groups["value"].Value}{tail}";

        return new LocalFix
        {
            RuleId = Id,
            Title = indent.Length == 0 ? $"Assign {name} without global" : $"Declare {name} global, then assign it",
            Explanation = indent.Length == 0
                ? "At the top level of a file every name is already global, so `global` has nothing to do - and it can never assign."
                : "`global` only declares that the name is the module's; the assignment has to be its own statement.",
            File = source.Path,
            StartLine = number,
            RemoveCount = 1,
            NewLines = indent.Length == 0 ? [assignment] : [$"{indent}global {name}", assignment],
        };
    }
}

// ======================================================================= built-in types used the wrong way

/// <summary><c>for i in len(items):</c> - a number is not something to loop over; a range of it is.</summary>
public sealed partial class PythonRangeForInt : ILocalFixRule
{
    public string Id => "python-range-for-int";

    [GeneratedRegex(@"^(?<head>\s*(?:async\s+)?for\s+.+?\s+in\s+)(?<expr>.+?)(?<colon>\s*:)$")]
    private static partial Regex ForIn();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "TypeError") || context.Error.Message != "'int' object is not iterable") return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var (code, tail) = CodeText.SplitComment(line, Syntax.Python);

        if (ForIn().Match(code) is not { Success: true } match) return null;

        var expression = match.Groups["expr"].Value;
        if (expression.StartsWith("range(", StringComparison.Ordinal)) return null;

        return LocalFix.ReplaceLine(
            Id, $"Loop over range({expression})",
            $"`{expression}` is a number, and a for loop needs something to step through. `range({expression})` counts from 0 up to it.",
            source.Path, number, $"{match.Groups["head"].Value}range({expression}){match.Groups["colon"].Value}{tail}");
    }
}

/// <summary>A float where a whole number is needed, from <c>/</c> - which always gives a float in Python 3.</summary>
public sealed partial class PythonFloatDivision : ILocalFixRule
{
    public string Id => "python-float-division";

    [GeneratedRegex(@"^(?:(?:list|tuple|str|string|byte|bytes) indices must be integers.*float|'float' object cannot be interpreted as an integer|slice indices must be integers)")]
    private static partial Regex Message();

    [GeneratedRegex(@"(?<![/])/(?![/=])")]
    private static partial Regex Slash();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "TypeError") || !Message().IsMatch(context.Error.Message ?? "")) return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var hits = Slash().Matches(CodeText.Mask(line, Syntax.Python));
        if (hits.Count != 1) return null;

        return LocalFix.ReplaceLine(
            Id, "Divide with // for a whole number",
            "`/` always gives a float in Python 3, even for 10 / 2, and an index or a range needs a whole number. `//` divides and keeps it whole.",
            source.Path, number, line[..hits[0].Index] + "//" + line[(hits[0].Index + 1)..]);
    }
}

/// <summary><c>'&gt;' not supported between instances of 'str' and 'int'</c> - text compared with a number.</summary>
public sealed partial class PythonComparisonTypes : ILocalFixRule
{
    public string Id => "python-comparison-types";

    [GeneratedRegex(@"^'(?<op><=|>=|<|>)' not supported between instances of '(?<a>str|int|float)' and '(?<b>str|int|float)'$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "TypeError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var a = message.Groups["a"].Value;
        var b = message.Groups["b"].Value;
        if ((a == "str") == (b == "str")) return null;

        if (Py.UnderlineSpan(context.Error, number, line) is not { } span) return null;

        var text = line[span.Start..span.End];
        var op = Regex.Escape(message.Groups["op"].Value);
        var hits = Regex.Matches(CodeText.Mask(text, Syntax.Python), $@"(?<![<>=!]){op}(?![=<>])");
        if (hits.Count != 1) return null;

        var opIndex = hits[0].Index;
        var leftText = text[..opIndex];
        var rightText = text[(opIndex + hits[0].Length)..];
        var left = leftText.Trim();
        var right = rightText.Trim();

        if (left.Length == 0 || right.Length == 0) return null;

        var leftAt = span.Start + leftText.IndexOf(left, StringComparison.Ordinal);
        var rightAt = span.Start + opIndex + hits[0].Length + rightText.IndexOf(right, StringComparison.Ordinal);

        string corrected;
        string value;

        if (a == "str" && Py.Number().Match(right) is { Success: true } rightNumber && !Py.StringLiteral().IsMatch(left))
        {
            var convert = rightNumber.Groups["fraction"].Success ? "float" : "int";
            corrected = line[..leftAt] + $"{convert}({left})" + line[(leftAt + left.Length)..];
            value = left;
        }
        else if (b == "str" && Py.Number().Match(left) is { Success: true } leftNumber && !Py.StringLiteral().IsMatch(right))
        {
            var convert = leftNumber.Groups["fraction"].Success ? "float" : "int";
            corrected = line[..rightAt] + $"{convert}({right})" + line[(rightAt + right.Length)..];
            value = right;
        }
        else
        {
            return null;
        }

        return LocalFix.ReplaceLine(
            Id, $"Turn {value} into a number before comparing",
            $"`{value}` holds text - `input()` always returns text - and Python will not compare text with a number. " +
            "Converting it compares the numbers.",
            source.Path, number, corrected);
    }
}

/// <summary><c>", ".join(numbers)</c> with numbers in the list - join only joins text.</summary>
public sealed partial class PythonJoinNonStrings : ILocalFixRule
{
    public string Id => "python-join-non-strings";

    [GeneratedRegex(@"^sequence item \d+: expected str instance, \w+ found$")]
    private static partial Regex Message();

    [GeneratedRegex(@"\.join\(")]
    private static partial Regex Join();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "TypeError") || !Message().IsMatch(context.Error.Message ?? "")) return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.Python);
        var hits = Join().Matches(masked);
        if (hits.Count != 1) return null;

        var open = hits[0].Index + hits[0].Length - 1;
        var close = Py.MatchForward(masked, open);
        if (close < 0) return null;

        var argument = line[(open + 1)..close].Trim();
        if (argument.Length == 0 || argument.StartsWith("map(str", StringComparison.Ordinal)) return null;

        return LocalFix.ReplaceLine(
            Id, "Turn each item into text before joining",
            "`join` puts text together, and something in the list is not text. `map(str, ...)` converts each item first.",
            source.Path, number, line[..(open + 1)] + $"map(str, {argument})" + line[close..]);
    }
}

/// <summary><c>for n in numbers.sort():</c> - sort changes the list and returns nothing; sorted returns a new one.</summary>
public sealed partial class PythonSortedNotSort : ILocalFixRule
{
    public string Id => "python-sorted-not-sort";

    [GeneratedRegex(@"\.(?<method>sort|reverse)\(")]
    private static partial Regex InPlace();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "TypeError") || context.Error.Message != "'NoneType' object is not iterable") return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.Python);
        var hits = InPlace().Matches(masked);
        if (hits.Count != 1) return null;

        var dot = hits[0].Index;
        var open = dot + hits[0].Length - 1;
        var close = Py.MatchForward(masked, open);
        var start = Py.ReceiverStart(masked, dot);
        if (close < 0 || start < 0) return null;

        var receiver = line[start..dot];
        var arguments = line[(open + 1)..close].Trim();
        var method = hits[0].Groups["method"].Value;

        if (method == "reverse" && arguments.Length > 0) return null;

        var replacement = method == "sort"
            ? $"sorted({receiver}{(arguments.Length > 0 ? ", " + arguments : "")})"
            : $"reversed({receiver})";

        return LocalFix.ReplaceLine(
            Id, $"Use {(method == "sort" ? "sorted" : "reversed")}({receiver})",
            $"`{receiver}.{method}()` changes the list where it is and returns None, so there was nothing to loop over. " +
            $"`{(method == "sort" ? "sorted" : "reversed")}(...)` gives the result back instead.",
            source.Path, number, line[..start] + replacement + line[(close + 1)..]);
    }
}

/// <summary>
/// <c>numbers = numbers.append(3)</c>, found from the later line where numbers turned out to be None.
/// </summary>
public sealed partial class PythonInPlaceResult : ILocalFixRule
{
    public string Id => "python-in-place-result";

    [GeneratedRegex(@"^(?:'NoneType' object has no attribute '\w+'|'NoneType' object is not (?:iterable|subscriptable))$")]
    private static partial Regex Message();

    [GeneratedRegex(@"(?<![\w.])(?<name>[A-Za-z_]\w*)(?=\s*[.\[])|\bin\s+(?<name>[A-Za-z_]\w*)")]
    private static partial Regex Used();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.LanguageId != "python" || !Message().IsMatch(context.Error.Message ?? "")) return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.Python);

        var names = Used().Matches(masked[number - 1]).Select(m => m.Groups["name"].Value).Distinct().ToList();
        var found = new List<(int Line, string Replacement)>();

        foreach (var name in names)
        {
            var word = Regex.Escape(name);

            for (var k = number - 2; k >= 0; k--)
            {
                if (Regex.Match(masked[k], $@"^(?<indent>\s*){word}\s*=(?!=)\s*(?<rhs>.+?)\s*$") is not { Success: true } assignment) continue;

                if (Regex.IsMatch(assignment.Groups["rhs"].Value, $@"^{word}\.(?:append|extend|insert|sort|reverse|remove|clear|update|add|discard)\(.*\)$"))
                {
                    var rhs = assignment.Groups["rhs"];
                    found.Add((k, assignment.Groups["indent"].Value + source.Lines[k][rhs.Index..(rhs.Index + rhs.Length)] + source.Lines[k][(rhs.Index + rhs.Length)..]));
                }

                break;
            }
        }

        if (found.Count != 1) return null;

        var (index, replacement) = found[0];

        return LocalFix.ReplaceLine(
            Id, "Call it without assigning the result",
            $"Line {index + 1} changes the list where it is - and methods that do that return None, so assigning the result " +
            "replaced the list with None. Calling the method on its own keeps the list.",
            source.Path, index + 1, replacement);
    }
}

/// <summary><c>math.pi()</c> - a constant is a value, not a function.</summary>
public sealed partial class PythonCalledConstant : ILocalFixRule
{
    public string Id => "python-called-constant";

    [GeneratedRegex(@"(?<![\w.])(?:math|cmath)\.(?:pi|e|tau|inf|nan|infj|nanj)(?<call>\s*\(\s*\))")]
    private static partial Regex CalledConstant();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "TypeError") || context.Error.Message is not ("'float' object is not callable" or "'complex' object is not callable")) return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var hits = CalledConstant().Matches(CodeText.Mask(line, Syntax.Python));
        if (hits.Count != 1) return null;

        var call = hits[0].Groups["call"];

        return LocalFix.ReplaceLine(
            Id, "Use the constant without brackets",
            "A constant such as `math.pi` is a number. Brackets after it try to call the number as if it were a function.",
            source.Path, number, line[..call.Index] + line[(call.Index + call.Length)..]);
    }
}

/// <summary><c>isinstance(x, "int")</c> - the type itself, not its name in quotes.</summary>
public sealed partial class PythonIsinstanceString : ILocalFixRule
{
    public string Id => "python-isinstance-string";

    [GeneratedRegex(@"\b(?:isinstance|issubclass)\([^,()]+,\s*(?<quoted>(?<q>[""'])(?<type>int|str|float|bool|list|dict|tuple|set|bytes|complex)\k<q>)\s*\)")]
    private static partial Regex QuotedType();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "TypeError") || context.Error.Message?.Contains("arg 2 must be a type", StringComparison.Ordinal) != true) return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var hits = QuotedType().Matches(line);
        if (hits.Count != 1) return null;

        var quoted = hits[0].Groups["quoted"];
        var type = hits[0].Groups["type"].Value;

        return LocalFix.ReplaceLine(
            Id, $"Pass the type {type}, not the text \"{type}\"",
            $"`isinstance` checks against a type. `\"{type}\"` is only text that spells its name; `{type}` is the type.",
            source.Path, number, line[..quoted.Index] + type + line[(quoted.Index + quoted.Length)..]);
    }
}

/// <summary><c>dictionary changed size during iteration</c> - loop over a copy, and change the original freely.</summary>
public sealed partial class PythonChangedDuringIteration : ILocalFixRule
{
    public string Id => "python-changed-during-iteration";

    [GeneratedRegex(@"^(?:dictionary|set|deque) changed size during iteration$")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?<head>\s*for\s+.+?\s+in\s+)(?<expr>.+?)(?<colon>\s*:)$")]
    private static partial Regex ForIn();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "RuntimeError") || !Message().IsMatch(context.Error.Message ?? "")) return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var (code, tail) = CodeText.SplitComment(line, Syntax.Python);

        if (ForIn().Match(code) is not { Success: true } match) return null;

        var expression = match.Groups["expr"].Value;
        if (expression.StartsWith("list(", StringComparison.Ordinal)) return null;

        return LocalFix.ReplaceLine(
            Id, $"Loop over a copy: list({expression})",
            "The loop removes or adds entries in the very collection it is walking through, which Python refuses to keep " +
            "track of. Looping over a copy lets the original change.",
            source.Path, number, $"{match.Groups["head"].Value}list({expression}){match.Groups["colon"].Value}{tail}");
    }
}

// ======================================================================= imports

/// <summary><c>from .helpers import greet</c> in a script run directly, where there is no package to be relative to.</summary>
public sealed partial class PythonRelativeImportInScript : ILocalFixRule
{
    public string Id => "python-relative-import";

    [GeneratedRegex(@"^(?<head>\s*from\s+)\.(?<module>[A-Za-z_]\w*)(?<rest>\s+import\b.*)$")]
    private static partial Regex RelativeImport();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "ImportError") || context.Error.Message?.StartsWith("attempted relative import with no known parent package", StringComparison.Ordinal) != true) return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (RelativeImport().Match(line) is not { Success: true } match) return null;

        var module = match.Groups["module"].Value;

        // Only when the module is really there beside the script; otherwise dropping the dot names nothing.
        if (!File.Exists(Path.Combine(Path.GetDirectoryName(source.Path)!, module + ".py"))) return null;

        return LocalFix.ReplaceLine(
            Id, $"Import {module} without the dot",
            $"The dot means \"from this package\", and a script run on its own is not in one. `{module}.py` is right beside it, " +
            "so it can be imported by name.",
            source.Path, number, $"{match.Groups["head"].Value}{module}{match.Groups["rest"].Value}");
    }
}

/// <summary><c>import from math sqrt</c>, which Python writes <c>from math import sqrt</c>.</summary>
public sealed partial class PythonImportFromBackwards : ILocalFixRule
{
    public string Id => "python-import-from-backwards";

    [GeneratedRegex(@"^(?<indent>\s*)import\s+from\s+(?<module>[\w.]+)\s+(?<names>[\w\s,*]+?)$")]
    private static partial Regex Backwards();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "SyntaxError") || Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var (code, tail) = CodeText.SplitComment(line, Syntax.Python);

        if (Backwards().Match(code) is not { Success: true } match) return null;

        return LocalFix.ReplaceLine(
            Id, "Write it as from ... import ...",
            "Importing names out of a module is written `from module import names`.",
            source.Path, number, $"{match.Groups["indent"].Value}from {match.Groups["module"].Value} import {match.Groups["names"].Value}{tail}");
    }
}

// ======================================================================= strings, brackets and layout

/// <summary><c>f"Hello {name"</c> - an f-string with a brace that never closes.</summary>
public sealed partial class PythonFStringBrace : ILocalFixRule
{
    public string Id => "python-fstring-brace";

    [GeneratedRegex(@"(?<![\w])(?:[fF][rR]?|[rR][fF])(?<q>[""'])(?<body>[^""']*)\k<q>")]
    private static partial Regex FString();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "SyntaxError") || context.Error.Message != "f-string: expecting '}'") return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;

        var unclosed = FString().Matches(line).Where(m =>
        {
            var body = m.Groups["body"].Value.Replace("{{", "").Replace("}}", "");
            return body.Count(c => c == '{') == body.Count(c => c == '}') + 1 && body.LastIndexOf('{') > body.LastIndexOf('}');
        }).ToList();

        if (unclosed.Count != 1) return null;

        var body = unclosed[0].Groups["body"];
        var end = body.Index + body.Length;

        return LocalFix.ReplaceLine(
            Id, "Close the brace in the f-string",
            "Each `{` in an f-string opens a value to put in, and needs a `}` to end it.",
            source.Path, number, line[..end] + "}" + line[end..]);
    }
}

/// <summary><c>unterminated string literal</c> - a missing closing quote, or an apostrophe that ended the string early.</summary>
public sealed class PythonUnterminatedString : ILocalFixRule
{
    public string Id => "python-unterminated-string";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "SyntaxError") || context.Error.Message?.StartsWith("unterminated string literal", StringComparison.Ordinal) != true) return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var singles = line.Count(c => c == '\'');
        var doubles = line.Count(c => c == '"');

        // 'It's here' - the apostrophe ended the string. Double quotes around it let the apostrophe stay.
        if (singles == 3 && doubles == 0)
        {
            var first = line.IndexOf('\'');
            var last = line.LastIndexOf('\'');

            return LocalFix.ReplaceLine(
                Id, "Put the text in double quotes",
                "The apostrophe inside the text was read as the end of the string. Wrapping the text in double quotes lets it contain one.",
                source.Path, number, line[..first] + "\"" + line[(first + 1)..last] + "\"" + line[(last + 1)..]);
        }

        char quote;
        if (doubles == 1 && singles % 2 == 0) quote = '"';
        else if (singles == 1 && doubles % 2 == 0) quote = '\'';
        else return null;

        var at_ = line.IndexOf(quote);
        var before = line[..at_];
        var after = line[(at_ + 1)..].TrimEnd();
        var trailing = line[(at_ + 1 + after.Length)..];

        var maskedBefore = CodeText.Mask(before, Syntax.Python);
        var open = maskedBefore.Count(c => c is '(' or '[' or '{') - maskedBefore.Count(c => c is ')' or ']' or '}');

        var closers = 0;
        while (open > 0 && after.Length - closers > 0 && after[after.Length - 1 - closers] is ')' or ']' or '}')
        {
            closers++;
            open--;
        }

        var content = after[..^closers];
        if (content.Length == 0) return null;

        return LocalFix.ReplaceLine(
            Id, "Close the string",
            "The text starts with a quote that never ends. The closing quote goes after the text, before the brackets that close around it.",
            source.Path, number, before + quote + content + quote + after[^closers..] + trailing);
    }
}

/// <summary><c>unmatched ')'</c> - one closing bracket too many, at the end of the line.</summary>
public sealed partial class PythonUnmatchedClosing : ILocalFixRule
{
    public string Id => "python-unmatched-closing";

    [GeneratedRegex(@"^unmatched '(?<close>[)\]}])'$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "SyntaxError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var close = message.Groups["close"].Value[0];
        var open = close switch { ')' => '(', ']' => '[', _ => '{' };

        var (code, tail) = CodeText.SplitComment(line, Syntax.Python);
        var masked = CodeText.Mask(code, Syntax.Python);

        if (!code.EndsWith(close) || masked.Count(c => c == close) - masked.Count(c => c == open) != 1) return null;

        return LocalFix.ReplaceLine(
            Id, $"Remove the extra {close}",
            $"There is one more `{close}` than `{open}` on this line, and the last one closes nothing.",
            source.Path, number, code[..^1] + tail);
    }
}

/// <summary><c>unexpected indent</c> - a line, or a run of lines, indented further than anything opened.</summary>
public sealed class PythonUnexpectedIndent : ILocalFixRule
{
    public string Id => "python-unexpected-indent";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "IndentationError") || context.Error.Message != "unexpected indent") return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var previous = number - 2;
        while (previous >= 0 && source.Lines[previous].Trim().Length == 0) previous--;

        var target = previous >= 0 ? CodeText.Indentation(source.Lines[previous]) : "";

        if (previous >= 0 && CodeText.SplitComment(source.Lines[previous], Syntax.Python).Code.EndsWith(':')) return null;

        var current = CodeText.Indentation(line);
        if (!current.StartsWith(target, StringComparison.Ordinal) || current.Length <= target.Length) return null;

        var extra = current.Length - target.Length;
        var end = number - 1;

        while (end + 1 < source.Count)
        {
            var next = source.Lines[end + 1];
            if (next.Trim().Length > 0 && !CodeText.Indentation(next).StartsWith(current, StringComparison.Ordinal)) break;
            end++;
        }

        while (end > number - 1 && source.Lines[end].Trim().Length == 0) end--;

        var lines = source.Lines.Skip(number - 1).Take(end - number + 2)
            .Select(text => text.Trim().Length == 0 ? text : text[extra..])
            .ToList();

        return new LocalFix
        {
            RuleId = Id,
            Title = lines.Count == 1 ? $"Line up line {number} with the line before" : $"Line up lines {number}-{end + 1} with the line before",
            Explanation = "Indentation means \"inside the block the line above opened\", and the line above opens no block, so these lines go back level with it.",
            File = source.Path,
            StartLine = number,
            RemoveCount = lines.Count,
            NewLines = lines,
        };
    }
}

/// <summary><c>unindent does not match any outer indentation level</c> - a line dedented to a level nothing is at.</summary>
public sealed class PythonUnindentMismatch : ILocalFixRule
{
    public string Id => "python-unindent-mismatch";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "IndentationError") || context.Error.Message != "unindent does not match any outer indentation level") return null;
        if (Py.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var width = CodeText.Indentation(line).Length;

        string? outer = null;

        for (var k = number - 2; k >= 0; k--)
        {
            var text = source.Lines[k];
            if (text.Trim().Length == 0) continue;

            var indentation = CodeText.Indentation(text);
            if (indentation.Length < width && (outer is null || indentation.Length > outer.Length)) outer = indentation;
        }

        if (outer is null) return null;

        var end = number - 1;
        while (end + 1 < source.Count && source.Lines[end + 1].Trim().Length > 0 && CodeText.Indentation(source.Lines[end + 1]).Length == width) end++;

        var lines = source.Lines.Skip(number - 1).Take(end - number + 2).Select(text => outer + text.TrimStart()).ToList();

        return new LocalFix
        {
            RuleId = Id,
            Title = $"Line up line {number} with the block it belongs to",
            Explanation = $"Line {number} is indented {width} spaces, which matches no block above it. The nearest level it could be closing back to is {outer.Length}.",
            File = source.Path,
            StartLine = number,
            RemoveCount = lines.Count,
            NewLines = lines,
        };
    }
}

/// <summary><c>inconsistent use of tabs and spaces</c> - tabs turned into the spaces the rest of the file uses.</summary>
public sealed class PythonTabsAndSpaces : ILocalFixRule
{
    public string Id => "python-tabs-and-spaces";

    private const int MaxSpan = 80;

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Py.Is(context, "TabError") || context.Read(context.Frame?.File) is not { } source) return null;

        var widths = source.Lines
            .Where(text => text.Trim().Length > 0)
            .Select(CodeText.Indentation)
            .Where(indentation => indentation.Length > 0 && !indentation.Contains('\t'))
            .Select(indentation => indentation.Length)
            .ToList();

        var unit = new string(' ', widths.Count > 0 ? widths.Min() : 4);

        var tabbed = Enumerable.Range(0, source.Count).Where(i => CodeText.Indentation(source.Lines[i]).Contains('\t')).ToList();
        if (tabbed.Count == 0 || tabbed[^1] - tabbed[0] > MaxSpan) return null;

        var lines = Enumerable.Range(tabbed[0], tabbed[^1] - tabbed[0] + 1).Select(i =>
        {
            var text = source.Lines[i];
            var indentation = CodeText.Indentation(text);
            return indentation.Replace("\t", unit) + text[indentation.Length..];
        }).ToList();

        return new LocalFix
        {
            RuleId = Id,
            Title = "Indent with spaces throughout",
            Explanation = $"Some lines are indented with tabs and others with spaces, and Python will not guess how wide a tab is. Each tab becomes {unit.Length} spaces, as the rest of the file indents.",
            File = source.Path,
            StartLine = tabbed[0] + 1,
            RemoveCount = lines.Count,
            NewLines = lines,
        };
    }
}
