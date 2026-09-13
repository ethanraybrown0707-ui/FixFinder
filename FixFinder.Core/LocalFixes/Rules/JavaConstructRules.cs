using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>What the Java construct rules share: the line javac named, its caret, and the notes under it.</summary>
/// <remarks>
/// javac prints an error as a line, an echo of the source, a caret under the exact column, and then
/// notes such as <c>first type: char</c> or <c>required: reference</c>. The message alone often says
/// too little - <c>bad operand types for binary operator '=='</c> - and the notes say the rest.
/// </remarks>
internal static partial class JavaCode
{
    public static (SourceFile Source, int Number, string Line)? Locate(LocalFixContext context)
    {
        if (!Java.IsCompileError(context.Error)) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (!source.Path.EndsWith(".java", StringComparison.OrdinalIgnoreCase) || source.Line(number) is not { } line) return null;

        return (source, number, line);
    }

    public static int? Caret(LocalFixContext context, string line) =>
        CodeText.Caret(context.Output, context.Error) is { } caret && caret.Echo == line && caret.Column <= line.Length
            ? caret.Column
            : null;

    /// <summary>A note javac prints under the caret, such as <c>first type: char</c>.</summary>
    public static string? Note(LocalFixContext context, string label)
    {
        var output = context.Output;
        var start = -1;

        for (var i = 0; i < output.Count && start < 0; i++)
            if (output[i].Sequence == context.Error.FirstLineSequence) start = i;

        if (start < 0) return null;

        for (var i = start + 1; i < output.Count && i <= start + 8; i++)
        {
            if (DiagnosticLine().IsMatch(output[i].Text)) break;

            if (Regex.Match(output[i].Text, $@"^\s+{Regex.Escape(label)}:\s+(?<value>.+?)\s*$") is { Success: true } note)
                return note.Groups["value"].Value;
        }

        return null;
    }

    [GeneratedRegex(@"\.java:\d+: (?:error|warning):")]
    private static partial Regex DiagnosticLine();

    [GeneratedRegex(@"^cannot find symbol \(symbol:\s+(?<kind>variable|method|class)\s+(?<name>[A-Za-z_$][\w$]*)(?<arguments>\([^)]*\))?(?:,\s*location:\s*(?<location>.+))?\)$")]
    public static partial Regex Symbol();

    [GeneratedRegex(@"^variable (?<receiver>[\w$]+) of type (?<type>.+)$")]
    public static partial Regex VariableLocation();

    public static readonly Dictionary<string, string> Wrappers = new(StringComparer.Ordinal)
    {
        ["int"] = "Integer", ["long"] = "Long", ["double"] = "Double", ["float"] = "Float",
        ["boolean"] = "Boolean", ["char"] = "Character", ["short"] = "Short", ["byte"] = "Byte",
    };

    public static readonly Dictionary<string, string> Parsers = new(StringComparer.Ordinal)
    {
        ["int"] = "Integer.parseInt", ["long"] = "Long.parseLong", ["double"] = "Double.parseDouble", ["float"] = "Float.parseFloat",
    };

    public static bool IsCollection(string type) =>
        Regex.IsMatch(type, @"^(?:java\.util\.)?(?:List|ArrayList|LinkedList|Vector|Stack|Set|HashSet|TreeSet|LinkedHashSet|Collection|Queue|Deque|ArrayDeque|PriorityQueue|Map|HashMap|TreeMap|LinkedHashMap)\b");

    public static string Simple(string type) => type.IndexOf('<') is var generic and > 0 ? type[..generic] : type;

    /// <summary>The first and last line (0-based) of the method around a line: the block one level inside a class.</summary>
    public static (int First, int Last) EnclosingMethod(IReadOnlyList<string> masked, int index)
    {
        var depths = CCode.DepthAtStart(masked);

        var first = index;
        while (first > 0 && depths[first] > 1) first--;

        var last = index;
        while (last + 1 < masked.Count && depths[last + 1] > 1) last++;

        return (first, last);
    }

    /// <summary>The last line of a loop body that starts after <paramref name="after"/> on <paramref name="line"/>.</summary>
    public static int? BodyEnd(IReadOnlyList<string> masked, int line, int after)
    {
        var rest = masked[line][after..];
        var brace = rest.IndexOf('{');

        if (brace < 0) return rest.Trim().Length > 0 ? line : line + 1 < masked.Count ? line + 1 : null;

        var depth = 0;

        for (var i = line; i < masked.Count; i++)
        {
            for (var c = i == line ? after + brace : 0; c < masked[i].Length; c++)
            {
                if (masked[i][c] == '{') depth++;
                else if (masked[i][c] == '}' && --depth == 0) return i;
            }
        }

        return null;
    }
}

/// <summary><c>elif</c> from Python, which Java writes <c>else if</c>.</summary>
public sealed partial class JavaElif : ILocalFixRule
{
    public string Id => "java-elif";

    [GeneratedRegex(@"(?<![\w$.])elif(?=\s*\()")]
    private static partial Regex Elif();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var hits = Elif().Matches(CodeText.Mask(line, Syntax.CLike));
        if (hits.Count == 0) return null;

        return LocalFix.ReplaceLine(
            Id, "Write elif as else if", "Java has no `elif` - that is Python. In Java it is `else if`.",
            source.Path, number, CCode.Replace(line, hits, _ => "else if"));
    }
}

/// <summary><c>foreach (...)</c> from C#, and <c>for (String n in names)</c> from C# and Python - Java writes <c>for (String n : names)</c>.</summary>
public sealed partial class JavaForEach : ILocalFixRule
{
    public string Id => "java-for-each";

    [GeneratedRegex(@"(?<![\w$.])foreach(?=\s*\()")]
    private static partial Regex Foreach();

    [GeneratedRegex(@"\b(?:for|foreach)\s*\(\s*(?:final\s+)?[\w$<>\[\],.? ]*?[\w$>\]]\s+[A-Za-z_$][\w$]*\s+(?<in>in)\s+")]
    private static partial Regex In();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.CLike);

        var foreachHits = Foreach().Matches(masked);
        var inHit = In().Match(masked);

        if (foreachHits.Count == 0 && !inHit.Success) return null;

        var corrected = line;
        if (inHit.Success) corrected = corrected[..inHit.Groups["in"].Index] + ":" + corrected[(inHit.Groups["in"].Index + 2)..];
        corrected = CCode.Replace(corrected, foreachHits, _ => "for");

        return LocalFix.ReplaceLine(
            Id, "Write the loop as for (type item : items)",
            "Java's loop over every element is `for (Type item : items)` - the keyword is `for`, not `foreach`, and a colon stands where other languages write `in`.",
            source.Path, number, corrected);
    }
}

/// <summary>Words from other languages javac cannot find: <c>True</c>, <c>None</c>, <c>bool</c>, <c>print</c>, <c>Console.WriteLine</c>.</summary>
public sealed partial class JavaForeignWord : ILocalFixRule
{
    public string Id => "java-foreign-word";

    private static readonly Dictionary<string, string> Values = new(StringComparer.Ordinal)
    {
        ["True"] = "true", ["False"] = "false", ["None"] = "null", ["NULL"] = "null", ["nil"] = "null", ["Null"] = "null",
    };

    private static readonly Dictionary<string, string> Types = new(StringComparer.Ordinal)
    {
        ["bool"] = "boolean", ["Bool"] = "boolean", ["str"] = "String", ["Int"] = "int",
    };

    [GeneratedRegex(@"(?<![\w$.])Console\s*\.\s*(?<method>WriteLine|Write)\s*\(")]
    private static partial Regex ConsoleCall();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at) return null;
        if (JavaCode.Symbol().Match(context.Error.Message ?? "") is not { Success: true } symbol) return null;

        var (source, number, line) = at;
        var kind = symbol.Groups["kind"].Value;
        var name = symbol.Groups["name"].Value;
        var masked = CodeText.Mask(line, Syntax.CLike);

        if (kind == "variable" && Values.TryGetValue(name, out var value))
            return Words(source, number, line, masked, name, value, $"Java writes `{name}` as `{value}`, in lower case.");

        if (kind == "class" && Types.TryGetValue(name, out var type))
            return Words(source, number, line, masked, name, type, $"`{name}` is not a Java type. Java calls it `{type}`.");

        if (kind == "variable" && name == "Console" && ConsoleCall().Matches(masked) is { Count: 1 } calls)
        {
            var call = calls[0];
            var replacement = call.Groups["method"].Value == "WriteLine" ? "System.out.println(" : "System.out.print(";

            return LocalFix.ReplaceLine(
                Id, $"Print with {replacement.TrimEnd('(')}",
                "`Console.WriteLine` is C#. Java prints to the console with `System.out.println`.",
                source.Path, number, line[..call.Index] + replacement + line[(call.Index + call.Length)..]);
        }

        if (kind == "method" && name is "print" or "println" or "printf" or "puts")
        {
            var prints = Regex.Matches(masked, $@"(?<![\w$.]){name}\s*\(");
            if (prints.Count != 1) return null;

            // A method of that name in the file is the user's own, called wrongly - not a borrowed word.
            if (CodeText.MaskAll(source.Lines, Syntax.CLike).Any(text => Regex.IsMatch(text, $@"\b(?:void|static|public|private|protected)\b[^;=]*\b{name}\s*\(")))
                return null;

            var target = name == "printf" ? "System.out.printf" : "System.out.println";

            return LocalFix.ReplaceLine(
                Id, $"Print with {target}",
                $"`{name}(...)` on its own is not Java - printing goes through `System.out`.",
                source.Path, number, line[..prints[0].Index] + target + line[(prints[0].Index + name.Length)..]);
        }

        return null;
    }

    private LocalFix? Words(SourceFile source, int number, string line, string masked, string name, string right, string explanation)
    {
        var hits = Regex.Matches(masked, $@"(?<![\w$.]){Regex.Escape(name)}(?![\w$])");
        if (hits.Count == 0) return null;

        return LocalFix.ReplaceLine(Id, $"Write {name} as {right}", explanation, source.Path, number, CCode.Replace(line, hits, _ => right));
    }
}

/// <summary><c>package system does not exist</c> - a java.lang class written in lower case.</summary>
public sealed partial class JavaLowercaseClass : ILocalFixRule
{
    public string Id => "java-lowercase-class";

    [GeneratedRegex(@"^package (?<name>[a-z]\w*) does not exist$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;

        var (source, number, line) = at;
        var name = message.Groups["name"].Value;
        var right = char.ToUpperInvariant(name[0]) + name[1..];

        if (!JavaTypes.Lang.Contains(right)) return null;

        var hits = Regex.Matches(CodeText.Mask(line, Syntax.CLike), $@"(?<![\w$.]){Regex.Escape(name)}(?=\s*\.)");
        if (hits.Count == 0) return null;

        return LocalFix.ReplaceLine(
            Id, $"Write {name} as {right}",
            $"Java is case-sensitive, and the class is `{right}` with a capital letter. Written in lower case, javac took `{name}` for a package.",
            source.Path, number, CCode.Replace(line, hits, _ => right));
    }
}

/// <summary><c>length</c>, <c>length()</c> and <c>size()</c> mixed up, and a method named without its brackets.</summary>
/// <remarks>
/// An array has the field <c>length</c>, a String the method <c>length()</c>, and a collection the
/// method <c>size()</c>. javac says which type the variable is, so which one was meant is not a guess.
/// </remarks>
public sealed partial class JavaLengthAndSize : ILocalFixRule
{
    public string Id => "java-length-size";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at) return null;
        if (JavaCode.Symbol().Match(context.Error.Message ?? "") is not { Success: true } symbol) return null;
        if (JavaCode.VariableLocation().Match(symbol.Groups["location"].Value) is not { Success: true } location) return null;

        var (source, number, line) = at;
        var kind = symbol.Groups["kind"].Value;
        var name = symbol.Groups["name"].Value;
        var receiver = location.Groups["receiver"].Value;
        var type = location.Groups["type"].Value;

        string? right = null;
        var call = false;
        var explanation = "";

        // An argument inside the brackets means some other method was meant.
        if (kind == "method" && symbol.Groups["arguments"].Value != "()") return null;

        if (type.EndsWith("[]", StringComparison.Ordinal) && name is "length" or "size" or "count" or "Length" or "Count")
        {
            right = "length";
            explanation = "An array's length is a field in Java - `length`, with no brackets. `length()` belongs to String, and `size()` to collections.";
        }
        else if (type == "String" && name is "length" or "size" or "Length" or "count")
        {
            (right, call) = ("length", true);
            explanation = "A String's length is a method in Java - `length()`, with brackets. (An array's is the field `length`.)";
        }
        else if (JavaCode.IsCollection(type) && name is "length" or "Length" or "count" or "Count")
        {
            (right, call) = ("size", true);
            explanation = $"A {JavaCode.Simple(type)} counts its elements with `size()`. `length` is for arrays and `length()` for Strings.";
        }
        else if (kind == "variable" && JavaTypes.Fqn(JavaCode.Simple(type), source.Lines) is { } fqn && JavaTypes.Members(fqn, methods: true).Contains(name))
        {
            (right, call) = (name, true);
            explanation = $"`{name}` is a method of {JavaCode.Simple(type)}, and calling a method takes brackets: `{name}()`.";
        }

        if (right is null) return null;

        var masked = CodeText.Mask(line, Syntax.CLike);
        var access = Regex.Matches(masked, $@"(?<![\w$.]){Regex.Escape(receiver)}\s*\.\s*(?<name>{Regex.Escape(name)})(?<brackets>\s*\(\s*\))?(?![\w$])");
        if (access.Count != 1) return null;

        var member = access[0].Groups["name"];
        var brackets = access[0].Groups["brackets"];
        var end = brackets.Success ? brackets.Index + brackets.Length : member.Index + member.Length;
        var corrected = line[..member.Index] + right + (call ? "()" : "") + line[end..];

        if (corrected == line) return null;

        return LocalFix.ReplaceLine(
            Id, $"Use {receiver}.{right}{(call ? "()" : "")}", explanation, source.Path, number, corrected);
    }
}

/// <summary><c>array required, but List&lt;String&gt; found</c> - square brackets on a List or a String.</summary>
public sealed partial class JavaIndexing : ILocalFixRule
{
    public string Id => "java-indexing";

    [GeneratedRegex(@"^array required, but (?<type>.+) found$")]
    private static partial Regex Message();

    [GeneratedRegex(@"(?<![\w$.])(?<receiver>[A-Za-z_$][\w$]*)\s*\[(?<index>[^\[\]]+)\]")]
    private static partial Regex Indexed();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;

        var (source, number, line) = at;
        var type = message.Groups["type"].Value;

        string method, explanation;

        if (type == "String")
        {
            method = "charAt";
            explanation = "A Java String cannot be indexed with square brackets. `charAt` gives the character at a position.";
        }
        else if (Regex.IsMatch(type, @"^(?:List|ArrayList|LinkedList|Vector)<"))
        {
            method = "get";
            explanation = "Square brackets only index arrays in Java. A List gives the element at a position with `get`.";
        }
        else
        {
            return null;
        }

        var masked = CodeText.Mask(line, Syntax.CLike);
        var hits = Indexed().Matches(masked).ToList();
        var caret = JavaCode.Caret(context, line);
        var hit = hits.Count == 1 ? hits[0] : hits.FirstOrDefault(h => caret is { } c && c >= h.Index && c < h.Index + h.Length);
        if (hit is null) return null;

        // Assigning through the brackets is set, not get - and a String cannot be changed at all.
        var after = masked[(hit.Index + hit.Length)..].TrimStart();
        if (after.StartsWith('=') && !after.StartsWith("==", StringComparison.Ordinal)) return null;

        var receiver = hit.Groups["receiver"].Value;
        var index = line.Substring(hit.Groups["index"].Index, hit.Groups["index"].Length).Trim();

        return LocalFix.ReplaceLine(
            Id, $"Use {receiver}.{method}({index})", explanation,
            source.Path, number, line[..hit.Index] + $"{receiver}.{method}({index})" + line[(hit.Index + hit.Length)..]);
    }
}

/// <summary>A constructor called without <c>new</c>: <c>ArrayList&lt;String&gt; names = ArrayList&lt;&gt;();</c></summary>
public sealed partial class JavaMissingNew : ILocalFixRule
{
    public string Id => "java-missing-new";

    [GeneratedRegex(@"(?:=|\breturn\b|\(|,)\s*(?<type>[A-Z][\w$]*\s*<[^<>()]*>)\s*\(")]
    private static partial Regex Diamond();

    [GeneratedRegex(@"^cannot find symbol \(symbol:\s+method (?<name>[A-Z][\w$]*)\(")]
    private static partial Regex MissingMethod();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.CLike);
        var message = context.Error.Message ?? "";
        int column;
        string type;

        if (message == "illegal start of expression" && Diamond().Matches(masked) is { Count: 1 } diamonds)
        {
            column = diamonds[0].Groups["type"].Index;
            type = JavaCode.Simple(diamonds[0].Groups["type"].Value).Trim();
        }
        else if (MissingMethod().Match(message) is { Success: true } missing)
        {
            type = missing.Groups["name"].Value;

            var isClass = JavaTypes.Packages.ContainsKey(type) || JavaTypes.Lang.Contains(type) ||
                          CodeText.MaskAll(source.Lines, Syntax.CLike).Any(text => Regex.IsMatch(text, $@"\b(?:class|record|enum)\s+{Regex.Escape(type)}\b"));
            if (!isClass) return null;

            var calls = Regex.Matches(masked, $@"(?:=|\breturn\b|\(|,)\s*(?<type>{Regex.Escape(type)})\s*\(");
            if (calls.Count != 1) return null;

            column = calls[0].Groups["type"].Index;
        }
        else
        {
            return null;
        }

        return LocalFix.ReplaceLine(
            Id, $"Create the {type} with new",
            $"`{type}` is a class. Calling it like a method does not make one - `new` does.",
            source.Path, number, line[..column] + "new " + line[column..]);
    }
}

/// <summary>Single and double quotes mixed up: <c>'hello'</c>, <c>char c = "a"</c>, <c>word.charAt(0) == "a"</c>.</summary>
public sealed partial class JavaCharAndString : ILocalFixRule
{
    public string Id => "java-char-string";

    [GeneratedRegex(@"'(?<text>[^'\\\r\n]{2,})'")]
    private static partial Regex SingleQuotedText();

    [GeneratedRegex(@"^bad operand types for binary operator '(?:==|!=)'$")]
    private static partial Regex Comparison();

    [GeneratedRegex(@"(?<=(?:==|!=)\s*)""(?<c>[^""\\'])""")]
    private static partial Regex RightLetter();

    [GeneratedRegex(@"""(?<c>[^""\\'])""(?=\s*(?:==|!=))")]
    private static partial Regex LeftLetter();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var message = context.Error.Message ?? "";

        if (message == "unclosed character literal")
        {
            var hits = SingleQuotedText().Matches(line);
            if (hits.Count != 1 || hits[0].Groups["text"].Value.Contains('"')) return null;

            return LocalFix.ReplaceLine(
                Id, "Put the text in double quotes",
                "Single quotes hold exactly one character in Java. Text - a String - goes in double quotes.",
                source.Path, number, CCode.Replace(line, hits, hit => $"\"{hit.Groups["text"].Value}\""));
        }

        if (message == "incompatible types: String cannot be converted to char")
        {
            if (JavaCode.Caret(context, line) is not { } column || column + 3 > line.Length) return null;
            if (line[column] != '"' || line[column + 2] != '"' || line[column + 1] is '\\' or '\'') return null;

            return LocalFix.ReplaceLine(
                Id, "Use single quotes for one character",
                "A `char` is written in single quotes in Java. Double quotes make a String, even around one letter.",
                source.Path, number, line[..column] + $"'{line[column + 1]}'" + line[(column + 3)..]);
        }

        if (Comparison().IsMatch(message))
        {
            var first = JavaCode.Note(context, "first type");
            var second = JavaCode.Note(context, "second type");

            var hits = (first, second) switch
            {
                ("char", "String") => RightLetter().Matches(line),
                ("String", "char") => LeftLetter().Matches(line),
                _ => null,
            };

            if (hits is not { Count: 1 }) return null;

            return LocalFix.ReplaceLine(
                Id, "Compare the character with a char in single quotes",
                "`charAt` gives a `char`, and a `char` compares with another `char` - one letter in single quotes. In double quotes it is a String, which `==` cannot compare with a char.",
                source.Path, number, CCode.Replace(line, hits, hit => $"'{hit.Groups["c"].Value}'"));
        }

        return null;
    }
}

/// <summary><c>reached end of file while parsing</c> - a block never closed.</summary>
public sealed class JavaMissingClosingBrace : ILocalFixRule
{
    public string Id => "java-missing-closing-brace";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.Message != "reached end of file while parsing" || JavaCode.Locate(context) is not { Source: var source }) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var unclosed = new Stack<int>();

        for (var i = 0; i < masked.Count; i++)
        {
            foreach (var c in masked[i])
            {
                if (c == '{') unclosed.Push(i);
                else if (c == '}' && !unclosed.TryPop(out _)) return null;
            }
        }

        if (unclosed.Count != 1) return null;

        var opener = unclosed.Pop();
        var last = source.Count - 1;
        while (last > 0 && source.Lines[last].Trim().Length == 0) last--;

        return LocalFix.Insert(
            Id, $"Close the block opened on line {opener + 1}",
            $"The `{{` on line {opener + 1} is never closed - the file ends first.",
            source.Path, last + 2, [CodeText.Indentation(source.Lines[opener]) + "}"]);
    }
}

/// <summary><c>int cannot be dereferenced</c> - <c>x.equals(5)</c> on a primitive.</summary>
public sealed partial class JavaPrimitiveMethod : ILocalFixRule
{
    public string Id => "java-primitive-method";

    [GeneratedRegex(@"^(?<type>int|long|double|float|char|boolean|short|byte) cannot be dereferenced$")]
    private static partial Regex Message();

    [GeneratedRegex(@"(?<not>!\s*)?(?<![\w$.])(?<receiver>[A-Za-z_$][\w$]*)\s*\.\s*(?<method>equals|toString)\s*\((?<argument>[^()]*)\)")]
    private static partial Regex Call();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.CLike);
        var hits = Call().Matches(masked).ToList();
        var caret = JavaCode.Caret(context, line);
        var hit = hits.Count == 1 ? hits[0] : hits.FirstOrDefault(h => caret is { } c && c >= h.Index && c < h.Index + h.Length);
        if (hit is null) return null;

        var receiver = hit.Groups["receiver"].Value;
        var argument = line.Substring(hit.Groups["argument"].Index, hit.Groups["argument"].Length).Trim();
        var negated = hit.Groups["not"].Success;
        string replacement, explanation;

        if (hit.Groups["method"].Value == "equals" && argument.Length > 0)
        {
            // Only where == reads the same as the call did: between brackets, an assignment, or && and ||.
            var before = masked[..hit.Index].TrimEnd();
            var after = masked[(hit.Index + hit.Length)..].TrimStart();
            if (before.Length > 0 && !"(=&|,?:".Contains(before[^1]) && !before.EndsWith("return", StringComparison.Ordinal)) return null;
            if (after.Length > 0 && !");&|,?:".Contains(after[0])) return null;

            replacement = $"{receiver} {(negated ? "!=" : "==")} {argument}";
            explanation = $"`{receiver}` is an `{message.Groups["type"].Value}`, a primitive, and primitives have no methods. They compare with `==`; `equals` is for objects such as Strings.";
        }
        else if (hit.Groups["method"].Value == "toString" && argument.Length == 0 && !negated)
        {
            replacement = $"String.valueOf({receiver})";
            explanation = $"`{receiver}` is an `{message.Groups["type"].Value}`, a primitive, and primitives have no methods. `String.valueOf` gives its text.";
        }
        else
        {
            return null;
        }

        return LocalFix.ReplaceLine(
            Id, $"Write it as {replacement}", explanation,
            source.Path, number, line[..hit.Index] + replacement + line[(hit.Index + hit.Length)..]);
    }
}

/// <summary><c>if (x = 5)</c> - an assignment where a comparison was meant.</summary>
public sealed partial class JavaAssignmentInCondition : ILocalFixRule
{
    public string Id => "java-assignment-in-condition";

    [GeneratedRegex(@"^incompatible types: (?<from>[\w.$<>\[\]]+) cannot be converted to boolean$")]
    private static partial Regex Message();

    [GeneratedRegex(@"\b(?:if|while)\s*(?<open>\()")]
    private static partial Regex Condition();

    [GeneratedRegex(@"(?<![=!<>+\-*/%&|^])=(?!=)")]
    private static partial Regex Assignment();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at || Message().Match(context.Error.Message ?? "") is not { Success: true }) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.CLike);

        if (Condition().Matches(masked) is not { Count: 1 } conditions) return null;

        var open = conditions[0].Groups["open"].Index;
        if (CCode.Matching(masked, open) is not { } close) return null;

        var assignments = Assignment().Matches(masked[(open + 1)..close]);
        if (assignments.Count != 1) return null;

        var at_ = open + 1 + assignments[0].Index;

        return LocalFix.ReplaceLine(
            Id, "Compare with == instead of assigning with =",
            "A single `=` assigns a value. A condition compares, and comparing is `==`.",
            source.Path, number, line[..at_] + "==" + line[(at_ + 1)..]);
    }
}

/// <summary><c>for (i = 0; ...)</c> with <c>i</c> never declared.</summary>
public sealed partial class JavaForCounter : ILocalFixRule
{
    public string Id => "java-for-counter";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at) return null;
        if (JavaCode.Symbol().Match(context.Error.Message ?? "") is not { Success: true } symbol || symbol.Groups["kind"].Value != "variable") return null;

        var (source, number, _) = at;
        var name = symbol.Groups["name"].Value;
        var escaped = Regex.Escape(name);
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var header = new Regex($@"\bfor\s*(?<open>\()\s*(?<name>{escaped})\s*=(?!=)");

        for (var k = number - 1; k >= Math.Max(0, number - 16); k--)
        {
            if (header.Match(masked[k]) is not { Success: true } loop) continue;
            if (CCode.Matching(masked[k], loop.Groups["open"].Index) is not { } close) return null;
            if (JavaCode.BodyEnd(masked, k, close + 1) is not { } end || number - 1 > end) return null;

            var (first, last) = JavaCode.EnclosingMethod(masked, k);
            var word = new Regex($@"(?<![\w$.]){escaped}(?![\w$])");

            for (var i = first; i <= last; i++)
                if ((i < k || i > end) && word.IsMatch(masked[i])) return null;

            var column = loop.Groups["name"].Index;

            return LocalFix.ReplaceLine(
                Id, $"Declare {name} in the loop",
                $"`{name}` is the loop counter but is never declared. Nothing outside the loop uses it, so declaring it in the loop with `int` is all it needs.",
                source.Path, k + 1, source.Lines[k][..column] + "int " + source.Lines[k][column..]);
        }

        return null;
    }
}

/// <summary><c>unexpected type (required: reference, found: int)</c> - <c>ArrayList&lt;int&gt;</c>.</summary>
public sealed partial class JavaGenericPrimitive : ILocalFixRule
{
    public string Id => "java-generic-primitive";

    [GeneratedRegex(@"(?<=<[^<>()]*?)\b(?<primitive>int|long|double|float|boolean|char|short|byte)\b(?=[^<>()]*>)")]
    private static partial Regex Primitive();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.Message != "unexpected type" || JavaCode.Locate(context) is not { } at) return null;
        if (JavaCode.Note(context, "required") != "reference") return null;

        var (source, number, line) = at;
        var hits = Primitive().Matches(CodeText.Mask(line, Syntax.CLike));
        if (hits.Count == 0) return null;

        var names = hits.Select(h => h.Groups["primitive"].Value).Distinct().ToList();

        return LocalFix.ReplaceLine(
            Id, string.Join(", ", names.Select(n => $"Use {JavaCode.Wrappers[n]} for {n}")),
            "A generic type holds objects, and a primitive such as `int` is not one. Each primitive has an object form - " +
            string.Join(", ", names.Select(n => $"`{JavaCode.Wrappers[n]}` for `{n}`")) + " - which Java converts to and from automatically.",
            source.Path, number, CCode.Replace(line, hits, hit => JavaCode.Wrappers[hit.Groups["primitive"].Value]));
    }
}

/// <summary><c>possible lossy conversion from double to int</c>, and <c>Object cannot be converted to String</c>: casts.</summary>
public sealed partial class JavaCast : ILocalFixRule
{
    public string Id => "java-cast";

    [GeneratedRegex(@"^incompatible types: possible lossy conversion from (?<from>\w+) to (?<to>\w+)$")]
    private static partial Regex Lossy();

    [GeneratedRegex(@"^incompatible types: Object cannot be converted to (?<to>[\w.$<>\[\], ?]+)$")]
    private static partial Regex FromObject();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at) return null;

        var message = context.Error.Message ?? "";
        var lossy = Lossy().Match(message);
        var fromObject = FromObject().Match(message);

        if (!lossy.Success && !fromObject.Success) return null;

        var (source, number, line) = at;
        if (JavaCode.Caret(context, line) is not { } start) return null;

        var masked = CodeText.Mask(line, Syntax.CLike);
        var expression = line[start..Cs.ExpressionEnd(masked, start)].TrimEnd();
        if (expression.Length == 0) return null;

        var to = lossy.Success ? lossy.Groups["to"].Value : fromObject.Groups["to"].Value;
        var cast = Cs.IsSimple(expression) ? $"({to}) {expression}" : $"({to}) ({expression})";

        var explanation = lossy.Success
            ? $"A {lossy.Groups["from"].Value} can hold values an {to} cannot, so Java will not narrow it without being told. The cast does it on purpose " +
              $"and drops whatever does not fit. If that loses something that matters, declare the variable as {lossy.Groups["from"].Value} instead."
            : $"The value is declared as Object, so all Java knows is that it is some object. The cast says it is a {to} - and throws " +
              "ClassCastException when the program runs if it is anything else.";

        return LocalFix.ReplaceLine(
            Id, $"Cast it to {to}", explanation,
            source.Path, number, line[..start] + cast + line[(start + expression.Length)..]);
    }
}

/// <summary><c>bad operand types for binary operator '-'</c> between a String and a number.</summary>
public sealed partial class JavaStringArithmetic : ILocalFixRule
{
    public string Id => "java-string-arithmetic";

    [GeneratedRegex(@"^bad operand types for binary operator '(?<op>[-*/%])'$")]
    private static partial Regex Message();

    [GeneratedRegex(@"(?<![\w$.""])(?<operand>[A-Za-z_$][\w$.]*(?:\([^()]*\))?|""-?\d+(?:\.\d+)?"")\s*$")]
    private static partial Regex LeftOperand();

    [GeneratedRegex(@"^\s*(?<operand>[A-Za-z_$][\w$.]*(?:\([^()]*\))?|""-?\d+(?:\.\d+)?"")")]
    private static partial Regex RightOperand();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;

        var (source, number, line) = at;
        var op = message.Groups["op"].Value;
        if (JavaCode.Caret(context, line) is not { } column || column >= line.Length || line[column].ToString() != op) return null;

        var first = JavaCode.Note(context, "first type");
        var second = JavaCode.Note(context, "second type");

        Match operand;
        string numberType;

        if (first == "String" && second is not null && JavaCode.Parsers.ContainsKey(second))
        {
            operand = LeftOperand().Match(line[..column]);
            numberType = second;
        }
        else if (second == "String" && first is not null && JavaCode.Parsers.ContainsKey(first))
        {
            operand = RightOperand().Match(line[(column + 1)..]);
            numberType = first;
        }
        else
        {
            return null;
        }

        if (!operand.Success) return null;

        var group = operand.Groups["operand"];
        var index = first == "String" ? group.Index : column + 1 + group.Index;
        var text = group.Value;
        var parser = JavaCode.Parsers[numberType];

        var replacement = text.StartsWith('"') ? text[1..^1] : $"{parser}({text})";

        return LocalFix.ReplaceLine(
            Id, text.StartsWith('"') ? $"Write {text} as the number {text[1..^1]}" : $"Convert {text} with {parser}",
            text.StartsWith('"')
                ? $"A number in quotes is text, and Java cannot do arithmetic with text. Without the quotes it is the number."
                : $"`{text}` is a String, and `{op}` only works on numbers - Java will not read digits out of text by itself. {parser} does, and throws NumberFormatException if the text is not a number.",
            source.Path, number, line[..index] + replacement + line[(index + text.Length)..]);
    }
}

/// <summary><c>variable total might not have been initialized</c> - a number or boolean declared without a value.</summary>
public sealed partial class JavaUninitialised : ILocalFixRule
{
    public string Id => "java-uninitialised";

    [GeneratedRegex(@"^variable (?<name>[\w$]+) might not have been initialized$")]
    private static partial Regex Message();

    private static readonly Dictionary<string, string> Defaults = new(StringComparer.Ordinal)
    {
        ["int"] = "0", ["long"] = "0", ["short"] = "0", ["byte"] = "0", ["double"] = "0", ["float"] = "0", ["boolean"] = "false",
    };

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;

        var (source, number, _) = at;
        var name = message.Groups["name"].Value;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var declaration = new Regex($@"^\s*(?<type>int|long|short|byte|double|float|boolean)\s+{Regex.Escape(name)}(?<end>)\s*;");

        for (var k = number - 1; k >= 0; k--)
        {
            if (declaration.Match(masked[k]) is not { Success: true } match) continue;

            var end = match.Groups["end"].Index;
            var value = Defaults[match.Groups["type"].Value];

            return LocalFix.ReplaceLine(
                Id, $"Start {name} at {value}",
                $"Java will not read a local variable that may never have been given a value, and `{name}` is declared on line {k + 1} " +
                $"without one. Starting it at {value} gives it one - if it should start somewhere else, that is the value to put there.",
                source.Path, k + 1, source.Lines[k][..end] + " = " + value + source.Lines[k][end..]);
        }

        return null;
    }
}
