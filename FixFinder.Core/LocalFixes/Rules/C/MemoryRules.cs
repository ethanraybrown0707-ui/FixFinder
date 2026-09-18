using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>AddressSanitizer's <c>attempting double-free</c>, where the same pointer is freed twice in a row.</summary>
public sealed partial class CDoubleFree : ILocalFixRule
{
    public string Id => "c-double-free";

    [GeneratedRegex(@"^\s*(?:free\s*\(\s*(?<pointer>[A-Za-z_][\w.]*(?:->\w+)*)\s*\)|(?<delete>delete)\s*(?:\[\s*\])?\s*(?<pointer>[A-Za-z_][\w.]*(?:->\w+)*))\s*;\s*$")]
    private static partial Regex Free();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "gcc", ExceptionType: "attempting double-free" }) return null;
        if ((CCode.Locate(context) ?? FirstInProgram(context)) is not { } at) return null;

        var (source, number, _) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (Free().Match(masked[number - 1]) is not { Success: true } second) return null;

        var pointer = second.Groups["pointer"].Value;
        var escaped = Regex.Escape(pointer);
        var depths = Brackets.BraceDepths(masked);
        var (first, _) = CCode.EnclosingFunction(masked, number - 1);

        for (var k = number - 2; k > first; k--)
        {
            if (depths[k] < depths[number - 1] || masked[k].Contains('}')) return null;
            if (Regex.IsMatch(masked[k], $@"(?<![\w.>]){escaped}\s*=(?!=)")) return null;

            if (Free().Match(masked[k]) is { Success: true } earlier && earlier.Groups["pointer"].Value == pointer)
            {
                var word = second.Groups["delete"].Success ? "delete" : "free";

                return CCode.RemoveLine(
                    Id, word == "free" ? $"Remove the second free({pointer})" : $"Remove the second delete {pointer}",
                    $"`{pointer}` is {(word == "free" ? "freed" : "deleted")} on line {k + 1} and then again on line {number}. Memory can only be " +
                    $"given back once - the second `{word}` corrupts the heap, which is what AddressSanitizer caught.",
                    source.Path, number);
            }
        }

        return null;
    }

    private static (SourceFile Source, int Number, string Line)? FirstInProgram(LocalFixContext context)
    {
        foreach (var frame in context.Error.Frames)
        {
            if (frame.Line is not { } number || context.Read(frame.File) is not { } source || !CCode.IsNative(source)) continue;
            if (source.Line(number) is { } line) return (source, number, line);
        }

        return null;
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

/// <summary><c>gets(name);</c> - which cannot know how big <c>name</c> is, and writes past its end on a long enough line.</summary>
public sealed partial class CGets : ILocalFixRule
{
    public string Id => "c-gets";

    [GeneratedRegex(@"^(?:call to 'gets' declared with attribute warning|implicit declaration of function 'gets')")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^(?<lead>\s*)gets\s*\(\s*(?<name>[A-Za-z_]\w*)\s*\)\s*;\s*$")]
    private static partial Regex Call();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.ExceptionType != "compile warning" || CCode.GccMessage(context.Error, GccMessage()) is null) return null;
        if (CCode.Locate(context) is not { } at || Call().Match(at.Line) is not { Success: true } call) return null;

        var name = call.Groups["name"].Value;
        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.CLike);

        if (!CCode.DeclaredAsArray(masked, at.Number - 1, name)) return null;

        var strip = CCode.Includes(at.Source, "string.h")
            ? $"{name}[strcspn({name}, \"\\n\")] = '\\0';"
            : $"{{ char *end = {name}; while (*end && *end != '\\n') end++; *end = '\\0'; }}";

        return LocalFix.ReplaceLine(
            Id, $"Read with fgets, which knows how big {name} is",
            $"`gets` reads a whole line into `{name}` without knowing how big `{name}` is, so a line longer than it writes over whatever " +
            "memory comes next. Whether that shows depends on what is there, which is why the program can run without failing and " +
            $"still be wrong. `fgets` is given the size and stops there. It keeps the newline that `gets` dropped, so the newline is " +
            "removed again and the program prints what it printed before.",
            at.Source.Path, at.Number, $"{call.Groups["lead"].Value}if (fgets({name}, sizeof {name}, stdin) != NULL) {strip}") with
        {
            ResolvesWarning = "gets",
        };
    }
}

/// <summary><c>char name[5] = "Ethanol";</c> - an array too short for the string it is given.</summary>
public sealed partial class CStringTooLong : ILocalFixRule
{
    public string Id => "c-string-too-long";

    [GeneratedRegex(@"^initializer-string for array of 'char' is too long")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^'(?<name>\w+)': array bounds overflow$")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"^(?<head>.*?\bchar\s+(?<name>[A-Za-z_]\w*)\s*)\[\s*\d+\s*\](?<tail>\s*=\s*""(?:[^""\\]|\\.)*""\s*;.*)$")]
    private static partial Regex Declaration();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if (error.ExceptionType != "compile warning") return null;
        if ((CCode.GccMessage(error, GccMessage()) ?? CCode.MsvcMessage(error, "C4045", MsvcMessage())) is null) return null;
        if (CCode.Locate(context) is not { } at || Declaration().Match(at.Line) is not { Success: true } declaration) return null;

        var name = declaration.Groups["name"].Value;

        return LocalFix.ReplaceLine(
            Id, $"Let {name} be as long as its text: {name}[]",
            $"`{name}` is given more characters than its size holds, so the rest - including the `\\0` that marks where the text " +
            "ends - is cut off. Printing it then reads on past the array into whatever follows, which sometimes looks fine. With the " +
            "size left out, the array is made exactly long enough for the text and its end marker.",
            at.Source.Path, at.Number, declaration.Groups["head"].Value + "[]" + declaration.Groups["tail"].Value) with
        {
            ResolvesWarning = error.LanguageId == "msvc" ? "C4045" : "initializer-string for array of 'char' is too long",
        };
    }
}

/// <summary><c>return text;</c> for a local array - the address of memory that is gone once the function returns.</summary>
public sealed partial class CReturnLocalAddress : ILocalFixRule
{
    public string Id => "c-return-local-address";

    [GeneratedRegex(@"^function returns address of local variable")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^returning address of local variable or temporary(?: : (?<name>\w+))?$")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"^\s*return\s+&?\s*(?<name>[A-Za-z_]\w*)\s*;")]
    private static partial Regex Return();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if (error.ExceptionType != "compile warning") return null;
        if ((CCode.GccMessage(error, GccMessage()) ?? CCode.MsvcMessage(error, "C4172", MsvcMessage())) is null) return null;
        if (CCode.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (Return().Match(masked[at.Number - 1]) is not { Success: true } returned) return null;

        var name = returned.Groups["name"].Value;
        var (header, _) = CCode.EnclosingFunction(masked, at.Number - 1);
        var depths = Brackets.BraceDepths(masked);

        var declaration = new Regex($@"^(?<lead>\s*)(?!return\b|static\b)(?:const\s+)?[A-Za-z_][\w\s]*?[\s*]{Regex.Escape(name)}\s*(?:\[[^\]]*\])?\s*(?:=[^;]*)?;");
        var found = Enumerable.Range(header + 1, Math.Max(0, at.Number - 2 - header))
            .Where(i => depths[i] == 1 && declaration.IsMatch(masked[i]))
            .ToList();

        if (found is not [var line]) return null;

        var lead = declaration.Match(masked[line]).Groups["lead"].Value;
        var original = source.Lines[line];

        return LocalFix.ReplaceLine(
            Id, $"Keep {name} alive after the function returns: static",
            $"`{name}` belongs to the function, and stops existing the moment it returns - so the address handed back points at " +
            "memory the next function call reuses. The program can print the right thing anyway, until something else is called " +
            $"first. `static` makes `{name}` last for the whole program, so the address stays good. Every call now shares the same " +
            $"`{name}`, so a caller that needs its own copy should copy it.",
            source.Path, line + 1, lead + "static " + original[lead.Length..]) with
        {
            ResolvesWarning = error.LanguageId == "msvc" ? "C4172" : "function returns address of local variable",
        };
    }
}

/// <summary><c>int *values = malloc(10);</c> - ten bytes, where ten ints were meant.</summary>
public sealed partial class CMallocElementSize : ILocalFixRule
{
    public string Id => "c-malloc-element-size";

    [GeneratedRegex(@"^\s*allocated by thread \w+ here:")]
    private static partial Regex AllocatedBy();

    [GeneratedRegex(@"^\s*#\d+\s+0x[0-9a-fA-F]+\s+in\s+\S+\s+(?<file>.+):(?<line>\d+)")]
    private static partial Regex Frame();

    [GeneratedRegex(@"(?<type>\b[A-Za-z_][\w\s]*?)\s*\*\s*(?<ptr>[A-Za-z_]\w*)\s*=\s*(?:\(\s*[\w\s]+\*\s*\)\s*)?malloc\s*\(\s*(?<count>[^;]*?)\s*\)\s*;|(?<ptr>[A-Za-z_]\w*)\s*=\s*(?:\(\s*[\w\s]+\*\s*\)\s*)?malloc\s*\(\s*(?<count>[^;]*?)\s*\)\s*;")]
    private static partial Regex Malloc();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "gcc", ExceptionType: "heap-buffer-overflow" }) return null;

        var output = context.Output.Select(l => l.Text).ToList();
        var allocated = output.FindIndex(l => AllocatedBy().IsMatch(l));
        if (allocated < 0) return null;

        foreach (var line in output.Skip(allocated + 1).TakeWhile(l => l.Trim().Length > 0))
        {
            if (Frame().Match(line) is not { Success: true } frame || context.Read(frame.Groups["file"].Value.Trim()) is not { } source) continue;

            var number = int.Parse(frame.Groups["line"].Value);
            if (source.Line(number) is not { } text || Malloc().Match(CodeText.MaskAll(source.Lines, Syntax.CLike)[number - 1]) is not { Success: true } call) return null;

            var count = call.Groups["count"];
            if (count.Value.Length == 0 || count.Value.Contains("sizeof", StringComparison.Ordinal)) return null;

            var ptr = call.Groups["ptr"].Value;
            var type = call.Groups["type"].Success ? call.Groups["type"].Value.Trim() : Declared(source, ptr);
            if (type is null || Regex.IsMatch(type, @"^(?:(?:unsigned|signed|const)\s+)*char$")) return null;

            var size = Regex.IsMatch(count.Value, @"^\w+$") ? $"{count.Value} * sizeof *{ptr}" : $"({count.Value}) * sizeof *{ptr}";

            return LocalFix.ReplaceLine(
                Id, $"Allocate {count.Value} of what {ptr} points to: * sizeof *{ptr}",
                $"`malloc` counts bytes, not elements, so `malloc({count.Value})` is {count.Value} bytes - enough for {count.Value} `char`s, and " +
                $"not for {count.Value} `{type}`s, which are several bytes each. Writing the later elements goes past the end of the block. " +
                $"AddressSanitizer caught it; an ordinary run often does not notice. `sizeof *{ptr}` is the size of one element, whatever " +
                $"type `{ptr}` points to.",
                source.Path, number, text[..count.Index] + size + text[(count.Index + count.Length)..]);
        }

        return null;
    }

    private static string? Declared(SourceFile source, string ptr)
    {
        var pattern = new Regex($@"(?<type>\b[A-Za-z_][\w\s]*?)\s*\*\s*{Regex.Escape(ptr)}\s*[;=,)]");
        var matches = source.Lines.Select(l => pattern.Match(l)).Where(m => m.Success).ToList();

        return matches is [var only] ? only.Groups["type"].Value.Trim() : null;
    }
}

/// <summary><c>malloc(sizeof(struct node *))</c> for a <c>struct node *</c>, or <c>sizeof(int)</c> for rows of <c>int *</c> - the
/// size of the wrong type.</summary>
public sealed partial class CMallocWrongSizeof : ILocalFixRule
{
    public string Id => "c-malloc-wrong-sizeof";

    [GeneratedRegex(@"^\s*allocated by thread \w+ here:")]
    private static partial Regex AllocatedBy();

    [GeneratedRegex(@"(?:(?<declared>\b(?:(?:const|struct|unsigned|signed|long|short)\s+)*[A-Za-z_]\w*\s*\*+)\s*)?(?<ptr>[A-Za-z_]\w*)\s*=\s*(?:\([^()]*\*\s*\)\s*)?(?<function>malloc|calloc|realloc)\s*\(")]
    private static partial Regex Allocation();

    [GeneratedRegex(@"\bsizeof\s*\(\s*(?<type>(?:(?:const|struct|unsigned|signed|long|short)\s+)*[A-Za-z_]\w*\s*\**)\s*\)")]
    private static partial Regex Sizeof();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "gcc", ExceptionType: "heap-buffer-overflow" }) return null;
        if (CCode.FrameAfter(context, AllocatedBy()) is not { } at || at.Source.Line(at.Number) is not { } line) return null;

        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.CLike);
        if (Allocation().Match(masked[at.Number - 1]) is not { Success: true } allocation) return null;

        var call = masked[at.Number - 1][(allocation.Index + allocation.Length - 1)..];
        if (Sizeof().Matches(call).ToList() is not [var size]) return null;

        var ptr = allocation.Groups["ptr"].Value;
        var declared = allocation.Groups["declared"].Success ? allocation.Groups["declared"].Value : Declared(masked, at.Number - 1, ptr);
        if (declared is null) return null;

        var pointee = Normalise(declared[..declared.LastIndexOf('*')]);
        var measured = Normalise(size.Groups["type"].Value);

        if (pointee == measured || !(pointee + "*" == measured || measured + "*" == pointee)) return null;

        var start = allocation.Index + allocation.Length - 1 + size.Index;

        return LocalFix.ReplaceLine(
            Id, $"Allocate the size of what {ptr} points to: sizeof *{ptr}",
            $"`{ptr}` points to `{Readable(pointee)}`, and the allocation measured `{Readable(measured)}` - " +
            $"{(measured.EndsWith('*') ? "the size of a pointer, 8 bytes, not the size of the thing it points at" : "the size of one element, not of the pointer each row needs")}. " +
            $"Writing past what was allocated goes into memory the program does not own; AddressSanitizer caught it, and an ordinary run " +
            $"often does not notice. `sizeof *{ptr}` is always the size of what `{ptr}` points to, so it stays right if the type changes.",
            at.Source.Path, at.Number, line[..start] + $"sizeof *{ptr}" + line[(start + size.Length)..]);
    }

    private static string Normalise(string type) => Regex.Replace(type, @"\s+", " ").Replace(" *", "*", StringComparison.Ordinal).Trim();

    private static string Readable(string type) => Regex.Replace(type, @"(?<=\w)\*", " *");

    private static string? Declared(IReadOnlyList<string> masked, int index, string ptr)
    {
        var pattern = new Regex($@"(?<type>\b(?:(?:const|struct|unsigned|signed|long|short)\s+)*[A-Za-z_]\w*\s*\*+)\s*{Regex.Escape(ptr)}\s*[;=,)]");
        var found = Enumerable.Range(0, index + 1).Select(i => pattern.Match(masked[i])).Where(m => m.Success).ToList();

        return found is [var only] ? only.Groups["type"].Value : null;
    }
}

/// <summary><c>for (n = head; n != NULL; n = n-&gt;next) free(n);</c> - reading <c>n-&gt;next</c> from the node just freed.</summary>
public sealed partial class CFreeWhileWalking : ILocalFixRule
{
    public string Id => "c-free-while-walking";

    [GeneratedRegex(@"^(?<lead>\s*)for\s*\(\s*(?<type>(?:struct\s+)?[A-Za-z_]\w*\s*\*\s*)?(?<var>[A-Za-z_]\w*)\s*=\s*(?<start>[^;]+?)\s*;\s*(?<condition>[^;]*?)\s*;\s*\k<var>\s*=\s*\k<var>\s*->\s*(?<next>[A-Za-z_]\w*)\s*\)\s*(?<rest>.*)$")]
    private static partial Regex Walk();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "gcc", ExceptionType: "heap-use-after-free" }) return null;
        if (CCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (Walk().Match(masked[number - 1]) is not { Success: true } walk) return null;

        var variable = walk.Groups["var"].Value;
        var field = walk.Groups["next"].Value;
        var rest = walk.Groups["rest"].Value.Trim();
        var freeing = new Regex($@"\bfree\s*\(\s*{Regex.Escape(variable)}\s*\)\s*;");

        var (function, end) = CCode.EnclosingFunction(masked, number - 1);
        var saved = Enumerable.Range(function, end - function + 1).Any(i => Regex.IsMatch(masked[i], @"(?<![.>\w])next\b"))
            ? "following"
            : "next";

        var lead = walk.Groups["lead"].Value;
        var original = source.Lines[number - 1];
        string header;

        if (walk.Groups["type"].Success)
        {
            var typeText = walk.Groups["type"].Value.TrimEnd();
            header = $"{lead}for ({typeText}{(typeText.EndsWith('*') ? "" : " ")}{variable} = {walk.Groups["start"].Value}, *{saved}; {walk.Groups["condition"].Value}; {variable} = {saved})";
        }
        else
        {
            return null;
        }

        if (rest == "{")
        {
            if (Brackets.BlockEnd(masked, number - 1) is not { } close) return null;
            if (!Enumerable.Range(number, close - number).Any(i => freeing.IsMatch(masked[i]))) return null;

            var inner = Enumerable.Range(number, close - number).Select(i => source.Lines[i]).FirstOrDefault(l => l.Trim().Length > 0) is { } bodyLine
                ? CodeText.Indentation(bodyLine)
                : lead + "    ";

            return new LocalFix
            {
                RuleId = Id, Title = Title(variable, field), Explanation = Explanation(variable, field, saved), File = source.Path,
                StartLine = number, RemoveCount = 1, NewLines = [header + " {", $"{inner}{saved} = {variable}->{field};"],
            };
        }

        if (!freeing.IsMatch(rest) || rest.Contains('{')) return null;

        var body = original[(original.Length - rest.Length)..].Trim();

        return LocalFix.ReplaceLine(
            Id, Title(variable, field), Explanation(variable, field, saved),
            source.Path, number, $"{header} {{ {saved} = {variable}->{field}; {body} }}");
    }

    private static string Title(string variable, string field) => $"Read {variable}->{field} before freeing {variable}";

    private static string Explanation(string variable, string field, string saved) =>
        $"The loop frees `{variable}` in its body and then reads `{variable}->{field}` to move on - from memory that was just given back. It " +
        $"usually still holds the old value, which is why the loop seems to work, until something reuses it. AddressSanitizer caught the " +
        $"read. `{saved}` keeps the next node before `{variable}` is freed.";
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

/// <summary><c>name = "Ethan";</c> for a char array, which C cannot assign to.</summary>
public sealed partial class CArrayAssignString : ILocalFixRule
{
    public string Id => "c-array-assign-string";

    [GeneratedRegex(@"^'=': left operand must be l-value$")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"^assignment to expression with array type$")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^(?<lead>\s*)(?<name>[A-Za-z_]\w*)\s*=\s*(?<value>""(?:[^""\\]|\\.)*"")\s*;(?<tail>.*)$")]
    private static partial Regex Assignment();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        var recognised = CCode.MsvcMessage(error, "C2106", MsvcMessage()) is not null || CCode.GccMessage(error, GccMessage()) is not null;

        if (!recognised || CCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (Assignment().Match(line) is not { Success: true } assignment) return null;

        if (!CCode.Includes(source, "string.h")) return null;

        var name = assignment.Groups["name"].Value;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var declaration = masked.Select(text => Regex.Match(text, $@"\bchar\s+{Regex.Escape(name)}\s*\[\s*(?<size>\d+)\s*\]")).FirstOrDefault(m => m.Success);
        if (declaration is null) return null;

        var value = assignment.Groups["value"].Value;
        var length = Regex.Replace(value[1..^1], @"\\.", "x").Length;
        var size = int.Parse(declaration.Groups["size"].Value);

        if (length + 1 > size) return null;

        return LocalFix.ReplaceLine(
            Id, $"Copy the text into {name} with strcpy",
            $"A C array cannot be assigned to - `=` only works where it is declared. `strcpy` copies the text in instead, and " +
            $"{value} with its terminating zero is {length + 1} characters, which fits the {size} that `{name}` has.",
            source.Path, number, $"{assignment.Groups["lead"].Value}strcpy({name}, {value});{assignment.Groups["tail"].Value}");
    }
}

/// <summary><c>scanf("%19s", &amp;name)</c> for a <c>char</c> array - whose name already is the address.</summary>
public sealed partial class CScanfArrayAddress : ILocalFixRule
{
    public string Id => "c-scanf-array-address";

    [GeneratedRegex(@"^format '%\d*(?:s|\[[^\]]*\])' expects argument of type 'char \*', but argument \d+ has type 'char \(\*\)\[\d+\]'")]
    private static partial Regex GccMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.ExceptionType != "compile warning" || CCode.GccMessage(context.Error, GccMessage()) is null) return null;
        if (CCode.Locate(context) is not { } at) return null;

        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.CLike);
        var addresses = Regex.Matches(masked[at.Number - 1], @"&\s*(?<name>[A-Za-z_]\w*)\b(?!\s*[\[.(]|\s*->)")
            .Where(m => CCode.DeclaredAsArray(masked, at.Number - 1, m.Groups["name"].Value))
            .ToList();

        if (addresses is not [var address]) return null;

        var name = address.Groups["name"].Value;

        return LocalFix.ReplaceLine(
            Id, $"Pass the array itself: {name}, not &{name}",
            $"`{name}` is an array, and an array's name already stands for the address of its first character, which is what `%s` wants. " +
            $"`&{name}` is the address of the whole array - the same number, of a different type - so it happens to work, and the compiler " +
            "warns because it is not what the format promises.",
            at.Source.Path, at.Number, at.Line[..address.Index] + name + at.Line[(address.Index + address.Length)..]) with
        {
            ResolvesWarning = "expects argument of type 'char *'",
        };
    }
}
