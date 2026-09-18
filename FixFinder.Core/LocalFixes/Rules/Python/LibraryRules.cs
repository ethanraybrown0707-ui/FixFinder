using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary><c>json.loads(f)</c> given a file, or <c>json.load(text)</c> given text - one letter apart.</summary>
public sealed partial class PythonJsonLoadOrLoads : ILocalFixRule
{
    public string Id => "python-json-load-loads";

    [GeneratedRegex(@"^the JSON object must be str, bytes or bytearray, not (?:TextIOWrapper|BufferedReader)$")]
    private static partial Regex GivenAFile();

    [GeneratedRegex(@"^'str' object has no attribute 'read'$")]
    private static partial Regex GivenText();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "TypeError") && !PythonCode.Raised(context, "AttributeError")) return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var message = context.Error.Message ?? "";
        var masked = CodeText.Mask(line, Syntax.Python);

        if (GivenAFile().IsMatch(message))
        {
            if (Regex.Matches(masked, @"\bjson\.loads\s*\(").ToList() is not [var call]) return null;

            return LocalFix.ReplaceLine(
                Id, "Read JSON from a file with json.load",
                "`json.loads` - with an s, for string - reads JSON from text, and it was given an open file. `json.load` reads it " +
                "from the file.",
                source.Path, number, line[..call.Index] + "json.load(" + line[(call.Index + call.Length)..]);
        }

        if (!GivenText().IsMatch(message) || Regex.Matches(masked, @"\bjson\.load\s*\(\s*(?<name>[A-Za-z_]\w*)\s*\)").ToList() is not [var load]) return null;

        var name = load.Groups["name"].Value;
        var all = CodeText.MaskAll(source.Lines, Syntax.Python);
        var assigned = Enumerable.Range(0, number - 1).Where(i => Regex.IsMatch(all[i], $@"^\s*{Regex.Escape(name)}\s*=")).ToList();

        if (assigned is not [var index] || !Regex.IsMatch(source.Lines[index], $@"=\s*(?:[\w.]+\.read\(\)|['""]\s*[\[{{])")) return null;

        return LocalFix.ReplaceLine(
            Id, "Read JSON from text with json.loads",
            $"`json.load` reads JSON from an open file, and `{name}` is text that has already been read. `json.loads` - with an s, for " +
            "string - reads it from text.",
            source.Path, number, line[..load.Index] + "json.loads(" + line[(load.Index + "json.load(".Length)..]);
    }
}

/// <summary><c>worker() argument after * must be an iterable, not int</c> - <c>args=(5)</c>, which is 5 in brackets, not a tuple.</summary>
public sealed partial class PythonArgsTuple : ILocalFixRule
{
    public string Id => "python-args-tuple";

    [GeneratedRegex(@"^(?:__main__\.|__mp_main__\.)?(?<function>[A-Za-z_]\w*)\(\) argument after \* must be an iterable, not (?<type>\w+)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "TypeError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (context.SourceRoot is not { } root || !Directory.Exists(root)) return null;

        var function = message.Groups["function"].Value;
        var call = new Regex($@"\btarget\s*=\s*{Regex.Escape(function)}\b.*?\bargs\s*=\s*(?<args>\((?<inner>[^(),]*(?:\([^()]*\))?[^(),]*)\)|(?<bare>[A-Za-z_]\w*|\d+|""[^""]*""|'[^']*'))(?=\s*[,)])");

        var hits = new List<(SourceFile Source, int Index, Match Match)>();

        foreach (var path in Directory.EnumerateFiles(root, "*.py").Take(200))
        {
            if (SourceFile.Read(path) is not { } source) continue;
            var masked = CodeText.MaskAll(source.Lines, Syntax.Python);

            for (var i = 0; i < masked.Count; i++)
                if (call.Match(masked[i]) is { Success: true } m) hits.Add((source, i, m));
        }

        if (hits is not [var hit]) return null;

        var args = hit.Match.Groups["args"];
        var line = hit.Source.Lines[hit.Index];
        var value = hit.Match.Groups["inner"].Success ? line.Substring(hit.Match.Groups["inner"].Index, hit.Match.Groups["inner"].Length).Trim() : line.Substring(args.Index, args.Length).Trim();

        if (value.Length == 0 || (hit.Match.Groups["bare"].Success && message.Groups["type"].Value is "list" or "tuple")) return null;

        return LocalFix.ReplaceLine(
            Id, $"Pass the argument as a tuple: args=({value},)",
            $"`args` has to be a tuple of the arguments for `{function}`, and `({value})` is not a tuple - brackets around one value are " +
            "just brackets. The comma is what makes a tuple of one: `(" + value + ",)`.",
            hit.Source.Path, hit.Index + 1, line[..args.Index] + $"({value},)" + line[(args.Index + args.Length)..]);
    }
}

/// <summary><c>Incorrect number of bindings supplied.</summary>
public sealed partial class PythonSqlParameterTuple : ILocalFixRule
{
    public string Id => "python-sql-parameter-tuple";

    [GeneratedRegex(@"^Incorrect number of bindings supplied\. The current statement uses 1, and there are (?<n>\d+) supplied\.$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "python" } || !(context.Error.ExceptionType ?? "").EndsWith("ProgrammingError", StringComparison.Ordinal)) return null;
        if (Message().Match(context.Error.Message ?? "") is not { Success: true } || PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.Python);
        if (Regex.Matches(masked, @"\.execute(?:many)?\s*\(").ToList() is not [var execute]) return null;

        if (PythonCode.CallArguments(line, masked, execute.Index + execute.Length - 1) is not [_, var parameters]) return null;

        var text = parameters.Text;
        string corrected;

        if (text.StartsWith('(') && Brackets.Closing(CodeText.Mask(text, Syntax.Python), 0) == text.Length - 1 && !CodeText.Mask(text, Syntax.Python)[1..^1].Contains(','))
            corrected = text[..^1].TrimEnd() + ",)";
        else if (Regex.IsMatch(text, @"^(?:[A-Za-z_][\w.]*|""[^""]*""|'[^']*')$"))
            corrected = $"({text},)";
        else
            return null;

        return LocalFix.ReplaceLine(
            Id, $"Pass the one parameter as a tuple: {corrected}",
            "The statement has one `?`, so it needs a sequence holding one value. Brackets around a single value are only brackets, so " +
            "sqlite3 was given the text itself, and a string is a sequence of its characters - one binding per letter. The comma makes " +
            "it a tuple of one.",
            source.Path, number, line[..parameters.Start] + corrected + line[(parameters.Start + text.Length)..]);
    }
}

/// <summary><c>socket.bind() takes exactly one argument (2 given)</c> - the host and port given separately, where the address is
/// one pair.</summary>
public sealed partial class PythonSocketAddress : ILocalFixRule
{
    public string Id => "python-socket-address";

    [GeneratedRegex(@"^socket\.(?<method>bind|connect|connect_ex)\(\) takes exactly one argument \(2 given\)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "TypeError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var method = message.Groups["method"].Value;
        var masked = CodeText.Mask(line, Syntax.Python);

        if (Regex.Matches(masked, $@"\.{method}\s*\(").ToList() is not [var call]) return null;
        if (PythonCode.CallArguments(line, masked, call.Index + call.Length - 1) is not [var host, var port]) return null;

        var start = host.Start;
        var end = port.Start + port.Text.Length;

        return LocalFix.ReplaceLine(
            Id, $"Pass the address as one pair: {method}(({host.Text}, {port.Text}))",
            $"A socket address is one value - a tuple of host and port - so `{method}` takes a single argument. The two go inside one more " +
            "pair of brackets.",
            source.Path, number, line[..start] + $"({host.Text}, {port.Text})" + line[end..]);
    }
}

/// <summary>The <c>bootstrapping phase</c> error: starting a process at the top level of a script, which Windows runs again in
/// the child.</summary>
public sealed partial class PythonMainGuard : ILocalFixRule
{
    public string Id => "python-main-guard";

    [GeneratedRegex(@"^(?:@|def\s|async\s+def\s|class\s|import\s|from\s|#|if\s+__name__\s*==)")]
    private static partial Regex Setup();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "RuntimeError")) return null;
        if (!context.Error.RawText.Contains("bootstrapping phase", StringComparison.Ordinal) &&
            !context.Output.Any(l => l.Text.Contains("bootstrapping phase", StringComparison.Ordinal))) return null;
        if (context.Read(context.Frame?.File) is not { } source) return null;

        var lines = source.Lines;
        var masked = CodeText.MaskAll(lines, Syntax.Python);
        if (masked.Any(l => Regex.IsMatch(l, @"^if\s+__name__\s*=="))) return null;

        var statements = Enumerable.Range(0, lines.Count)
            .Where(i => masked[i].Trim().Length > 0 && CodeText.Indentation(masked[i]).Length == 0 && !Setup().IsMatch(masked[i]))
            .ToList();
        if (statements.Count == 0) return null;

        var first = statements[0];

        if (Enumerable.Range(first, lines.Count - first).Any(i => CodeText.Indentation(masked[i]).Length == 0 && Regex.IsMatch(masked[i], @"^(?:@|def\s|async\s+def\s|class\s)"))) return null;

        var end = lines.Count;
        while (end > first && lines[end - 1].Trim().Length == 0) end--;

        var unit = lines.Select(CodeText.Indentation).FirstOrDefault(i => i.Length > 0) ?? "    ";
        var wrapped = new List<string> { "if __name__ == \"__main__\":" };
        wrapped.AddRange(Enumerable.Range(first, end - first).Select(i => lines[i].Trim().Length == 0 ? "" : unit + lines[i]));

        return new LocalFix
        {
            RuleId = Id,
            Title = "Start processes only when run directly: if __name__ == \"__main__\":",
            Explanation =
                "On Windows a new process starts by importing this file again, so code at the top level runs again inside every child - " +
                "which starts another process before the first has finished starting, and Python stops it. Code under " +
                "`if __name__ == \"__main__\":` runs only in the process that was started directly, not on those imports.",
            File = source.Path, StartLine = first + 1, RemoveCount = end - first, NewLines = wrapped,
        };
    }
}

/// <summary><c>asyncio.gather([a(), b()])</c> - gather takes the awaitables themselves, not a list of them.</summary>
public sealed partial class PythonGatherList : ILocalFixRule
{
    public string Id => "python-gather-list";

    [GeneratedRegex(@"^(?:unhashable type: 'list'|An asyncio\.Future, a coroutine or an awaitable is required)$")]
    private static partial Regex Message();

    [GeneratedRegex(@"\bgather\s*\(\s*(?=[\[A-Za-z_])")]
    private static partial Regex Gather();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "TypeError") || !Message().IsMatch(context.Error.Message ?? "")) return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.Python);
        if (Gather().Matches(masked).ToList() is not [var gather]) return null;

        var open = masked.IndexOf('(', gather.Index);
        if (PythonCode.CallArguments(line, masked, open) is not [var only]) return null;

        string corrected;

        if (only.Text.StartsWith('[') && Brackets.Closing(CodeText.Mask(only.Text, Syntax.Python), 0) == only.Text.Length - 1)
            corrected = only.Text[1..^1].Trim();
        else if (Regex.IsMatch(only.Text, @"^[A-Za-z_]\w*$"))
            corrected = "*" + only.Text;
        else
            return null;

        return LocalFix.ReplaceLine(
            Id, "Give gather the awaitables themselves",
            "`asyncio.gather` takes each coroutine as its own argument and waits for them all, returning their results in a list. Given " +
            "one list instead, it tries to await the list. The items go in directly - or `*` spreads a list that is built elsewhere.",
            source.Path, number, line[..only.Start] + corrected + line[(only.Start + only.Text.Length)..]);
    }
}

/// <summary><c>unsupported operand type(s) for +: 'coroutine' and 'int'</c> - an <c>async def</c> called without <c>await</c>.</summary>
public sealed partial class PythonCoroutineNotAwaited : ILocalFixRule
{
    public string Id => "python-coroutine-not-awaited";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "python" } error || error.ExceptionType is not ("TypeError" or "AttributeError")) return null;
        if (!(error.Message ?? "").Contains("'coroutine'", StringComparison.Ordinal) || PythonCode.Locate(context) is not { } at) return null;

        var (source, number, _) = at;
        var lines = source.Lines;
        var masked = CodeText.MaskAll(lines, Syntax.Python);

        var asyncs = masked.Select(l => Regex.Match(l, @"^\s*async\s+def\s+(?<name>[A-Za-z_]\w*)\s*\(")).Where(m => m.Success).Select(m => m.Groups["name"].Value).ToHashSet();
        if (asyncs.Count == 0) return null;

        var function = PythonCode.EnclosingHeader(masked, number - 1, PythonCode.FunctionKeyword());
        if (function < 0 || !masked[function].TrimStart().StartsWith("async", StringComparison.Ordinal)) return null;

        var direct = Regex.Matches(masked[number - 1], @"(?<![\w.])(?<!await\s)(?<name>[A-Za-z_]\w*)\s*\(").Where(m => asyncs.Contains(m.Groups["name"].Value)).ToList();

        if (direct is [var call])
        {
            var text = lines[number - 1];
            var close = Brackets.Closing(masked[number - 1], masked[number - 1].IndexOf('(', call.Index));
            if (close < 0) return null;

            var expression = text[call.Index..(close + 1)];
            var standalone = masked[number - 1].Trim() == expression.Trim();

            return Fix(source, number, text[..call.Index] + (standalone ? $"await {expression}" : $"(await {expression})") + text[(close + 1)..], call.Groups["name"].Value);
        }

        var used = Regex.Matches(masked[number - 1], @"(?<![\w.])[A-Za-z_]\w*(?![\w(])").Select(m => m.Value).ToHashSet();

        var stored = Enumerable.Range(function + 1, number - 2 - function)
            .Select(i => (Index: i, Match: Regex.Match(masked[i], @"^(?<lead>\s*(?<name>[A-Za-z_]\w*)\s*=\s*)(?<call>(?<function>[A-Za-z_]\w*)\s*\()")))
            .Where(x => x.Match.Success && used.Contains(x.Match.Groups["name"].Value) && asyncs.Contains(x.Match.Groups["function"].Value))
            .ToList();

        if (stored is not [var assignment]) return null;

        var original = lines[assignment.Index];
        var lead = assignment.Match.Groups["lead"].Length;

        return Fix(source, assignment.Index + 1, original[..lead] + "await " + original[lead..], assignment.Match.Groups["function"].Value);
    }

    private LocalFix Fix(SourceFile source, int number, string corrected, string function) => LocalFix.ReplaceLine(
        Id, $"Wait for {function}: await",
        $"`{function}` is an `async def`, so calling it does not run it - it makes a coroutine, a promise of the result. `await` runs it " +
        "and gives back what it returns.",
        source.Path, number, corrected);
}

/// <summary><c>a coroutine was expected, got &lt;function main&gt;</c> - <c>asyncio.run(main)</c> without the call.</summary>
public sealed partial class PythonCoroutineNotCalled : ILocalFixRule
{
    public string Id => "python-coroutine-not-called";

    [GeneratedRegex(@"^a coroutine was expected, got <function (?<name>[A-Za-z_]\w*) at ")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "ValueError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        var name = Regex.Escape(message.Groups["name"].Value);
        var call = new Regex($@"\(\s*(?<name>{name})\s*[,)]");

        foreach (var frame in context.Error.RootCause.Frames.Concat(context.Error.Frames))
        {
            if (frame.Line is not { } number || context.Read(frame.File) is not { } source || source.Line(number) is not { } line) continue;

            var masked = CodeText.MaskAll(source.Lines, Syntax.Python);
            if (!masked.Any(text => Regex.IsMatch(text, $@"^\s*async\s+def\s+{name}\s*\("))) continue;

            var hits = call.Matches(masked[number - 1]);
            if (hits.Count != 1) continue;

            var end = hits[0].Groups["name"].Index + hits[0].Groups["name"].Length;

            return LocalFix.ReplaceLine(
                Id, $"Call {message.Groups["name"].Value}() to make the coroutine",
                "An `async def` does nothing until it is called: calling it makes the coroutine `asyncio.run` expects. Without the brackets it " +
                "is the function itself.",
                source.Path, number, line[..end] + "()" + line[end..]);
        }

        return null;
    }
}
