using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>Finding a C# type declared in the file, and the lines that are its members.</summary>
internal static class CsTypes
{
    public static int Declaration(IReadOnlyList<string> masked, string name) =>
        Enumerable.Range(0, masked.Count)
            .FirstOrDefault(i => Regex.IsMatch(masked[i], $@"\b(?:class|struct|record|interface|enum)\s+{Regex.Escape(name)}\b"), -1);

    /// <summary>The lines one level inside a type's braces.</summary>
    public static IEnumerable<int> Members(IReadOnlyList<string> masked, int declaration)
    {
        var depths = CCode.DepthAtStart(masked);
        var inside = depths[declaration] + 1;

        for (var i = declaration + 1; i < masked.Count; i++)
        {
            if (i == declaration + 1 && masked[i].Trim() == "{") continue;
            if (depths[i] < inside) yield break;
            if (depths[i] == inside) yield return i;
        }
    }

    /// <summary>True when an expression needs brackets before a member access is added to its end.</summary>
    public static bool NeedsBrackets(string masked)
    {
        var depth = 0;

        foreach (var c in masked)
        {
            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') depth--;
            else if (depth == 0 && "+-*/%<>=!&|?: ".Contains(c)) return true;
        }

        return false;
    }
}

// ======================================================================= inheritance and interfaces

/// <summary><c>CS0737</c>: a class member that implements an interface member, without being public.</summary>
public sealed partial class CSharpInterfaceMemberPublic : ILocalFixRule
{
    public string Id => "csharp-interface-member-public";

    [GeneratedRegex(@"^'(?<cls>\w+)' does not implement interface member '(?<iface>[\w.<>]+)\.(?<member>\w+)(?:\(.*\))?'\.")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context, "CS0737") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (Cs.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var declaration = CsTypes.Declaration(masked, message.Groups["cls"].Value);
        if (declaration < 0) return null;

        var member = Regex.Escape(message.Groups["member"].Value);
        var pattern = new Regex($@"^(?<lead>\s*)(?<access>(?:private|protected|internal)\s+)?(?:(?:static|virtual|override|async|abstract)\s+)*[\w<>\[\],.?]+\s+{member}\s*[({{]");

        var lines = CsTypes.Members(masked, declaration).Where(i => pattern.IsMatch(masked[i])).ToList();
        if (lines is not [var index]) return null;

        var match = pattern.Match(masked[index]);
        var original = source.Lines[index];
        var access = match.Groups["access"];
        var lead = match.Groups["lead"].Length;

        var corrected = access.Success
            ? original[..access.Index] + "public " + original[(access.Index + access.Length)..]
            : original[..lead] + "public " + original[lead..];

        return LocalFix.ReplaceLine(
            Id, $"Make {message.Groups["member"].Value} public",
            $"Everything an interface declares is public, so whatever implements `{message.Groups["iface"].Value}.{message.Groups["member"].Value}` " +
            "has to be public too - and a class member with no modifier is private.",
            source.Path, index + 1, corrected);
    }
}

/// <summary><c>CS0506</c>: <c>override</c> of a base method that is not <c>virtual</c>.</summary>
public sealed partial class CSharpVirtualBase : ILocalFixRule
{
    public string Id => "csharp-virtual-base";

    [GeneratedRegex(@"^'(?<derived>[\w.]+)(?:\(.*\))?': cannot override inherited member '(?<base>\w+)\.(?<member>\w+)(?:\(.*\))?' because it is not marked virtual, abstract, or override$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context, "CS0506") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (Cs.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var declaration = CsTypes.Declaration(masked, message.Groups["base"].Value);
        if (declaration < 0) return null;

        var member = Regex.Escape(message.Groups["member"].Value);
        var pattern = new Regex($@"^(?<lead>\s*(?:(?:public|protected|internal|private)\s+)*)[\w<>\[\],.?]+\s+{member}\s*[({{]");

        var lines = CsTypes.Members(masked, declaration)
            .Where(i => pattern.IsMatch(masked[i]) && !Regex.IsMatch(masked[i], @"\b(?:static|virtual|abstract|override|sealed)\b"))
            .ToList();

        if (lines is not [var index]) return null;

        var lead = pattern.Match(masked[index]).Groups["lead"].Length;
        var original = source.Lines[index];

        return LocalFix.ReplaceLine(
            Id, $"Mark {message.Groups["base"].Value}.{message.Groups["member"].Value} virtual",
            $"`override` can only replace a method the base class allows to be replaced - one marked `virtual`. " +
            $"`{message.Groups["base"].Value}.{message.Groups["member"].Value}` is not, so nothing could override it.",
            source.Path, index + 1, original[..lead] + "virtual " + original[lead..]);
    }
}

/// <summary><c>CS0115: no suitable method found to override</c> - a misspelt override.</summary>
public sealed partial class CSharpOverrideTypo : ILocalFixRule
{
    public string Id => "csharp-override-typo";

    [GeneratedRegex(@"^'(?<cls>\w+)\.(?<member>\w+)(?:\(.*\))?': no suitable method found to override$")]
    private static partial Regex Message();

    private static readonly string[] ObjectMembers = ["ToString", "Equals", "GetHashCode"];

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context, "CS0115") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (Cs.Locate(context) is not { } at || at.Index < 0) return null;

        var (source, number, line, index) = at;
        var member = message.Groups["member"].Value;
        if (!line[index..].StartsWith(member, StringComparison.Ordinal)) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var declaration = CsTypes.Declaration(masked, message.Groups["cls"].Value);
        if (declaration < 0) return null;

        var candidates = new HashSet<string>(ObjectMembers, StringComparer.Ordinal);
        var bases = Regex.Match(masked[declaration], $@"\b{Regex.Escape(message.Groups["cls"].Value)}\s*(?:<[^>]*>)?\s*:\s*(?<bases>[^{{]+)");

        foreach (Match name in Regex.Matches(bases.Groups["bases"].Value, @"[A-Za-z_]\w*"))
        {
            var baseDeclaration = CsTypes.Declaration(masked, name.Value);
            if (baseDeclaration < 0) continue;

            foreach (var i in CsTypes.Members(masked, baseDeclaration))
                if (Regex.Match(masked[i], @"\b(?:virtual|abstract|override)\b[^(=]*\s(?<name>\w+)\s*[({]") is { Success: true } m)
                    candidates.Add(m.Groups["name"].Value);
        }

        if (CodeText.Nearest(member, candidates.Where(c => c != member)) is not { } right) return null;

        return LocalFix.ReplaceLine(
            Id, $"Rename {member} to {right}",
            $"`override` says `{member}` replaces a method it inherits, and nothing it inherits is called `{member}`. The inherited method " +
            $"within a letter or two of it is `{right}`.",
            source.Path, number, line[..index] + right + line[(index + member.Length)..]);
    }
}

/// <summary><c>CS0542</c>: <c>public void Dog(string name)</c> inside <c>class Dog</c> - a constructor with a return type.</summary>
public sealed partial class CSharpConstructorReturnType : ILocalFixRule
{
    public string Id => "csharp-constructor-return-type";

    [GeneratedRegex(@"^'(?<name>\w+)': member names cannot be the same as their enclosing type$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context, "CS0542") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (Cs.Locate(context) is not { } at) return null;

        var (source, number, line, _) = at;
        var name = message.Groups["name"].Value;
        var method = Regex.Match(line, $@"^\s*(?:(?:public|protected|internal|private)\s+)?(?<void>void\s+){Regex.Escape(name)}\s*\(");
        if (!method.Success) return null;

        var remove = method.Groups["void"];

        return LocalFix.ReplaceLine(
            Id, $"Remove void from the {name} constructor",
            $"A constructor has no return type. With `void` in front, `{name}(...)` is an ordinary method - and C# does not allow a " +
            "method to share its class's name.",
            source.Path, number, line[..remove.Index] + line[(remove.Index + remove.Length)..]);
    }
}

/// <summary><c>CS0051</c> and friends: a public member whose signature uses a type that is not public.</summary>
public sealed partial class CSharpInconsistentAccessibility : ILocalFixRule
{
    public string Id => "csharp-inconsistent-accessibility";

    [GeneratedRegex(@"^Inconsistent accessibility: (?:parameter|return|field|property|indexer return|base class) type '(?<type>[^']+)' is less accessible than ")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context, "CS0050", "CS0051", "CS0052", "CS0053", "CS0060") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (Cs.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        foreach (Match name in Regex.Matches(message.Groups["type"].Value, @"[A-Za-z_]\w*").Reverse())
        {
            var declaration = CsTypes.Declaration(masked, name.Value);
            if (declaration < 0) continue;

            var type = Regex.Match(masked[declaration], $@"^(?<lead>\s*)(?<access>(?:internal|private|protected)\s+)?(?:(?:static|sealed|abstract|partial)\s+)*(?:class|struct|interface|enum|record)\s+{Regex.Escape(name.Value)}\b");
            if (!type.Success || Regex.IsMatch(masked[declaration], @"\bpublic\b")) return null;

            var original = source.Lines[declaration];
            var access = type.Groups["access"];
            var lead = type.Groups["lead"].Length;

            var corrected = access.Success
                ? original[..access.Index] + "public " + original[(access.Index + access.Length)..]
                : original[..lead] + "public " + original[lead..];

            return LocalFix.ReplaceLine(
                Id, $"Make {name.Value} public",
                $"A public member can be used from anywhere, so every type in its signature has to be usable from anywhere too. `{name.Value}` has " +
                "no access modifier, which makes it internal - visible only inside this project. Making it public matches the member " +
                "(making the member less public would too, if it is not meant to be used from outside).",
                source.Path, declaration + 1, corrected);
        }

        return null;
    }
}

// ======================================================================= members and values

/// <summary><c>CS0200: Property ... cannot be assigned to -- it is read only</c> on an auto-property with only <c>get</c>.</summary>
public sealed partial class CSharpReadOnlyProperty : ILocalFixRule
{
    public string Id => "csharp-read-only-property";

    [GeneratedRegex(@"^Property or indexer '(?<cls>\w+)\.(?<prop>\w+)' cannot be assigned to -- it is read only$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context, "CS0200") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (Cs.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var declaration = CsTypes.Declaration(masked, message.Groups["cls"].Value);
        if (declaration < 0) return null;

        var property = new Regex($@"\b{Regex.Escape(message.Groups["prop"].Value)}\s*\{{\s*(?<get>get\s*;)\s*\}}");
        var lines = CsTypes.Members(masked, declaration).Where(i => property.IsMatch(masked[i])).ToList();
        if (lines is not [var index]) return null;

        var get = property.Match(masked[index]).Groups["get"];
        var after = get.Index + get.Length;
        var original = source.Lines[index];

        return LocalFix.ReplaceLine(
            Id, $"Give {message.Groups["prop"].Value} a set",
            $"`{message.Groups["prop"].Value}` only has `get`, so it can be read but only ever assigned inside the class's constructor. " +
            "`set;` lets other code assign it (`init;` would allow that only while the object is being created).",
            source.Path, index + 1, original[..after] + " set;" + original[after..]);
    }
}

/// <summary><c>CS0176</c>: a static member reached through an object instead of its class.</summary>
public sealed partial class CSharpStaticThroughInstance : ILocalFixRule
{
    public string Id => "csharp-static-through-instance";

    [GeneratedRegex(@"^Member '(?<type>[\w.]+)\.(?<member>\w+)(?:\(.*\))?' cannot be accessed with an instance reference; qualify it with a type name instead$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context, "CS0176") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (Cs.Locate(context) is not { } at || at.Index < 0) return null;

        var (source, number, line, index) = at;
        var member = Regex.Escape(message.Groups["member"].Value);
        var access = Regex.Match(line[index..], $@"^(?<receiver>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s*\.\s*{member}\b");
        if (!access.Success) return null;

        var type = message.Groups["type"].Value;
        var receiver = access.Groups["receiver"];

        return LocalFix.ReplaceLine(
            Id, $"Use {type}.{message.Groups["member"].Value}",
            $"`{message.Groups["member"].Value}` is static: it belongs to `{type}` itself, not to any one object, so it is reached through the class name.",
            source.Path, number, line[..index] + type + line[(index + receiver.Length)..]);
    }
}

/// <summary><c>CS0428</c>: a method named without the brackets that call it.</summary>
public sealed partial class CSharpMethodGroup : ILocalFixRule
{
    public string Id => "csharp-method-group";

    [GeneratedRegex(@"^Cannot convert method group '(?<method>\w+)' to non-delegate type '[^']+'\. Did you intend to invoke the method\?$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context, "CS0428") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (Cs.Locate(context) is not { } at || at.Index < 0) return null;

        var (source, number, line, index) = at;
        var method = message.Groups["method"].Value;
        var name = Regex.Match(CodeText.Mask(line, Syntax.CLike)[index..], $@"\b{Regex.Escape(method)}\b(?!\s*\()");
        if (!name.Success) return null;

        var end = index + name.Index + name.Length;

        return LocalFix.ReplaceLine(
            Id, $"Call {method}()",
            $"Without brackets `{method}` names the method rather than calling it. `{method}()` runs it and gives its result.",
            source.Path, number, line[..end] + "()" + line[end..]);
    }
}

/// <summary><c>CS0128</c> / <c>CS0136</c>: a variable declared a second time where an assignment was meant.</summary>
public sealed partial class CSharpRedefinition : ILocalFixRule
{
    public string Id => "csharp-redefinition";

    [GeneratedRegex(@"^A local (?:variable or function|or parameter) named '(?<name>\w+)' (?:is already defined in this scope|cannot be declared in this scope)")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context, "CS0128", "CS0136") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (Cs.Locate(context) is not { } at) return null;

        var (source, number, line, _) = at;
        var name = Regex.Escape(message.Groups["name"].Value);
        var redeclared = Regex.Match(line, $@"^(?<lead>\s*)(?!return\b)[\w<>\[\],.?]+\s+{name}\s*=\s*(?<value>.+?)\s*;(?<tail>\s*(?://.*)?)$");
        if (!redeclared.Success || Regex.IsMatch(line, @"^\s*(?:const|using)\b")) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var (first, _) = JavaCode.EnclosingMethod(masked, number - 1);
        var earlier = new Regex($@"(?<![\w.])[\w<>\[\],.?]+\s+{name}\s*[=;]");

        var original = Enumerable.Range(first, Math.Max(0, number - 1 - first)).LastOrDefault(i => earlier.IsMatch(masked[i]), -1);
        if (original < 0) return null;

        var variable = message.Groups["name"].Value;

        return LocalFix.ReplaceLine(
            Id, $"Assign to {variable} instead of declaring it again",
            $"`{variable}` is already declared on line {original + 1}. Writing the type again declares a second variable with the same name; " +
            "without it, the line gives the existing one a new value.",
            source.Path, number, $"{redeclared.Groups["lead"].Value}{variable} = {redeclared.Groups["value"].Value};{redeclared.Groups["tail"].Value}");
    }
}

// ======================================================================= conversions

/// <summary><c>CS0019</c>: <c>word[0] == "a"</c>, and <c>"10" - 1</c> - operand types that cannot meet.</summary>
public sealed partial class CSharpOperandTypes : ILocalFixRule
{
    public string Id => "csharp-operand-types";

    [GeneratedRegex(@"^Operator '(?<op>[^']+)' cannot be applied to operands of type '(?<left>[^']+)' and '(?<right>[^']+)'$")]
    private static partial Regex Message();

    private static readonly Dictionary<string, string> Parsers = new(StringComparer.Ordinal)
    {
        ["int"] = "int.Parse", ["long"] = "long.Parse", ["double"] = "double.Parse", ["decimal"] = "decimal.Parse", ["float"] = "float.Parse",
    };

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context, "CS0019") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (Cs.Locate(context) is not { } at || at.Index < 0) return null;

        var (source, number, line, index) = at;
        var op = message.Groups["op"].Value;
        var left = message.Groups["left"].Value;
        var right = message.Groups["right"].Value;

        var masked = CodeText.Mask(line, Syntax.CLike);
        var end = Cs.ExpressionEnd(masked, index);
        var opAt = TopLevel(masked, index, end, op);
        if (opAt < 0) return null;

        var (leftStart, leftEnd) = Trimmed(line, index, opAt);
        var (rightStart, rightEnd) = Trimmed(line, opAt + op.Length, end);
        if (leftEnd <= leftStart || rightEnd <= rightStart) return null;

        if (op is "==" or "!=" && (left, right) is ("char", "string") or ("string", "char"))
        {
            var (start, stop) = left == "string" ? (leftStart, leftEnd) : (rightStart, rightEnd);
            var literal = line[start..stop];
            if (literal.Length != 3 || literal[0] != '"' || literal[2] != '"' || literal[1] is '\\' or '\'') return null;

            return LocalFix.ReplaceLine(
                Id, $"Compare with the char '{literal[1]}'",
                "Indexing a string gives a `char`, and a char compares with another char - one letter in single quotes. In double quotes it is a string.",
                source.Path, number, line[..start] + $"'{literal[1]}'" + line[stop..]);
        }

        if (op is "-" or "*" or "/" or "%" && (left == "string") != (right == "string"))
        {
            var number_ = left == "string" ? right : left;
            if (!Parsers.TryGetValue(number_, out var parser)) return null;

            var (start, stop) = left == "string" ? (leftStart, leftEnd) : (rightStart, rightEnd);
            var operand = line[start..stop];

            string replacement;
            if (Regex.Match(operand, @"^""(?<digits>-?\d+(?:\.\d+)?)""$") is { Success: true } quoted) replacement = quoted.Groups["digits"].Value;
            else if (Regex.IsMatch(operand, @"^[A-Za-z_][\w.]*(?:\(\))?$")) replacement = $"{parser}({operand})";
            else return null;

            return LocalFix.ReplaceLine(
                Id, $"Convert {operand} to a number",
                $"`{operand}` is text, and `{op}` only works on numbers - C# will not read digits out of a string by itself. " +
                (replacement.StartsWith(parser, StringComparison.Ordinal) ? $"`{parser}` does, and throws a FormatException if the text is not a number." : "Without the quotes it is the number."),
                source.Path, number, line[..start] + replacement + line[stop..]);
        }

        return null;
    }

    private static int TopLevel(string masked, int from, int to, string op)
    {
        var depth = 0;

        for (var i = from; i + op.Length <= to; i++)
        {
            var c = masked[i];

            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') depth--;
            else if (depth == 0 && string.CompareOrdinal(masked, i, op, 0, op.Length) == 0 &&
                     (op.Length > 1 || i + 1 >= masked.Length || masked[i + 1] is not ('=' or '>')))
                return i;
        }

        return -1;
    }

    private static (int Start, int End) Trimmed(string line, int start, int end)
    {
        while (start < end && char.IsWhiteSpace(line[start])) start++;
        while (end > start && char.IsWhiteSpace(line[end - 1])) end--;
        return (start, end);
    }
}

/// <summary><c>CS1503: Argument 1: cannot convert from 'string' to 'int'</c>.</summary>
public sealed partial class CSharpArgumentConversion : ILocalFixRule
{
    public string Id => "csharp-argument-conversion";

    [GeneratedRegex(@"^Argument \d+: cannot convert from '(?<from>[^']+)' to '(?<to>[^']+)'$")]
    private static partial Regex Message();

    private static readonly HashSet<string> Numbers = ["int", "long", "double", "decimal", "float", "short", "byte"];

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context, "CS1503") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (Cs.Locate(context) is not { } at || at.Index < 0) return null;

        var (source, number, line, start) = at;
        var from = message.Groups["from"].Value;
        var to = message.Groups["to"].Value;

        var masked = CodeText.Mask(line, Syntax.CLike);
        var end = Cs.ExpressionEnd(masked, start);
        var expression = line[start..end].TrimEnd();
        if (expression.Length == 0) return null;

        end = start + expression.Length;
        var simple = !CsTypes.NeedsBrackets(CodeText.Mask(expression, Syntax.CLike));

        string? replacement = null;
        string explanation = "";

        if (from == "string" && (Numbers.Contains(to) || to == "bool") && simple)
        {
            replacement = $"{to}.Parse({expression})";
            explanation = $"`{expression}` is text, and the method wants a {to}. `{to}.Parse` reads one out of it - and throws a FormatException if the text is not one.";
        }
        else if (to == "string" && (Numbers.Contains(from) || from is "bool" or "char"))
        {
            replacement = simple ? $"{expression}.ToString()" : $"({expression}).ToString()";
            explanation = $"The method wants a string, and a {from} is not one. `ToString()` gives its text.";
        }
        else if (from is "double" or "float" or "decimal" or "long" && to is "int" or "long" && from != to)
        {
            replacement = simple ? $"({to}){expression}" : $"({to})({expression})";
            explanation = $"The method wants a {to}, and a {from} can hold values a {to} cannot, so C# wants the conversion written out. The cast drops whatever does not fit.";
        }

        if (replacement is null) return null;

        return LocalFix.ReplaceLine(Id, $"Convert the argument to {to}", explanation, source.Path, number, line[..start] + replacement + line[end..]);
    }
}

/// <summary><c>CS0266</c> / <c>CS0029</c> between collections: a LINQ result or a List where a List or an array was declared.</summary>
public sealed partial class CSharpCollectionConversion : ILocalFixRule
{
    public string Id => "csharp-collection-conversion";

    [GeneratedRegex(@"^Cannot implicitly convert type '(?<from>[^']+)' to '(?<to>[^']+)'")]
    private static partial Regex Message();

    [GeneratedRegex(@"System\.(?:Collections\.Generic|Linq)\.")]
    private static partial Regex Namespaces();

    [GeneratedRegex(@"^(?:IEnumerable|IOrderedEnumerable|ICollection|IList|IReadOnlyList|IReadOnlyCollection|List|HashSet)<(?<element>.+)>$|^(?<element>.+)\[\]$")]
    private static partial Regex Sequence();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context, "CS0029", "CS0266") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (Cs.Locate(context) is not { } at || at.Index < 0) return null;

        var from = Namespaces().Replace(message.Groups["from"].Value, "");
        var to = Namespaces().Replace(message.Groups["to"].Value, "");

        if (Sequence().Match(from) is not { Success: true } source || Sequence().Match(to) is not { Success: true } target) return null;
        if (source.Groups["element"].Value != target.Groups["element"].Value) return null;

        var call = to.EndsWith("[]", StringComparison.Ordinal) ? "ToArray"
            : to.StartsWith("List<", StringComparison.Ordinal) ? "ToList"
            : to.StartsWith("HashSet<", StringComparison.Ordinal) ? "ToHashSet"
            : null;

        if (call is null) return null;

        var (file, number, line, start) = at;
        var masked = CodeText.Mask(line, Syntax.CLike);
        var end = Cs.ExpressionEnd(masked, start);
        var expression = line[start..end].TrimEnd();
        if (expression.Length == 0) return null;

        end = start + expression.Length;
        var wrapped = CsTypes.NeedsBrackets(CodeText.Mask(expression, Syntax.CLike)) ? $"({expression}).{call}()" : $"{expression}.{call}()";

        return LocalFix.ReplaceLine(
            Id, $"Make it a {to} with .{call}()",
            from.StartsWith("IEnumerable", StringComparison.Ordinal) || from.StartsWith("IOrdered", StringComparison.Ordinal)
                ? $"A LINQ query like `Where` or `Select` does not make a new collection - it describes one, as an `{from}`. `.{call}()` runs it and collects the results."
                : $"A `{from}` is not a `{to}`, even holding the same elements. `.{call}()` copies them into one.",
            file.Path, number, line[..start] + wrapped + line[end..]);
    }
}

// ======================================================================= control flow

/// <summary><c>CS0163: Control cannot fall through from one case label to another</c> - a missing <c>break</c>.</summary>
public sealed partial class CSharpSwitchFallThrough : ILocalFixRule
{
    public string Id => "csharp-switch-fall-through";

    [GeneratedRegex(@"^\s*(?:case\b.+|default)\s*:\s*$")]
    private static partial Regex Label();

    [GeneratedRegex(@"^\s*(?:break|return|throw|continue|goto)\b")]
    private static partial Regex Exit();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context, "CS0163") || Cs.Locate(context) is not { } at) return null;

        var (source, number, _, _) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var label = number - 1;
        if (!Label().IsMatch(masked[label])) return null;

        var depths = CCode.DepthAtStart(masked);
        var last = -1;

        for (var i = label + 1; i < masked.Count; i++)
        {
            if (depths[i] < depths[label]) break;
            if (depths[i] == depths[label] && (Label().IsMatch(masked[i]) || masked[i].TrimStart().StartsWith('}'))) break;
            if (masked[i].Trim().Length > 0) last = i;
        }

        if (last < 0 || depths[last] != depths[label] || !masked[last].TrimEnd().EndsWith(';') || Exit().IsMatch(masked[last])) return null;

        return LocalFix.Insert(
            Id, "End the case with break",
            "In C# one `case` cannot run on into the next - every section has to end with `break`, `return` or `throw`. This one runs its " +
            "statements and then has nowhere to go.",
            source.Path, last + 2, [CodeText.Indentation(source.Lines[last]) + "break;"]);
    }
}

/// <summary><c>CS0160: A previous catch clause already catches all exceptions of this or of a super type</c>.</summary>
public sealed class CSharpCatchOrder : ILocalFixRule
{
    public string Id => "csharp-catch-order";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context, "CS0160") || Cs.Locate(context) is not { } at) return null;

        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.CLike);
        if (Blocks.SwapCatch(at.Source.Lines, masked, at.Number - 1) is not { } swap) return null;

        return new LocalFix
        {
            RuleId = Id, Title = "Put the more specific catch first", Explanation = Blocks.CatchOrderExplanation,
            File = at.Source.Path, StartLine = swap.Start + 1, RemoveCount = swap.Count, NewLines = swap.Lines,
        };
    }
}

/// <summary><c>CS8641: 'else' cannot start a statement</c> - from <c>if (x);</c>.</summary>
public sealed class CSharpIfSemicolon : ILocalFixRule
{
    public string Id => "csharp-if-semicolon";

    public LocalFix? Propose(LocalFixContext context)
    {
        var recognised = Cs.Is(context, "CS8641") || Cs.Is(context, "CS1525") && context.Error.Message == "Invalid expression term 'else'";
        if (!recognised || Cs.Locate(context) is not { } at) return null;

        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.CLike);

        // Roslyn places the error at the end of the token before the else - the closing brace, a line above it.
        var elseLine = Enumerable.Range(at.Number - 1, 3).FirstOrDefault(i => i < masked.Count && Regex.IsMatch(masked[i], @"\belse\b"), -1);
        if (elseLine < 0 || Blocks.IfSemicolon(at.Source.Lines, masked, elseLine) is not { } fix) return null;

        return LocalFix.ReplaceLine(Id, Blocks.IfSemicolonTitle, Blocks.IfSemicolonExplanation, at.Source.Path, fix.Line + 1, fix.Corrected);
    }
}

/// <summary><c>InvalidOperationException: Collection was modified</c> from removing items inside a foreach over the same list.</summary>
public sealed class CSharpRemoveInForEach : ILocalFixRule
{
    public string Id => "csharp-remove-in-foreach";

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if (error.LanguageId != "csharp" || error.ExceptionType != "System.InvalidOperationException") return null;
        if (!(error.Message ?? "").StartsWith("Collection was modified", StringComparison.Ordinal)) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (Blocks.RemoveInLoop(source.Lines, masked, number - 1, java: false) is not { } loop) return null;

        return new LocalFix
        {
            RuleId = Id,
            Title = "Remove them with RemoveAll instead of inside the loop",
            Explanation = "A foreach walks the list with an enumerator, and changing the list underneath it breaks that walk. " +
                          "`RemoveAll` does the whole loop-and-remove itself, safely.",
            File = source.Path, StartLine = loop.Start + 1, RemoveCount = loop.Count, NewLines = [loop.Line],
        };
    }
}
