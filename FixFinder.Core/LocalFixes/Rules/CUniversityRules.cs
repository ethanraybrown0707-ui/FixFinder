using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary><c>#define SIZE 5;</c> - the semicolon is pasted in wherever the macro is used.</summary>
/// <remarks>
/// gcc reports the error at the #define itself, MSVC where the macro was used. Either way the edit is
/// the same one, on the #define.
/// </remarks>
public sealed partial class CDefineSemicolon : ILocalFixRule
{
    public string Id => "c-define-semicolon";

    [GeneratedRegex(@"^\s*#\s*define\s+(?<name>[A-Za-z_]\w*)(?:\([^)]*\))?\s+.*?(?<semicolon>;)\s*$")]
    private static partial Regex Define();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CCode.IsCompiler(context.Error) || CCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var lines = source.Lines;
        int index;
        Match define;

        if (Define().Match(line) is { Success: true } here)
        {
            (index, define) = (number - 1, here);
        }
        else
        {
            var used = CodeText.Identifiers([CodeText.Mask(line, Syntax.CLike)]);
            var macros = Enumerable.Range(0, lines.Count)
                .Select(i => (Index: i, Match: Define().Match(lines[i])))
                .Where(x => x.Match.Success && used.Contains(x.Match.Groups["name"].Value))
                .ToList();

            if (macros is not [var only]) return null;
            (index, define) = (only.Index, only.Match);
        }

        var name = define.Groups["name"].Value;
        var semicolon = define.Groups["semicolon"].Index;

        return LocalFix.ReplaceLine(
            Id, $"Remove the semicolon from #define {name}",
            $"`#define` pastes its text in wherever `{name}` is written - the semicolon too - so inside `[...]` or an expression `{name}` " +
            "leaves a stray `;` behind. A #define does not end with a semicolon.",
            source.Path, index + 1, lines[index][..semicolon] + lines[index][(semicolon + 1)..]);
    }
}

/// <summary>No <c>main</c>: MSVC's <c>LNK1561 entry point must be defined</c>, MinGW's <c>undefined reference to 'WinMain'</c>.</summary>
public sealed partial class CMainName : ILocalFixRule
{
    public string Id => "c-main-name";

    [GeneratedRegex(@"^\s*(?:static\s+)?(?:int|void)\s+(?<name>[A-Za-z_]\w*)\s*\([^;{)]*\)\s*\{?\s*$")]
    private static partial Regex Function();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        var message = error.Message ?? "";

        var noMain = error.LanguageId switch
        {
            "gcc" => message is "undefined reference to 'WinMain'" or "undefined reference to 'main'",
            "msvc" => error.ErrorCode == "LNK1561" ||
                      error.ErrorCode == "LNK2019" && Regex.IsMatch(message, @"^unresolved external symbol (?:main|WinMain) "),
            _ => false,
        };

        if (!noMain || context.SourceRoot is not { } root || !Directory.Exists(root)) return null;

        var functions = new List<(SourceFile Source, int Index, Group Name)>();

        foreach (var path in Directory.EnumerateFiles(root).Where(p => Path.GetExtension(p).ToLowerInvariant() is ".c" or ".cpp" or ".cc" or ".cxx").Take(200))
        {
            if (SourceFile.Read(path) is not { } source) continue;

            var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
            var depths = CCode.DepthAtStart(masked);

            for (var i = 0; i < masked.Count; i++)
            {
                if (depths[i] != 0 || Function().Match(masked[i]) is not { Success: true } function) continue;

                // A real main means the missing entry point is something else - a project setting, not a name.
                if (function.Groups["name"].Value == "main") return null;

                functions.Add((source, i, function.Groups["name"]));
            }
        }

        if (CodeText.Nearest("main", functions.Select(f => f.Name.Value).Distinct()) is not { } wrong) return null;
        if (functions.Where(f => f.Name.Value == wrong).ToList() is not [var (file, index, name)]) return null;

        var line = file.Lines[index];

        return LocalFix.ReplaceLine(
            Id, $"Rename {wrong} to main",
            $"A C program starts at a function called exactly `main`, in lower case, and there is none - `{wrong}` is a letter away - so the " +
            "linker had nowhere to start the program" +
            (error.LanguageId == "gcc" ? ". MinGW then looked for `WinMain`, the entry point of a Windows GUI program, and named that instead." : "."),
            file.Path, index + 1, line[..name.Index] + "main" + line[(name.Index + name.Length)..]);
    }
}

/// <summary>AddressSanitizer's <c>attempting free on address which was not malloc()-ed</c> - an array freed.</summary>
public sealed partial class CFreeNonHeap : ILocalFixRule
{
    public string Id => "c-free-non-heap";

    [GeneratedRegex(@"^\s*free\s*\(\s*(?<pointer>[A-Za-z_]\w*)\s*\)\s*;\s*$")]
    private static partial Regex Free();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "gcc", ExceptionType: "attempting free" }) return null;
        if (CCode.Locate(context) is not { } at) return null;

        var (source, number, _) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (Free().Match(masked[number - 1]) is not { Success: true } free) return null;

        var pointer = free.Groups["pointer"].Value;
        var escaped = Regex.Escape(pointer);
        var (first, _) = CCode.EnclosingFunction(masked, number - 1);
        var above = Enumerable.Range(first, number - 1 - first).Select(i => masked[i]).ToList();

        var array = above.Any(text => Regex.IsMatch(text, $@"^\s*(?:(?:static|const|unsigned|signed|struct\s+\w+)\s+)*[A-Za-z_]\w*\s+\**{escaped}\s*\["));
        var address = above.Any(text => Regex.IsMatch(text, $@"(?<![\w.]){escaped}\s*=\s*&"));

        if (!array && !address) return null;

        return CCode.RemoveLine(
            Id, $"Remove free({pointer})",
            (array
                ? $"`{pointer}` is an array on the stack, not memory from `malloc`. It is given back by itself when the function returns"
                : $"`{pointer}` points at a variable, not at memory from `malloc`, and that variable is given back by itself") +
            " - `free` may only be handed what `malloc`, `calloc` or `realloc` returned.",
            source.Path, number);
    }
}

/// <summary><c>'else' without a previous 'if'</c> / <c>C2181</c> - from <c>if (x); {</c>.</summary>
public sealed class CIfSemicolon : ILocalFixRule
{
    public string Id => "c-if-semicolon";

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        var recognised = error.LanguageId == "gcc" && error.Message == "'else' without a previous 'if'" || CCode.IsMsvc(error, "C2181");

        if (!recognised || CCode.Locate(context) is not { } at) return null;

        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.CLike);
        if (Blocks.IfSemicolon(at.Source.Lines, masked, at.Number - 1) is not { } fix) return null;

        return LocalFix.ReplaceLine(Id, Blocks.IfSemicolonTitle, Blocks.IfSemicolonExplanation, at.Source.Path, fix.Line + 1, fix.Corrected);
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

        if (Header().Match(code) is not { Success: true } header || CCode.Matching(code, header.Groups["open"].Index) is not { } close) return null;

        var func = header.Groups["func"].Value;
        var parameters = Split(code, header.Groups["open"].Index + 1, close);

        var position = parameters.FindIndex(p =>
            Regex.Match(code[p.Start..p.End], @"(?<name>[A-Za-z_]\w*)\s*\[\s*\]\s*\[\s*\]") is { Success: true } m &&
            (msvc is null || m.Groups["name"].Value == msvc.Groups["name"].Value));

        if (position < 0) return null;

        var parameter = Regex.Match(line[parameters[position].Start..parameters[position].End], @"(?<name>[A-Za-z_]\w*)\s*\[\s*\]\s*\[(?<empty>\s*)\]");
        var name = parameter.Groups["name"].Value;

        // The column count, read from every array this function is called with - all of which have to agree.
        var columns = new HashSet<string>(StringComparer.Ordinal);
        var call = new Regex($@"(?<![\w.]){Regex.Escape(func)}\s*(?<open>\()");

        for (var i = 0; i < masked.Count; i++)
        {
            if (i == number - 1) continue;

            foreach (Match m in call.Matches(masked[i]))
            {
                if (CCode.Matching(masked[i], m.Groups["open"].Index) is not { } end) continue;

                var arguments = Split(masked[i], m.Groups["open"].Index + 1, end);
                if (position >= arguments.Count) continue;

                var argument = masked[i][arguments[position].Start..arguments[position].End].Trim();
                if (!Regex.IsMatch(argument, @"^[A-Za-z_]\w*$")) return null;

                // A declaration, with its type - not an element read somewhere, like grid[0][0].
                var declared = masked
                    .Select(text => Regex.Match(text, $@"\b(?:int|char|double|float|long|short|unsigned|signed|bool|struct\s+\w+|[A-Za-z_]\w*_t)\s+{Regex.Escape(argument)}\s*\[\s*\w+\s*\]\s*\[\s*(?<columns>\w+)\s*\]"))
                    .FirstOrDefault(d => d.Success);
                if (declared is null) return null;

                columns.Add(declared.Groups["columns"].Value);
            }
        }

        // gcc accepts a zero-length array as an extension, so a 0 here would compile and still be wrong.
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

/// <summary><c>*p</c> where <c>p</c> is a <c>void *</c> - <c>invalid use of void expression</c> / <c>C2100</c>.</summary>
public sealed partial class CVoidPointerDereference : ILocalFixRule
{
    public string Id => "c-void-pointer-dereference";

    [GeneratedRegex(@"\*\s*(?<p>[A-Za-z_]\w*)")]
    private static partial Regex Dereference();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        var message = error.Message ?? "";

        var recognised = error.LanguageId == "gcc" && (message == "invalid use of void expression" || message.StartsWith("dereferencing 'void *' pointer", StringComparison.Ordinal)) ||
                         CCode.IsMsvc(error, "C2100");

        if (!recognised || CCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var code = masked[number - 1];
        var (first, _) = CCode.EnclosingFunction(masked, number - 1);

        var found = new List<(Match Match, string Type)>();

        foreach (Match deref in Dereference().Matches(code))
        {
            // `a * p` is multiplication; a dereference follows an operator, a bracket or nothing.
            var before = code[..deref.Index].TrimEnd();
            if (before.Length > 0 && (char.IsLetterOrDigit(before[^1]) || before[^1] is '_' or ')' or ']')) continue;

            var p = Regex.Escape(deref.Groups["p"].Value);
            var pointer = Enumerable.Range(first, number - first)
                .Select(i => Regex.Match(masked[i], $@"\bvoid\s*\*\s*{p}\s*=\s*&\s*(?<target>[A-Za-z_]\w*)"))
                .FirstOrDefault(m => m.Success);

            if (pointer is null) continue;

            var target = Regex.Escape(pointer.Groups["target"].Value);
            var declaration = Enumerable.Range(first, number - first)
                .Select(i => Regex.Match(masked[i], $@"(?<type>(?:unsigned\s+|signed\s+|long\s+|short\s+)*(?:int|char|double|float|long|short)|struct\s+\w+)\s+{target}\b"))
                .FirstOrDefault(m => m.Success);

            if (declaration is not null) found.Add((deref, Regex.Replace(declaration.Groups["type"].Value, @"\s+", " ")));
        }

        if (found is not [var (hit, type)]) return null;

        var name = hit.Groups["p"].Value;

        return LocalFix.ReplaceLine(
            Id, $"Cast {name} to {type} * before reading through it",
            $"A `void *` can point at anything, which is exactly why C will not read through one - it does not know what is there. " +
            $"`{name}` holds the address of a {type}, so `({type} *){name}` says what to read.",
            source.Path, number, line[..hit.Index] + $"*({type} *){name}" + line[(hit.Index + hit.Length)..]);
    }
}

/// <summary>
/// A printf or scanf argument of the wrong kind: <c>scanf("%d", age)</c> without its <c>&amp;</c>, and
/// <c>printf("%s", grade)</c> with a single char - read from MSVC's <c>C4477</c> or gcc's <c>-Wformat</c>.
/// </summary>
/// <remarks>
/// Both compile with a warning and then crash. gcc offers a fix-it for the second, but its answer is
/// <c>%d</c> - the char arrives as an int - where a person printing a grade means <c>%c</c>.
/// </remarks>
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
        if (CCode.Matching(masked, open) is not { } close) return null;

        var arguments = CFormatSpecifier.Arguments(masked, open + 1, close);

        // MSVC counts the arguments after the format string; gcc counts them all, from 1.
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
