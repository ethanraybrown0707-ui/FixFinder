using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

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
        if (!CSharpCode.HasErrorCode(context, "CS0029", "CS0266") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (CSharpCode.Locate(context) is not { } at) return null;

        var (source, number, line, start) = at;
        if (start < 0) return null;

        var from = message.Groups["from"].Value;
        var to = message.Groups["to"].Value;

        var masked = CodeText.Mask(line, Syntax.CLike);
        var end = CSharpCode.ExpressionEnd(masked, start);
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
            else if (CSharpCode.HasNoOperators(expression))
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
            replacement = NumberLiteral().IsMatch(expression) ? $"\"{expression}\"" : CSharpCode.HasNoOperators(expression) ? $"{expression}.ToString()" : $"({expression}).ToString()";
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
            replacement = CSharpCode.HasNoOperators(expression) ? $"({to}){expression}" : $"({to})({expression})";
            explanation = $"A {from} can hold values a {to} cannot, so C# wants the conversion written out. The cast drops anything that does not fit, such as the part after the decimal point.";
        }

        if (replacement is null) return null;

        return LocalFix.ReplaceLine(
            Id, $"Convert the {from} to {to}", explanation,
            source.Path, number, line[..start] + replacement + line[end..]);
    }
}

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
        if (!CSharpCode.HasErrorCode(context, "CS0019") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (CSharpCode.Locate(context) is not { } at || at.Index < 0) return null;

        var (source, number, line, index) = at;
        var op = message.Groups["op"].Value;
        var left = message.Groups["left"].Value;
        var right = message.Groups["right"].Value;

        var masked = CodeText.Mask(line, Syntax.CLike);
        var end = CSharpCode.ExpressionEnd(masked, index);
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
        if (!CSharpCode.HasErrorCode(context, "CS1503") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (CSharpCode.Locate(context) is not { } at || at.Index < 0) return null;

        var (source, number, line, start) = at;
        var from = message.Groups["from"].Value;
        var to = message.Groups["to"].Value;

        var masked = CodeText.Mask(line, Syntax.CLike);
        var end = CSharpCode.ExpressionEnd(masked, start);
        var expression = line[start..end].TrimEnd();
        if (expression.Length == 0) return null;

        end = start + expression.Length;
        var simple = !CSharpCode.NeedsBrackets(CodeText.Mask(expression, Syntax.CLike));

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
        if (!CSharpCode.HasErrorCode(context, "CS0029", "CS0266") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (CSharpCode.Locate(context) is not { } at || at.Index < 0) return null;

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
        var end = CSharpCode.ExpressionEnd(masked, start);
        var expression = line[start..end].TrimEnd();
        if (expression.Length == 0) return null;

        end = start + expression.Length;
        var wrapped = CSharpCode.NeedsBrackets(CodeText.Mask(expression, Syntax.CLike)) ? $"({expression}).{call}()" : $"{expression}.{call}()";

        return LocalFix.ReplaceLine(
            Id, $"Make it a {to} with .{call}()",
            from.StartsWith("IEnumerable", StringComparison.Ordinal) || from.StartsWith("IOrdered", StringComparison.Ordinal)
                ? $"A LINQ query like `Where` or `Select` does not make a new collection - it describes one, as an `{from}`. `.{call}()` runs it and collects the results."
                : $"A `{from}` is not a `{to}`, even holding the same elements. `.{call}()` copies them into one.",
            file.Path, number, line[..start] + wrapped + line[end..]);
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
        if (!CSharpCode.HasErrorCode(context, "CS0165") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (CSharpCode.Locate(context) is not { } at) return null;

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

/// <summary><c>'Student' does not implement interface member 'IComparable.CompareTo(object?)'</c> - for a class that already has <c>CompareTo(Student)</c>.</summary>
public sealed partial class CSharpGenericInterface : ILocalFixRule
{
    public string Id => "csharp-generic-interface";

    [GeneratedRegex(@"^'(?<class>\w+)' does not implement interface member '(?<interface>IComparable|IEquatable|IComparer)\.(?<member>CompareTo|Equals|Compare)\((?:object\??|T\??)(?:, ?(?:object\??|T\??))?\)'$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CSharpCode.HasErrorCode(context, "CS0535") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (CSharpCode.Locate(context) is not { } at) return null;

        var (source, number, line, _) = at;
        var interfaceName = message.Groups["interface"].Value;
        var member = message.Groups["member"].Value;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        if (Regex.Matches(masked[number - 1], $@"(?<![\w.]){interfaceName}(?!\s*[<\w])").ToList() is not [var mention]) return null;

        var depths = Brackets.BraceDepths(masked);
        var outer = depths[number - 1];
        var end = number;
        while (end < masked.Count && depths[end + 1] > outer) end++;

        var parameter = member == "Compare"
            ? $@"\bCompare\s*\(\s*(?<type>[A-Z]\w*\??)\s+\w+\s*,\s*\k<type>\s+\w+\s*\)"
            : $@"\b{member}\s*\(\s*(?<type>[A-Z]\w*\??)\s+\w+\s*\)";

        var types = Enumerable.Range(number - 1, Math.Max(0, end - number + 1))
            .Select(i => Regex.Match(masked[i], parameter))
            .Where(m => m.Success)
            .Select(m => m.Groups["type"].Value.TrimEnd('?'))
            .Where(t => t is not ("Object" or "object"))
            .Distinct()
            .ToList();

        if (types is not [var type]) return null;

        return LocalFix.ReplaceLine(
            Id, $"Say what it compares: {interfaceName}<{type}>",
            $"`{interfaceName}` without a type is the old interface whose `{member}` takes `object`, so the `{member}` taking `{type}` that is " +
            $"already here does not count. `{interfaceName}<{type}>` is the generic one, and that method is exactly what it asks for - " +
            "`List.Sort()` and the other collections use it.",
            source.Path, number, line[..mention.Index] + $"{interfaceName}<{type}>" + line[(mention.Index + mention.Length)..]);
    }
}

/// <summary><c>points[0].X = 5</c> - <c>Cannot modify the return value ... because it is not a variable</c>, for a struct in a list.</summary>
public sealed partial class CSharpStructInCollection : ILocalFixRule
{
    public string Id => "csharp-struct-in-collection";

    [GeneratedRegex(@"^Cannot modify the return value of '(?<container>[^']+)\.this\[[^\]]*\]' because it is not a variable$")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?<lead>\s*)(?<list>[A-Za-z_][\w.]*)\[(?<index>[^\[\]]+)\]\.(?<member>[A-Za-z_]\w*)\s*(?<assignment>(?:[+\-*/%]?=)\s*[^;]+|\+\+|--)\s*;(?<tail>\s*(?://.*)?)$")]
    private static partial Regex Statement();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CSharpCode.HasErrorCode(context, "CS1612") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (CSharpCode.Locate(context) is not { } at || Statement().Match(at.Line) is not { Success: true } statement) return null;

        var (source, number, _, _) = at;
        var list = statement.Groups["list"].Value;
        var index = statement.Groups["index"].Value.Trim();
        if (Regex.IsMatch(index, @"\(|\+\+|--")) return null;

        var simple = list.Split('.')[^1];
        var name = simple.Length > 1 && simple.EndsWith('s') ? simple[..^1] : "item";
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (masked.Any(l => Regex.IsMatch(l, $@"(?<![\w.]){Regex.Escape(name)}(?!\w)"))) name = "copy";
        if (masked.Any(l => Regex.IsMatch(l, $@"(?<![\w.]){Regex.Escape(name)}(?!\w)"))) return null;

        var lead = statement.Groups["lead"].Value;
        var assignment = statement.Groups["assignment"].Value.Trim();
        var member = statement.Groups["member"].Value;
        var change = assignment is "++" or "--" ? $"{name}.{member}{assignment};" : $"{name}.{member} {assignment};";

        return new LocalFix
        {
            RuleId = Id,
            Title = $"Change a copy of the struct, then store it back in {list}",
            Explanation =
                $"The items of `{list}` are structs, and a struct is a value: `{list}[{index}]` hands back a copy, so changing `{member}` on it " +
                "would change a copy that is thrown away straight after - C# refuses to let that silently do nothing. The copy is taken, " +
                $"changed, and put back. (If the type were a `class` instead, `{list}[{index}].{member}` would change the item itself.)",
            File = source.Path, StartLine = number, RemoveCount = 1,
            NewLines = [$"{lead}var {name} = {list}[{index}];", $"{lead}{change}", $"{lead}{list}[{index}] = {name};{statement.Groups["tail"].Value}"],
        };
    }
}

/// <summary><c>yield return</c> in a method declared to return <c>List&lt;int&gt;</c> - an iterator returns <c>IEnumerable&lt;int&gt;</c>.</summary>
public sealed partial class CSharpIteratorReturnType : ILocalFixRule
{
    public string Id => "csharp-iterator-return-type";

    [GeneratedRegex(@"^The body of '[^']+' cannot be an iterator block because '(?<type>[^']+)' is not an iterator interface type$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CSharpCode.HasErrorCode(context, "CS1624") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (CSharpCode.Locate(context) is not { } at) return null;

        var (source, number, line, _) = at;
        var type = message.Groups["type"].Value;

        var element = Regex.Match(type, @"^(?:System\.Collections\.Generic\.)?(?:List|IList|ICollection|IReadOnlyList|IReadOnlyCollection|Collection|HashSet|Queue|Stack)<(?<element>.+)>$") is { Success: true } generic
            ? generic.Groups["element"].Value
            : Regex.Match(type, @"^(?<element>[\w.<>]+)\[\]$") is { Success: true } array ? array.Groups["element"].Value : null;

        if (element is null) return null;

        var written = Regex.Escape(type.Replace("System.Collections.Generic.", "", StringComparison.Ordinal)).Replace(@"\ ", @"\s*", StringComparison.Ordinal);
        if (Regex.Matches(line, $@"(?<![\w.]){written}(?=\s+\w+\s*[(<])").ToList() is not [var declared]) return null;

        return LocalFix.ReplaceLine(
            Id, $"Return IEnumerable<{element}> from an iterator",
            $"`yield return` makes the method an iterator: it hands items out one at a time, as they are asked for, rather than building a " +
            $"`{type}`. An iterator's return type has to say that - `IEnumerable<{element}>`. A caller that needs a list can call `.ToList()` on it.",
            source.Path, number, line[..declared.Index] + $"IEnumerable<{element}>" + line[(declared.Index + declared.Length)..]);
    }
}

/// <summary><c>p.X = 5</c> on a record - init-only, where a changed copy is made with <c>with</c>.</summary>
public sealed partial class CSharpRecordWith : ILocalFixRule
{
    public string Id => "csharp-record-with";

    [GeneratedRegex(@"^Init-only property or indexer '(?<type>\w+)\.(?<property>\w+)' can only be assigned")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?<lead>\s*)(?<variable>[a-z_]\w*)\.(?<property>[A-Z]\w*)\s*=\s*(?<value>[^;]+?)\s*;(?<tail>\s*(?://.*)?)$")]
    private static partial Regex Assignment();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CSharpCode.HasErrorCode(context, "CS8852") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (CSharpCode.Locate(context) is not { } at || Assignment().Match(at.Line) is not { Success: true } assignment) return null;

        var (source, number, _, _) = at;
        var type = message.Groups["type"].Value;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        if (!masked.Any(l => Regex.IsMatch(l, $@"\brecord\s+(?:class\s+|struct\s+)?{Regex.Escape(type)}\b"))) return null;

        var variable = assignment.Groups["variable"].Value;
        var property = assignment.Groups["property"].Value;

        // A local that can be given a new value - not a readonly field or a parameter declared in.
        if (!masked.Take(number - 1).Any(l => Regex.IsMatch(l, $@"(?:\bvar|\b{Regex.Escape(type)}\??)\s+{Regex.Escape(variable)}\s*=")) ) return null;

        return LocalFix.ReplaceLine(
            Id, $"Make a changed copy: {variable} = {variable} with {{ {property} = ... }}",
            $"`{type}` is a record, and its properties can only be set when it is made - that is what keeps a record's value from changing " +
            $"underneath the code holding it. `with` makes a copy with `{property}` changed, and `{variable}` then holds the copy.",
            source.Path, number,
            $"{assignment.Groups["lead"].Value}{variable} = {variable} with {{ {property} = {assignment.Groups["value"].Value} }};{assignment.Groups["tail"].Value}");
    }
}

/// <summary><c>new Thread(Work())</c> - calling the method, where the thread wanted the method to call.</summary>
public sealed partial class CSharpDelegateCalled : ILocalFixRule
{
    public string Id => "csharp-delegate-called";

    [GeneratedRegex(@"^Argument \d+: cannot convert from 'void' to '(?:System\.Threading\.)?(?:ThreadStart|ParameterizedThreadStart)'|^Argument \d+: cannot convert from 'void' to 'System\.(?:Action|Func<[^']*>)'")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?<name>[A-Za-z_][\w.]*)\s*\((?<arguments>[^()]*)\)")]
    private static partial Regex Call();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CSharpCode.HasErrorCode(context, "CS1503") || !Message().IsMatch(context.Error.Message ?? "")) return null;
        if (CSharpCode.Locate(context) is not { Index: >= 0 } at) return null;

        var (source, number, line, index) = at;
        if (Call().Match(line[index..]) is not { Success: true } call) return null;

        var name = call.Groups["name"].Value;
        var arguments = call.Groups["arguments"].Value.Trim();
        var replacement = arguments.Length == 0 ? name : $"() => {name}({arguments})";

        return LocalFix.ReplaceLine(
            Id, arguments.Length == 0 ? $"Pass the method itself: {name}" : $"Pass a lambda that calls it: () => {name}(...)",
            $"`{name}(...)` runs the method straight away, on this thread, and passes on what it returns - nothing. The thread needs the " +
            $"method to run later, on its own: {(arguments.Length == 0 ? $"`{name}` without brackets" : "a lambda that makes the call")}.",
            source.Path, number, line[..index] + replacement + line[(index + call.Length)..]);
    }
}

/// <summary><c>CS0200: Property ... cannot be assigned to -- it is read only</c> on an auto-property with only <c>get</c>.</summary>
public sealed partial class CSharpReadOnlyProperty : ILocalFixRule
{
    public string Id => "csharp-read-only-property";

    [GeneratedRegex(@"^Property or indexer '(?<cls>\w+)\.(?<prop>\w+)' cannot be assigned to -- it is read only$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CSharpCode.HasErrorCode(context, "CS0200") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (CSharpCode.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var declaration = CSharpCode.TypeDeclarationLine(masked, message.Groups["cls"].Value);
        if (declaration < 0) return null;

        var property = new Regex($@"\b{Regex.Escape(message.Groups["prop"].Value)}\s*\{{\s*(?<get>get\s*;)\s*\}}");
        var lines = CSharpCode.MemberLines(masked, declaration).Where(i => property.IsMatch(masked[i])).ToList();
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
