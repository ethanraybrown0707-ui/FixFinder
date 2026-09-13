using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>What the C# rules share: recognising a Roslyn error, and where on the line it points.</summary>
/// <remarks>
/// Roslyn gives a line and a column for every error, and the column is exact: it lands on the missing
/// semicolon, on the start of an expression that will not convert, on the member name that does not
/// exist. That is what lets these rules change one token rather than guess at a line.
/// </remarks>
internal static partial class Cs
{
    public static bool Is(LocalFixContext context, params string[] codes) =>
        context.Error.LanguageId == "msvc" &&
        context.Error.ErrorCode is { } code &&
        code.StartsWith("CS", StringComparison.Ordinal) &&
        (codes.Length == 0 || codes.Contains(code));

    /// <summary>The C# file, line number, line text and 0-based column of the error, or null.</summary>
    public static (SourceFile Source, int Number, string Line, int Index)? Locate(LocalFixContext context)
    {
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (!source.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || source.Line(number) is not { } line) return null;

        var index = frame.Column is { } column ? Math.Clamp(column - 1, 0, line.Length) : -1;

        return (source, number, line, index);
    }

    public static readonly HashSet<string> Keywords = new(
        ("abstract as base bool break byte case catch char checked class const continue decimal default delegate do " +
         "double else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface " +
         "internal is lock long namespace new null object operator out override params private protected public readonly " +
         "ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true try typeof uint " +
         "ulong unchecked unsafe ushort using virtual void volatile while var async await dynamic nameof record get set " +
         "init value yield when where").Split(' '),
        StringComparer.Ordinal);

    /// <summary>Where an expression starting at <paramref name="start"/> ends: a semicolon, comma or unmatched bracket.</summary>
    public static int ExpressionEnd(string masked, int start)
    {
        var depth = 0;

        for (var i = start; i < masked.Length; i++)
        {
            var c = masked[i];

            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}')
            {
                if (depth == 0) return i;
                depth--;
            }
            else if (c is ';' or ',' && depth == 0) return i;
        }

        return masked.Length;
    }

    [GeneratedRegex(@"[+\-*/%<>=!&|?]")]
    public static partial Regex Operator();

    public static bool IsSimple(string expression) => !Operator().IsMatch(CodeText.Mask(expression, Syntax.CLike));
}

/// <summary>The .NET types a C# beginner reaches for, so their real members can be read by reflection.</summary>
/// <remarks>
/// FixFinder is itself a .NET program, so what <c>Console</c>, <c>string</c> or <c>List&lt;T&gt;</c>
/// really has is not a table somebody wrote - it is asked of the runtime. That is the C# counterpart
/// of reading a JDK class with javap.
/// </remarks>
internal static class CSharpTypes
{
    private static readonly Dictionary<string, Type> Known = new(StringComparer.Ordinal)
    {
        ["string"] = typeof(string), ["String"] = typeof(string), ["int"] = typeof(int), ["Int32"] = typeof(int),
        ["long"] = typeof(long), ["double"] = typeof(double), ["Double"] = typeof(double), ["float"] = typeof(float),
        ["decimal"] = typeof(decimal), ["bool"] = typeof(bool), ["Boolean"] = typeof(bool), ["char"] = typeof(char),
        ["Char"] = typeof(char), ["object"] = typeof(object), ["Object"] = typeof(object),
        ["List"] = typeof(List<>), ["Dictionary"] = typeof(Dictionary<,>), ["HashSet"] = typeof(HashSet<>),
        ["Queue"] = typeof(Queue<>), ["Stack"] = typeof(Stack<>), ["LinkedList"] = typeof(LinkedList<>),
        ["SortedDictionary"] = typeof(SortedDictionary<,>), ["Console"] = typeof(Console), ["Math"] = typeof(Math),
        ["DateTime"] = typeof(DateTime), ["TimeSpan"] = typeof(TimeSpan), ["Convert"] = typeof(Convert),
        ["Array"] = typeof(Array), ["StringBuilder"] = typeof(System.Text.StringBuilder), ["File"] = typeof(File),
        ["Path"] = typeof(Path), ["Directory"] = typeof(Directory), ["Random"] = typeof(Random), ["Guid"] = typeof(Guid),
        ["Environment"] = typeof(Environment), ["Task"] = typeof(Task), ["Thread"] = typeof(Thread),
        ["Regex"] = typeof(Regex), ["Enumerable"] = typeof(Enumerable), ["Enum"] = typeof(Enum),
    };

    public static IEnumerable<string> Names => Known.Keys;

    public static Type? Resolve(string name)
    {
        var bare = name.Trim();
        if (bare.EndsWith("[]", StringComparison.Ordinal)) return typeof(Array);

        var generic = bare.IndexOf('<');
        if (generic >= 0) bare = bare[..generic];

        var dot = bare.LastIndexOf('.');
        if (dot >= 0) bare = bare[(dot + 1)..];

        return Known.GetValueOrDefault(bare);
    }

    public static bool IsCollection(Type type) =>
        type != typeof(string) && type != typeof(Array) && typeof(IEnumerable).IsAssignableFrom(type);

    /// <summary>Public member names, and whether each is called with brackets.</summary>
    public static Dictionary<string, bool> Members(Type type, bool isStatic)
    {
        var members = new Dictionary<string, bool>(StringComparer.Ordinal);
        var flags = BindingFlags.Public | (isStatic ? BindingFlags.Static : BindingFlags.Instance);

        foreach (var member in type.GetMembers(flags))
        {
            switch (member)
            {
                case MethodInfo { IsSpecialName: true }:
                    continue;
                case MethodInfo:
                    members.TryAdd(member.Name, true);
                    break;
                case PropertyInfo or FieldInfo:
                    members[member.Name] = false;
                    break;
            }
        }

        // An instance collection also has everything LINQ adds to it, which implicit usings bring in.
        if (!isStatic && IsCollection(type))
            foreach (var method in typeof(Enumerable).GetMethods(BindingFlags.Public | BindingFlags.Static))
                members.TryAdd(method.Name, true);

        return members;
    }
}

/// <summary><c>elif</c> from Python, which C# writes <c>else if</c>.</summary>
public sealed partial class CSharpElif : ILocalFixRule
{
    public string Id => "csharp-elif";

    [GeneratedRegex(@"(?<![\w.])elif(?!\w)")]
    private static partial Regex Elif();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context) || Cs.Locate(context) is not { } at) return null;

        var (source, number, line, _) = at;
        var hits = Elif().Matches(CodeText.Mask(line, Syntax.CLike));
        if (hits.Count == 0) return null;

        var corrected = line;
        foreach (var hit in hits.Reverse()) corrected = corrected[..hit.Index] + "else if" + corrected[(hit.Index + hit.Length)..];

        return LocalFix.ReplaceLine(Id, "Write elif as else if", "C# has no `elif`; it is `else if`.", source.Path, number, corrected);
    }
}

/// <summary><c>System.out.println</c> from Java, which C# writes <c>Console.WriteLine</c>.</summary>
public sealed partial class CSharpJavaPrint : ILocalFixRule
{
    public string Id => "csharp-java-print";

    [GeneratedRegex(@"System\.out\.(?<method>println|print)\s*\(")]
    private static partial Regex JavaPrint();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context) || Cs.Locate(context) is not { } at) return null;

        var (source, number, line, _) = at;
        var hits = JavaPrint().Matches(CodeText.Mask(line, Syntax.CLike));
        if (hits.Count != 1) return null;

        var replacement = hits[0].Groups["method"].Value == "println" ? "Console.WriteLine(" : "Console.Write(";

        return LocalFix.ReplaceLine(
            Id, $"Write it as {replacement.TrimEnd('(')}",
            "`System.out.println` is Java. C# writes to the console with `Console.WriteLine`.",
            source.Path, number, line[..hits[0].Index] + replacement + line[(hits[0].Index + hits[0].Length)..]);
    }
}

/// <summary><c>if x &gt; 5 {</c> - in C# the condition needs its brackets.</summary>
public sealed partial class CSharpConditionParentheses : ILocalFixRule
{
    public string Id => "csharp-condition-parentheses";

    [GeneratedRegex(@"^(?<lead>\s*(?:\}\s*)?(?:else\s+)?)(?<keyword>if|while|switch)\s+(?<condition>[^({\s][^{]*?)\s*(?<brace>\{?)$")]
    private static partial Regex Condition();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context, "CS1003", "CS1525", "CS1026") || Cs.Locate(context) is not { } at) return null;

        var (source, number, line, _) = at;
        var (code, tail) = CodeText.SplitComment(line, Syntax.CLike);

        if (Condition().Match(code) is not { Success: true } match) return null;

        var brace = match.Groups["brace"].Length > 0 ? " {" : "";

        return LocalFix.ReplaceLine(
            Id, $"Put the {match.Groups["keyword"].Value} condition in brackets",
            "In C# the condition of `if`, `while` and `switch` always goes in brackets.",
            source.Path, number, $"{match.Groups["lead"].Value}{match.Groups["keyword"].Value} ({match.Groups["condition"].Value}){brace}{tail}");
    }
}

/// <summary><c>CS1002: ; expected</c>, placed exactly where Roslyn points.</summary>
public sealed class CSharpMissingSemicolon : ILocalFixRule
{
    public string Id => "csharp-missing-semicolon";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context, "CS1002") || Cs.Locate(context) is not { } at) return null;

        var (source, number, line, index) = at;
        if (index < 0) return null;

        var code = line[..index].TrimEnd();
        if (code.Length == 0 || code.EndsWith(';') || code.EndsWith('{') || code.EndsWith('}')) return null;

        return LocalFix.ReplaceLine(
            Id, "Add the missing semicolon",
            $"Every C# statement ends with a semicolon, and the one on line {number} does not.",
            source.Path, number, code + ";" + line[code.Length..]);
    }
}

/// <summary><c>CS1513: } expected</c> - the file ends with a block still open.</summary>
public sealed class CSharpMissingClosingBrace : ILocalFixRule
{
    public string Id => "csharp-missing-closing-brace";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context, "CS1513") || context.Read(context.Frame?.File) is not { } source) return null;
        if (!source.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var unclosed = new Stack<int>();
        var stray = 0;

        for (var i = 0; i < masked.Count; i++)
        {
            foreach (var c in masked[i])
            {
                if (c == '{') unclosed.Push(i);
                else if (c == '}' && !unclosed.TryPop(out _)) stray++;
            }
        }

        if (stray > 0 || unclosed.Count != 1) return null;

        var opener = unclosed.Pop();
        var last = source.Count - 1;
        while (last > 0 && source.Lines[last].Trim().Length == 0) last--;

        return LocalFix.Insert(
            Id, $"Close the block opened on line {opener + 1}",
            $"The `{{` on line {opener + 1} is never closed - the file ends first.",
            source.Path, last + 2, [CodeText.Indentation(source.Lines[opener]) + "}"]);
    }
}

/// <summary><c>CS1012: Too many characters in character literal</c> - text in single quotes.</summary>
public sealed class CSharpCharLiteralString : ILocalFixRule
{
    public string Id => "csharp-char-literal-string";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context, "CS1012") || Cs.Locate(context) is not { } at) return null;

        var (source, number, line, index) = at;
        if (index < 0 || index >= line.Length || line[index] != '\'') return null;

        var close = line.IndexOf('\'', index + 1);
        if (close < 0) return null;

        var content = line[(index + 1)..close];
        if (content.Contains('"') || content.Contains('\\')) return null;

        return LocalFix.ReplaceLine(
            Id, "Put the text in double quotes",
            "Single quotes hold one character in C#. Text - a string - goes in double quotes.",
            source.Path, number, line[..index] + "\"" + content + "\"" + line[(close + 1)..]);
    }
}

/// <summary><c>CS0230</c>: <c>foreach (item in items)</c> needs a type, and <c>var</c> is enough.</summary>
public sealed partial class CSharpForeachType : ILocalFixRule
{
    public string Id => "csharp-foreach-type";

    [GeneratedRegex(@"foreach\s*\(\s*(?<name>[A-Za-z_]\w*)\s+in\b")]
    private static partial Regex Foreach();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context, "CS0230") || Cs.Locate(context) is not { } at) return null;

        var (source, number, line, _) = at;
        if (Foreach().Match(CodeText.Mask(line, Syntax.CLike)) is not { Success: true } match) return null;

        var name = match.Groups["name"];

        return LocalFix.ReplaceLine(
            Id, $"Declare {name.Value} with var",
            "A foreach loop declares its variable, so it needs a type - `var` lets C# work it out.",
            source.Path, number, line[..name.Index] + "var " + line[name.Index..]);
    }
}

/// <summary><c>CS0103: The name 'x' does not exist in the current context</c>.</summary>
/// <remarks>
/// Four different mistakes produce this one message: a word from another language (<c>print</c>,
/// <c>True</c>, <c>None</c>, <c>len</c>), a loop counter nobody declared, or a plain typo. Each is
/// told apart by what the name is and where it sits.
/// </remarks>
public sealed partial class CSharpNameMissing : ILocalFixRule
{
    public string Id => "csharp-name-missing";

    [GeneratedRegex(@"^The name '(?<name>\w+)' does not exist in the current context$")]
    private static partial Regex Message();

    private static readonly Dictionary<string, string> Words = new(StringComparer.Ordinal)
    {
        ["True"] = "true", ["False"] = "false", ["None"] = "null", ["NULL"] = "null", ["nil"] = "null", ["Null"] = "null",
    };

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context, "CS0103") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (Cs.Locate(context) is not { } at) return null;

        var (source, number, line, index) = at;
        var name = message.Groups["name"].Value;
        var masked = CodeText.Mask(line, Syntax.CLike);
        var escaped = Regex.Escape(name);

        if (Words.TryGetValue(name, out var word))
            return Replace(source, number, line, masked, name, word, $"C# writes `{name}` as `{word}`, in lower case.");

        if (name is "print" or "println" or "printf" && Regex.Match(masked, $@"(?<![\w.]){escaped}\s*\(") is { Success: true } call)
        {
            return LocalFix.ReplaceLine(
                Id, "Write to the console with Console.WriteLine",
                $"`{name}` is not C#. Text goes to the console with `Console.WriteLine`.",
                source.Path, number, line[..call.Index] + "Console.WriteLine" + line[(call.Index + name.Length)..]);
        }

        if (name == "len" && Regex.Match(masked, @"(?<![\w.])len\s*\((?<arg>[^()]+)\)") is { Success: true } len)
        {
            var argument = line.Substring(len.Groups["arg"].Index, len.Groups["arg"].Length).Trim();

            return LocalFix.ReplaceLine(
                Id, $"Use {argument}.Length",
                "`len(...)` is Python. In C# a string or array has a `Length` property.",
                source.Path, number, line[..len.Index] + $"{argument}.Length" + line[(len.Index + len.Length)..]);
        }

        if (Regex.Match(masked, $@"\bfor\s*\(\s*(?<name>{escaped})\s*=") is { Success: true } counter)
        {
            var at_ = counter.Groups["name"].Index;

            return LocalFix.ReplaceLine(
                Id, $"Declare the loop counter {name}",
                $"`{name}` is used as the loop counter but never declared. Declaring it in the loop with `int` is the usual way.",
                source.Path, number, line[..at_] + "int " + line[at_..]);
        }

        var candidates = CodeText.Identifiers(CodeText.MaskAll(source.Lines, Syntax.CLike))
            .Where(identifier => identifier != name && !Cs.Keywords.Contains(identifier));

        if (CodeText.Nearest(name, candidates) is not { } right) return null;
        if (CodeText.ReplaceWord(line, name, right, Syntax.CLike, index) is not { } corrected) return null;

        return LocalFix.ReplaceLine(
            Id, $"Change {name} to {right}",
            $"Nothing called `{name}` exists here. `{right}` is the only name in this file within a letter or two of it.",
            source.Path, number, corrected);
    }

    private LocalFix? Replace(SourceFile source, int number, string line, string masked, string name, string right, string explanation)
    {
        var hits = Regex.Matches(masked, $@"(?<![\w.]){Regex.Escape(name)}(?!\w)");
        if (hits.Count == 0) return null;

        var corrected = line;
        foreach (var hit in hits.Reverse()) corrected = corrected[..hit.Index] + right + corrected[(hit.Index + hit.Length)..];

        return LocalFix.ReplaceLine(Id, $"Write {name} as {right}", explanation, source.Path, number, corrected);
    }
}

/// <summary>
/// <c>CS0117 / CS1061: 'X' does not contain a definition for 'y'</c> - matched against what the type
/// really has, read from the runtime.
/// </summary>
public sealed partial class CSharpMissingMember : ILocalFixRule
{
    public string Id => "csharp-missing-member";

    [GeneratedRegex(@"^'(?<type>[^']+)' does not contain a definition for '(?<name>\w+)'")]
    private static partial Regex Message();

    private static readonly HashSet<string> LengthWords = ["Length", "length", "Count", "count", "Size", "size", "len"];

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context, "CS0117", "CS1061") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (Cs.Locate(context) is not { } at) return null;

        var (source, number, line, index) = at;
        var typeName = message.Groups["type"].Value;
        var name = message.Groups["name"].Value;

        if (CSharpTypes.Resolve(typeName) is not { } type) return null;

        var isStatic = context.Error.ErrorCode == "CS0117";
        var members = CSharpTypes.Members(type, isStatic);

        string? right;

        if (!isStatic && LengthWords.Contains(name))
            right = CSharpTypes.IsCollection(type) ? "Count" : type == typeof(string) || type == typeof(Array) ? "Length" : null;
        else
            right = CodeText.Nearest(name, members.Keys.Where(member => member != name));

        if (right is null || !members.TryGetValue(right, out var callable)) return null;

        var masked = CodeText.Mask(line, Syntax.CLike);
        var hits = Regex.Matches(masked, $@"\.\s*(?<name>{Regex.Escape(name)})(?!\w)").Select(m => m.Groups["name"]).ToList();
        var chosen = hits.Count == 1 ? hits[0] : hits.FirstOrDefault(h => h.Index == index);
        if (chosen is null) return null;

        var after = chosen.Index + chosen.Length;
        var corrected = line[..chosen.Index] + right;
        var rest = line[after..];

        // Java's name.length() is a property in C#: the brackets have to go with the rename, or the
        // fix trades one error for "non-invocable member".
        if (!callable && Regex.Match(rest, @"^\s*\(\s*\)") is { Success: true } brackets) rest = rest[brackets.Length..];

        return LocalFix.ReplaceLine(
            Id, $"Change {name} to {right}",
            $"`{typeName}` has no `{name}`. What it does have is `{right}`" + (callable ? "." : ", which is a property rather than a method."),
            source.Path, number, corrected + rest);
    }
}

/// <summary><c>CS1955: Non-invocable member</c> - brackets after a property, or a type used without new.</summary>
public sealed partial class CSharpNonInvocable : ILocalFixRule
{
    public string Id => "csharp-non-invocable";

    [GeneratedRegex(@"^Non-invocable member '(?<member>[^']+)' cannot be used like a method\.$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context, "CS1955") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (Cs.Locate(context) is not { } at) return null;

        var (source, number, line, index) = at;
        if (index < 0) return null;

        var member = message.Groups["member"].Value;
        var dot = member.LastIndexOf('.');

        // A type - List<T> - called like a method: the object was never created. Roslyn points at the
        // type's own name, so `new` goes in front of any namespace written before it.
        if (dot < 0 || member.IndexOf('<') is var generic && generic >= 0 && generic < dot)
        {
            var start = index;
            while (start > 0 && (char.IsLetterOrDigit(line[start - 1]) || line[start - 1] is '_' or '.')) start--;

            return LocalFix.ReplaceLine(
                Id, "Create it with new",
                $"`{member}` is a type, and calling it like a method does not make one. `new` creates the object.",
                source.Path, number, line[..start] + "new " + line[start..]);
        }

        var name = member[(dot + 1)..];
        if (!line[index..].StartsWith(name, StringComparison.Ordinal)) return null;

        var after = index + name.Length;
        if (Regex.Match(line[after..], @"^\s*\(\s*\)") is not { Success: true } brackets) return null;

        return LocalFix.ReplaceLine(
            Id, $"Use {name} without brackets",
            $"`{name}` is a property, so it is read, not called.",
            source.Path, number, line[..after] + line[(after + brackets.Length)..]);
    }
}

/// <summary><c>CS0029 / CS0266: Cannot implicitly convert type 'a' to 'b'</c>.</summary>
/// <remarks>
/// <c>int next = input + 1;</c> is the case that must not be answered with <c>int.Parse(input + 1)</c>:
/// that parses the text "181". When the expression is a value plus a number, the value is what gets
/// converted.
/// </remarks>
public sealed partial class CSharpImplicitConversion : ILocalFixRule
{
    public string Id => "csharp-implicit-conversion";

    [GeneratedRegex(@"^Cannot implicitly convert type '(?<from>[^']+)' to '(?<to>[^']+)'")]
    private static partial Regex Message();

    [GeneratedRegex(@"^""(?<digits>-?\d+(?:\.\d+)?)""$")]
    private static partial Regex QuotedNumber();

    [GeneratedRegex(@"^(?<value>[A-Za-z_][\w.]*(?:\(\))?)\s*(?<op>[+\-*/])\s*(?<number>\d+(?:\.\d+)?)$")]
    private static partial Regex ValueOperatorNumber();

    [GeneratedRegex(@"^(?<left>[A-Za-z_][\w.\[\]]*)\s*=\s*(?<right>[^=].*)$")]
    private static partial Regex Assignment();

    [GeneratedRegex(@"^-?\d+(?:\.\d+)?[fFdDmMlL]?$")]
    private static partial Regex NumberLiteral();

    private static readonly HashSet<string> Numbers = ["int", "long", "double", "float", "decimal", "short", "byte"];

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context, "CS0029", "CS0266") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (Cs.Locate(context) is not { } at) return null;

        var (source, number, line, start) = at;
        if (start < 0) return null;

        var from = message.Groups["from"].Value;
        var to = message.Groups["to"].Value;

        var masked = CodeText.Mask(line, Syntax.CLike);
        var end = Cs.ExpressionEnd(masked, start);
        var expression = line[start..end].TrimEnd();
        end = start + expression.Length;

        if (expression.Length == 0) return null;

        string? replacement = null;
        string explanation = "";

        if (from == "string" && (Numbers.Contains(to) || to == "bool"))
        {
            if (QuotedNumber().Match(expression) is { Success: true } quoted && Numbers.Contains(to))
            {
                replacement = quoted.Groups["digits"].Value;
                explanation = "A number in quotes is text. Without the quotes it is the number itself.";
            }
            else if (ValueOperatorNumber().Match(expression) is { Success: true } arithmetic)
            {
                replacement = $"{to}.Parse({arithmetic.Groups["value"].Value}) {arithmetic.Groups["op"].Value} {arithmetic.Groups["number"].Value}";
                explanation = $"`{arithmetic.Groups["value"].Value}` is text, so it is converted to a number before the arithmetic - parsing the whole expression would parse the text with the digits stuck on the end.";
            }
            else if (Cs.IsSimple(expression))
            {
                replacement = $"{to}.Parse({expression})";
                explanation = $"`{expression}` is text, and a {to} has to be read out of it with `{to}.Parse`. It throws if the text is not one.";
            }
        }
        else if (from == "string" && to == "char" && expression.Length == 3 && expression[0] == '"' && expression[2] == '"' && expression[1] != '\'')
        {
            replacement = $"'{expression[1]}'";
            explanation = "A single character goes in single quotes in C#; double quotes make a string.";
        }
        else if (to == "string" && from is "int" or "long" or "double" or "float" or "decimal" or "bool" or "char")
        {
            replacement = NumberLiteral().IsMatch(expression) ? $"\"{expression}\"" : Cs.IsSimple(expression) ? $"{expression}.ToString()" : $"({expression}).ToString()";
            explanation = $"A {from} is not a string. `ToString()` gives its text form.";
        }
        else if (to == "bool" && from is "int" or "long" or "double")
        {
            if (Assignment().Match(expression) is { Success: true } assignment)
            {
                replacement = $"{assignment.Groups["left"].Value} == {assignment.Groups["right"].Value}";
                explanation = "A single `=` assigns. A condition compares with `==`.";
            }
            else if (Regex.IsMatch(expression, @"^[A-Za-z_][\w.]*$"))
            {
                replacement = $"{expression} != 0";
                explanation = "C# does not treat a number as true or false. Comparing it with 0 says what the condition means.";
            }
        }
        else if (context.Error.ErrorCode == "CS0266" && from is "double" or "float" or "decimal" or "long" && Numbers.Contains(to))
        {
            replacement = Cs.IsSimple(expression) ? $"({to}){expression}" : $"({to})({expression})";
            explanation = $"A {from} can hold values a {to} cannot, so C# wants the conversion written out. The cast drops anything that does not fit, such as the part after the decimal point.";
        }

        if (replacement is null) return null;

        return LocalFix.ReplaceLine(
            Id, $"Convert the {from} to {to}", explanation,
            source.Path, number, line[..start] + replacement + line[end..]);
    }
}

/// <summary><c>CS0120</c>: a method called from <c>static Main</c> that is not static.</summary>
public sealed partial class CSharpNonStaticMember : ILocalFixRule
{
    public string Id => "csharp-non-static-member";

    [GeneratedRegex(@"^An object reference is required for the non-static field, method, or property '(?:[\w.]+\.)?(?<type>\w+)\.(?<member>\w+)(?:\(.*\))?'$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context, "CS0120") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (context.Read(context.Frame?.File) is not { } source) return null;

        var member = Regex.Escape(message.Groups["member"].Value);
        var declaration = new Regex($@"^(?<lead>\s*(?:(?:public|private|protected|internal|virtual|override|sealed|async|readonly)\s+)*)(?<type>[\w<>\[\],.?]+)\s+{member}\s*[({{=;]");

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var found = masked.Select((text, i) => (Index: i, Match: declaration.Match(text)))
            .Where(d => d.Match.Success && d.Match.Groups["type"].Value is not ("return" or "new" or "else"))
            .ToList();

        if (found.Count != 1 || Regex.IsMatch(masked[found[0].Index], @"\b(?:static|const)\b")) return null;

        var (index, match) = found[0];
        var lead = match.Groups["lead"].Length;
        var original = source.Lines[index];

        return LocalFix.ReplaceLine(
            Id, $"Make {message.Groups["member"].Value} static",
            $"`Main` is static, so it has no object to call `{message.Groups["member"].Value}` on. It uses nothing that belongs to one - it compiles as static - so that is the direct fix.",
            source.Path, index + 1, original[..lead] + "static " + original[lead..]);
    }
}

/// <summary><c>CS0246</c>: a type that needs a using, or whose name is misspelt or borrowed from Java.</summary>
public sealed partial class CSharpTypeNotFound : ILocalFixRule
{
    public string Id => "csharp-type-not-found";

    [GeneratedRegex(@"^The type or namespace name '(?<name>\w+)(?:<[^']*>)?' could not be found")]
    private static partial Regex Message();

    [GeneratedRegex(@"^\s*using\s+[\w.]+\s*;")]
    private static partial Regex UsingLine();

    private static readonly Dictionary<string, string> Namespaces = new(StringComparer.Ordinal)
    {
        ["List"] = "System.Collections.Generic", ["Dictionary"] = "System.Collections.Generic", ["HashSet"] = "System.Collections.Generic",
        ["Queue"] = "System.Collections.Generic", ["Stack"] = "System.Collections.Generic", ["LinkedList"] = "System.Collections.Generic",
        ["SortedDictionary"] = "System.Collections.Generic", ["KeyValuePair"] = "System.Collections.Generic",
        ["StringBuilder"] = "System.Text", ["Encoding"] = "System.Text", ["Regex"] = "System.Text.RegularExpressions",
        ["Task"] = "System.Threading.Tasks", ["Thread"] = "System.Threading", ["File"] = "System.IO", ["Path"] = "System.IO",
        ["Directory"] = "System.IO", ["StreamReader"] = "System.IO", ["StreamWriter"] = "System.IO", ["Stopwatch"] = "System.Diagnostics",
        ["HttpClient"] = "System.Net.Http", ["JsonSerializer"] = "System.Text.Json", ["CultureInfo"] = "System.Globalization",
    };

    private static readonly Dictionary<string, string> Borrowed = new(StringComparer.Ordinal)
    {
        ["boolean"] = "bool", ["Integer"] = "int", ["HashMap"] = "Dictionary", ["str"] = "string",
    };

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context, "CS0246") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (Cs.Locate(context) is not { } at) return null;

        var (source, number, line, _) = at;
        var name = message.Groups["name"].Value;

        if (Namespaces.TryGetValue(name, out var ns))
        {
            if (source.Lines.Any(l => l.Trim() == $"using {ns};")) return null;

            var last = -1;
            for (var i = 0; i < source.Count; i++)
                if (UsingLine().IsMatch(source.Lines[i])) last = i;

            return LocalFix.Insert(
                Id, $"Add using {ns};",
                $"`{name}` lives in `{ns}`, which this file does not use.",
                source.Path, last + 2, [$"using {ns};"]);
        }

        var declared = CodeText.MaskAll(source.Lines, Syntax.CLike)
            .SelectMany(text => Regex.Matches(text, @"\b(?:class|struct|record|interface|enum)\s+(?<name>\w+)").Select(m => m.Groups["name"].Value));

        var right = Borrowed.GetValueOrDefault(name) ??
                    CodeText.Nearest(name, Namespaces.Keys.Concat(CSharpTypes.Names).Concat(declared).Where(n => n != name));

        if (right is null) return null;

        var hits = Regex.Matches(CodeText.Mask(line, Syntax.CLike), $@"(?<![\w.]){Regex.Escape(name)}(?!\w)");
        if (hits.Count == 0) return null;

        var corrected = line;
        foreach (var hit in hits.Reverse()) corrected = corrected[..hit.Index] + right + corrected[(hit.Index + hit.Length)..];

        return LocalFix.ReplaceLine(
            Id, $"Change {name} to {right}",
            Borrowed.ContainsKey(name)
                ? $"`{name}` is how another language says it. In C# the type is `{right}`."
                : $"There is no type called `{name}`. `{right}` is the only one within a letter or two of it.",
            source.Path, number, corrected);
    }
}

/// <summary><c>CS0234</c>: a namespace, or a type written with its namespace, spelt almost right - <c>System.Collection.Generic</c>.</summary>
public sealed partial class CSharpNamespaceTypo : ILocalFixRule
{
    public string Id => "csharp-namespace-typo";

    [GeneratedRegex(@"^The type or namespace name '(?<name>\w+)(?:<[^']*>)?' does not exist in the namespace '(?<parent>[\w.]+)'")]
    private static partial Regex Message();

    private static readonly string[] Known =
    [
        "System.Collections", "System.Collections.Generic", "System.Collections.Concurrent", "System.IO", "System.Linq",
        "System.Text", "System.Text.Json", "System.Text.RegularExpressions", "System.Threading", "System.Threading.Tasks",
        "System.Net", "System.Net.Http", "System.Diagnostics", "System.Globalization", "System.Numerics", "System.Reflection",
        "System.Runtime", "System.Security", "System.Security.Cryptography", "System.Xml", "System.Xml.Linq", "System.Data",
        "System.ComponentModel", "System.Buffers", "System.Timers",
    ];

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context, "CS0234") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (Cs.Locate(context) is not { } at) return null;

        var (source, number, line, _) = at;
        var parent = message.Groups["parent"].Value;
        var name = message.Groups["name"].Value;

        // What the parent namespace holds: the namespaces under it, and the types it declares.
        var children = Known
            .Where(ns => ns.StartsWith(parent + ".", StringComparison.Ordinal))
            .Select(ns => ns[(parent.Length + 1)..].Split('.')[0])
            .Concat(CSharpTypes.Names.Where(type => CSharpTypes.Resolve(type)?.Namespace == parent))
            .Distinct();

        if (CodeText.Nearest(name, children) is not { } right) return null;

        var wrong = $"{parent}.{name}";
        var at_ = line.IndexOf(wrong, StringComparison.Ordinal);
        if (at_ < 0 || line.IndexOf(wrong, at_ + 1, StringComparison.Ordinal) >= 0) return null;

        return LocalFix.ReplaceLine(
            Id, $"Change {wrong} to {parent}.{right}",
            $"`{parent}` has no `{name}`. What it does have is `{right}`.",
            source.Path, number, line[..at_] + $"{parent}.{right}" + line[(at_ + wrong.Length)..]);
    }
}

/// <summary><c>CS4033</c>: <c>await</c> in a method that is not async.</summary>
public sealed partial class CSharpAwaitWithoutAsync : ILocalFixRule
{
    public string Id => "csharp-await-without-async";

    [GeneratedRegex(@"^(?<lead>\s*(?:(?:public|private|protected|internal|static|virtual|override|sealed)\s+)*)(?<ret>void|[A-Za-z_][\w<>\[\],.?]*)\s+(?<name>[A-Za-z_]\w*)\s*\([^;]*$")]
    private static partial Regex Method();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context, "CS4033", "CS4032") || Cs.Locate(context) is not { } at) return null;

        var (source, number, _, _) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var unmatched = 0;

        for (var k = number - 2; k >= 0; k--)
        {
            for (var c = masked[k].Length - 1; c >= 0; c--)
            {
                if (masked[k][c] == '}') unmatched++;
                else if (masked[k][c] == '{' && unmatched-- == 0)
                {
                    var headerLine = masked[k][..c].Trim().Length == 0 && k > 0 ? k - 1 : k;

                    if (Method().Match(masked[headerLine]) is not { Success: true } method) return null;
                    if (method.Groups["name"].Value is "if" or "for" or "while" or "foreach" or "switch" or "using" or "lock") continue;

                    var ret = method.Groups["ret"];
                    var original = source.Lines[headerLine];
                    var task = ret.Value == "void" ? "Task" : $"Task<{ret.Value}>";

                    return LocalFix.ReplaceLine(
                        Id, $"Make {method.Groups["name"].Value} async",
                        "`await` only works inside a method marked `async`, which returns a Task.",
                        source.Path, headerLine + 1, original[..ret.Index] + "async " + task + original[(ret.Index + ret.Length)..]);
                }
            }
        }

        return null;
    }
}

/// <summary><c>CS0122</c>: a member of a class in this file used from outside it, without being public.</summary>
public sealed partial class CSharpInaccessible : ILocalFixRule
{
    public string Id => "csharp-inaccessible";

    [GeneratedRegex(@"^'(?:[\w.]+\.)?(?<type>\w+)\.(?<member>\w+)(?:\(.*\))?' is inaccessible due to its protection level$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context, "CS0122") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (context.Read(context.Frame?.File) is not { } source) return null;

        var type = Regex.Escape(message.Groups["type"].Value);
        var member = Regex.Escape(message.Groups["member"].Value);
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        var classes = Enumerable.Range(0, masked.Count).Where(i => Regex.IsMatch(masked[i], $@"\b(?:class|struct|record)\s+{type}\b")).ToList();
        if (classes.Count != 1) return null;

        var depth = 0;
        var started = false;
        var declaration = new Regex($@"^(?<lead>\s*)(?<access>(?:private|protected|internal)\s+)?(?<rest>(?:(?:static|virtual|override|readonly|async)\s+)*[\w<>\[\],.?]+\s+{member}\s*[({{=;])");

        for (var i = classes[0]; i < masked.Count; i++)
        {
            if (started && depth == 1 && declaration.Match(masked[i]) is { Success: true } match)
            {
                var original = source.Lines[i];
                var access = match.Groups["access"];
                var corrected = access.Success
                    ? original[..access.Index] + "public " + original[(access.Index + access.Length)..]
                    : original[..match.Groups["lead"].Length] + "public " + original[match.Groups["lead"].Length..];

                return LocalFix.ReplaceLine(
                    Id, $"Make {message.Groups["member"].Value} public",
                    $"Members of a class are private unless they say otherwise, so `{message.Groups["member"].Value}` cannot be used from outside `{message.Groups["type"].Value}`.",
                    source.Path, i + 1, corrected);
            }

            foreach (var c in masked[i])
            {
                if (c == '{') { depth++; started = true; }
                else if (c == '}') depth--;
            }

            if (started && depth == 0) break;
        }

        return null;
    }
}

/// <summary>
/// <c>IndexOutOfRangeException</c> or <c>ArgumentOutOfRangeException</c> from a loop running while
/// <c>i &lt;= values.Length</c>.
/// </summary>
public sealed partial class CSharpOffByOneLoop : ILocalFixRule
{
    public string Id => "csharp-off-by-one-loop";

    [GeneratedRegex(@"\[\s*(?<var>[A-Za-z_]\w*)\s*\]")]
    private static partial Regex IndexUse();

    [GeneratedRegex(@"\bfor\s*\(\s*(?:int\s+|var\s+|long\s+)?(?<var>[A-Za-z_]\w*)\s*=[^;]*;\s*\k<var>\s*(?<op><=)\s*(?<bound>[^;]+?)\s*;")]
    private static partial Regex Loop();

    [GeneratedRegex(@"\.(?:Length|Count)(?:\(\))?$")]
    private static partial Regex LengthBound();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        if (error.LanguageId != "csharp" || error.ExceptionType is not ("System.IndexOutOfRangeException" or "System.ArgumentOutOfRangeException")) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (number < 1 || number > masked.Count) return null;

        var indexes = IndexUse().Matches(masked[number - 1]).Select(m => m.Groups["var"].Value).ToHashSet(StringComparer.Ordinal);
        if (indexes.Count == 0) return null;

        for (var k = number - 1; k >= Math.Max(0, number - 16); k--)
        {
            if (Loop().Match(masked[k]) is not { Success: true } loop || !indexes.Contains(loop.Groups["var"].Value)) continue;
            if (!LengthBound().IsMatch(loop.Groups["bound"].Value.Trim())) return null;

            var op = loop.Groups["op"];

            return LocalFix.ReplaceLine(
                Id, "Stop the loop at the last element: < instead of <=",
                $"The valid indexes run from 0 to one less than `{loop.Groups["bound"].Value.Trim()}`. The loop on line {k + 1} runs while {loop.Groups["var"].Value} <= it, which reaches one past the end.",
                source.Path, k + 1, source.Lines[k][..op.Index] + "<" + source.Lines[k][(op.Index + 2)..]);
        }

        return null;
    }
}

/// <summary><c>CS0165: Use of unassigned local variable</c> - a number or bool declared without a value.</summary>
public sealed partial class CSharpUnassignedLocal : ILocalFixRule
{
    public string Id => "csharp-unassigned-local";

    [GeneratedRegex(@"^Use of unassigned local variable '(?<name>\w+)'$")]
    private static partial Regex Message();

    private static readonly Dictionary<string, string> Defaults = new(StringComparer.Ordinal)
    {
        ["int"] = "0", ["long"] = "0", ["short"] = "0", ["byte"] = "0", ["double"] = "0", ["float"] = "0",
        ["decimal"] = "0", ["bool"] = "false",
    };

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context, "CS0165") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (Cs.Locate(context) is not { } at) return null;

        var (source, number, _, _) = at;
        var name = message.Groups["name"].Value;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var declaration = new Regex($@"^\s*(?<type>int|long|short|byte|double|float|decimal|bool)\s+{Regex.Escape(name)}(?<end>)\s*;");

        for (var k = number - 1; k >= 0; k--)
        {
            if (declaration.Match(masked[k]) is not { Success: true } match) continue;

            var end = match.Groups["end"].Index;
            var value = Defaults[match.Groups["type"].Value];

            return LocalFix.ReplaceLine(
                Id, $"Start {name} at {value}",
                $"C# will not read a variable that may never have been given a value, and `{name}` is declared on line {k + 1} " +
                $"without one. Starting it at {value} gives it one - if it should start somewhere else, that is the value to put there.",
                source.Path, k + 1, source.Lines[k][..end] + " = " + value + source.Lines[k][end..]);
        }

        return null;
    }
}

/// <summary><c>CS1520: Method must have a return type</c>, when what it returns says which.</summary>
public sealed partial class CSharpMissingReturnType : ILocalFixRule
{
    public string Id => "csharp-missing-return-type";

    [GeneratedRegex(@"^\s*(?:(?:public|private|protected|internal|static|virtual|override|sealed|async)\s+)*(?<name>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)\s*\{?\s*$")]
    private static partial Regex Header();

    [GeneratedRegex(@"\breturn\b\s*(?<value>[^;]*);")]
    private static partial Regex Return();

    [GeneratedRegex(@"\bclass\s+(?<name>\w+)")]
    private static partial Regex Class();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context, "CS1520") || Cs.Locate(context) is not { } at) return null;

        var (source, number, _, _) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (Header().Match(masked[number - 1]) is not { Success: true } header) return null;

        var name = header.Groups["name"].Value;

        // A constructor whose name does not match its class gets the same error, and a return type is not its fix.
        var owner = Enumerable.Range(0, number).Reverse().Select(i => Class().Match(masked[i])).FirstOrDefault(m => m.Success);
        if (owner is not null && CodeText.Distance(owner.Groups["name"].Value, name) <= 2) return null;

        if (Body(masked, number - 1) is not { } body) return null;

        var parameters = header.Groups["parameters"].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[1], p => p[0], StringComparer.Ordinal);

        var returned = body.SelectMany(text => Return().Matches(text)).Select(m => m.Groups["value"].Value.Trim()).ToList();

        var types = returned.All(value => value.Length == 0)
            ? ["void"]
            : returned.Select(value => TypeOf(value, parameters)).Distinct().ToList();

        if (types is not [{ } type]) return null;

        var column = header.Groups["name"].Index;
        var line = source.Lines[number - 1];

        return LocalFix.ReplaceLine(
            Id, $"Give {name} the return type {type}",
            type == "void"
                ? $"Every C# method says what it returns. `{name}` returns nothing, which is written `void`."
                : $"Every C# method says what it returns, and everything `{name}` returns is {(type == "int" ? "an" : "a")} `{type}`.",
            source.Path, number, line[..column] + type + " " + line[column..]);
    }

    private static List<string>? Body(IReadOnlyList<string> masked, int header)
    {
        var depth = 0;
        var started = false;
        var lines = new List<string>();

        for (var i = header; i < masked.Count && i < header + 200; i++)
        {
            lines.Add(masked[i]);

            foreach (var c in masked[i])
            {
                if (c == '{') { depth++; started = true; }
                else if (c == '}') depth--;
            }

            if (started && depth == 0) return lines;
        }

        return null;
    }

    private static string? TypeOf(string value, IReadOnlyDictionary<string, string> parameters)
    {
        if (Regex.IsMatch(value, @"^-?\d+$")) return "int";
        if (Regex.IsMatch(value, @"^-?\d+\.\d+$")) return "double";
        if (value is "true" or "false") return "bool";
        if (Regex.IsMatch(value, @"^""[^""]*""$")) return "string";

        // Arithmetic on parameters of one numeric type is that type.
        if (!Regex.IsMatch(value, @"^[\w\s+\-*/%()]+$")) return null;

        var types = Regex.Matches(value, @"[A-Za-z_]\w*").Select(m => parameters.GetValueOrDefault(m.Value)).Distinct().ToList();

        return types is [{ } only] && only is "int" or "long" or "double" or "float" or "decimal" ? only : null;
    }
}
