using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes.Rules;

// Mistakes in C and C++ that often run without failing: the compiler warns, or AddressSanitizer catches the program
// touching memory it does not own, but an ordinary run happens to survive. Each rule here changes one line.

/// <summary><c>gets(name);</c> - which cannot know how big <c>name</c> is, and writes past its end on a long enough line.</summary>
/// <remarks>
/// <c>fgets</c> is told the size, and stops there. It also keeps the newline <c>gets</c> threw away, so the replacement
/// removes it again - with <c>strcspn</c> when <c>&lt;string.h&gt;</c> is already included, and with a short loop that needs
/// no header when it is not - so the program prints exactly what it printed before.
/// </remarks>
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

        // sizeof name is the buffer's size only for an array; for a pointer it is the size of the pointer.
        if (!SilentMistakes.DeclaredAsArray(masked, at.Number - 1, name)) return null;

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
/// <remarks>
/// <c>static</c> keeps the array alive after the function returns, which is the one-line change that makes the returned
/// address valid. Every call shares that one array, which the explanation says.
/// </remarks>
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
        var depths = CCode.DepthAtStart(masked);

        // The declaration at the function's own level - not one inside a nested block, and not a parameter.
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
/// <remarks>
/// Found by AddressSanitizer as a write just past the end of a block, with the line that allocated it. Offered only when
/// that line calls <c>malloc</c> with no <c>sizeof</c> in its size, for a pointer to something bigger than a <c>char</c>.
/// </remarks>
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

        // The first frame of the allocation that is in a file of the program's own.
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

/// <summary><c>delete values;</c> for memory from <c>new int[10]</c> - which must be <c>delete[]</c>.</summary>
public sealed partial class CppDeleteArray : ILocalFixRule
{
    public string Id => "cpp-delete-array";

    [GeneratedRegex(@"called on pointer returned from a mismatched allocation function")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^(?<lead>\s*)delete\s+(?<name>[A-Za-z_]\w*)\s*;(?<tail>.*)$")]
    private static partial Regex Delete();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.ExceptionType != "compile warning" || CCode.GccMessage(context.Error, GccMessage()) is null) return null;
        if (Cpp.Locate(context) is not { } at || Delete().Match(at.Line) is not { Success: true } delete) return null;

        var name = delete.Groups["name"].Value;
        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.CLike);

        if (!masked.Any(l => Regex.IsMatch(l, $@"\b{Regex.Escape(name)}\s*=\s*new\s+[\w:<>,\s]+\["))) return null;

        return LocalFix.ReplaceLine(
            Id, $"Free an array with delete[]: delete[] {name}",
            $"`{name}` came from `new ...[]`, an array, and an array has to be freed with `delete[]`. Plain `delete` frees it as one " +
            "object, which is undefined behaviour - often nothing visible happens, and sometimes the heap is quietly damaged.",
            at.Source.Path, at.Number, $"{delete.Groups["lead"].Value}delete[] {name};{delete.Groups["tail"].Value}") with
        {
            ResolvesWarning = "mismatched allocation function",
        };
    }
}

internal static class SilentMistakes
{
    /// <summary>Whether <paramref name="name"/> is declared as an array in the function around <paramref name="index"/>, or at file level.</summary>
    public static bool DeclaredAsArray(IReadOnlyList<string> masked, int index, string name)
    {
        var (header, _) = CCode.EnclosingFunction(masked, index);
        var array = new Regex($@"\b{Regex.Escape(name)}\s*\[[^\]]*\]");
        var pointer = new Regex($@"\*\s*{Regex.Escape(name)}\b");
        var depths = CCode.DepthAtStart(masked);

        var scope = Enumerable.Range(header, Math.Max(0, index - header)).Concat(Enumerable.Range(0, masked.Count).Where(i => depths[i] == 0));

        return scope.Any(i => array.IsMatch(masked[i])) && !scope.Any(i => pointer.IsMatch(masked[i]) && !array.IsMatch(masked[i]));
    }
}
