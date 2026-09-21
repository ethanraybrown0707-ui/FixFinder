using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary><c>C4477</c>: a printf conversion that does not match the argument - <c>%s</c> given an int.</summary>
public sealed partial class CFormatSpecifier : ILocalFixRule
{
    public string Id => "c-format-specifier";

    [GeneratedRegex(@"^'(?<func>\w+)' : format string '(?<spec>[^']+)' requires an argument of type '(?<want>[^']+)', but variadic argument (?<arg>\d+) has type '(?<have>[^']+)'")]
    private static partial Regex Message();

    [GeneratedRegex(@"%(?:%|(?<flags>[-+ #0]*)(?<width>\*|\d+)?(?:\.(?<precision>\*|\d+))?(?<length>hh|h|ll|l|L|z|j|t|I64|I32|I)?(?<conversion>[diouxXeEfFgGaAcspn]))")]
    internal static partial Regex Conversion();

    private static readonly Dictionary<string, string> Conversions = new(StringComparer.Ordinal)
    {
        ["int"] = "d", ["unsigned int"] = "u", ["long"] = "ld", ["unsigned long"] = "lu", ["__int64"] = "lld",
        ["unsigned __int64"] = "llu", ["long long"] = "lld", ["unsigned long long"] = "llu", ["double"] = "f",
        ["char *"] = "s", ["const char *"] = "s",
    };

    internal static readonly Dictionary<string, int> FormatPosition = new(StringComparer.Ordinal)
    {
        ["printf"] = 0, ["printf_s"] = 0, ["fprintf"] = 1, ["fprintf_s"] = 1, ["sprintf"] = 1, ["sprintf_s"] = 2, ["snprintf"] = 2,
        ["scanf"] = 0, ["scanf_s"] = 0, ["fscanf"] = 1, ["sscanf"] = 1,
    };

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if (CCode.MsvcMessage(error, "C4477", Message()) is not { } message || CCode.Locate(context) is not { } at) return null;
        if (!Conversions.TryGetValue(message.Groups["have"].Value, out var fits)) return null;
        if (!FormatPosition.TryGetValue(message.Groups["func"].Value, out var position)) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.CLike);

        var calls = Regex.Matches(masked, $@"(?<![\w.]){Regex.Escape(message.Groups["func"].Value)}\s*(?<open>\()");
        if (calls.Count != 1) return null;

        var open = calls[0].Groups["open"].Index;
        if (Brackets.ClosingParenthesis(masked, open) is not { } close) return null;

        var arguments = Arguments(masked, open + 1, close);
        if (position >= arguments.Count) return null;

        var (argumentStart, argumentEnd) = arguments[position];
        var literal = line[argumentStart..argumentEnd].Trim();
        if (literal.Length < 2 || literal[0] != '"' || literal[^1] != '"') return null;

        var literalStart = line.IndexOf('"', argumentStart);
        var conversions = Conversion().Matches(literal[1..^1]).Where(c => c.Value != "%%").ToList();

        if (conversions.Any(c => c.Groups["width"].Value == "*" || c.Groups["precision"].Value == "*")) return null;

        var index = int.Parse(message.Groups["arg"].Value) - 1;
        if (index < 0 || index >= conversions.Count || conversions[index].Value != message.Groups["spec"].Value) return null;

        var wrong = conversions[index];
        var keepPrecision = fits is "f" or "s" && wrong.Groups["precision"].Success;
        var replacement = "%" + wrong.Groups["flags"].Value + wrong.Groups["width"].Value +
                          (keepPrecision ? "." + wrong.Groups["precision"].Value : "") + fits;

        if (replacement == wrong.Value) return null;

        var from = literalStart + 1 + wrong.Index;

        return LocalFix.ReplaceLine(
            Id, $"Change {wrong.Value} to {replacement}",
            $"`{wrong.Value}` tells {message.Groups["func"].Value} to expect a {message.Groups["want"].Value}, but the argument is a " +
            $"{message.Groups["have"].Value}. printf cannot tell - it reads the value as the wrong type, and for `%s` that means " +
            "following a number as if it were an address, which crashes.",
            source.Path, number, line[..from] + replacement + line[(from + wrong.Length)..]) with
        {
            ResolvesWarning = error.Message,
        };
    }

    internal static List<(int Start, int End)> Arguments(string masked, int from, int to)
    {
        var arguments = new List<(int, int)>();
        var depth = 0;
        var start = from;

        for (var i = from; i < to; i++)
        {
            if (masked[i] is '(' or '[' or '{') depth++;
            else if (masked[i] is ')' or ']' or '}') depth--;
            else if (masked[i] == ',' && depth == 0)
            {
                arguments.Add((start, i));
                start = i + 1;
            }
        }

        arguments.Add((start, to));
        return arguments;
    }
}

/// <summary>A printf or scanf argument of the wrong kind: <c>scanf("%d", age)</c> without its <c>&amp;</c>, and <c>printf("%s",
/// grade)</c> with a single char - read from MSVC's <c>C4477</c> or gcc's <c>-Wformat</c>.</summary>
public sealed partial class CFormatArgument : ILocalFixRule
{
    public string Id => "c-format-argument";

    [GeneratedRegex(@"^'(?<func>\w+)' : format string '(?<spec>[^']+)' requires an argument of type '(?<want>[^']+)', but variadic argument (?<arg>\d+) has type '(?<have>[^']+)'")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"^format '(?<spec>%[^']+)' expects argument of type '(?<want>[^']+)', but argument (?<arg>\d+) has type '(?<have>[^']+)'")]
    private static partial Regex GccMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if (error.ExceptionType != "compile warning") return null;

        var msvc = CCode.MsvcMessage(error, "C4477", MsvcMessage());
        var gcc = msvc is null ? CCode.GccMessage(error, GccMessage()) : null;

        if ((msvc ?? gcc) is not { } message || CCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.CLike);

        var calls = CFormatSpecifier.FormatPosition.Keys
            .SelectMany(f => Regex.Matches(masked, $@"(?<![\w.])(?<func>{Regex.Escape(f)})\s*(?<open>\()"))
            .Where(m => msvc is null || m.Groups["func"].Value == msvc.Groups["func"].Value)
            .ToList();

        if (calls is not [var call]) return null;

        var func = call.Groups["func"].Value;
        var position = CFormatSpecifier.FormatPosition[func];
        var open = call.Groups["open"].Index;
        if (Brackets.ClosingParenthesis(masked, open) is not { } close) return null;

        var arguments = CFormatSpecifier.Arguments(masked, open + 1, close);

        var variadic = int.Parse(message.Groups["arg"].Value) - (gcc is not null ? position + 1 : 0);
        if (variadic < 1 || position + variadic >= arguments.Count) return null;

        var (start, end) = arguments[position + variadic];
        var argument = line[start..end].Trim();
        var argumentStart = start + (end - start) - line[start..end].TrimStart().Length;

        var want = message.Groups["want"].Value.Replace(" ", "");
        var have = message.Groups["have"].Value.Replace(" ", "");

        if (!Regex.IsMatch(argument, @"^[A-Za-z_]\w*$")) return null;

        if (want == have + "*")
        {
            return LocalFix.ReplaceLine(
                Id, $"Pass &{argument} so {func} can store into it",
                $"`{func}` stores what it reads, so it needs the address of `{argument}` - `&{argument}` - not its current value. Given the " +
                "value, it writes to whatever address that number happens to be, and the program is killed.",
                source.Path, number, line[..argumentStart] + "&" + line[argumentStart..]) with
            {
                ResolvesWarning = error.Message,
            };
        }

        var isChar = CodeText.MaskAll(source.Lines, Syntax.CLike).Any(text => Regex.IsMatch(text, $@"\bchar\s+{Regex.Escape(argument)}\s*(?:=|;|,)"));
        if (want != "char*" || have is not ("int" or "char") || !isChar) return null;

        var (formatStart, formatEnd) = arguments[position];
        var literal = line[formatStart..formatEnd].Trim();
        if (literal.Length < 2 || literal[0] != '"' || literal[^1] != '"') return null;

        var conversions = CFormatSpecifier.Conversion().Matches(literal[1..^1]).Where(c => c.Value != "%%").ToList();
        if (conversions.Any(c => c.Groups["width"].Value == "*" || c.Groups["precision"].Value == "*")) return null;
        if (variadic - 1 >= conversions.Count || conversions[variadic - 1].Value != message.Groups["spec"].Value) return null;

        var wrong = conversions[variadic - 1];
        var letter = line.IndexOf('"', formatStart) + 1 + wrong.Groups["conversion"].Index;

        return LocalFix.ReplaceLine(
            Id, $"Change {wrong.Value} to %c",
            $"`{argument}` is a single `char`, and `%s` expects the address of a whole string - so printf took the letter's code for an " +
            "address and followed it. `%c` prints one character.",
            source.Path, number, line[..letter] + "c" + line[(letter + 1)..]) with
        {
            ResolvesWarning = error.Message,
        };
    }
}

/// <summary><c>-&gt;</c> on a struct, or <c>.</c> on a pointer to one.</summary>
public sealed partial class CMemberOperator : ILocalFixRule
{
    public string Id => "c-member-operator";

    [GeneratedRegex(@"^'->(?<member>\w+)': left operand has '(?:struct|union|class)' type, use '\.'$")]
    private static partial Regex ArrowOnValue();

    [GeneratedRegex(@"^'\.(?<member>\w+)': left operand points to '(?:struct|union|class)', use '->'$")]
    private static partial Regex DotOnPointer();

    [GeneratedRegex(@"^invalid type argument of '->' \(have '[^']+'\)$")]
    private static partial Regex GccArrow();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if (CCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.CLike);

        string wrong, right;
        MatchCollection hits;

        if ((CCode.MsvcMessage(error, "C2232", ArrowOnValue()) ?? CCode.GccMessage(error, GccArrow())) is { } arrow)
        {
            var member = arrow.Groups["member"].Success ? Regex.Escape(arrow.Groups["member"].Value) : @"\w+";
            hits = Regex.Matches(masked, $@"->(?=\s*{member}\b)");
            (wrong, right) = ("->", ".");
        }
        else if (CCode.MsvcMessage(error, "C2231", DotOnPointer()) is { } dot)
        {
            hits = Regex.Matches(masked, $@"(?<=[\w\])])\.(?=\s*{Regex.Escape(dot.Groups["member"].Value)}\b)");
            (wrong, right) = (".", "->");
        }
        else
        {
            return null;
        }

        if (hits.Count != 1) return null;

        return LocalFix.ReplaceLine(
            Id, $"Use {right} instead of {wrong}",
            wrong == "->"
                ? "`->` reaches a member through a pointer. This is the struct itself, not a pointer to it, so it is `.`."
                : "This is a pointer to a struct, and a member is reached through a pointer with `->`.",
            source.Path, number, CCode.ReplaceEach(line, hits, _ => right));
    }
}

/// <summary><c>int grid[][]</c> as a parameter - <c>array type has incomplete element type</c> / <c>C2087 missing subscript</c>.</summary>
public sealed partial class CArrayParameterSize : ILocalFixRule
{
    public string Id => "c-array-parameter-size";

    [GeneratedRegex(@"^'(?<name>\w+)': missing subscript$")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"^array type has incomplete element type")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"(?<func>[A-Za-z_]\w*)\s*(?<open>\()")]
    private static partial Regex Header();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        var msvc = CCode.MsvcMessage(error, "C2087", MsvcMessage());
        if (msvc is null && CCode.GccMessage(error, GccMessage()) is null) return null;
        if (CCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var code = masked[number - 1];

        if (Header().Match(code) is not { Success: true } header || Brackets.ClosingParenthesis(code, header.Groups["open"].Index) is not { } close) return null;

        var func = header.Groups["func"].Value;
        var parameters = Split(code, header.Groups["open"].Index + 1, close);

        var position = parameters.FindIndex(p =>
            Regex.Match(code[p.Start..p.End], @"(?<name>[A-Za-z_]\w*)\s*\[\s*\]\s*\[\s*\]") is { Success: true } m &&
            (msvc is null || m.Groups["name"].Value == msvc.Groups["name"].Value));

        if (position < 0) return null;

        var parameter = Regex.Match(line[parameters[position].Start..parameters[position].End], @"(?<name>[A-Za-z_]\w*)\s*\[\s*\]\s*\[(?<empty>\s*)\]");
        var name = parameter.Groups["name"].Value;

        var columns = new HashSet<string>(StringComparer.Ordinal);
        var call = new Regex($@"(?<![\w.]){Regex.Escape(func)}\s*(?<open>\()");

        for (var i = 0; i < masked.Count; i++)
        {
            if (i == number - 1) continue;

            foreach (Match m in call.Matches(masked[i]))
            {
                if (Brackets.ClosingParenthesis(masked[i], m.Groups["open"].Index) is not { } end) continue;

                var arguments = Split(masked[i], m.Groups["open"].Index + 1, end);
                if (position >= arguments.Count) continue;

                var argument = masked[i][arguments[position].Start..arguments[position].End].Trim();
                if (!Regex.IsMatch(argument, @"^[A-Za-z_]\w*$")) return null;

                var declared = masked
                    .Select(text => Regex.Match(text, $@"\b(?:int|char|double|float|long|short|unsigned|signed|bool|struct\s+\w+|[A-Za-z_]\w*_t)\s+{Regex.Escape(argument)}\s*\[\s*\w+\s*\]\s*\[\s*(?<columns>\w+)\s*\]"))
                    .FirstOrDefault(d => d.Success);
                if (declared is null) return null;

                columns.Add(declared.Groups["columns"].Value);
            }
        }

        if (columns is not { Count: 1 } || columns.Single() == "0") return null;

        var count = columns.Single();
        var empty = parameters[position].Start + parameter.Groups["empty"].Index;

        return LocalFix.ReplaceLine(
            Id, $"Give {name} its column count: {name}[][{count}]",
            $"C has to know how many columns a two-dimensional array parameter has - it uses that to find where `{name}[1]` starts, " +
            $"one whole row in. Only the first size may be left out, and `{func}` is called with arrays that have {count} columns.",
            source.Path, number, line[..empty] + count + line[(empty + parameter.Groups["empty"].Length)..]);
    }

    private static List<(int Start, int End)> Split(string masked, int from, int to)
    {
        var parts = new List<(int, int)>();
        var depth = 0;
        var start = from;

        for (var i = from; i < to; i++)
        {
            if (masked[i] is '(' or '[' or '{') depth++;
            else if (masked[i] is ')' or ']' or '}') depth--;
            else if (masked[i] == ',' && depth == 0)
            {
                parts.Add((start, i));
                start = i + 1;
            }
        }

        parts.Add((start, to));
        return parts;
    }
}

/// <summary><c>void worker(void *arg)</c> handed to <c>pthread_create</c>, which calls it as <c>void *(*)(void *)</c>.</summary>
public sealed partial class CPthreadStartRoutine : ILocalFixRule
{
    public string Id => "c-pthread-start-routine";

    [GeneratedRegex(@"^passing argument 3 of 'pthread_create' from incompatible pointer type")]
    private static partial Regex GccMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.ExceptionType is not ("compile warning" or "compile error") || CCode.GccMessage(context.Error, GccMessage()) is null) return null;
        if (CCode.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (Regex.Match(masked[at.Number - 1], @"\bpthread_create\s*\([^,]+,[^,]+,\s*&?\s*(?<function>[A-Za-z_]\w*)\s*,") is not { Success: true } call) return null;

        var function = call.Groups["function"].Value;
        var definition = new Regex($@"^(?<lead>\s*(?:static\s+)?)void\s+{Regex.Escape(function)}\s*\((?<parameters>\s*void\s*\*\s*\w*\s*)\)\s*\{{\s*$");
        if (Enumerable.Range(0, masked.Count).Where(i => definition.IsMatch(masked[i])).ToList() is not [var header]) return null;
        if (Brackets.BlockEnd(masked, header) is not { } close) return null;

        var match = definition.Match(source.Lines[header]);
        var inner = CodeText.Indentation(source.Lines[Math.Min(header + 1, close)]) is { Length: > 0 } indent ? indent : match.Groups["lead"].Value + "    ";

        var body = Enumerable.Range(header + 1, close - header - 1)
            .Select(i => Regex.Replace(source.Lines[i], @"\breturn\s*;", "return NULL;"))
            .ToList();

        return new LocalFix
        {
            RuleId = Id,
            Title = $"Make {function} a thread start routine: void *{function}(void *)",
            Explanation =
                $"`pthread_create` calls `{function}` as a function that takes a `void *` and returns one - the thread's result, which " +
                $"`pthread_join` can collect. `{function}` returns nothing, so the call goes through the wrong type, which is undefined and " +
                "only happens to work. It now returns a pointer, and `NULL` when it has nothing to hand back.",
            File = source.Path, StartLine = header + 1, RemoveCount = close - header + 1,
            NewLines = [$"{match.Groups["lead"].Value}void *{function}({match.Groups["parameters"].Value.Trim()}) {{", .. body, $"{inner}return NULL;", source.Lines[close]],
            ResolvesWarning = "pthread_create",
        };
    }
}

/// <summary><c>int compare(int *a, int *b)</c> given to <c>qsort</c>, which calls it with <c>const void *</c>.</summary>
public sealed partial class CQsortComparator : ILocalFixRule
{
    public string Id => "c-qsort-comparator";

    [GeneratedRegex(@"^passing argument 4 of 'qsort' from incompatible pointer type")]
    private static partial Regex GccMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.ExceptionType is not ("compile warning" or "compile error") || CCode.GccMessage(context.Error, GccMessage()) is null) return null;
        if (CCode.Locate(context) is not { } at || CppCode.IsCpp(at.Source)) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var calls = Regex.Matches(masked[at.Number - 1], @"\bqsort\s*\(.*,\s*&?\s*(?<function>[A-Za-z_]\w*)\s*\)").ToList();
        if (calls is not [var call]) return null;

        var function = call.Groups["function"].Value;
        var definition = new Regex($@"^(?<lead>\s*(?:static\s+)?)int\s+{Regex.Escape(function)}\s*\(\s*(?:const\s+)?(?<type>(?:struct\s+)?\w+)\s*\*\s*(?<first>\w+)\s*,\s*(?:const\s+)?\k<type>\s*\*\s*(?<second>\w+)\s*\)\s*\{{\s*$");

        if (Enumerable.Range(0, masked.Count).Where(i => definition.IsMatch(masked[i])).ToList() is not [var header]) return null;

        var match = definition.Match(source.Lines[header]);
        var type = match.Groups["type"].Value;
        var (firstName, secondName) = (match.Groups["first"].Value, match.Groups["second"].Value);
        var (firstVoidName, secondVoidName) = ("p" + firstName, "p" + secondName);

        if (masked.Any(l => Regex.IsMatch(l, $@"\b(?:{firstVoidName}|{secondVoidName})\b"))) return null;

        var inner = header + 1 < source.Count && CodeText.Indentation(source.Lines[header + 1]) is { Length: > 0 } indent ? indent : match.Groups["lead"].Value + "    ";

        return new LocalFix
        {
            RuleId = Id,
            Title = $"Give {function} the parameters qsort calls it with: const void *",
            Explanation =
                $"`qsort` sorts any type, so it calls the comparison with two `const void *` - addresses of elements, of no particular type. " +
                $"`{function}` takes `{type} *`, so it is called through the wrong type, which only happens to work. It now takes what `qsort` " +
                $"passes, and turns each back into a `const {type} *` under the old names, so the body does not change.",
            File = source.Path, StartLine = header + 1, RemoveCount = 1,
            NewLines =
            [
                $"{match.Groups["lead"].Value}int {function}(const void *{firstVoidName}, const void *{secondVoidName}) {{",
                $"{inner}const {type} *{firstName} = {firstVoidName};",
                $"{inner}const {type} *{secondName} = {secondVoidName};",
            ],
            ResolvesWarning = "incompatible pointer type",
        };
    }
}

/// <summary><c>if (answer == "yes")</c> - comparing where two strings are, which is never where the text is equal.</summary>
public sealed partial class CStringCompare : ILocalFixRule
{
    public string Id => "c-string-compare";

    [GeneratedRegex(@"^comparison with string literal results in unspecified behavio")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"(?<left>(?<![\w.\]>])[A-Za-z_][\w.]*(?:->\w+)*(?:\[[^\]]*\])?)\s*(?<op>==|!=)\s*(?<literal>""(?:[^""\\]|\\.)*"")|(?<literal>""(?:[^""\\]|\\.)*"")\s*(?<op>==|!=)\s*(?<left>[A-Za-z_][\w.]*(?:->\w+)*(?:\[[^\]]*\])?)(?![\w(\[])")]
    private static partial Regex Comparison();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.ExceptionType != "compile warning" || CCode.GccMessage(context.Error, GccMessage()) is null) return null;
        if (CCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (Comparison().Matches(line).ToList() is not [var comparison]) return null;

        var left = comparison.Groups["left"].Value;
        var literal = comparison.Groups["literal"].Value;
        var op = comparison.Groups["op"].Value;
        var cpp = CppCode.IsCpp(source);

        return CCode.WithHeader(
            Id, $"Compare the text with strcmp: strcmp({left}, {literal}) {op} 0",
            $"`==` on two strings compares where they are in memory, not what they say, and `{left}` and the literal `{literal}` are never " +
            $"in the same place - so this is {(op == "==" ? "false" : "true")} even when the text matches. `strcmp` compares the characters " +
            "and returns 0 when they are the same.",
            source, number, cpp ? "cstring" : "string.h",
            line[..comparison.Index] + $"strcmp({left}, {literal}) {op} 0" + line[(comparison.Index + comparison.Length)..]) with
        {
            ResolvesWarning = "comparison with string literal",
        };
    }
}
