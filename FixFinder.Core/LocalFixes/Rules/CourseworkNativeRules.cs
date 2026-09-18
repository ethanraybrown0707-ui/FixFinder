using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes.Rules;

// The C and C++ mistakes of a computer science degree past the first weeks: linked lists and dynamic arrays, headers split
// across files, threads, comparators, operator overloading, inheritance and exceptions. Several of them run without failing,
// and are found by a compiler warning or by AddressSanitizer; each rule proposes one change.

internal static partial class NativeCourse
{
    [GeneratedRegex(@"^\s*#\d+\s+0x[0-9a-fA-F]+\s+in\s+\S+\s+(?<file>.+?):(?<line>\d+)")]
    public static partial Regex Frame();

    /// <summary>The first frame in the program's own files after a line of AddressSanitizer's report matching <paramref name="section"/>.</summary>
    public static (SourceFile Source, int Number)? FrameAfter(LocalFixContext context, Regex section)
    {
        var output = context.Output.Select(l => l.Text).ToList();
        var start = output.FindIndex(l => section.IsMatch(l));
        if (start < 0) return null;

        foreach (var line in output.Skip(start + 1).TakeWhile(l => l.Trim().Length > 0))
        {
            if (Frame().Match(line) is not { Success: true } frame || context.Read(frame.Groups["file"].Value.Trim()) is not { } source) continue;
            if (!CCode.IsNative(source)) continue;

            return (source, int.Parse(frame.Groups["line"].Value));
        }

        return null;
    }

    /// <summary>A header added after the last <c>#include</c>, with a change further down, as one run of lines.</summary>
    public static LocalFix WithHeader(string rule, string title, string explanation, SourceFile source, int number, string header, string changed)
    {
        if (CCode.Includes(source, header))
            return LocalFix.ReplaceLine(rule, title, explanation, source.Path, number, changed);

        var lines = source.Lines;
        var last = Enumerable.Range(0, number - 1).LastOrDefault(i => Msvc.Include().IsMatch(lines[i]), -1);
        var start = last + 2;

        return new LocalFix
        {
            RuleId = rule, Title = title, Explanation = explanation, File = source.Path,
            StartLine = start, RemoveCount = number - start + 1,
            NewLines = [$"#include <{header}>", .. lines.Skip(start - 1).Take(number - start), changed],
        };
    }

    /// <summary>The closing brace of the block opened on <paramref name="line"/>, or null.</summary>
    public static int? BlockEnd(IReadOnlyList<string> masked, int line)
    {
        var depth = 0;
        var opened = false;

        for (var i = line; i < masked.Count; i++)
        {
            foreach (var c in masked[i])
            {
                if (c == '{') { depth++; opened = true; }
                else if (c == '}' && --depth == 0 && opened) return i;
            }

            if (!opened && i > line) return null;
        }

        return null;
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
            .Where(m => SilentMistakes.DeclaredAsArray(masked, at.Number - 1, m.Groups["name"].Value))
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

/// <summary><c>malloc(sizeof(struct node *))</c> for a <c>struct node *</c>, or <c>sizeof(int)</c> for rows of <c>int *</c> - the size of the wrong type.</summary>
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
        if (NativeCourse.FrameAfter(context, AllocatedBy()) is not { } at || at.Source.Line(at.Number) is not { } line) return null;

        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.CLike);
        if (Allocation().Match(masked[at.Number - 1]) is not { Success: true } allocation) return null;

        var call = masked[at.Number - 1][(allocation.Index + allocation.Length - 1)..];
        if (Sizeof().Matches(call).ToList() is not [var size]) return null;

        var ptr = allocation.Groups["ptr"].Value;
        var declared = allocation.Groups["declared"].Success ? allocation.Groups["declared"].Value : Declared(masked, at.Number - 1, ptr);
        if (declared is null) return null;

        var pointee = Normalise(declared[..declared.LastIndexOf('*')]);
        var measured = Normalise(size.Groups["type"].Value);

        // Off by exactly one star, either way - the size of the pointer where the thing was meant, or the reverse.
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

        // "next", unless the function already has something of that name that is not a member.
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
            if (NativeCourse.BlockEnd(masked, number - 1) is not { } close) return null;
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

/// <summary><c>redefinition of 'struct point'</c> in a header included twice - a header with no include guard.</summary>
public sealed partial class CHeaderGuard : ILocalFixRule
{
    public string Id => "c-header-guard";

    // A type defined twice is an error before C23; from C23 an identical one is allowed, and a variable defined twice is what
    // still fails - so both are read.
    [GeneratedRegex(@"^redefinition of '(?:(?:struct|union|enum|class) )?(?<name>\w+)'$")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^'(?<name>\w+)': (?:'(?:struct|union|enum|class)' type redefinition|redefinition(?:; multiple initialization)?)$")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"^\s*#\s*(?:pragma\s+once|ifndef\s+\w+)")]
    private static partial Regex Guard();

    public LocalFix? Propose(LocalFixContext context)
    {
        if ((CCode.GccMessage(context.Error, GccMessage()) ?? CCode.MsvcMessage(context.Error, "C2011", MsvcMessage()) ??
             CCode.MsvcMessage(context.Error, "C2374", MsvcMessage()) ?? CCode.MsvcMessage(context.Error, "C2086", MsvcMessage())) is null) return null;
        if (context.Frame?.File is not { } file || context.Read(file) is not { } source) return null;

        if (Path.GetExtension(source.Path).ToLowerInvariant() is not (".h" or ".hpp" or ".hh" or ".hxx")) return null;
        if (!source.EndsWithNewline || source.Lines.FirstOrDefault(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith("//", StringComparison.Ordinal)) is not { } first || Guard().IsMatch(first)) return null;

        var guard = Regex.Replace(Path.GetFileName(source.Path).ToUpperInvariant(), @"[^A-Z0-9]", "_");
        if (char.IsDigit(guard[0])) guard = "_" + guard;

        var body = source.Lines.ToList();
        while (body.Count > 0 && body[^1].Trim().Length == 0) body.RemoveAt(body.Count - 1);

        return new LocalFix
        {
            RuleId = Id,
            Title = $"Guard {Path.GetFileName(source.Path)} against being included twice",
            Explanation =
                $"`{Path.GetFileName(source.Path)}` is included more than once in the same file - directly, and again through another header - " +
                "so everything in it is defined twice, and a second definition of the same type is an error. An include guard skips the " +
                $"header's contents the second time: `{guard}` is defined the first time through, and `#ifndef` sees it after that.",
            File = source.Path, StartLine = 1, RemoveCount = source.Count,
            NewLines = [$"#ifndef {guard}", $"#define {guard}", "", .. body, "", $"#endif"],
        };
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
        if (NativeCourse.BlockEnd(masked, header) is not { } close) return null;

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
        var cpp = Cpp.IsCpp(source);

        return NativeCourse.WithHeader(
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

/// <summary><c>int compare(int *a, int *b)</c> given to <c>qsort</c>, which calls it with <c>const void *</c>.</summary>
public sealed partial class CQsortComparator : ILocalFixRule
{
    public string Id => "c-qsort-comparator";

    [GeneratedRegex(@"^passing argument 4 of 'qsort' from incompatible pointer type")]
    private static partial Regex GccMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.ExceptionType is not ("compile warning" or "compile error") || CCode.GccMessage(context.Error, GccMessage()) is null) return null;
        if (CCode.Locate(context) is not { } at || Cpp.IsCpp(at.Source)) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var calls = Regex.Matches(masked[at.Number - 1], @"\bqsort\s*\(.*,\s*&?\s*(?<function>[A-Za-z_]\w*)\s*\)").ToList();
        if (calls is not [var call]) return null;

        var function = call.Groups["function"].Value;
        var definition = new Regex($@"^(?<lead>\s*(?:static\s+)?)int\s+{Regex.Escape(function)}\s*\(\s*(?:const\s+)?(?<type>(?:struct\s+)?\w+)\s*\*\s*(?<a>\w+)\s*,\s*(?:const\s+)?\k<type>\s*\*\s*(?<b>\w+)\s*\)\s*\{{\s*$");

        if (Enumerable.Range(0, masked.Count).Where(i => definition.IsMatch(masked[i])).ToList() is not [var header]) return null;

        var match = definition.Match(source.Lines[header]);
        var type = match.Groups["type"].Value;
        var (a, b) = (match.Groups["a"].Value, match.Groups["b"].Value);
        var (pa, pb) = ("p" + a, "p" + b);

        if (masked.Any(l => Regex.IsMatch(l, $@"\b(?:{pa}|{pb})\b"))) return null;

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
                $"{match.Groups["lead"].Value}int {function}(const void *{pa}, const void *{pb}) {{",
                $"{inner}const {type} *{a} = {pa};",
                $"{inner}const {type} *{b} = {pb};",
            ],
            ResolvesWarning = "incompatible pointer type",
        };
    }
}

/// <summary><c>C4700: uninitialized local variable 'sum' used</c> - a total that starts from whatever was in memory.</summary>
public sealed partial class CUninitialisedAccumulator : ILocalFixRule
{
    public string Id => "c-uninitialised-accumulator";

    [GeneratedRegex(@"^uninitialized local variable '(?<name>\w+)' used$")]
    private static partial Regex MsvcMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (CCode.MsvcMessage(context.Error, "C4700", MsvcMessage()) is not { } message || CCode.Locate(context) is not { } at) return null;

        var name = message.Groups["name"].Value;
        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        // Only a running total or product: the first use adds to it, and nothing sets it before that.
        var use = masked[at.Number - 1];
        var start = Regex.IsMatch(use, $@"(?<![\w.>]){Regex.Escape(name)}\s*(?:\+=|-=|\+\+|--)|(?:\+\+|--)\s*{Regex.Escape(name)}\b|(?<![\w.>]){Regex.Escape(name)}\s*=\s*{Regex.Escape(name)}\s*[+-]")
            ? "0"
            : Regex.IsMatch(use, $@"(?<![\w.>]){Regex.Escape(name)}\s*\*=|(?<![\w.>]){Regex.Escape(name)}\s*=\s*{Regex.Escape(name)}\s*\*") ? "1" : null;

        if (start is null) return null;

        var (function, _) = CCode.EnclosingFunction(masked, at.Number - 1);
        var declaration = new Regex($@"^(?<head>\s*(?:(?:unsigned|signed|long|short|const)\s+)*(?:int|long|short|float|double|size_t|char)\s+(?:[A-Za-z_]\w*\s*(?:=[^,;]+)?\s*,\s*)*{Regex.Escape(name)})(?<tail>\s*[,;].*)$");

        var found = Enumerable.Range(function, at.Number - 1 - function).Where(i => declaration.IsMatch(masked[i])).ToList();
        if (found is not [var index]) return null;

        var original = source.Lines[index];
        var match = declaration.Match(original);

        return LocalFix.ReplaceLine(
            Id, $"Start {name} from {start}",
            $"`{name}` is declared without a value, so it starts as whatever happened to be in that memory, and the {(start == "0" ? "total" : "product")} " +
            $"is built on top of it. The program can print the right answer by luck - on another run, or another machine, it will not. A " +
            $"{(start == "0" ? "running total starts from 0" : "running product starts from 1")}.",
            source.Path, index + 1, match.Groups["head"].Value + " = " + start + match.Groups["tail"].Value) with
        {
            ResolvesWarning = "C4700",
        };
    }
}

// ======================================================================= C++

/// <summary><c>std::ostream&amp; operator&lt;&lt;(std::ostream&amp;, const Point&amp;)</c> written inside the class, without <c>friend</c>.</summary>
public sealed partial class CppStreamOperatorFriend : ILocalFixRule
{
    public string Id => "cpp-stream-operator-friend";

    [GeneratedRegex(@"operator(?:<<|>>)\([^']*\)' must (?:have|take) exactly one argument$")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^binary operator '(?:<<|>>)' has too many parameters$")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"^(?<lead>\s*)(?!friend\b)(?<rest>(?:std::)?[io]?stream\s*&\s*operator\s*(?:<<|>>)\s*\(.*)$")]
    private static partial Regex Declaration();

    public LocalFix? Propose(LocalFixContext context)
    {
        if ((CCode.GccMessage(context.Error, GccMessage()) ?? CCode.MsvcMessage(context.Error, "C2804", MsvcMessage())) is null) return null;
        if (Cpp.Locate(context) is not { } at || Declaration().Match(at.Line) is not { Success: true } declaration) return null;

        return LocalFix.ReplaceLine(
            Id, "Make the stream operator a friend of the class",
            "An operator written inside a class takes the object itself as its left side, so `operator<<` there would be used as " +
            "`point << stream` and can only take one more argument. `std::cout << point` has the stream on the left, which needs a " +
            "function outside the class; `friend` makes this one exactly that, while still letting it read the private members.",
            at.Source.Path, at.Number, declaration.Groups["lead"].Value + "friend " + declaration.Groups["rest"].Value);
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
        if (Cpp.Locate(context) is not { } at) return null;

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

/// <summary><c>double area() override</c> where the base declares <c>virtual double area() const</c> - the missing <c>const</c> makes it a different function.</summary>
public sealed partial class CppOverrideMissingConst : ILocalFixRule
{
    public string Id => "cpp-override-missing-const";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (CppClass.Override(context.Error) is not { } message || Cpp.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var member = message.Groups["member"].Value;

        var own = Regex.Match(masked[number - 1], $@"(?<![\w:~]){Regex.Escape(member)}\s*\((?<parameters>[^()]*)\)\s*(?<const>const\s*)?(?<override>override)\b");
        if (!own.Success || own.Groups["const"].Success) return null;

        var header = CppClass.Header(masked, message.Groups["class"].Value);
        if (header < 0) return null;

        var parameters = Normalise(own.Groups["parameters"].Value);
        var constInBase = false;

        foreach (var (name, _, _) in CppClass.Bases(masked[header]))
        {
            var baseHeader = CppClass.Header(masked, name);
            if (baseHeader < 0) continue;

            foreach (var i in CppClass.Declaring(masked, baseHeader, member))
            {
                var declared = Regex.Match(masked[i], $@"\bvirtual\b.*?(?<![\w:~]){Regex.Escape(member)}\s*\((?<parameters>[^()]*)\)\s*(?<const>const)?");
                if (declared.Success && declared.Groups["const"].Success && Normalise(declared.Groups["parameters"].Value) == parameters) constInBase = true;
            }
        }

        if (!constInBase) return null;

        var at0 = own.Groups["override"].Index;

        return LocalFix.ReplaceLine(
            Id, $"Match the base class: {member}() const override",
            $"The base class declares `{member}` as `const` - it promises not to change the object - and `const` is part of which function " +
            $"it is. Without it, this `{member}` is a different function that overrides nothing, and the class stays abstract. Adding " +
            "`const` makes it the same function.",
            source.Path, number, line[..at0] + "const " + line[at0..]);
    }

    private static string Normalise(string parameters) =>
        string.Join(",", parameters.Split(',').Select(p => Regex.Replace(Regex.Replace(p.Trim(), @"\s+\w+$", ""), @"\s+", " "))).Trim();
}

/// <summary><c>std::thread worker(increment, counter)</c> for <c>void increment(int&amp;)</c> - a thread copies its arguments unless told otherwise.</summary>
public sealed partial class CppThreadReference : ILocalFixRule
{
    public string Id => "cpp-thread-reference";

    [GeneratedRegex(@"std::thread arguments must be invocable after conversion to rvalues|'std::invoke': no matching overloaded function found")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?<file>.+?\.(?:cpp|cc|cxx|c\+\+)):(?<line>\d+):\d+:\s+required from here|^(?<file>.+?\.(?:cpp|cc|cxx|c\+\+))\((?<line>\d+)\)\s*:\s*note: see reference to function template instantiation 'std::thread::thread")]
    private static partial Regex Instantiated();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.LanguageId is not ("gcc" or "msvc") || !Message().IsMatch(context.Error.Message ?? "")) return null;

        foreach (var text in context.Output.Select(l => l.Text.Trim()))
        {
            if (Instantiated().Match(text) is not { Success: true } place || context.Read(place.Groups["file"].Value) is not { } source) continue;

            var number = int.Parse(place.Groups["line"].Value);
            if (source.Line(number) is not { } line) continue;

            return Wrap(source, number, line);
        }

        return null;
    }

    private LocalFix? Wrap(SourceFile source, int number, string line)
    {
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var construct = Regex.Match(masked[number - 1], @"\bstd::thread\s*(?:[A-Za-z_]\w*\s*)?[({]\s*(?<function>[A-Za-z_]\w*)\s*,(?<arguments>[^;]*)[)}]\s*;");
        if (!construct.Success) return null;

        var function = construct.Groups["function"].Value;
        var definition = masked.Select(l => Regex.Match(l, $@"^\s*(?:static\s+)?void\s+{Regex.Escape(function)}\s*\((?<parameters>[^()]*)\)")).FirstOrDefault(m => m.Success);
        if (definition is null) return null;

        var parameters = definition.Groups["parameters"].Value.Split(',').Select(p => p.Trim()).ToList();
        var group = construct.Groups["arguments"];
        var parts = Cpp.SplitTopLevel(masked[number - 1], group.Index, group.Index + group.Length, ',');
        if (parts.Count != parameters.Count) return null;

        var wrapped = new List<(int Start, int End, string Name)>();

        for (var i = 0; i < parts.Count; i++)
        {
            var (start, end) = parts[i];
            var argument = line[start..end].Trim();

            if (Regex.IsMatch(parameters[i], @"^(?!const\b)[\w:<>\s]+&\s*\w*$") && Regex.IsMatch(argument, @"^[A-Za-z_]\w*$"))
                wrapped.Add((start + line[start..end].IndexOf(argument, StringComparison.Ordinal), 0, argument));
        }

        if (wrapped.Count == 0) return null;

        var result = line;
        foreach (var (start, _, name) in wrapped.OrderByDescending(w => w.Start))
            result = result[..start] + $"std::ref({name})" + result[(start + name.Length)..];

        var names = string.Join(", ", wrapped.Select(w => w.Name));

        return LocalFix.ReplaceLine(
            Id, $"Pass {names} by reference: std::ref({wrapped[0].Name})",
            $"A `std::thread` copies every argument it is given, so the new thread would get its own copy of `{names}` and `{function}` would " +
            $"change the copy - which a non-const reference is not allowed to bind to, so it does not compile. `std::ref` passes the " +
            "variable itself. It must still exist until the thread is joined.",
            source.Path, number, result);
    }
}

/// <summary><c>delete pet</c> through a base class whose destructor is not <c>virtual</c> - the derived destructor never runs.</summary>
public sealed partial class CppVirtualDestructor : ILocalFixRule
{
    public string Id => "cpp-virtual-destructor";

    [GeneratedRegex(@"^deleting object of (?:abstract|polymorphic) class type '(?<class>\w+)' which has non-virtual destructor")]
    private static partial Regex GccMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.ExceptionType != "compile warning" || CCode.GccMessage(context.Error, GccMessage()) is not { } message) return null;
        if (context.Read(context.Frame?.File) is not { } source) return null;

        var name = message.Groups["class"].Value;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var header = CppClass.Header(masked, name);
        if (header < 0 || CppClass.Body(masked, header) is not { } body) return null;

        var members = CppClass.Members(masked, header);
        var destructor = members.Where(i => Regex.IsMatch(masked[i], $@"(?<![\w:])~{Regex.Escape(name)}\s*\(")).ToList();

        const string Explanation =
            "The object is deleted through a pointer to its base class, and the base class's destructor is not `virtual` - so only the base " +
            "part is destroyed, the derived class's destructor never runs, and whatever it would have released leaks. A class meant to be " +
            "used through a base pointer needs a `virtual` destructor, which makes `delete` find the right one.";

        if (destructor is [var line])
        {
            var text = source.Lines[line];
            var at = Regex.Match(text, $@"(?<![\w:])~{Regex.Escape(name)}\s*\(").Index;
            if (Regex.IsMatch(masked[line][..at], @"\bvirtual\b")) return null;

            return LocalFix.ReplaceLine(Id, $"Make ~{name}() virtual", Explanation, source.Path, line + 1, text[..at] + "virtual " + text[at..]) with
            {
                ResolvesWarning = "non-virtual destructor",
            };
        }

        if (destructor.Count > 0) return null;

        var isStruct = Regex.IsMatch(masked[header], @"^\s*struct\b");
        var publicLine = members.FirstOrDefault(i => Regex.IsMatch(masked[i], @"^\s*public\s*:"), -1);
        var indent = members.Select(i => source.Lines[i]).FirstOrDefault(l => l.Trim().Length > 0 && !l.Trim().EndsWith(':')) is { } memberLine
            ? CodeText.Indentation(memberLine)
            : "    ";

        IReadOnlyList<string> added = publicLine >= 0 || isStruct
            ? [$"{indent}virtual ~{name}() = default;"]
            : [$"public:", $"{indent}virtual ~{name}() = default;"];

        var before = publicLine >= 0 ? publicLine + 2 : body.Open + 2;

        return LocalFix.Insert(Id, $"Give {name} a virtual destructor", Explanation, source.Path, before, added) with
        {
            ResolvesWarning = "non-virtual destructor",
        };
    }
}

/// <summary><c>catch (std::exception e)</c> - a copy of just the base part, which loses what was really thrown.</summary>
public sealed partial class CppCatchByReference : ILocalFixRule
{
    public string Id => "cpp-catch-by-reference";

    [GeneratedRegex(@"^catching polymorphic type '(?:class |struct )?(?<type>[\w:]+)' by value")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"\bcatch\s*\(\s*(?<const>const\s+)?(?<type>[\w:]+)\s+(?<name>[A-Za-z_]\w*)\s*\)")]
    private static partial Regex Catch();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.ExceptionType != "compile warning" || CCode.GccMessage(context.Error, GccMessage()) is null) return null;
        if (Cpp.Locate(context) is not { } at || Catch().Matches(at.Line).ToList() is not [var clause]) return null;

        var type = clause.Groups["type"].Value;
        var name = clause.Groups["name"].Value;

        return LocalFix.ReplaceLine(
            Id, $"Catch by reference: const {type}& {name}",
            $"Catching `{type}` by value copies just the `{type}` part of whatever was thrown - so `{name}.what()` answers for a plain " +
            $"`{type}`, and the real message, from the class that was thrown, is lost. A reference is the thrown object itself.",
            at.Source.Path, at.Number, at.Line[..clause.Index] + $"catch (const {type}& {name})" + at.Line[(clause.Index + clause.Length)..]) with
        {
            ResolvesWarning = "by value",
        };
    }
}
