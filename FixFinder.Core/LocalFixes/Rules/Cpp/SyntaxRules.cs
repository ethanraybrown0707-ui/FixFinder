using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary><c>System.out.println</c>, <c>Console.WriteLine</c> and <c>print</c> in C++, which prints with <c>std::cout</c>.</summary>
public sealed partial class CppForeignPrint : ILocalFixRule
{
    public string Id => "cpp-foreign-print";

    [GeneratedRegex(@"^(?<lead>\s*)(?:System\s*\.\s*out\s*\.\s*(?<call>println|print)|Console\s*\.\s*(?<call>WriteLine|Write)|(?<call>print))\s*\((?<args>.*)\)\s*;(?<tail>.*)$")]
    private static partial Regex Statement();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (CppCode.UndeclaredName(context.Error) is not ("System" or "Console" or "print") || CppCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (!CCode.Includes(source, "iostream")) return null;

        var masked = CodeText.Mask(line, Syntax.CLike);
        if (Statement().Match(masked) is not { Success: true } statement) return null;

        var call = statement.Groups["call"].Value;
        var (language, newline) = call switch
        {
            "println" => ("Java", true),
            "print" when masked.Contains("System", StringComparison.Ordinal) => ("Java", false),
            "WriteLine" => ("C#", true),
            "Write" => ("C#", false),
            _ => ("Python", true),
        };

        var args = statement.Groups["args"];
        var pieces = new List<string>();

        if (masked[args.Index..(args.Index + args.Length)].Trim().Length > 0)
        {
            var arguments = CppCode.SplitTopLevel(masked, args.Index, args.Index + args.Length, ',');

            // Python's print separates its arguments with a space. Anywhere else a comma means a format string.
            if (arguments.Count > 1 && language != "Python") return null;

            foreach (var (start, end) in arguments)
            {
                if (arguments.Count > 1 && pieces.Count > 0) pieces.Add("\" \"");

                var joined = CppCode.SplitTopLevel(masked, start, end, '+');
                var parts = joined.Select(p => line[p.Start..p.End].Trim()).ToList();

                if (parts.Any(part => part.Length == 0 || part.StartsWith("$\"", StringComparison.Ordinal) || part.StartsWith("@\"", StringComparison.Ordinal)))
                    return null;

                // "Total: " + total joins text in Java and C#; a sum with no text in it is still a sum.
                if (parts.Count > 1 && parts.Any(part => part.StartsWith('"'))) pieces.AddRange(parts);
                else pieces.Add(line[start..end].Trim());
            }
        }

        if (pieces.Count == 0 && !newline) return null;

        var cout = CppCode.StdQualified(source, "cout");
        var endl = CppCode.StdQualified(source, "endl");
        var printed = string.Join(" << ", pieces.Prepend(cout).Concat(newline ? [endl] : []));

        return LocalFix.ReplaceLine(
            Id, $"Print with {cout}",
            $"`{(language == "Python" ? "print" : line.Trim().Split('(')[0])}` is {language}. C++ prints by sending each value to `{cout}` with `<<`" +
            (newline ? $", and `{endl}` ends the line." : "."),
            source.Path, number, $"{statement.Groups["lead"].Value}{printed};{line[statement.Groups["tail"].Index..]}");
    }
}

/// <summary><c>null</c>, <c>True</c>, <c>boolean</c>, <c>String</c> - words from other languages that C++ spells differently.</summary>
public sealed class CppForeignWord : ILocalFixRule
{
    public string Id => "cpp-foreign-word";

    private static readonly Dictionary<string, (string Right, string From)> Words = new(StringComparer.Ordinal)
    {
        ["null"] = ("nullptr", "Java, C# and JavaScript"),
        ["None"] = ("nullptr", "Python"),
        ["True"] = ("true", "Python"),
        ["False"] = ("false", "Python"),
        ["boolean"] = ("bool", "Java"),
        ["String"] = ("string", "Java and C#"),
    };

    public LocalFix? Propose(LocalFixContext context)
    {
        if (CppCode.UndeclaredName(context.Error) is not { } word || !Words.TryGetValue(word, out var entry)) return null;
        if (CppCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var right = entry.Right;

        if (right == "string")
        {
            if (!CppCode.HasStdString(source)) return null;
            right = CppCode.StdQualified(source, "string");
        }

        var hits = CppCode.UnqualifiedUses(CodeText.Mask(line, Syntax.CLike), word);
        if (hits.Count == 0) return null;

        var corrected = line;
        foreach (var index in Enumerable.Reverse(hits)) corrected = corrected[..index] + right + corrected[(index + word.Length)..];

        return LocalFix.ReplaceLine(
            Id, $"Write {word} as {right}",
            $"`{word}` is {entry.From}. In C++ it is `{right}`.",
            source.Path, number, corrected);
    }
}

/// <summary><c>std::cout &gt;&gt; total</c> and <c>std::cin &lt;&lt; age</c> - the stream's arrows pointing the wrong way.</summary>
public sealed partial class CppStreamArrows : ILocalFixRule
{
    public string Id => "cpp-stream-arrows";

    [GeneratedRegex(@"^no match for 'operator(?<op>>>|<<)' \(operand types are 'std::(?<stream>ostream|istream)'")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^binary '(?<op>>>|<<)': no operator found which takes a left-hand operand of type 'std::(?<stream>ostream|istream)'")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"(?<![\w:])(?:std\s*::\s*)?(?<name>cout|cerr|clog|cin)\b")]
    private static partial Regex Stream();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if ((CCode.GccMessage(error, GccMessage()) ?? CCode.MsvcMessage(error, "C2678", MsvcMessage())) is not { } message) return null;
        if (CppCode.Locate(context) is not { } at) return null;

        var output = message.Groups["stream"].Value == "ostream";
        var (wrong, right) = output ? (">>", "<<") : ("<<", ">>");
        if (message.Groups["op"].Value != wrong) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.CLike);

        var streams = Stream().Matches(masked).Where(m => (m.Groups["name"].Value == "cin") != output).ToList();
        if (streams is not [var stream]) return null;

        var from = stream.Index + stream.Length;
        var end = masked.IndexOf(';', from);
        if (end < 0) end = masked.Length;

        var arrows = new List<int>();
        var depth = 0;

        for (var i = from; i + 1 < end; i++)
        {
            if (masked[i] is '(' or '[' or '{') depth++;
            else if (masked[i] is ')' or ']' or '}') depth--;
            else if (depth == 0 && masked.AsSpan(i, 2).SequenceEqual(wrong) && (i + 2 >= end || masked[i + 2] is not ('<' or '>' or '=')))
            {
                arrows.Add(i);
                i++;
            }
        }

        if (arrows.Count == 0) return null;

        var corrected = line;
        foreach (var index in Enumerable.Reverse(arrows)) corrected = corrected[..index] + right + corrected[(index + 2)..];

        return LocalFix.ReplaceLine(
            Id, output ? $"Print with << into {stream.Value}" : $"Read with >> from {stream.Value}",
            "The arrows point the way the data goes: `<<` sends a value into an output stream like `std::cout`, and `>>` takes one out of " +
            $"an input stream like `std::cin`. `{stream.Value} {wrong}` points the wrong way.",
            source.Path, number, corrected);
    }
}

/// <summary><c>void main()</c> - gcc's <c>'::main' must return 'int'</c>.</summary>
public sealed partial class CppMainReturnsInt : ILocalFixRule
{
    public string Id => "cpp-main-returns-int";

    [GeneratedRegex(@"^'::main' must return 'int'$")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^(?<lead>\s*)void(?<rest>\s+main\s*\()")]
    private static partial Regex VoidMain();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (CCode.GccMessage(context.Error, GccMessage()) is null || CppCode.Locate(context) is not { } at) return null;
        if (VoidMain().Match(at.Line) is not { Success: true } main) return null;

        return LocalFix.ReplaceLine(
            Id, "Declare main as int main",
            "`main` gives back an `int` - the exit code, 0 for success - and returns it without a `return` at the end. MSVC lets " +
            "`void main` through, but the C++ standard and gcc do not.",
            at.Source.Path, at.Number, main.Groups["lead"].Value + "int" + at.Line[(main.Groups["rest"].Index)..]);
    }
}

/// <summary><c>std::string name = 'Ada';</c> - text in single quotes, which C++ reads as one number.</summary>
public sealed partial class CppMultiCharString : ILocalFixRule
{
    public string Id => "cpp-multichar-string";

    public LocalFix? Propose(LocalFixContext context)
    {
        var message = context.Error.Message ?? "";
        if (!message.Contains("'int'", StringComparison.Ordinal) || !message.Contains("string", StringComparison.Ordinal)) return null;
        if (CppCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;

        var multi = CppCode.StringLiterals(line)
            .Where(l => l.Quote == '\'' && Regex.Replace(line[(l.Start + 1)..(l.End - 1)], @"\\.", "x").Length > 1)
            .ToList();

        if (multi.Count == 0) return null;

        var corrected = line;
        foreach (var (start, end, _) in Enumerable.Reverse(multi))
            corrected = corrected[..start] + "\"" + line[(start + 1)..(end - 1)].Replace("\"", "\\\"") + "\"" + corrected[end..];

        var first = line[multi[0].Start..multi[0].End];

        return LocalFix.ReplaceLine(
            Id, "Put the text in double quotes",
            $"Single quotes hold one character, like `'A'`. Text longer than that goes in double quotes. C++ reads {first} as a single number " +
            "made from the characters' codes, which is why the error talks about an `int`.",
            source.Path, number, corrected);
    }
}

/// <summary><c>std::vector&lt;int&gt; values();</c> - empty brackets that declare a function instead of a variable.</summary>
public sealed partial class CppVexingParse : ILocalFixRule
{
    public string Id => "cpp-vexing-parse";

    [GeneratedRegex(@"^request for member '(?<member>\w+)' in '(?<name>\w+)', which is of non-class type '[^']*\(\)'$")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^left of '\.(?<member>\w+)' must have class/struct/union$")]
    private static partial Regex MsvcMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if ((CCode.GccMessage(error, GccMessage()) ?? CCode.MsvcMessage(error, "C2228", MsvcMessage())) is not { } message) return null;
        if (CppCode.Locate(context) is not { } at) return null;

        var (source, number, _) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        var names = message.Groups["name"].Success
            ? [message.Groups["name"].Value]
            : Regex.Matches(masked[number - 1], $@"(?<![\w.>])(?<name>[A-Za-z_]\w*)\s*\.\s*{Regex.Escape(message.Groups["member"].Value)}\b")
                .Select(m => m.Groups["name"].Value).Distinct().ToList();

        if (names is not [var name]) return null;

        var (first, _) = CCode.EnclosingFunction(masked, number - 1);
        var declaration = new Regex($@"^(?<lead>\s*)(?<type>(?!return\b)[A-Za-z_][\w:]*(?:\s*<[^;()]*>)?)\s+{Regex.Escape(name)}\s*(?<brackets>\(\s*\))\s*;\s*$");

        for (var i = number - 2; i > first; i--)
        {
            if (declaration.Match(masked[i]) is not { Success: true } found) continue;

            var brackets = found.Groups["brackets"];
            var original = source.Lines[i];

            return LocalFix.ReplaceLine(
                Id, $"Remove the empty brackets after {name}",
                $"With empty brackets, `{found.Groups["type"].Value} {name}();` does not make a variable - C++ reads it as declaring a " +
                $"function called `{name}` that returns a {found.Groups["type"].Value}. Without the brackets it is the variable that was meant. " +
                "(This is C++'s \"most vexing parse\".)",
                source.Path, i + 1, original[..brackets.Index].TrimEnd() + original[(brackets.Index + brackets.Length)..]);
        }

        return null;
    }
}

/// <summary><c>"Hello, " + "world"</c> - two string literals, which are character arrays and cannot be added.</summary>
public sealed partial class CppLiteralConcatenation : ILocalFixRule
{
    public string Id => "cpp-literal-concatenation";

    [GeneratedRegex(@"^invalid operands of types 'const char ?\[\d+\]' and 'const char ?\[\d+\]' to binary 'operator\+'$")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^'\+': cannot add two pointers$")]
    private static partial Regex MsvcMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if ((CCode.GccMessage(error, GccMessage()) ?? CCode.MsvcMessage(error, "C2110", MsvcMessage())) is null) return null;
        if (CppCode.Locate(context) is not { } at || !CppCode.HasStdString(at.Source)) return null;

        var (source, number, line) = at;
        var literals = CppCode.StringLiterals(line).Where(l => l.Quote == '"').ToList();

        var starts = new List<int>();

        for (var k = 0; k + 1 < literals.Count; k++)
        {
            if (line[literals[k].End..literals[k + 1].Start].Trim() != "+") continue;
            if (k > 0 && line[literals[k - 1].End..literals[k].Start].Trim() == "+") continue;

            // Something already added in front - a std::string - makes this pair legal.
            if (line[..literals[k].Start].TrimEnd().EndsWith('+')) continue;

            starts.Add(k);
        }

        if (starts is not [var only]) return null;

        var (start, end, _) = literals[only];
        var type = CppCode.StdQualified(source, "string");

        return LocalFix.ReplaceLine(
            Id, $"Make the first piece a {type}",
            $"{line[start..end]} and the text after it are not `{type}`s but arrays of characters, and C++ cannot add two arrays. " +
            $"Making the first one a `{type}` lets `+` join them: a `{type}` plus text is a `{type}`.",
            source.Path, number, line[..start] + $"{type}({line[start..end]})" + line[end..]);
    }
}

/// <summary><c>add(int a, int b) { ... }</c> with no return type - MSVC's <c>C4430 missing type specifier - int assumed</c>.</summary>
/// <remarks>gcc accepts it with a warning, the way C once did, and the program runs; the C++ standard does not.</remarks>
public sealed partial class CppMissingReturnType : ILocalFixRule
{
    public string Id => "cpp-missing-return-type";

    [GeneratedRegex(@"^missing type specifier - int assumed")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"^\s*(?<name>[A-Za-z_]\w*)\s*\((?<parameters>[^()]*)\)\s*(?:const\s*)?\{?\s*$")]
    private static partial Regex Header();

    [GeneratedRegex(@"\breturn\b\s*(?<value>[^;]*);")]
    private static partial Regex Return();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (CCode.MsvcMessage(context.Error, "C4430", MsvcMessage()) is null || CppCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        if (Brackets.BraceDepths(masked)[number - 1] != 0 || Header().Match(masked[number - 1]) is not { Success: true } header) return null;
        if (number >= masked.Count) return null;

        var name = header.Groups["name"].Value;
        if (CStandardLibrary.Keywords.Contains(name)) return null;

        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var parameter in header.Groups["parameters"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = Regex.Match(parameter, @"^(?:const\s+)?(?<type>[A-Za-z_][\w:]*(?:\s*<[^>]*>)?)\s*[&*]?\s*(?<name>[A-Za-z_]\w*)$");
            if (parts.Success) parameters[parts.Groups["name"].Value] = parts.Groups["type"].Value;
        }

        var (_, end) = CCode.EnclosingFunction(masked, number);
        var returned = Enumerable.Range(number - 1, end - number + 2)
            .SelectMany(i => Return().Matches(masked[i]).Select(m => source.Lines[i].Substring(m.Groups["value"].Index, m.Groups["value"].Length).Trim()))
            .ToList();

        var types = returned.All(value => value.Length == 0)
            ? ["void"]
            : returned.Select(value => TypeOf(value, parameters, source)).Distinct().ToList();

        if (types is not [{ } type]) return null;

        var column = header.Groups["name"].Index;

        return LocalFix.ReplaceLine(
            Id, $"Give {name} the return type {type}",
            "C++ needs every function to say what it returns - it will not assume `int` the way old C did. " +
            (type == "void" ? $"`{name}` returns nothing, which is written `void`." : $"Everything `{name}` returns is {(type == "int" ? "an" : "a")} `{type}`."),
            source.Path, number, line[..column] + type + " " + line[column..]);
    }

    private static string? TypeOf(string value, Dictionary<string, string> parameters, SourceFile source)
    {
        if (value is "true" or "false") return "bool";
        if (Regex.IsMatch(value, @"^\d+$")) return "int";
        if (Regex.IsMatch(value, @"^\d+\.\d*(?:[eE][-+]?\d+)?$")) return "double";
        if (Regex.IsMatch(value, @"^""(?:[^""\\]|\\.)*""$")) return CppCode.HasStdString(source) ? CppCode.StdQualified(source, "string") : null;

        // Parameters of one type, combined with arithmetic, give that type back.
        if (!Regex.IsMatch(value, @"^[\w\s+\-*/%()]+$")) return null;

        var words = Regex.Matches(value, @"[A-Za-z_]\w*").Select(m => m.Value).ToList();
        if (words.Count == 0 || words.Any(word => !parameters.ContainsKey(word))) return null;

        var types = words.Select(word => parameters[word]).Distinct().ToList();
        return types is [var only] && only is "int" or "long" or "double" or "float" or "short" ? only : null;
    }
}

/// <summary>gcc's <c>need 'typename' before 'std::vector&lt;T&gt;::const_iterator' because ... is a dependent scope</c>.</summary>
public sealed partial class CppTypename : ILocalFixRule
{
    public string Id => "cpp-typename";

    /// <remarks>GCC 15 reports errors inside a template's body under <c>-Wtemplate-body</c>, and names the option after the message.</remarks>
    [GeneratedRegex(@"^need 'typename' before '(?<name>[^']+)' because '[^']+' is a dependent scope(?:\s*\[-[\w=+-]+\])?$")]
    private static partial Regex GccMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (CCode.GccMessage(context.Error, GccMessage()) is not { } message || CppCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var name = message.Groups["name"].Value;
        var tokens = Regex.Matches(name, @"\w+|[^\w\s]").Select(t => Regex.Escape(t.Value));
        var pattern = new Regex($@"(?<!\btypename\s+)(?<![\w:]){string.Join(@"\s*", tokens)}");

        var hits = pattern.Matches(CodeText.Mask(line, Syntax.CLike)).ToList();
        if (hits is not [var hit]) return null;

        return LocalFix.ReplaceLine(
            Id, $"Add typename before {name}",
            $"Inside a template, C++ cannot tell whether `{name}` names a type or a value until it knows what the template's types are - so it " +
            "assumes a value, unless told otherwise. `typename` in front says it is a type.",
            source.Path, number, line[..hit.Index] + "typename " + line[hit.Index..]);
    }
}

/// <summary>A default argument written again on the definition, after the declaration already gave it.</summary>
public sealed partial class CppDefaultArgumentRepeated : ILocalFixRule
{
    public string Id => "cpp-default-argument-repeated";

    [GeneratedRegex(@"^default argument given for parameter \d+ of '")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^'(?<name>\w+)': redefinition of default argument: parameter \d+$")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"(?<name>[A-Za-z_][\w:]*)\s*(?<open>\()")]
    private static partial Regex Function();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if ((CCode.GccMessage(error, GccMessage()) ?? CCode.MsvcMessage(error, "C2572", MsvcMessage())) is null) return null;
        if (CppCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var code = masked[number - 1];

        if (Function().Match(code) is not { Success: true } function || Brackets.ClosingParenthesis(code, function.Groups["open"].Index) is not { } close) return null;

        var name = function.Groups["name"].Value.Split("::")[^1];
        var declaredEarlier = Enumerable.Range(0, number - 1).Any(i =>
            Regex.IsMatch(masked[i], $@"(?<![\w.>]){Regex.Escape(name)}\s*\([^;]*=[^;]*\)\s*;"));

        if (!declaredEarlier) return null;

        var corrected = line;
        var removed = 0;

        foreach (var (start, end) in Enumerable.Reverse(CppCode.SplitTopLevel(code, function.Groups["open"].Index + 1, close, ',')))
        {
            var equals = code.IndexOf('=', start, end - start);
            if (equals < 0) continue;

            var from = equals;
            while (from > start && char.IsWhiteSpace(line[from - 1])) from--;

            var to = end;
            while (to > equals && char.IsWhiteSpace(line[to - 1])) to--;

            corrected = corrected[..from] + corrected[to..];
            removed++;
        }

        if (removed == 0) return null;

        return LocalFix.ReplaceLine(
            Id, $"Leave the default argument to {name}'s declaration",
            $"A default argument is given once, where the function is first declared - `{name}`'s declaration above already has it. The " +
            "definition repeats the parameter without the default.",
            source.Path, number, corrected);
    }
}
