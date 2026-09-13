using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

// ======================================================================= inheritance and interfaces

/// <summary><c>area() in Circle cannot implement area() in Shape</c> - <c>attempting to assign weaker access privileges; was public</c>.</summary>
public sealed partial class JavaWeakerAccess : ILocalFixRule
{
    public string Id => "java-weaker-access";

    [GeneratedRegex(@"^(?<method>[\w$]+)\([^)]*\) in [\w$.]+ cannot (?:implement|override) [\w$]+\([^)]*\) in (?<super>[\w$.]+)$")]
    private static partial Regex Message();

    [GeneratedRegex(@"weaker access privileges; was (?<access>public|protected)")]
    private static partial Regex Was();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (context.Output.Select(line => Was().Match(line.Text)).FirstOrDefault(m => m.Success) is not { } was) return null;

        var (source, number, line) = at;
        var method = message.Groups["method"].Value;
        var declaration = Regex.Match(line, $@"^(?<lead>\s*)(?<access>(?:private|protected|public)\s+)?(?:(?:static|final|abstract|synchronized)\s+)*[\w<>\[\],.? ]+\s+{Regex.Escape(method)}\s*\(");
        if (!declaration.Success) return null;

        var access = was.Groups["access"].Value;
        var current = declaration.Groups["access"];
        var lead = declaration.Groups["lead"].Length;

        var corrected = current.Success
            ? line[..current.Index] + access + " " + line[(current.Index + current.Length)..]
            : line[..lead] + access + " " + line[lead..];

        return LocalFix.ReplaceLine(
            Id, $"Make {method} {access}",
            $"`{method}` comes from `{message.Groups["super"].Value}`, where it is {access}. An implementation can make a method more visible " +
            $"but never less - and with no modifier it is visible only inside its package - so it has to be {access} here as well.",
            source.Path, number, corrected);
    }
}

/// <summary><c>method does not override or implement a method from a supertype</c> - a misspelt <c>@Override</c>.</summary>
public sealed partial class JavaOverrideTypo : ILocalFixRule
{
    public string Id => "java-override-typo";

    [GeneratedRegex(@"^\s*(?:(?:public|protected|private|static|final|synchronized|abstract|default)\s+)*(?:<[^>]+>\s+)?[\w$<>\[\],.? ]+?\s+(?<name>[\w$]+)\s*\(")]
    private static partial Regex Method();

    [GeneratedRegex(@"\b(?:class|record|enum|interface)\s+(?<name>[\w$]+)(?:<[^>]*>)?(?<rest>[^{]*)")]
    private static partial Regex TypeDeclaration();

    [GeneratedRegex(@"^\s*@\w+(?:\([^)]*\))?\s*$")]
    private static partial Regex Annotation();

    private static readonly string[] ObjectMethods = ["toString", "equals", "hashCode", "clone", "finalize"];

    private static readonly Dictionary<string, string[]> JdkMethods = new(StringComparer.Ordinal)
    {
        ["Runnable"] = ["run"], ["Thread"] = ["run"], ["Comparable"] = ["compareTo"], ["Comparator"] = ["compare"],
        ["Iterable"] = ["iterator"], ["Iterator"] = ["hasNext", "next", "remove"], ["AutoCloseable"] = ["close"], ["Closeable"] = ["close"],
        ["Callable"] = ["call"], ["Supplier"] = ["get"], ["Consumer"] = ["accept"], ["Function"] = ["apply"], ["Predicate"] = ["test"],
    };

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.Message != "method does not override or implement a method from a supertype" || JavaCode.Locate(context) is not { } at) return null;

        var (source, number, _) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        // javac points at the @Override; the method is the first line after it that is not another annotation.
        var k = number - 1;
        while (k < masked.Count && (masked[k].Trim().Length == 0 || Annotation().IsMatch(masked[k]))) k++;
        if (k >= masked.Count || Method().Match(masked[k]) is not { Success: true } method) return null;

        var depths = CCode.DepthAtStart(masked);
        var owner = Enumerable.Range(0, k).Reverse().FirstOrDefault(i => depths[i] < depths[k] && TypeDeclaration().IsMatch(masked[i]), -1);
        if (owner < 0) return null;

        var candidates = new HashSet<string>(ObjectMethods, StringComparer.Ordinal);

        foreach (Match supertype in Regex.Matches(TypeDeclaration().Match(masked[owner]).Groups["rest"].Value, @"[A-Z][\w$]*"))
        {
            if (JdkMethods.TryGetValue(supertype.Value, out var known)) candidates.UnionWith(known);
            candidates.UnionWith(DeclaredMethods(masked, depths, supertype.Value));
        }

        var name = method.Groups["name"].Value;
        if (CodeText.Nearest(name, candidates.Where(c => c != name)) is not { } right) return null;

        var group = method.Groups["name"];
        var original = source.Lines[k];

        return LocalFix.ReplaceLine(
            Id, $"Rename {name} to {right}",
            $"`@Override` says `{name}` replaces a method it inherits, and nothing it inherits is called `{name}`. The inherited method within a " +
            $"letter or two of it is `{right}` - as written, `{name}` is a new method and `{right}` is never replaced.",
            source.Path, k + 1, original[..group.Index] + right + original[(group.Index + group.Length)..]);
    }

    private static IEnumerable<string> DeclaredMethods(IReadOnlyList<string> masked, int[] depths, string type)
    {
        var declaration = Enumerable.Range(0, masked.Count)
            .FirstOrDefault(i => Regex.IsMatch(masked[i], $@"\b(?:class|interface|record|enum)\s+{Regex.Escape(type)}\b"), -1);

        if (declaration < 0) yield break;

        var member = depths[declaration] + 1;

        for (var i = declaration + 1; i < masked.Count; i++)
        {
            if (i == declaration + 1 && masked[i].Trim() == "{") continue;
            if (depths[i] < member) break;

            if (depths[i] == member && !masked[i].Contains('=') && Method().Match(masked[i]) is { Success: true } m)
                yield return m.Groups["name"].Value;
        }
    }
}

/// <summary><c>interface expected here</c> and <c>no interface expected here</c> - <c>extends</c> and <c>implements</c> swapped.</summary>
public sealed partial class JavaExtendsImplements : ILocalFixRule
{
    public string Id => "java-extends-implements";

    private static readonly HashSet<string> JdkInterfaces =
    [
        "Runnable", "Comparable", "Comparator", "Iterable", "Iterator", "Collection", "List", "Set", "Map", "Queue", "Deque",
        "AutoCloseable", "Closeable", "Serializable", "Cloneable", "Callable", "Supplier", "Consumer", "Function", "Predicate", "CharSequence",
    ];

    [GeneratedRegex(@"\bimplements\s+(?<name>[A-Z][\w$]*)(?:<[^>]*>)?\s*(?:\{|$)")]
    private static partial Regex SingleImplements();

    [GeneratedRegex(@"\bextends\s+(?<name>[A-Z][\w$]*)(?:<[^>]*>)?\s*(?:\{|$)")]
    private static partial Regex SingleExtends();

    public LocalFix? Propose(LocalFixContext context)
    {
        var message = context.Error.Message;
        if (message is not ("interface expected here" or "no interface expected here") || JavaCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var code = masked[number - 1];

        if (Regex.IsMatch(code, @"\binterface\s")) return null;

        if (message == "interface expected here")
        {
            if (Regex.IsMatch(code, @"\bextends\b") || SingleImplements().Match(code) is not { Success: true } implements) return null;

            var name = implements.Groups["name"].Value;
            if (!masked.Any(text => Regex.IsMatch(text, $@"\bclass\s+{Regex.Escape(name)}\b"))) return null;

            return LocalFix.ReplaceLine(
                Id, $"extends {name}, not implements",
                $"`{name}` is a class, and a class is inherited from with `extends`. `implements` is only for interfaces.",
                source.Path, number, line[..implements.Index] + "extends" + line[(implements.Index + "implements".Length)..]);
        }

        if (Regex.IsMatch(code, @"\bimplements\b") || SingleExtends().Match(code) is not { Success: true } extends) return null;

        var interfaceName = extends.Groups["name"].Value;
        var isInterface = JdkInterfaces.Contains(interfaceName) || masked.Any(text => Regex.IsMatch(text, $@"\binterface\s+{Regex.Escape(interfaceName)}\b"));
        if (!isInterface) return null;

        return LocalFix.ReplaceLine(
            Id, $"implements {interfaceName}, not extends",
            $"`{interfaceName}` is an interface, and a class takes on an interface with `implements`. `extends` is for inheriting from a class.",
            source.Path, number, line[..extends.Index] + "implements" + line[(extends.Index + "extends".Length)..]);
    }
}

/// <summary><c>call to super must be first statement in constructor</c>.</summary>
public sealed partial class JavaSuperFirst : ILocalFixRule
{
    public string Id => "java-super-first";

    [GeneratedRegex(@"^call to (?<call>super|this) must be first statement in constructor$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;

        var (source, number, _) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var call = number - 1;
        var keyword = message.Groups["call"].Value;

        if (!Regex.IsMatch(masked[call], $@"^\s*{keyword}\s*\(.*\)\s*;\s*$")) return null;

        // The constructor's opening brace: the nearest one above that is still open.
        var open = -1;
        var depth = 0;

        for (var i = call - 1; i >= 0 && open < 0; i--)
        {
            for (var c = masked[i].Length - 1; c >= 0; c--)
            {
                if (masked[i][c] == '}') depth++;
                else if (masked[i][c] == '{' && depth-- == 0)
                {
                    open = i;
                    break;
                }
            }
        }

        if (open < 0 || masked[open][(masked[open].LastIndexOf('{') + 1)..].Trim().Length > 0) return null;

        var first = open + 1;
        if (first >= call) return null;

        return new LocalFix
        {
            RuleId = Id,
            Title = $"Move {keyword}(...) to the top of the constructor",
            Explanation = $"A constructor has to call `{keyword}(...)` before anything else - the object is not built until it has. " +
                          "The statements that came before it now come straight after.",
            File = source.Path,
            StartLine = first + 1,
            RemoveCount = call - first + 1,
            NewLines = [source.Lines[call], .. source.Lines.Skip(first).Take(call - first)],
        };
    }
}

/// <summary><c>constructor Dog in class Dog cannot be applied to given types</c> - because the "constructor" says <c>void</c>.</summary>
public sealed partial class JavaConstructorReturnType : ILocalFixRule
{
    public string Id => "java-constructor-return-type";

    [GeneratedRegex(@"^constructor (?<cls>[\w$]+) in class (?<owner>[\w$]+) cannot be applied to given types;?$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (message.Groups["cls"].Value != message.Groups["owner"].Value) return null;

        var source = at.Source;
        var cls = Regex.Escape(message.Groups["cls"].Value);
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        var methods = Enumerable.Range(0, masked.Count)
            .Select(i => (Index: i, Match: Regex.Match(masked[i], $@"^(?<lead>\s*(?:(?:public|protected|private)\s+)?)(?<void>void\s+){cls}\s*\(")))
            .Where(x => x.Match.Success)
            .ToList();

        if (methods is not [var (index, match)]) return null;

        var original = source.Lines[index];
        var remove = match.Groups["void"];

        return LocalFix.ReplaceLine(
            Id, $"Remove void from the {message.Groups["cls"].Value} constructor",
            $"A constructor has no return type. With `void` in front, `{message.Groups["cls"].Value}(...)` is an ordinary method that happens to " +
            "share the class's name - so there is no constructor that takes these arguments.",
            source.Path, index + 1, original[..remove.Index] + original[(remove.Index + remove.Length)..]);
    }
}

/// <summary><c>modifier private not allowed here</c>.</summary>
public sealed partial class JavaIllegalModifier : ILocalFixRule
{
    public string Id => "java-illegal-modifier";

    [GeneratedRegex(@"^modifier (?<modifiers>[\w,]+) not allowed here$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;

        var (source, number, line) = at;
        var modifiers = message.Groups["modifiers"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var masked = CodeText.Mask(line, Syntax.CLike);

        var hits = modifiers.Select(m => Regex.Match(masked, $@"(?<![\w$]){Regex.Escape(m)}\s+")).ToList();
        if (hits.Any(h => !h.Success)) return null;

        var corrected = CCode.Replace(line, hits, _ => "");
        var named = string.Join(" and ", modifiers.Select(m => $"`{m}`"));

        return LocalFix.ReplaceLine(
            Id, $"Remove {string.Join(" and ", modifiers)}",
            $"Java does not allow {named} on this declaration. A top-level class is either `public` or has no modifier - `private`, " +
            "`protected` and `static` only mean something for a member inside another class.",
            source.Path, number, corrected);
    }
}

// ======================================================================= values and variables

/// <summary><c>integer number too large</c> - a long literal without its <c>L</c>.</summary>
public sealed partial class JavaLongLiteral : ILocalFixRule
{
    public string Id => "java-long-literal";

    [GeneratedRegex(@"(?<![\w.$])(?<digits>\d[\d_]*)(?![\w.$])")]
    private static partial Regex Digits();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!(context.Error.Message ?? "").StartsWith("integer number too large", StringComparison.Ordinal) || JavaCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var hits = Digits().Matches(CodeText.Mask(line, Syntax.CLike))
            .Where(m => long.TryParse(m.Groups["digits"].Value.Replace("_", ""), out var value) && value > int.MaxValue)
            .ToList();

        if (hits is not [var hit]) return null;

        var end = hit.Index + hit.Length;

        return LocalFix.ReplaceLine(
            Id, $"Write it as a long: {hit.Value}L",
            "A whole number written in Java is an `int`, and an int stops at 2,147,483,647. An `L` on the end makes it a `long`.",
            source.Path, number, line[..end] + "L" + line[end..]);
    }
}

/// <summary><c>variable x is already defined in method main</c> - a second declaration where an assignment was meant.</summary>
public sealed partial class JavaRedefinition : ILocalFixRule
{
    public string Id => "java-redefinition";

    [GeneratedRegex(@"^variable (?<name>[\w$]+) is already defined in (?:method|constructor) ")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;

        var (source, number, line) = at;
        var name = Regex.Escape(message.Groups["name"].Value);
        var redeclared = Regex.Match(line, $@"^(?<lead>\s*)(?<type>(?!return\b)[\w$<>\[\],.? ]+?)\s+{name}\s*=\s*(?<value>.+?)\s*;(?<tail>\s*(?://.*)?)$");
        if (!redeclared.Success || redeclared.Groups["type"].Value.Contains("final")) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var (first, _) = JavaCode.EnclosingMethod(masked, number - 1);
        var type = Regex.Replace(Regex.Escape(redeclared.Groups["type"].Value.Trim()), @"(\\ )+", @"\s+");
        var earlier = new Regex($@"(?<![\w$.]){type}\s+{name}\s*[=;,]");

        var original = Enumerable.Range(first, Math.Max(0, number - 1 - first)).LastOrDefault(i => earlier.IsMatch(masked[i]), -1);
        if (original < 0) return null;

        var variable = message.Groups["name"].Value;

        return LocalFix.ReplaceLine(
            Id, $"Assign to {variable} instead of declaring it again",
            $"`{variable}` is already declared on line {original + 1}. Writing the type again declares a second variable with the same name, " +
            "which one method cannot have; without the type, the line gives the existing one a new value.",
            source.Path, number, $"{redeclared.Groups["lead"].Value}{variable} = {redeclared.Groups["value"].Value};{redeclared.Groups["tail"].Value}");
    }
}

/// <summary><c>generic array creation</c> - <c>new T[10]</c>.</summary>
public sealed partial class JavaGenericArray : ILocalFixRule
{
    public string Id => "java-generic-array";

    [GeneratedRegex(@"\bnew\s+(?<type>[A-Z][\w$]*)\s*\[(?<size>[^\[\]]*)\]")]
    private static partial Regex NewArray();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.Message != "generic array creation" || JavaCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (NewArray().Matches(CodeText.Mask(line, Syntax.CLike)) is not { Count: 1 } hits) return null;

        var type = hits[0].Groups["type"].Value;
        var escaped = Regex.Escape(type);
        var all = string.Join("\n", CodeText.MaskAll(source.Lines, Syntax.CLike));

        var isParameter = Regex.IsMatch(all, $@"\b(?:class|interface|record)\s+[\w$]+\s*<[^>]*\b{escaped}\b") ||
                          Regex.IsMatch(all, $@"<[^<>]*\b{escaped}\b[^<>]*>\s*[\w$<>\[\]]+\s+[\w$]+\s*\(");

        if (!isParameter || Regex.IsMatch(all, $@"\b(?:class|interface|record|enum)\s+{escaped}\b")) return null;

        var size = line.Substring(hits[0].Groups["size"].Index, hits[0].Groups["size"].Length);

        return LocalFix.ReplaceLine(
            Id, $"Make an Object array and cast it to {type}[]",
            $"Java cannot make an array of a type parameter: when the program runs, `{type}` is no longer known. The usual way round is an " +
            $"`Object[]` cast to `{type}[]`, which compiles with an unchecked warning.",
            source.Path, number, line[..hits[0].Index] + $"({type}[]) new Object[{size}]" + line[(hits[0].Index + hits[0].Length)..]);
    }
}

// ======================================================================= collections and strings

/// <summary><c>cannot find symbol: method stream()</c> on an array.</summary>
public sealed partial class JavaArrayStream : ILocalFixRule
{
    public string Id => "java-array-stream";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at) return null;
        if (JavaCode.Symbol().Match(context.Error.Message ?? "") is not { Success: true } symbol) return null;
        if (symbol.Groups["kind"].Value != "method" || symbol.Groups["name"].Value != "stream" || symbol.Groups["arguments"].Value != "()") return null;
        if (JavaCode.VariableLocation().Match(symbol.Groups["location"].Value) is not { Success: true } location) return null;
        if (!location.Groups["type"].Value.EndsWith("[]", StringComparison.Ordinal)) return null;

        var (source, number, line) = at;
        var receiver = location.Groups["receiver"].Value;
        var hits = Regex.Matches(CodeText.Mask(line, Syntax.CLike), $@"(?<![\w$.]){Regex.Escape(receiver)}\s*\.\s*stream\s*\(\s*\)");
        if (hits.Count != 1) return null;

        var imported = source.Lines.Any(l => Regex.IsMatch(l, @"^\s*import\s+java\.util\.(?:Arrays|\*)\s*;"));
        var call = $"{(imported ? "Arrays" : "java.util.Arrays")}.stream({receiver})";

        return LocalFix.ReplaceLine(
            Id, $"Use {call}",
            "An array has no methods of its own beyond `length`, so it has no `stream()`. `Arrays.stream` makes a stream from it.",
            source.Path, number, CCode.Replace(line, hits, _ => call));
    }
}

/// <summary><c>ConcurrentModificationException</c> from removing items inside a for-each over the same list.</summary>
public sealed class JavaRemoveInForEach : ILocalFixRule
{
    public string Id => "java-remove-in-for-each";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "java", ExceptionType: "java.util.ConcurrentModificationException" }) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (Blocks.RemoveInLoop(source.Lines, masked, number - 1, java: true) is not { } loop) return null;

        return new LocalFix
        {
            RuleId = Id,
            Title = "Remove them with removeIf instead of inside the loop",
            Explanation = "A for-each loop walks the list with an iterator, and removing from the list underneath it breaks that walk. " +
                          "`removeIf` does the whole loop-and-remove itself, safely.",
            File = source.Path,
            StartLine = loop.Start + 1,
            RemoveCount = loop.Count,
            NewLines = [loop.Line],
        };
    }
}

/// <summary><c>split(".")</c> - a regular expression where a plain character was meant.</summary>
public sealed partial class JavaSplitRegex : ILocalFixRule
{
    public string Id => "java-split-regex";

    [GeneratedRegex(@"^Index \d+ out of bounds for length [01]$")]
    private static partial Regex EmptyArray();

    [GeneratedRegex(@"^Dangling meta character '(?<ch>.)'")]
    private static partial Regex Dangling();

    [GeneratedRegex(@"(?<![\w$.])(?<array>[A-Za-z_$][\w$]*)\s*\[")]
    private static partial Regex Indexed();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if (error.LanguageId != "java") return null;

        var empty = error.ExceptionType == "java.lang.ArrayIndexOutOfBoundsException" && EmptyArray().IsMatch(error.Message ?? "");
        var dangling = error.ExceptionType == "java.util.regex.PatternSyntaxException" && Dangling().IsMatch(error.Message ?? "");

        if (!empty && !dangling || context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;

        var lines = source.Lines;
        var masked = CodeText.MaskAll(lines, Syntax.CLike);
        var split = new Regex(@"\.split\(\s*""(?<ch>[.|$^*+?])""\s*[,)]");

        int candidate = -1;

        if (split.IsMatch(lines[number - 1]))
        {
            candidate = number - 1;
        }
        else if (empty)
        {
            var (first, _) = JavaCode.EnclosingMethod(masked, number - 1);

            foreach (Match array in Indexed().Matches(masked[number - 1]))
            {
                var assigned = new Regex($@"(?<![\w$.]){Regex.Escape(array.Groups["array"].Value)}\s*=[^=]");
                var found = Enumerable.Range(first, number - first).LastOrDefault(i => assigned.IsMatch(masked[i]) && split.IsMatch(lines[i]), -1);

                if (found >= 0)
                {
                    candidate = found;
                    break;
                }
            }
        }

        if (candidate < 0 || split.Matches(lines[candidate]) is not { Count: 1 } hits) return null;

        var ch = hits[0].Groups["ch"];
        var literal = ch.Index - 1;
        var corrected = lines[candidate][..literal] + "\"\\\\" + ch.Value + "\"" + lines[candidate][(ch.Index + 2)..];

        return LocalFix.ReplaceLine(
            Id, $"Split on a real {ch.Value}: \"\\\\{ch.Value}\"",
            $"`split` takes a regular expression, and in a regular expression `{ch.Value}` is not a plain character" +
            (ch.Value == "." ? " - it means any character, so every character is a separator and nothing is left." : ".") +
            $" `\\\\{ch.Value}` means the character itself.",
            source.Path, candidate + 1, corrected);
    }
}

// ======================================================================= shapes shared with C and C#

/// <summary><c>unclosed string literal</c>.</summary>
public sealed class JavaUnclosedString : ILocalFixRule
{
    public string Id => "java-unclosed-string";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.Message != "unclosed string literal" || JavaCode.Locate(context) is not { } at) return null;
        if (Blocks.CloseString(at.Line, Syntax.CLike) is not { } corrected) return null;

        return LocalFix.ReplaceLine(Id, "Close the string", Blocks.CloseStringExplanation, at.Source.Path, at.Number, corrected);
    }
}

/// <summary><c>'else' without 'if'</c> - from <c>if (x); {</c>.</summary>
public sealed class JavaIfSemicolon : ILocalFixRule
{
    public string Id => "java-if-semicolon";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.Message != "'else' without 'if'" || JavaCode.Locate(context) is not { } at) return null;

        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.CLike);
        if (Blocks.IfSemicolon(at.Source.Lines, masked, at.Number - 1) is not { } fix) return null;

        return LocalFix.ReplaceLine(Id, Blocks.IfSemicolonTitle, Blocks.IfSemicolonExplanation, at.Source.Path, fix.Line + 1, fix.Corrected);
    }
}

/// <summary><c>exception NumberFormatException has already been caught</c> - catch clauses in the wrong order.</summary>
public sealed partial class JavaCatchOrder : ILocalFixRule
{
    public string Id => "java-catch-order";

    [GeneratedRegex(@"^exception (?<type>[\w$.]+) has already been caught$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;

        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.CLike);
        if (Blocks.SwapCatch(at.Source.Lines, masked, at.Number - 1) is not { } swap) return null;

        return new LocalFix
        {
            RuleId = Id,
            Title = $"Catch {message.Groups["type"].Value} first",
            Explanation = Blocks.CatchOrderExplanation,
            File = at.Source.Path,
            StartLine = swap.Start + 1,
            RemoveCount = swap.Count,
            NewLines = swap.Lines,
        };
    }
}
