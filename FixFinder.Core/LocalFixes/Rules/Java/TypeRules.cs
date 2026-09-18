using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary><c>incompatible types: String cannot be converted to int</c>, and the reverse.</summary>
public sealed partial class JavaStringConversion : ILocalFixRule
{
    public string Id => "java-string-conversion";

    [GeneratedRegex(@"^incompatible types: (?<from>String|int|long|double|float|boolean|char) cannot be converted to (?<to>String|int|long|double|float|boolean|short|byte)$")]
    private static partial Regex Message();

    private static readonly Dictionary<string, string> Parsers = new(StringComparer.Ordinal)
    {
        ["int"] = "Integer.parseInt",
        ["long"] = "Long.parseLong",
        ["double"] = "Double.parseDouble",
        ["float"] = "Float.parseFloat",
        ["boolean"] = "Boolean.parseBoolean",
        ["short"] = "Short.parseShort",
        ["byte"] = "Byte.parseByte",
    };

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        if (!JavaCode.IsCompileError(error)) return null;
        if (Message().Match(error.Message ?? "") is not { Success: true } message) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (source.Line(number) is not { } line) return null;

        var from = message.Groups["from"].Value;
        var to = message.Groups["to"].Value;

        string method;

        if (from == "String" && Parsers.TryGetValue(to, out var parser)) method = parser;
        else if (to == "String" && from != "String") method = "String.valueOf";
        else return null;

        if (CodeText.Caret(context.Output, error) is not { } caret || caret.Echo != line) return null;

        var start = caret.Column;
        var before = line[..start].TrimEnd();

        var assigned = before.EndsWith('=') && !before.EndsWith("==") && (before.Length < 2 || !"!<>+-*/%&|^".Contains(before[^2]));
        if (!assigned && !before.EndsWith("return", StringComparison.Ordinal)) return null;

        var masked = CodeText.Mask(line, Syntax.CLike);
        var depth = 0;
        var end = -1;

        for (var i = start; i < masked.Length; i++)
        {
            if (masked[i] is '(' or '[' or '{') depth++;
            else if (masked[i] is ')' or ']' or '}') depth--;
            else if (masked[i] == ';' && depth == 0) { end = i; break; }
        }

        if (end < 0) return null;

        var expression = line[start..end].TrimEnd();
        if (expression.Length == 0) return null;

        var explanation = method == "String.valueOf"
            ? $"A {from} is not a String in Java, and is not turned into one automatically here; String.valueOf makes its text form."
            : $"A String holding digits is still text to Java. {method} reads the {to} out of it - and throws " +
              "NumberFormatException when the program runs if the text is not one.";

        return LocalFix.ReplaceLine(
            Id,
            $"Convert it with {method}",
            explanation,
            source.Path, number, line[..start] + $"{method}({expression})" + line[(start + expression.Length)..]);
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
                source.Path, number, CCode.ReplaceEach(line, hits, hit => $"\"{hit.Groups["text"].Value}\""));
        }

        if (message == "incompatible types: String cannot be converted to char")
        {
            if (JavaCode.CaretColumn(context, line) is not { } column || column + 3 > line.Length) return null;
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
                source.Path, number, CCode.ReplaceEach(line, hits, hit => $"'{hit.Groups["c"].Value}'"));
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
            Id, string.Join(", ", names.Select(n => $"Use {JavaCode.WrapperTypes[n]} for {n}")),
            "A generic type holds objects, and a primitive such as `int` is not one. Each primitive has an object form - " +
            string.Join(", ", names.Select(n => $"`{JavaCode.WrapperTypes[n]}` for `{n}`")) + " - which Java converts to and from automatically.",
            source.Path, number, CCode.ReplaceEach(line, hits, hit => JavaCode.WrapperTypes[hit.Groups["primitive"].Value]));
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
        if (JavaCode.CaretColumn(context, line) is not { } start) return null;

        var masked = CodeText.Mask(line, Syntax.CLike);
        var expression = line[start..CSharpCode.ExpressionEnd(masked, start)].TrimEnd();
        if (expression.Length == 0) return null;

        var to = lossy.Success ? lossy.Groups["to"].Value : fromObject.Groups["to"].Value;
        var cast = CSharpCode.HasNoOperators(expression) ? $"({to}) {expression}" : $"({to}) ({expression})";

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
        if (JavaCode.CaretColumn(context, line) is not { } column || column >= line.Length || line[column].ToString() != op) return null;

        var first = JavaCode.Note(context, "first type");
        var second = JavaCode.Note(context, "second type");

        Match operand;
        string numberType;

        if (first == "String" && second is not null && JavaCode.ParseMethods.ContainsKey(second))
        {
            operand = LeftOperand().Match(line[..column]);
            numberType = second;
        }
        else if (second == "String" && first is not null && JavaCode.ParseMethods.ContainsKey(first))
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
        var parser = JavaCode.ParseMethods[numberType];

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

/// <summary><c>cannot find symbol: method stream()</c> on an array.</summary>
public sealed partial class JavaArrayStream : ILocalFixRule
{
    public string Id => "java-array-stream";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at) return null;
        if (JavaCode.CannotFindSymbol().Match(context.Error.Message ?? "") is not { Success: true } symbol) return null;
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
            source.Path, number, CCode.ReplaceEach(line, hits, _ => call));
    }
}

/// <summary><c>IllegalFormatConversionException: d != java.lang.Double</c> - a format code for one kind of value given another.</summary>
public sealed partial class JavaFormatConversion : ILocalFixRule
{
    public string Id => "java-format-conversion";

    [GeneratedRegex(@"^(?<code>[a-zA-Z]) != java\.(?:lang|math)\.(?<type>\w+)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.AtRuntime(context, "java.util.IllegalFormatConversionException") is not { } at) return null;
        if (Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;

        var code = message.Groups["code"].Value;
        var wanted = message.Groups["type"].Value switch
        {
            "Double" or "Float" or "BigDecimal" => "f",
            "Integer" or "Long" or "Short" or "Byte" or "BigInteger" => "d",
            "String" => "s",
            "Character" => "c",
            "Boolean" => "b",
            _ => null,
        };

        if (wanted is null || wanted == code) return null;

        var (source, number, line) = at;
        if (!Regex.IsMatch(line, @"\b(?:format|printf|formatted)\s*\(")) return null;

        // The one specifier with that code on the line; two of them and it cannot be told which argument was wrong.
        var specifiers = Regex.Matches(line, $@"%(?:\d+\$)?[-#+ 0,(]*\d*(?:\.\d+)?{Regex.Escape(code)}").ToList();
        if (specifiers is not [var specifier]) return null;

        // A precision means nothing to %d, so it is kept only where the new code uses one.
        var text = specifier.Value[..^1];
        if (wanted is "d" or "c" or "b") text = Regex.Replace(text, @"\.\d+$", "");

        var corrected = text + wanted;

        return LocalFix.ReplaceLine(
            Id, $"Format a {message.Groups["type"].Value} with %{wanted}",
            $"`%{code}` formats {Describe(code)}, and the value given for it is a `{message.Groups["type"].Value}`. Java checks this when the " +
            $"line runs, not when it compiles. `%{wanted}` is the code for {Describe(wanted)}.",
            source.Path, number, line[..specifier.Index] + corrected + line[(specifier.Index + specifier.Length)..]);
    }

    private static string Describe(string code) => code switch
    {
        "d" => "a whole number", "f" => "a decimal number", "s" => "text", "c" => "a character", "b" => "a true or false value",
        _ => $"`%{code}` values",
    };
}

/// <summary><c>Student is not abstract and does not override abstract method compareTo(Object) in Comparable</c> - a raw <c>Comparable</c>.</summary>
/// <remarks>
/// The class already has <c>compareTo(Student)</c>; it is the missing <c>&lt;Student&gt;</c> that makes Java look for
/// <c>compareTo(Object)</c> instead. Tried before the rule that writes missing methods, which would add the wrong one.
/// </remarks>
public sealed partial class JavaRawComparable : ILocalFixRule
{
    public string Id => "java-raw-comparable";

    [GeneratedRegex(@"^(?<class>[\w$]+) is not abstract and does not override abstract method (?<method>compareTo|compare)\((?:Object|T)(?:,(?:Object|T))?\) in (?<interface>Comparable|Comparator)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!JavaCode.IsCompileError(context.Error) || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (JavaCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var interfaceName = message.Groups["interface"].Value;
        var method = message.Groups["method"].Value;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        var raw = Regex.Matches(masked[number - 1], $@"(?<![\w.]){interfaceName}(?!\s*[<\w])").ToList();
        if (raw is not [var mention]) return null;

        // The type the class's own compareTo or compare already takes - a method one level inside this class's body.
        var depths = Brackets.BraceDepths(masked);
        var outer = depths[number - 1];
        var end = number;
        while (end < masked.Count && depths[end + 1] > outer) end++;

        var parameter = method == "compareTo"
            ? $@"\bcompareTo\s*\(\s*(?:final\s+)?(?<type>[A-Z][\w$.<>]*)\s+\w+\s*\)"
            : $@"\bcompare\s*\(\s*(?:final\s+)?(?<type>[A-Z][\w$.<>]*)\s+\w+\s*,\s*(?:final\s+)?\k<type>\s+\w+\s*\)";

        var types = Enumerable.Range(number, Math.Max(0, end - number))
            .Where(i => depths[i] == outer + 1)
            .Select(i => Regex.Match(masked[i], parameter))
            .Where(m => m.Success && m.Groups["type"].Value != "Object")
            .Select(m => m.Groups["type"].Value)
            .Distinct()
            .ToList();

        if (types is not [var type]) return null;

        return LocalFix.ReplaceLine(
            Id, $"Say what it compares: {interfaceName}<{type}>",
            $"`{interfaceName}` without a type means `{interfaceName}<Object>`, so Java looks for `{method}` taking `Object` and does not count " +
            $"the `{method}` taking `{type}` that is already there. `{interfaceName}<{type}>` says what is being compared, and that method is " +
            "then the one it needs.",
            source.Path, number, line[..mention.Index] + $"{interfaceName}<{type}>" + line[(mention.Index + mention.Length)..]);
    }
}

/// <summary><c>cannot find symbol: class T</c> in a method that uses a type parameter it never declared.</summary>
public sealed partial class JavaGenericMethodParameter : ILocalFixRule
{
    public string Id => "java-generic-method-parameter";

    [GeneratedRegex(@"^(?<modifiers>\s*(?:(?:public|private|protected|static|final|synchronized|abstract)\s+)*)(?<rest>(?!return\b|new\b)[\w$<>\[\],.?\s]+?\s+[a-z_$][\w$]*\s*\(.*)$")]
    private static partial Regex Header();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!JavaCode.IsCompileError(context.Error) || JavaCode.CannotFindSymbol().Match(context.Error.Message ?? "") is not { Success: true } symbol) return null;
        if (symbol.Groups["kind"].Value != "class" || !Regex.IsMatch(symbol.Groups["name"].Value, @"^[A-Z]$")) return null;
        if (JavaCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var name = symbol.Groups["name"].Value;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        if (Header().Match(masked[number - 1]) is not { Success: true } header || !masked[number - 1].TrimEnd().EndsWith('{')) return null;
        if (Regex.IsMatch(header.Groups["rest"].Value, @"^\s*<")) return null;
        if (!Regex.IsMatch(masked[number - 1], $@"(?<![\w$]){name}(?![\w$])")) return null;

        // A class or interface that declares the parameter itself has a different problem.
        if (masked.Any(l => Regex.IsMatch(l, $@"\b(?:class|interface|record)\s+[\w$]+\s*<[^>]*\b{name}\b"))) return null;

        var at0 = header.Groups["rest"].Index;

        return LocalFix.ReplaceLine(
            Id, $"Declare the type parameter: <{name}>",
            $"`{name}` is used as a type in this method, but nothing declares it - the class is not generic. A generic method declares its " +
            $"own type parameter, in angle brackets before the return type, and Java then works `{name}` out from each call.",
            source.Path, number, line[..at0] + $"<{name}> " + line[at0..]);
    }
}

/// <summary><c>UnsupportedOperationException</c> from <c>add</c> on a list made by <c>Arrays.asList</c> or <c>List.of</c>.</summary>
public sealed partial class JavaFixedSizeCollection : ILocalFixRule
{
    public string Id => "java-fixed-size-collection";

    [GeneratedRegex(@"(?<![\w.])(?<name>[A-Za-z_$][\w$]*)\s*\.\s*(?:add|addAll|remove|removeIf|removeAll|clear|put|putAll)\s*\(")]
    private static partial Regex Change();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.AtRuntime(context, "java.lang.UnsupportedOperationException") is not { } at) return null;
        if (!Regex.IsMatch(context.Error.RawText, @"java\.util\.(?:AbstractList|ImmutableCollections|AbstractCollection|AbstractMap)")) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (Change().Matches(masked[number - 1]).ToList() is not [var change]) return null;

        var name = change.Groups["name"].Value;
        var declaration = new Regex($@"(?<![\w.]){Regex.Escape(name)}\s*=\s*(?<init>(?<factory>Arrays\.asList|List\.of|Set\.of|Map\.of|Collections\.unmodifiable\w+)\s*\()");
        var found = Enumerable.Range(0, number - 1).Where(i => declaration.IsMatch(masked[i])).ToList();
        if (found is not [var index]) return null;

        var match = declaration.Match(masked[index]);
        var factory = match.Groups["factory"].Value;
        var init = match.Groups["init"];
        var close = Brackets.ClosingParenthesis(masked[index], init.Index + init.Length - 1);
        if (close is null) return null;

        var copy = factory switch
        {
            "Set.of" => "HashSet",
            "Map.of" => "HashMap",
            _ when factory.Contains("Set", StringComparison.Ordinal) => "HashSet",
            _ when factory.Contains("Map", StringComparison.Ordinal) => "HashMap",
            _ => "ArrayList",
        };

        var original = source.Lines[index];
        var type = JavaCode.QualifiedName(source, "java.util", copy);
        var expression = original[init.Index..(close.Value + 1)];

        return LocalFix.ReplaceLine(
            Id, $"Make a copy that can change: new {copy}<>({factory}(...))",
            $"`{factory}` gives back a {(factory == "Arrays.asList" ? "fixed-size list - a view of the array, so nothing can be added or removed" : "collection that cannot be changed at all")}, " +
            $"and `{name}` is then changed. `new {copy}<>(...)` copies the items into an ordinary {copy} that can grow and shrink.",
            source.Path, index + 1, original[..init.Index] + $"new {type}<>({expression})" + original[(close.Value + 1)..]);
    }
}

/// <summary><c>counts.get(word)++</c> - <c>unexpected type</c>, because a value read from a map cannot be added to in place.</summary>
public sealed partial class JavaMapIncrement : ILocalFixRule
{
    public string Id => "java-map-increment";

    [GeneratedRegex(@"^(?<lead>\s*)(?:(?<pre>\+\+|--)\s*)?(?<map>[A-Za-z_$][\w$.]*)\.get\((?<key>[^()]*)\)\s*(?:(?<post>\+\+|--)|(?<op>[+\-*])=\s*(?<amount>[^;]+?))?\s*;(?<tail>.*)$")]
    private static partial Regex Statement();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!JavaCode.IsCompileError(context.Error) || context.Error.Message != "unexpected type") return null;
        if (JavaCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (Statement().Match(line) is not { Success: true } statement) return null;

        var change = statement.Groups["pre"].Success ? statement.Groups["pre"].Value : statement.Groups["post"].Success ? statement.Groups["post"].Value : null;
        var (op, amount) = change is not null
            ? (change == "++" ? "+" : "-", "1")
            : statement.Groups["op"].Success ? (statement.Groups["op"].Value, statement.Groups["amount"].Value.Trim()) : (null, null);

        if (op is null) return null;

        var map = statement.Groups["map"].Value;
        var key = statement.Groups["key"].Value.Trim();
        if (key.Length == 0 || key.Contains(',')) return null;

        return LocalFix.ReplaceLine(
            Id, $"Store the new count back: {map}.put({key}, ...)",
            $"`{map}.get({key})` hands back a copy of the value, not a place to store one, so it cannot be changed with `{change ?? op + "="}`. " +
            $"The new value is worked out and stored back with `put`.",
            source.Path, number, $"{statement.Groups["lead"].Value}{map}.put({key}, {map}.get({key}) {op} {amount});{statement.Groups["tail"].Value}");
    }
}

/// <summary><c>x has private access in Point</c> for a record - whose components are read with <c>p.x()</c>.</summary>
public sealed partial class JavaRecordAccessor : ILocalFixRule
{
    public string Id => "java-record-accessor";

    [GeneratedRegex(@"^(?<field>[\w$]+) has private access in (?<type>[\w$.]+)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!JavaCode.IsCompileError(context.Error) || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (JavaCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var field = message.Groups["field"].Value;
        var type = message.Groups["type"].Value.Split('.')[^1];

        var record = JavaCode.SourceFiles(context).Prepend(source)
            .Select(f => f.Lines.Select(l => Regex.Match(CodeText.Mask(l, Syntax.CLike), $@"\brecord\s+{Regex.Escape(type)}\s*(?:<[^>]*>)?\s*\((?<components>[^)]*)\)")).FirstOrDefault(m => m.Success))
            .FirstOrDefault(m => m is not null);

        if (record is null || !Regex.IsMatch(record.Groups["components"].Value, $@"\b{Regex.Escape(field)}\s*(?:,|$)")) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var receivers = new Regex($@"(?:\b{Regex.Escape(type)}(?:<[^>]*>)?\s+|\bvar\s+)(?<name>[A-Za-z_$][\w$]*)\s*(?:=\s*new\s+{Regex.Escape(type)}\b)?");
        var names = masked.SelectMany(l => receivers.Matches(l)).Where(m => m.Value.StartsWith(type, StringComparison.Ordinal) || m.Value.Contains("new", StringComparison.Ordinal)).Select(m => m.Groups["name"].Value).ToHashSet();

        // Every component of this record the build reported on the same line, so p.x + p.y is corrected in one go.
        var fields = context.AllErrors
            .Where(e => JavaCode.IsCompileError(e) && LocalFixContext.OwnFrame(e)?.Line == number)
            .Select(e => Message().Match(e.Message ?? ""))
            .Where(m => m.Success && m.Groups["type"].Value.Split('.')[^1] == type && Regex.IsMatch(record.Groups["components"].Value, $@"\b{Regex.Escape(m.Groups["field"].Value)}\s*(?:,|$)"))
            .Select(m => m.Groups["field"].Value)
            .Append(field)
            .Distinct()
            .ToList();

        var uses = Regex.Matches(masked[number - 1], $@"(?<![\w$.])(?<receiver>[A-Za-z_$][\w$]*)\.(?<field>{string.Join("|", fields.Select(Regex.Escape))})\b(?!\s*[(=])")
            .Where(m => names.Contains(m.Groups["receiver"].Value))
            .ToList();
        if (uses.Count == 0) return null;

        var list = string.Join(", ", fields.Select(f => f + "()"));

        return LocalFix.ReplaceLine(
            Id, $"Read the record's components with {list}",
            $"`{type}` is a record, and a record keeps each component in a private field, read through a method of the same name. " +
            $"`{list}` {(fields.Count == 1 ? "is that method" : "are those methods")}.",
            source.Path, number, CCode.ReplaceEach(line, uses, m => m.Value + "()"));
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
