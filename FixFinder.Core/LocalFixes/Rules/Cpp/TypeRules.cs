using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary><c>Dog d = new Dog();</c> - how Java and C# make an object, which in C++ hands back a pointer.</summary>
public sealed partial class CppNewWithoutPointer : ILocalFixRule
{
    public string Id => "cpp-new-without-pointer";

    [GeneratedRegex(@"^conversion from '(?<from>[\w:]+) ?\*' to non-scalar type '(?<to>[\w:]+)' requested$")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^'initializing': cannot convert from '(?<from>[\w:]+) \*' to '(?<to>[\w:]+)'$")]
    private static partial Regex MsvcMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if ((CCode.GccMessage(error, GccMessage()) ?? CCode.MsvcMessage(error, "C2440", MsvcMessage())) is not { } message) return null;
        if (message.Groups["from"].Value != message.Groups["to"].Value || CppCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var type = Regex.Escape(message.Groups["to"].Value.Split("::")[^1]);

        var statement = Regex.Match(line,
            $@"^(?<lead>\s*)(?<type>(?:[\w]+::)*{type})\s+(?<name>[A-Za-z_]\w*)\s*=\s*new\s+(?:[\w]+::)*{type}\s*(?:\((?<args>[^;]*)\)|\{{(?<brace>[^;]*)\}})?\s*;(?<tail>.*)$");

        if (!statement.Success) return null;

        var name = statement.Groups["name"].Value;
        var declared = statement.Groups["args"].Value.Trim() is { Length: > 0 } args
            ? $"{name}({args})"
            : statement.Groups["brace"].Value.Trim() is { Length: > 0 } brace ? $"{name}{{{brace}}}" : name;

        var corrected = $"{statement.Groups["lead"].Value}{statement.Groups["type"].Value} {declared};{statement.Groups["tail"].Value}";

        return LocalFix.ReplaceLine(
            Id, $"Declare {name} without new",
            "`new` builds an object somewhere else and hands back a pointer to it - which is how Java and C# make every object. In C++ an " +
            $"object can simply be a variable: `{corrected.Trim()}` makes one, and it is cleaned up by itself when `{name}` goes out of scope.",
            source.Path, number, corrected);
    }
}

/// <summary><c>char name = "Ada";</c> - a single character given a whole string.</summary>
public sealed partial class CppCharForString : ILocalFixRule
{
    public string Id => "cpp-char-for-string";

    [GeneratedRegex(@"^invalid conversion from 'const char\*' to 'char'")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^'initializing': cannot convert from 'const char \[\d+\]' to 'char'$")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"^(?<lead>\s*)(?:const\s+)?char\s+(?<name>[A-Za-z_]\w*)\s*=\s*(?<value>""(?:[^""\\]|\\.)*"")\s*;(?<tail>.*)$")]
    private static partial Regex Declaration();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if ((CCode.GccMessage(error, GccMessage()) ?? CCode.MsvcMessage(error, "C2440", MsvcMessage())) is null) return null;
        if (CppCode.Locate(context) is not { } at || !CppCode.HasStdString(at.Source)) return null;

        var (source, number, line) = at;
        if (Declaration().Match(line) is not { Success: true } declaration) return null;

        var type = CppCode.StdQualified(source, "string");
        var name = declaration.Groups["name"].Value;

        return LocalFix.ReplaceLine(
            Id, $"Declare {name} as {type}",
            $"`char` holds a single character, like `'A'`, and {declaration.Groups["value"].Value} is a whole string. Text in C++ is a `{type}`.",
            source.Path, number,
            $"{declaration.Groups["lead"].Value}{type} {name} = {declaration.Groups["value"].Value};{declaration.Groups["tail"].Value}");
    }
}

/// <summary><c>text + age</c> with <c>age</c> an <c>int</c> - a number added to a string.</summary>
public sealed partial class CppStringPlusNumber : ILocalFixRule
{
    public string Id => "cpp-string-plus-number";

    [GeneratedRegex(@"^no match for 'operator\+' \(operand types are '(?<left>[^']+)'(?: \{aka '[^']+'\})? and '(?<right>[^']+)'(?: \{aka '[^']+'\})?\)$")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^binary '\+': 'std::(?:string|basic_string<[^']*>)' does not define this operator")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"^(?:(?:unsigned|signed|long|short)\s+)*(?:int|long|short|double|float|size_t|std::size_t|unsigned|int\d+_t|uint\d+_t)$")]
    private static partial Regex Numeric();

    [GeneratedRegex(@"string")]
    private static partial Regex Text();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        if (CCode.GccMessage(error, GccMessage()) is { } gcc)
        {
            var (left, right) = (gcc.Groups["left"].Value, gcc.Groups["right"].Value);
            if (!(Text().IsMatch(left) && Numeric().IsMatch(right)) && !(Text().IsMatch(right) && Numeric().IsMatch(left))) return null;
        }
        else if (CCode.MsvcMessage(error, "C2676", MsvcMessage()) is null)
        {
            return null;
        }

        if (CppCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var code = masked[number - 1];

        var operands = Regex.Matches(code, @"(?<=\+\s*)(?<name>[A-Za-z_]\w*)\b(?!\s*[(\[.:])|(?<![\w.>:])(?<name>[A-Za-z_]\w*)(?=\s*\+(?![+=]))")
            .Where(m => !code[..m.Index].TrimEnd().EndsWith("++", StringComparison.Ordinal))
            .Where(m => CppCode.VariableDeclaration(masked, number - 1, m.Groups["name"].Value) is { Pointer: false, Reference: false } d && Numeric().IsMatch(d.Type))
            .GroupBy(m => m.Index)
            .Select(g => g.First())
            .ToList();

        if (operands is not [var operand]) return null;

        var name = operand.Groups["name"];
        var call = CppCode.StdQualified(source, "to_string");

        return LocalFix.ReplaceLine(
            Id, $"Turn {name.Value} into text with {call}",
            $"C++ does not turn a number into text by adding it to a string - `+` between a string and `{name.Value}` has no meaning. " +
            $"`{call}({name.Value})` makes the text first, and text can be added to text.",
            source.Path, number, line[..name.Index] + $"{call}({name.Value})" + line[(name.Index + name.Length)..]);
    }
}

/// <summary><c>word[0] == "a"</c> - one character compared with a string.</summary>
public sealed partial class CppCharComparedWithString : ILocalFixRule
{
    public string Id => "cpp-char-compared-with-string";

    [GeneratedRegex(@"^ISO C\+\+ forbids comparison between pointer and integer")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^'(?:==|!=)': no conversion from 'const char \[2\]' to '(?:int|char)'$")]
    private static partial Regex MsvcMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if ((CCode.GccMessage(error, GccMessage()) ?? CCode.MsvcMessage(error, "C2446", MsvcMessage())) is null) return null;
        if (CppCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        var single = CppCode.StringLiterals(line)
            .Where(l => l.Quote == '"' && Regex.IsMatch(line[(l.Start + 1)..(l.End - 1)], @"^(?:[^""\\]|\\.)$"))
            .Where(l => ComparedWithCharacter(line, masked, number - 1, l.Start, l.End))
            .ToList();

        if (single is not [var (start, end, _)]) return null;

        var inner = line[(start + 1)..(end - 1)];
        var character = "'" + (inner == "'" ? "\\'" : inner) + "'";

        return LocalFix.ReplaceLine(
            Id, $"Compare with the character {character}",
            $"The other side is a single character, a `char`, and {line[start..end]} is a string - C++ cannot compare the two. One character " +
            $"is written in single quotes: {character}.",
            source.Path, number, line[..start] + character + line[end..]);
    }

    private static bool ComparedWithCharacter(string line, IReadOnlyList<string> masked, int index, int start, int end)
    {
        var before = line[..start].TrimEnd();
        var after = line[end..].TrimStart();

        string other;

        if (before.EndsWith("==", StringComparison.Ordinal) || before.EndsWith("!=", StringComparison.Ordinal)) other = before[..^2].TrimEnd();
        else if (after.StartsWith("==", StringComparison.Ordinal) || after.StartsWith("!=", StringComparison.Ordinal)) other = after[2..].TrimStart();
        else return false;

        var left = other == before[..^2].TrimEnd();

        // An element read with [i], .at(i), .front() or .back() - or a variable declared char.
        if (left ? Regex.IsMatch(other, @"(?:\]|\.\s*(?:at\s*\([^()]*\)|front\s*\(\s*\)|back\s*\(\s*\)))$")
                 : Regex.IsMatch(other, @"^[A-Za-z_]\w*\s*(?:\[[^\]]*\]|\.\s*(?:at\s*\([^()]*\)|front\s*\(\s*\)|back\s*\(\s*\)))"))
            return true;

        var word = left ? Regex.Match(other, @"(?<name>[A-Za-z_]\w*)$") : Regex.Match(other, @"^(?<name>[A-Za-z_]\w*)\b");
        return word.Success && CppCode.VariableDeclaration(masked, index, word.Groups["name"].Value) is { Type: "char", Pointer: false };
    }
}

/// <summary><c>void show(auto value)</c> - a C++20 abbreviated template, which MSVC refuses under C++17.</summary>
public sealed partial class CppAutoParameter : ILocalFixRule
{
    public string Id => "cpp-auto-parameter";

    [GeneratedRegex(@"^a parameter cannot have a type that contains 'auto'$")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"(?<![\w:])auto(?!\w)")]
    private static partial Regex Auto();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (CCode.MsvcMessage(context.Error, "C3533", MsvcMessage()) is null || CppCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var code = masked[number - 1];

        if (Brackets.BraceDepths(masked)[number - 1] != 0) return null;
        if (masked.Any(text => Regex.IsMatch(text, @"(?<![\w:])T(?!\w)"))) return null;

        var open = code.IndexOf('(');
        if (open < 0 || Brackets.ClosingParenthesis(code, open) is not { } close) return null;

        var autos = Auto().Matches(code[..close]).Where(m => m.Index > open).ToList();
        if (autos is not [var only]) return null;

        var name = Regex.Match(code[..open], @"(?<name>[A-Za-z_]\w*)\s*$").Groups["name"].Value;
        var corrected = line[..only.Index] + "T" + line[(only.Index + only.Length)..];

        return new LocalFix
        {
            RuleId = Id,
            Title = $"Make {name} a template",
            Explanation =
                $"A parameter declared `auto` - so `{name}` takes a value of any type - arrived in C++20, and this program is built as C++17. A " +
                "template says the same thing in C++17: `template <typename T>` names the type, and the parameter uses it.",
            File = source.Path,
            StartLine = number,
            RemoveCount = 1,
            NewLines = [CodeText.Indentation(line) + "template <typename T>", corrected],
        };
    }
}

/// <summary>A lambda using a local it never captured, or changing one it captured by copy.</summary>
public sealed partial class CppLambdaCapture : ILocalFixRule
{
    public string Id => "cpp-lambda-capture";

    [GeneratedRegex(@"^'(?<name>\w+)' is not captured$")]
    private static partial Regex GccNotCaptured();

    [GeneratedRegex(@"^'(?<name>\w+)' cannot be implicitly captured because no default capture mode has been specified$")]
    private static partial Regex MsvcNotCaptured();

    [GeneratedRegex(@"^(?:increment|decrement|assignment) of read-only variable '(?<name>\w+)'$")]
    private static partial Regex GccReadOnly();

    [GeneratedRegex(@"^'(?<name>\w+)': a by copy capture cannot be modified in a non-mutable lambda$")]
    private static partial Regex MsvcByCopy();

    [GeneratedRegex(@"(?<=(?:^|[=(,{;]|\breturn)\s*)(?<open>\[)(?<captures>[^\[\]]*)(?<close>\])\s*(?:\([^()]*\))?\s*(?:mutable\b\s*)?(?:->\s*[^{;]+)?\{")]
    private static partial Regex Lambda();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        var missing = CCode.GccMessage(error, GccNotCaptured()) ?? CCode.MsvcMessage(error, "C3493", MsvcNotCaptured());
        var copied = missing is null ? CCode.GccMessage(error, GccReadOnly()) ?? CCode.MsvcMessage(error, "C3491", MsvcByCopy()) : null;

        if ((missing ?? copied) is not { } message || CppCode.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var name = message.Groups["name"].Value;

        for (var i = at.Number - 1; i >= Math.Max(0, at.Number - 11); i--)
        {
            if (Lambda().Matches(masked[i]).LastOrDefault() is not { } lambda) continue;

            var captures = lambda.Groups["captures"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

            if (missing is not null)
            {
                if (captures.Any(c => c is "=" or "&" || c.TrimStart('&') == name)) return null;
                captures.Add("&" + name);
            }
            else
            {
                var index = captures.IndexOf(name);
                if (index >= 0) captures[index] = "&" + name;
                else if (captures.Contains("=")) captures.Add("&" + name);
                else return null;
            }

            var original = source.Lines[i];
            var open = lambda.Groups["open"].Index;
            var close = lambda.Groups["close"].Index;

            return LocalFix.ReplaceLine(
                Id, $"Capture {name} by reference: [&{name}]",
                missing is not null
                    ? $"A lambda can only use the local variables it captures, listed in its `[ ]`. `&{name}` there lets it use `{name}` - by " +
                      "reference, so it sees the variable itself rather than a copy taken when the lambda was made."
                    : $"`[{name}]` gives the lambda its own copy of `{name}`, taken when the lambda was made, and a lambda may not change its " +
                      $"copies. `[&{name}]` captures the variable itself, so the change reaches the real `{name}`.",
                source.Path, i + 1, original[..(open + 1)] + string.Join(", ", captures) + original[close..]);
        }

        return null;
    }
}

/// <summary>A <c>std::unique_ptr</c> copied - <c>auto second = first;</c> or passed by value - which only moving allows.</summary>
public sealed partial class CppMoveUniquePtr : ILocalFixRule
{
    public string Id => "cpp-move-unique-ptr";

    [GeneratedRegex(@"^use of deleted function 'std::unique_ptr<[^']*>::unique_ptr\(const std::unique_ptr<[^']*>&\)")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^'std::unique_ptr<[^']*>::unique_ptr\(const std::unique_ptr<[^']*> &\)': attempting to reference a deleted function$")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"(?<=(?:[=(,])\s*)(?<name>[A-Za-z_]\w*)(?=\s*[;,)])")]
    private static partial Regex Operand();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if ((CCode.GccMessage(error, GccMessage()) ?? CCode.MsvcMessage(error, "C2280", MsvcMessage())) is null) return null;
        if (CppCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var code = masked[number - 1];

        var owners = Operand().Matches(code)
            .Where(m => CppCode.VariableDeclaration(masked, number - 1, m.Groups["name"].Value) is { Pointer: false, Reference: false } d &&
                        d.Line != number - 1 &&
                        (d.Type.Contains("unique_ptr", StringComparison.Ordinal) ||
                         (d.Type == "auto" && Regex.IsMatch(masked[d.Line], @"\b(?:make_unique|unique_ptr)\b"))))
            .ToList();

        if (owners is not [var owner]) return null;

        var name = owner.Groups["name"].Value;
        var (_, last) = CCode.EnclosingFunction(masked, number - 1);
        var used = new Regex($@"(?<![\w.>:]){Regex.Escape(name)}\b");

        // Moved from, it is empty. Anything that still uses it afterwards would read a null pointer.
        if (used.IsMatch(code[(owner.Index + owner.Length)..])) return null;
        for (var i = number; i <= last && i < masked.Count; i++)
            if (used.IsMatch(masked[i])) return null;

        var move = CppCode.StdQualified(source, "move");

        return LocalFix.ReplaceLine(
            Id, $"Hand {name} over with {move}",
            $"A `std::unique_ptr` is the only owner of what it points to, so it cannot be copied - that would make two owners. `{move}({name})` " +
            $"hands ownership over instead, and leaves `{name}` empty - which is safe here, because nothing uses `{name}` afterwards.",
            source.Path, number, line[..owner.Index] + $"{move}({name})" + line[(owner.Index + owner.Length)..]);
    }
}

/// <summary><c>ages["ada"]</c> on a <c>const std::map</c> - <c>[]</c> adds a missing key, so a const map has none.</summary>
public sealed partial class CppConstMapIndex : ILocalFixRule
{
    public string Id => "cpp-const-map-index";

    [GeneratedRegex(@"^passing 'const std::(?:__cxx11::)?(?:map|unordered_map)<.*' as 'this' argument discards qualifiers")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^binary '\[': no operator found which takes a left-hand operand of type 'const std::(?:map|unordered_map)<")]
    private static partial Regex MsvcMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        if ((CCode.GccMessage(context.Error, GccMessage()) ?? CCode.MsvcMessage(context.Error, "C2678", MsvcMessage())) is null) return null;
        if (CppCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        var maps = masked.SelectMany(l => Regex.Matches(l, @"\bconst\s+(?:std::)?(?:map|unordered_map)\s*<.*?>\s*&?\s*(?<name>[A-Za-z_]\w*)\b"))
            .Select(m => m.Groups["name"].Value).ToHashSet();

        var uses = Regex.Matches(masked[number - 1], @"(?<![\w.>])(?<name>[A-Za-z_]\w*)\s*\[").Where(m => maps.Contains(m.Groups["name"].Value)).ToList();
        if (uses.Count == 0) return null;

        var result = line;

        foreach (var use in uses.OrderByDescending(u => u.Index))
        {
            var open = use.Index + use.Length - 1;
            var close = Close(masked[number - 1], open);
            if (close is null || Regex.IsMatch(masked[number - 1][(close.Value + 1)..], @"^\s*(?:=(?!=)|\+=|-=|\+\+|--)")) return null;

            result = result[..open] + ".at(" + result[(open + 1)..close.Value] + ")" + result[(close.Value + 1)..];
            result = result[..use.Index] + use.Groups["name"].Value + result[(use.Index + use.Groups["name"].Length + (open - use.Index - use.Groups["name"].Length))..];
        }

        var name = uses[0].Groups["name"].Value;

        return LocalFix.ReplaceLine(
            Id, $"Look up a const map with at(): {name}.at(...)",
            $"`[]` on a map inserts the key when it is missing, so it changes the map - and `{name}` is `const`, so it cannot be used. " +
            "`.at()` only looks the key up, and throws `std::out_of_range` if it is not there rather than adding it.",
            source.Path, number, result);
    }

    private static int? Close(string masked, int open)
    {
        var depth = 0;

        for (var i = open; i < masked.Length; i++)
        {
            if (masked[i] == '[') depth++;
            else if (masked[i] == ']' && --depth == 0) return i;
        }

        return null;
    }
}

/// <summary><c>std::sort(values.begin(), values.end())</c> on a <c>std::list</c>, which has its own <c>sort</c>.</summary>
public sealed partial class CppSortList : ILocalFixRule
{
    public string Id => "cpp-sort-list";

    [GeneratedRegex(@"^no match for 'operator-' \(operand types are 'std::_List_(?:const_)?iterator<")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^binary '-': 'const std::_List_(?:unchecked_)?(?:const_)?iterator<")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"(?<![\w.>])(?:std\s*::\s*)?sort\s*\(\s*(?<object>[A-Za-z_]\w*)\s*\.\s*begin\s*\(\s*\)\s*,\s*\k<object>\s*\.\s*end\s*\(\s*\)\s*\)")]
    private static partial Regex Sort();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if ((CCode.GccMessage(error, GccMessage()) ?? CCode.MsvcMessage(error, "C2676", MsvcMessage())) is null) return null;

        var found = new List<(SourceFile Source, int Line, Match Call)>();

        // The error is reported from inside <algorithm>, so the call is found in the program instead.
        foreach (var source in CppCode.SourceFiles(context))
        {
            var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

            for (var i = 0; i < masked.Count; i++)
            {
                foreach (Match call in Sort().Matches(masked[i]))
                {
                    var name = Regex.Escape(call.Groups["object"].Value);
                    if (masked.Any(text => Regex.IsMatch(text, $@"\b(?:std\s*::\s*)?list\s*<[^;]*>\s*{name}\b"))) found.Add((source, i, call));
                }
            }
        }

        if (found is not [var (file, line, only)]) return null;

        var list = only.Groups["object"].Value;
        var original = file.Lines[line];

        return LocalFix.ReplaceLine(
            Id, $"Sort the list with {list}.sort()",
            "`std::sort` jumps straight to elements by position, which needs random access - a `std::vector`, a `std::deque` or an array. A " +
            $"`std::list` can only be walked one element at a time, so it has its own `sort` member that works that way: `{list}.sort()`.",
            file.Path, line + 1, original[..only.Index] + $"{list}.sort()" + original[(only.Index + only.Length)..]);
    }
}
