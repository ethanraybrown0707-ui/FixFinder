using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>Where Python's underline starts on a line, read from the traceback.</summary>
/// <remarks>
/// Python 3.11 and later put <c>^</c> and <c>~</c> under the expression that failed. When a name
/// appears twice on a line, that is what says which one Python meant - and inside an f-string,
/// where a masked line hides the name entirely, it is the only thing that can find it.
/// </remarks>
internal static partial class PythonUnderline
{
    [GeneratedRegex(@"^\s+File\s+"".+"",\s+line\s+(?<line>\d+)")]
    private static partial Regex FileLine();

    [GeneratedRegex(@"^\s*[~^]+\s*$")]
    private static partial Regex Underline();

    /// <summary>The column in the file's line where the underline for that line begins, or null.</summary>
    public static int? Column(ParsedError error, int number, string fileLine)
    {
        var raw = error.RawText.Split('\n').Select(text => text.TrimEnd('\r')).ToList();
        var at = raw.FindLastIndex(text => FileLine().Match(text) is { Success: true } m && m.Groups["line"].Value == number.ToString());

        if (at < 0 || at + 2 >= raw.Count || !Underline().IsMatch(raw[at + 2])) return null;

        var echo = raw[at + 1];
        var echoed = echo.Trim();
        var offset = fileLine.IndexOf(echoed, StringComparison.Ordinal);

        if (echoed.Length == 0 || offset < 0) return null;

        var underline = raw[at + 2];
        var start = underline.Length - underline.TrimStart().Length;
        var echoStart = echo.Length - echo.TrimStart().Length;

        return offset + (start - echoStart);
    }
}

/// <summary>
/// A name Python could not find, and did not suggest a replacement for: <c>name 'nmae' is not
/// defined</c>, or <c>'Dog' object has no attribute 'nmae'</c> on a class in this file.
/// </summary>
/// <remarks>
/// Python suggests names itself, but it scores a swap of two letters as two changes, so
/// <c>nmae</c> and <c>pritn</c> - the commonest typos there are - get no suggestion at all. The
/// candidates here are the names this file binds, and Python's builtins; for an attribute, what the
/// class defines. A tie is a refusal, as everywhere. The check can only prove the file still parses,
/// and the answer says so.
/// </remarks>
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

    /// <summary>
    /// The line with the one occurrence replaced: the only one in the code, or the one Python underlined.
    /// </summary>
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
            // Several, or none because the name sits inside an f-string the mask blanked: only the
            // underline can say which, and it has to land exactly on the name.
            if (PythonUnderline.Column(error, number, line) is not { } column) return null;

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

    /// <summary>What a class in this file defines: its methods, its class attributes, and what it assigns to self.</summary>
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

/// <summary><c>else if</c>, which Python spells <c>elif</c>.</summary>
public sealed partial class PythonElseIf : ILocalFixRule
{
    public string Id => "python-else-if";

    [GeneratedRegex(@"^(?<indent>\s*)else\s*if\b")]
    private static partial Regex ElseIf();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "python", ExceptionType: "SyntaxError" }) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (source.Line(number) is not { } line || ElseIf().Match(line) is not { Success: true } match) return null;

        return LocalFix.ReplaceLine(
            Id, "Write else if as elif",
            "Python has no `else if`. The word for \"otherwise, if\" is `elif`.",
            source.Path, number, match.Groups["indent"].Value + "elif" + line[match.Length..]);
    }
}

/// <summary><c>else age &lt; 18:</c> - a condition on an else, which is what elif is for.</summary>
public sealed partial class PythonElseWithCondition : ILocalFixRule
{
    public string Id => "python-else-with-condition";

    [GeneratedRegex(@"^(?<indent>\s*)else\s+(?!if\b)(?<condition>[^:]+?)\s*:?\s*$")]
    private static partial Regex ElseCondition();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "python", ExceptionType: "SyntaxError" }) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (source.Line(number) is not { } line) return null;

        var (code, tail) = CodeText.SplitComment(line, Syntax.Python);
        if (ElseCondition().Match(code) is not { Success: true } match) return null;

        var condition = match.Groups["condition"].Value;

        return LocalFix.ReplaceLine(
            Id, "Use elif for an else with a condition",
            $"`else` never takes a condition - it runs when nothing above it did. A branch that runs when `{condition}` " +
            "is `elif`.",
            source.Path, number, $"{match.Groups["indent"].Value}elif {condition}:{tail}");
    }
}

/// <summary><c>=&gt;</c>, <c>=&lt;</c> and <c>&lt;&gt;</c>, which Python writes <c>&gt;=</c>, <c>&lt;=</c> and <c>!=</c>.</summary>
public sealed partial class PythonArrowOperator : ILocalFixRule
{
    public string Id => "python-comparison-operator";

    [GeneratedRegex(@"(?<![=<>!])(?<op>=>|=<|<>)(?![=<>])")]
    private static partial Regex Operator();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "python", ExceptionType: "SyntaxError" }) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (source.Line(number) is not { } line) return null;

        var hits = Operator().Matches(CodeText.Mask(line, Syntax.Python));
        if (hits.Count != 1) return null;

        var op = hits[0].Groups["op"];
        var right = op.Value switch { "=>" => ">=", "=<" => "<=", _ => "!=" };

        return LocalFix.ReplaceLine(
            Id, $"Write {op.Value} as {right}",
            $"Python spells this comparison `{right}`. The equals sign always comes second.",
            source.Path, number, line[..op.Index] + right + line[(op.Index + op.Length)..]);
    }
}

/// <summary>A <c>//</c> comment, from JavaScript, C or Java, where Python uses <c>#</c>.</summary>
public sealed partial class PythonSlashComment : ILocalFixRule
{
    public string Id => "python-slash-comment";

    [GeneratedRegex(@"^(?<indent>\s*)//\s?")]
    private static partial Regex Slashes();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "python", ExceptionType: "SyntaxError" }) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (source.Line(number) is not { } line || Slashes().Match(line) is not { Success: true } match) return null;

        return LocalFix.ReplaceLine(
            Id, "Start the comment with #",
            "In Python a comment starts with `#`. `//` is whole-number division, so Python tried to read the comment as code.",
            source.Path, number, match.Groups["indent"].Value + "# " + line[match.Length..]);
    }
}

/// <summary><c>lambda x: return x * x</c> - a lambda is already the value it returns.</summary>
public sealed partial class PythonLambdaReturn : ILocalFixRule
{
    public string Id => "python-lambda-return";

    [GeneratedRegex(@"\blambda\b[^:]*:\s*(?<return>return\s+)")]
    private static partial Regex LambdaReturn();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "python", ExceptionType: "SyntaxError" }) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (source.Line(number) is not { } line) return null;

        var hits = LambdaReturn().Matches(CodeText.Mask(line, Syntax.Python));
        if (hits.Count != 1) return null;

        var keyword = hits[0].Groups["return"];

        return LocalFix.ReplaceLine(
            Id, "Take return out of the lambda",
            "A lambda is a single expression, and its value is what it returns - so it never contains `return`.",
            source.Path, number, line[..keyword.Index] + line[(keyword.Index + keyword.Length)..]);
    }
}

/// <summary><c>'[' was never closed</c>, on a line that plainly ends where the bracket should.</summary>
/// <remarks>
/// Only when the line is complete apart from the bracket. A list spread over several lines that is
/// missing its closer at the bottom would take a <c>]</c> on its first line and leave the rest as
/// stray expressions that still parse - so a following line indented further, or a line ending in
/// a comma or an operator, is a refusal.
/// </remarks>
public sealed partial class PythonUnclosedBracket : ILocalFixRule
{
    public string Id => "python-unclosed-bracket";

    [GeneratedRegex(@"^'(?<open>[\[({])' was never closed$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "python", ExceptionType: "SyntaxError" } error) return null;
        if (Message().Match(error.Message ?? "") is not { Success: true } message) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (source.Line(number) is not { } line) return null;

        var open = message.Groups["open"].Value[0];
        var close = open switch { '[' => ']', '(' => ')', _ => '}' };

        var (code, tail) = CodeText.SplitComment(line, Syntax.Python);
        var masked = CodeText.Mask(code, Syntax.Python);

        if (masked.Count(c => c == open) - masked.Count(c => c == close) != 1) return null;
        if (code.Length == 0 || ",+-*/%=([{\\".Contains(code[^1]) || code.TrimEnd().EndsWith(open)) return null;

        for (var k = number; k < source.Count; k++)
        {
            if (source.Lines[k].Trim().Length == 0) continue;
            if (CodeText.Indentation(source.Lines[k]).Length > CodeText.Indentation(line).Length) return null;
            break;
        }

        var corrected = code.EndsWith(':') ? code[..^1] + close + ":" : code + close;

        return LocalFix.ReplaceLine(
            Id, $"Close the {open} on line {number}",
            $"The `{open}` on line {number} is never closed. The line is otherwise complete, so the `{close}` goes at its end.",
            source.Path, number, corrected + tail);
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

/// <summary><c>super.__init__()</c> - super has to be called before anything can be looked up on it.</summary>
public sealed partial class PythonSuperCall : ILocalFixRule
{
    public string Id => "python-super-call";

    [GeneratedRegex(@"^descriptor '\w+' (?:of|for) 'super' object needs an argument$")]
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

/// <summary><c>No module named 'maths'</c>, when the file plainly means the standard <c>math</c>.</summary>
public sealed partial class PythonStdlibModuleTypo : ILocalFixRule
{
    public string Id => "python-stdlib-module-typo";

    [GeneratedRegex(@"^No module named '(?<name>[A-Za-z_]\w*)'$")]
    private static partial Regex Message();

    /// <summary>How far apart the import and its last use may be before a single change stops being sensible.</summary>
    private const int MaxSpan = 25;

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "python", ExceptionType: "ModuleNotFoundError" } error) return null;
        if (Message().Match(error.Message ?? "") is not { Success: true } message) return null;
        if (context.Read(context.Frame?.File) is not { } source) return null;

        var wrong = message.Groups["name"].Value;
        if (PythonStdlib.TypoOf(context.PythonInterpreter, wrong, source.Lines) is not { } right) return null;

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

        var used = PythonStdlib.UsedNames(wrong, source.Lines);

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
