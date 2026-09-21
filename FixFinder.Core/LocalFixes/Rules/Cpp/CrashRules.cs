using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>libstdc++'s <c>vector::_M_range_check: __n (which is 3) &gt;= this-&gt;size() (which is 3)</c> - an <c>.at(i)</c> in
/// a loop that runs to <c>&lt;= size()</c>.</summary>
public sealed partial class CppAtOutOfRange : ILocalFixRule
{
    public string Id => "cpp-at-out-of-range";

    [GeneratedRegex(@"^(?:vector::_M_range_check|basic_string::at): __n \(which is (?<index>\d+)\) >= this->size\(\) \(which is (?<size>\d+)\)")]
    private static partial Regex Message();

    [GeneratedRegex(@"^invalid (?:vector subscript|string position)$")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"\bfor\s*\(\s*(?:[\w:]+\s+)?(?<var>[A-Za-z_]\w*)\s*=\s*0\s*;\s*\k<var>\s*(?<op><=)\s*(?<object>[A-Za-z_]\w*)\s*\.\s*(?:size|length)\s*\(\s*\)\s*;")]
    private static partial Regex Loop();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "gcc", ExceptionType: "std::out_of_range" } error) return null;

        var message = Message().Match(error.Message ?? "");
        if (message.Success ? message.Groups["index"].Value != message.Groups["size"].Value : !MsvcMessage().IsMatch(error.Message ?? "")) return null;

        if (context.SourceRoot is not { } root || !Directory.Exists(root)) return null;

        var found = new List<(SourceFile Source, int Line, Match Loop)>();

        foreach (var path in Directory.EnumerateFiles(root).Where(p => Path.GetExtension(p).ToLowerInvariant() is ".cpp" or ".cc" or ".cxx" or ".c++").Take(200))
        {
            if (SourceFile.Read(path) is not { } source) continue;

            var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

            for (var i = 0; i < masked.Count; i++)
            {
                if (Loop().Match(masked[i]) is not { Success: true } loop) continue;

                var use = new Regex($@"\b{Regex.Escape(loop.Groups["object"].Value)}\s*\.\s*at\s*\(\s*{Regex.Escape(loop.Groups["var"].Value)}\s*\)");
                if (Body(masked, i, loop.Index + loop.Length).Any(k => use.IsMatch(masked[k]))) found.Add((source, i, loop));
            }
        }

        if (found is not [var (file, index, only)]) return null;

        var op = only.Groups["op"];
        var original = file.Lines[index];
        var vector = only.Groups["object"].Value;
        var variable = only.Groups["var"].Value;

        var counted = message.Success
            ? $"`{vector}` had {message.Groups["size"].Value} elements, numbered 0 to {int.Parse(message.Groups["size"].Value) - 1}, and the loop on " +
              $"line {index + 1} runs while `{variable} <= {vector}.size()` - so it asked for element {message.Groups["size"].Value}."
            : $"The loop on line {index + 1} runs while `{variable} <= {vector}.size()`, so its last pass asks for the element one past the end.";

        return LocalFix.ReplaceLine(
            Id, $"Stop the loop before {vector}.size(): <",
            $"`{vector}.at({variable})` checks the index and throws `std::out_of_range` when it is past the end. {counted} The exception came " +
            "with no line number; this is the only loop in the program that reads `.at()` up to and including `.size()`.",
            file.Path, index + 1, original[..op.Index] + "<" + original[(op.Index + op.Length)..]);
    }

    private static IEnumerable<int> Body(IReadOnlyList<string> masked, int line, int after)
    {
        var rest = masked[line][after..];
        var brace = rest.IndexOf('{');

        if (brace < 0)
        {
            yield return rest.Trim().TrimEnd(')').Trim().Length > 0 ? line : Math.Min(line + 1, masked.Count - 1);
            yield break;
        }

        var depth = 0;

        for (var i = line; i < masked.Count; i++)
        {
            yield return i;

            for (var c = i == line ? after + brace : 0; c < masked[i].Length; c++)
            {
                if (masked[i][c] == '{') depth++;
                else if (masked[i][c] == '}' && --depth == 0) yield break;
            }
        }
    }
}

/// <summary>libstdc++'s <c>terminate called without an active exception</c> - a <c>std::thread</c> never joined.</summary>
public sealed partial class CppThreadNotJoined : ILocalFixRule
{
    public string Id => "cpp-thread-not-joined";

    [GeneratedRegex(@"^\s*(?:std\s*::\s*)?thread\s+(?<name>[A-Za-z_]\w*)\s*[({]")]
    private static partial Regex Thread();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "gcc", ExceptionType: "std::terminate" }) return null;

        var missing = new List<(SourceFile Source, int Line, int Before, string Name)>();

        foreach (var source in CppCode.SourceFiles(context))
        {
            var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
            var depths = Brackets.BraceDepths(masked);

            for (var i = 0; i < masked.Count; i++)
            {
                if (depths[i] == 0 || Thread().Match(masked[i]) is not { Success: true } thread) continue;

                var name = thread.Groups["name"].Value;
                var (_, last) = CCode.EnclosingFunction(masked, i);
                var finished = new Regex($@"\b{Regex.Escape(name)}\s*\.\s*(?:join|detach)\s*\(");

                if (Enumerable.Range(i + 1, Math.Max(0, last - i)).Any(k => finished.IsMatch(masked[k]))) continue;

                var before = Enumerable.Range(i + 1, Math.Max(0, last - i - 1))
                    .FirstOrDefault(k => depths[k] == depths[i] && Regex.IsMatch(masked[k], @"^\s*return\b"), last);

                missing.Add((source, i, before, name));
            }
        }

        if (missing is not [var (file, line, at, threadName)]) return null;

        return LocalFix.Insert(
            Id, $"Wait for {threadName} to finish: {threadName}.join()",
            $"A `std::thread` still attached to its thread when its variable goes out of scope ends the whole program - that is the `terminate " +
            $"called without an active exception`. `{threadName}.join()` waits for the thread to finish first. (`detach()` would let it run on " +
            "unwatched, which is rarely what was meant.)",
            file.Path, at + 1, [CodeText.Indentation(file.Lines[line]) + $"{threadName}.join();"]);
    }
}

/// <summary><c>values[0] = 5;</c> on a vector that was created empty - AddressSanitizer's write to a null address.</summary>
public sealed partial class CppIndexEmptyVector : ILocalFixRule
{
    public string Id => "cpp-index-empty-vector";

    [GeneratedRegex(@"^(?<lead>\s*)(?<vector>[A-Za-z_]\w*)\s*\[\s*0\s*\]\s*=\s*(?<value>[^;=]+?)\s*;(?<tail>.*)$")]
    private static partial Regex Write();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "gcc", ExceptionType: "access-violation" } error) return null;
        if (!(error.Message ?? "").Contains("(a null pointer)", StringComparison.Ordinal) || CCode.Locate(context) is not { } at) return null;
        if (!CppCode.IsCpp(at.Source)) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (Write().Match(masked[number - 1]) is not { Success: true } write) return null;

        var vector = write.Groups["vector"].Value;
        if (CppCode.VariableDeclaration(masked, number - 1, vector) is not { Pointer: false, Reference: false } declaration) return null;
        if (!Regex.IsMatch(declaration.Type, @"^(?:std\s*::\s*)?vector\s*<")) return null;
        if (!Regex.IsMatch(masked[declaration.Line], $@"\b{Regex.Escape(vector)}\s*(?:\{{\s*\}})?\s*;")) return null;

        var (_, last) = CCode.EnclosingFunction(masked, number - 1);
        var touched = new Regex($@"(?<![\w.>]){Regex.Escape(vector)}\s*(?:\.|->|=(?!=)|\[)");

        for (var i = declaration.Line + 1; i < number - 1; i++)
            if (touched.IsMatch(masked[i])) return null;

        for (var i = number; i <= last && i < masked.Count; i++)
            if (Regex.IsMatch(masked[i], $@"(?<![\w.>]){Regex.Escape(vector)}\s*\[[^\]]*\]\s*=(?!=)")) return null;

        var value = line.Substring(write.Groups["value"].Index, write.Groups["value"].Length);

        return LocalFix.ReplaceLine(
            Id, $"Add to {vector} with push_back",
            $"`{vector}` was created empty, and `[0]` does not add an element - it reaches one that must already be there. An empty vector has " +
            $"nothing at `[0]`, so the write went to a null address. `{vector}.push_back({value})` adds the value as a new element.",
            source.Path, number, $"{write.Groups["lead"].Value}{vector}.push_back({value});{line[write.Groups["tail"].Index..]}");
    }
}

/// <summary><c>for (int v : values) if (...) values.erase(std::find(...));</c> - erasing from a vector inside its own range-based
/// loop, which AddressSanitizer catches walking off the end.</summary>
public sealed class CppEraseInLoop : ILocalFixRule
{
    public string Id => "cpp-erase-in-loop";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "gcc", ExceptionType: "container-overflow" or "heap-use-after-free" or "heap-buffer-overflow" }) return null;
        if (CCode.Locate(context) is not { } at || !CppCode.IsCpp(at.Source)) return null;

        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.CLike);
        if (BraceRules.RemoveInLoop(at.Source.Lines, masked, at.Number - 1, "cpp") is not { } fix) return null;

        return new LocalFix
        {
            RuleId = Id,
            Title = "Erase with std::remove_if instead of inside the loop",
            Explanation =
                "Erasing from a vector closes the gap by moving every later element down one place - while the range-based `for` keeps " +
                "walking with the positions it already had, so it skips elements and runs off the end, which is what AddressSanitizer " +
                "caught. `std::remove_if` moves the elements to keep to the front in one pass, and a single `erase` then trims the rest.",
            File = at.Source.Path,
            StartLine = fix.Start + 1,
            RemoveCount = fix.Count,
            NewLines = [fix.Line],
        };
    }
}

/// <summary>A function returning a reference to its own local variable: MSVC's <c>C4172</c>, gcc's <c>-Wreturn-local-addr</c>.</summary>
public sealed partial class CppReturnLocalReference : ILocalFixRule
{
    public string Id => "cpp-return-local-reference";

    [GeneratedRegex(@"^returning address of local variable or temporary(?: : (?<name>\w+))?$")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"^reference to local variable '(?<name>\w+)' returned")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^(?<lead>\s*)(?:const\s+)?(?<type>[A-Za-z_][\w:]*(?:\s*<[^()]*>)?)\s*&\s*(?<function>[A-Za-z_][\w:]*)\s*\(")]
    private static partial Regex Header();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if (error.ExceptionType != "compile warning") return null;
        if ((CCode.MsvcMessage(error, "C4172", MsvcMessage()) ?? CCode.GccMessage(error, GccMessage())) is not { } message) return null;
        if (CppCode.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var (first, _) = CCode.EnclosingFunction(masked, at.Number - 1);

        if (Header().Match(masked[first]) is not { Success: true } header) return null;

        var original = source.Lines[first];
        var type = original.Substring(header.Groups["type"].Index, header.Groups["type"].Length);
        var function = header.Groups["function"];
        var local = message.Groups["name"].Success ? message.Groups["name"].Value : "a local variable";

        return LocalFix.ReplaceLine(
            Id, $"Return {type} by value",
            $"`{function.Value}` returns a reference to `{local}`, which exists only while `{function.Value}` runs - by the time the caller reads " +
            $"it, it is gone, and whatever sits at that address next is what gets read. Returning the `{type}` itself hands back a copy that " +
            "lives on.",
            source.Path, first + 1, header.Groups["lead"].Value + type + " " + original[function.Index..]) with
        {
            ResolvesWarning = error.Message,
        };
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
        if (CppCode.Locate(context) is not { } at || Delete().Match(at.Line) is not { Success: true } delete) return null;

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

/// <summary><c>std::thread worker(increment, counter)</c> for <c>void increment(int&amp;)</c> - a thread copies its arguments
/// unless told otherwise.</summary>
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
        var parts = CppCode.SplitTopLevel(masked[number - 1], group.Index, group.Index + group.Length, ',');
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
