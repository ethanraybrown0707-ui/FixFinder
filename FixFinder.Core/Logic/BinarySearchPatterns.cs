using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Logic;

/// <summary>
/// A binary search that can loop for ever or read past its list: <c>lo = mid</c> where <c>mid</c> is rounded down - once
/// <c>hi</c> is <c>lo + 1</c>, <c>mid</c> is <c>lo</c> again and nothing moves; <c>hi = mid</c> under <c>while lo &lt;= hi</c>
/// - once they meet, nothing moves; a search started again at <c>mid</c> - <c>binsearch(mid, end)</c> - which calls itself the
/// same way for ever; and <c>hi</c> started at the list's length, one past its last position, under <c>while lo &lt;= hi</c>,
/// which lets <c>arr[mid]</c> read past the end. And, where the language's whole numbers can overflow, a middle worked out as
/// <c>(lo + hi) / 2</c>. One check, read in each language's own words from the table below.
/// </summary>
/// <remarks>
/// A search whose loop stops before the two meet - <c>while hi - lo &gt; 1</c> - or whose middle is rounded up -
/// <c>(lo + hi + 1) / 2</c> - may set <c>lo = mid</c> as it means to, and is left alone. QuixBugs' two binary searches are
/// both found: find_in_sorted's <c>binsearch(mid, end)</c> and find_first_in_sorted's <c>while lo &lt;= hi</c>.
/// </remarks>
public static partial class BinarySearchPatterns
{
    /// <summary>How one language writes what this check reads.</summary>
    /// <param name="Name">A variable's name, with the sigil the language puts before it.</param>
    /// <param name="Assigns">How the language gives a variable a value: =, :=, &lt;-.</param>
    /// <param name="Function">A function's first line: its name as "name" and its parameters as "parameters", or null.</param>
    /// <param name="Length">The length of a list, as the language asks for it, with {0} for the list's name.</param>
    /// <param name="Overflows">Whether the language's whole numbers stop at 2 147 483 647, so (lo + hi) / 2 can overflow.</param>
    private sealed record Dialect(string Id, IReadOnlySet<string> Files, Syntax Syntax, string Name, string Assigns, Regex? Function,
        IReadOnlyList<string> Length, bool Overflows = false);

    private const string Plain = @"[A-Za-z_]\w*";
    private const string Dollar = @"\$[A-Za-z_]\w*";

    private static readonly Dialect[] Dialects =
    [
        new("logic-python-binary-search", CodePattern.Files(".py", ".pyw"), Syntax.Python, Plain, "=",
            new(@"^\s*def\s+(?<name>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)"), ["len({0})"]),
        new("logic-java-binary-search", CodePattern.Files(".java"), Syntax.CLike, Plain, "=",
            new(@"^\s*(?:(?:public|private|protected|static|final)\s+)*[\w<>\[\]]+\s+(?<name>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)"),
            ["{0}.length", "{0}.size()"], Overflows: true),
        new("logic-binary-search", CodePattern.Files(".c", ".cpp", ".cc", ".cxx", ".c++", ".cs", ".js", ".mjs", ".cjs"), Syntax.CLike, Plain, "=",
            new(@"^\s*(?:(?:public|private|protected|internal|static|export)\s+)*(?:function\s+|[\w<>\[\]*&]+\s+)(?<name>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)"),
            ["{0}.length", "{0}.Length", "{0}.Count", "{0}.size()"], Overflows: true),
        new("logic-go-binary-search", CodePattern.Files(".go"), Syntax.CLike, Plain, "(?::=|=)",
            new(@"^\s*func\s+(?<name>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)"), ["len({0})"]),
        new("logic-rust-binary-search", CodePattern.Files(".rs"), Syntax.CLike, Plain, "=",
            new(@"^\s*(?:pub\s+)?fn\s+(?<name>[A-Za-z_]\w*)\s*(?:<[^>]*>)?\s*\((?<parameters>[^)]*)\)"), ["{0}.len()"]),
        new("logic-ruby-binary-search", CodePattern.Files(".rb"), Syntax.Python, Plain, "=",
            new(@"^\s*def\s+(?<name>[A-Za-z_]\w*)\s*\(?(?<parameters>[^)]*)\)?"), ["{0}.length", "{0}.size"]),
        new("logic-php-binary-search", CodePattern.Files(".php"), Syntax.CLike, Dollar, "=",
            new(@"^\s*function\s+(?<name>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)"), ["count({0})"]),
        new("logic-lua-binary-search", CodePattern.Files(".lua"), Syntax.Lua, Plain, "=",
            new(@"^\s*(?:local\s+)?function\s+(?<name>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)"), ["#{0}"]),
        new("logic-perl-binary-search", CodePattern.Files(".pl", ".pm"), Syntax.Perl, Dollar, "=", null, ["scalar(@{0})", "@{0}"]),
        new("logic-dart-binary-search", CodePattern.Files(".dart"), Syntax.CLike, Plain, "=",
            new(@"^\s*[\w<>\[\]]+\s+(?<name>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)"), ["{0}.length"]),
        new("logic-scala-binary-search", CodePattern.Files(".scala", ".sc"), Syntax.Scala, Plain, "=",
            new(@"^\s*def\s+(?<name>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)"), ["{0}.length", "{0}.size"]),
        new("logic-powershell-binary-search", CodePattern.Files(".ps1"), Syntax.Python, Dollar, "=",
            new(@"^\s*function\s+(?<name>[A-Za-z_][\w-]*)\s*\((?<parameters>[^)]*)\)", RegexOptions.IgnoreCase), ["{0}.Count", "{0}.Length"]),
        new("logic-fsharp-binary-search", CodePattern.Files(".fs", ".fsx"), Syntax.FSharp, Plain, "<-",
            new(@"^\s*let\s+rec\s+(?<name>[A-Za-z_]\w*)\s+(?<parameters>[^=]+?)\s*=\s*$"), ["Array.length {0}", "{0}.Length"]),
        new("logic-elixir-binary-search", CodePattern.Files(".ex", ".exs"), Syntax.Python, Plain, "=",
            new(@"^\s*defp?\s+(?<name>[a-z_]\w*)\s*\((?<parameters>[^)]*)\)"), ["length({0})"]),
        new("logic-erlang-binary-search", CodePattern.Files(".erl"), Syntax.Erlang, @"[A-Z]\w*", "=",
            new(@"^\s*(?<name>[a-z]\w*)\s*\((?<parameters>[^)]*)\)\s*(?:when\b.*)?->"), ["length({0})"]),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
        [.. Dialects.Select(dialect => new CodePattern(dialect.Id, dialect.Files, dialect.Syntax,
            (id, source, masked) => Searches(id, source, masked, dialect), Severity.Warning, Confidence.Likely))];

    /// <summary>Halving: / 2, // 2, >> 1, >>> 1, div(..., 2), intdiv(..., 2).</summary>
    [GeneratedRegex(@"(?:~?/{1,2}\s*2(?:\.0)?\b|>>>?\s*1\b|\b(?:div|intdiv)\s*\(.*,\s*2\s*\)|\bquot\s*\(.*,\s*2\s*\)|\bdiv\s+2\b)")]
    private static partial Regex Halved();

    [GeneratedRegex(@"\+\s*1\b")]
    private static partial Regex RoundedUp();

    private static IEnumerable<LogicFinding> Searches(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        var name = dialect.Name;

        // The middle: a name given a value worked out from two others and halved, rounded down.
        var middle = new Regex($@"^\s*(?:(?:let|var|const|final|int|long|my|local|auto|val|mut)\s+)*(?<mid>{name})\s*(?::\s*\w+\s*)?(?:{dialect.Assigns}|=)(?!=)\s*(?<right>.*[^;,\s])\s*(?:[;,]\s*)?$");

        for (var at = 0; at < masked.Count; at++)
        {
            if (middle.Match(masked[at]) is not { Success: true } worked) continue;
            var right = worked.Groups["right"].Value;
            if (!Halved().IsMatch(right) || RoundedUp().IsMatch(right)) continue;

            var mid = worked.Groups["mid"].Value;
            var names = Regex.Matches(right, name).Select(match => match.Value).Where(each => each != mid).Distinct().ToList();

            // A loop just above it that runs while the two are apart, or the function it is in, whose parameters are the two.
            if (LoopAbove(masked, at, names, name) is { } loop)
            {
                foreach (var finding in InLoop(id, source, masked, loop, at, mid, dialect)) yield return finding;
            }
            else if (dialect.Function is not null && FunctionAround(masked, at, dialect.Function) is { } function)
            {
                foreach (var finding in Recursing(id, source, masked, function, at, mid, names)) yield return finding;
            }

            if (dialect.Overflows && Overflows(source, masked, at, right, names) is { } overflow) yield return overflow;
        }
    }

    /// <summary>The loop around the middle, with its two ends and how it compares them.</summary>
    private sealed record Loop(int Line, string Low, string High, string Op, Group OpAt, int End);

    /// <summary>The while - or Go's for - a few lines above that runs while lo is below hi, and where its body ends, by indentation.</summary>
    private static Loop? LoopAbove(IReadOnlyList<string> masked, int at, List<string> names, string name)
    {
        var header = new Regex($@"\b(?:while|for|do\s+while)\b\s*\(?\s*(?<low>{name})\s*(?<op><=|<|-le|-lt|\.le\.|\.lt\.)\s*(?<high>{name})\s*\)?", RegexOptions.IgnoreCase);

        for (var line = at - 1; line >= Math.Max(0, at - 3); line--)
        {
            if (header.Match(masked[line]) is not { Success: true } loop) continue;
            var (low, high) = (loop.Groups["low"].Value, loop.Groups["high"].Value);
            if (!names.Contains(low) || !names.Contains(high)) return null;

            var end = BodyEnd(masked, line);

            var op = loop.Groups["op"].Value.ToLowerInvariant() is "<=" or "-le" or ".le." ? "<=" : "<";
            return new Loop(line, low, high, op, loop.Groups["op"], end);
        }

        return null;
    }

    private static IEnumerable<LogicFinding> InLoop(string id, SourceFile source, IReadOnlyList<string> masked, Loop loop, int at, string mid, Dialect dialect)
    {
        var body = Enumerable.Range(at + 1, Math.Max(0, loop.End - at - 1)).ToList();
        int? SetTo(string end, string value) => body.Cast<int?>().FirstOrDefault(line =>
            Regex.IsMatch(masked[line!.Value], $@"(?<![\w$]){Regex.Escape(end)}\s*{dialect.Assigns}(?!=)\s*{Regex.Escape(value)}\s*(?:[;,]\s*)?(?:$|\b(?:end|then)\b)"));

        var lowToMid = SetTo(loop.Low, mid);
        var highToMid = SetTo(loop.High, mid);
        var highPastMid = SetTo(loop.High, $"{mid} - 1") ?? SetTo(loop.High, $"{mid}-1");

        // hi started at the list's length, one past its last position.
        var startedPastTheEnd = Enumerable.Range(Math.Max(0, loop.Line - 6), loop.Line - Math.Max(0, loop.Line - 6))
            .Select(line => (Line: line, Length: PastTheEnd(masked[line], loop.High, dialect)))
            .LastOrDefault(each => each.Length is not null);

        if (lowToMid is { } low)
        {
            yield return Moved(id, source, low, loop.Low, mid, "+ 1",
                $"`{loop.Low} = {mid}` on line {low + 1} can leave the search where it was: `{mid}` is rounded down, so once `{loop.High}` is `{loop.Low} + 1`, " +
                $"`{mid}` is `{loop.Low}` again, `{loop.Low}` does not move, and the loop on line {loop.Line + 1} runs for ever.",
                $"`{loop.Low} = {mid} + 1` moves past the position just looked at, so each pass leaves fewer to look through.");
        }

        if (loop.Op == "<=" && startedPastTheEnd.Length is { } length && highToMid is not null && highPastMid is null)
        {
            // A search that keeps hi one past the part still to look at - started at the length, set to mid - stops when they meet.
            var header = source.Lines[loop.Line];
            var changed = header[..loop.OpAt.Index] + (loop.OpAt.Value.StartsWith('-') ? "-lt" : loop.OpAt.Value.StartsWith('.') ? ".lt." : "<") +
                          header[(loop.OpAt.Index + loop.OpAt.Length)..];
            yield return Finding(id, source, loop.Line, changed,
                $"`{loop.High}` starts at `{length}` on line {startedPastTheEnd.Line + 1}, one past the last position, and is only ever set to `{mid}` - so it " +
                $"stays one past what is left to look at. `while {loop.Low} {loop.OpAt.Value} {loop.High}` still runs once they are the same, and then " +
                $"`{mid}` is that position: past the end of the list, or the same pass again for ever.",
                $"Run while {loop.Low} < {loop.High}",
                $"With `{loop.High}` one past what is left, nothing is left once they are the same, so the loop stops there.");
        }
        else if (loop.Op == "<=" && highToMid is { } high)
        {
            yield return Moved(id, source, high, loop.High, mid, "- 1",
                $"`{loop.High} = {mid}` on line {high + 1}, with `while {loop.Low} {loop.OpAt.Value} {loop.High}` on line {loop.Line + 1}, can leave the search " +
                $"where it was: once `{loop.Low}` and `{loop.High}` are the same, `{mid}` is that position too, `{loop.High}` does not move, and the loop runs for ever.",
                $"`{loop.High} = {mid} - 1` moves past the position just looked at, so each pass leaves fewer to look through.");
        }
        else if (loop.Op == "<=" && startedPastTheEnd.Length is { } past && highPastMid is not null)
        {
            var line = startedPastTheEnd.Line;
            var text = source.Lines[line];
            var at2 = text.LastIndexOf(past, StringComparison.Ordinal);
            if (at2 >= 0)
            {
                var changed = text[..(at2 + past.Length)] + " - 1" + text[(at2 + past.Length)..];
                yield return Finding(id, source, line, changed,
                    $"`{loop.High}` starts at `{past}` on line {line + 1}, one past the last position, and `while {loop.Low} {loop.OpAt.Value} {loop.High}` lets " +
                    $"`{mid}` reach it - reading the list there reads past its end.",
                    $"Start {loop.High} at the last position: {past} - 1",
                    $"The last position is one less than the length, and `{loop.Low} {loop.OpAt.Value} {loop.High}` looks at every position up to it.");
            }
        }
    }

    /// <summary>The length the line starts hi at - len(arr), arr.length, count($arr) - or null.</summary>
    private static string? PastTheEnd(string masked, string high, Dialect dialect)
    {
        foreach (var form in dialect.Length)
        {
            var list = form.StartsWith('#') || form.Contains("@{0}") ? @"[A-Za-z_]\w*" : dialect.Name;
            var pattern = Regex.Escape(form).Replace(@"\{0}", $"(?:{list})", StringComparison.Ordinal);
            if (Regex.Match(masked, $@"(?<![\w$]){Regex.Escape(high)}\s*{dialect.Assigns}(?!=)\s*(?<length>{pattern})\s*(?:[;,]\s*)?$") is { Success: true } started)
                return started.Groups["length"].Value;
        }

        return null;
    }

    /// <summary>
    /// Where the body under a header ends, by indentation: the first line no further in than the header - after a { on a line
    /// of its own under it, as C# writes it, and the } that closes it.
    /// </summary>
    private static int BodyEnd(IReadOnlyList<string> masked, int header)
    {
        var indent = CodeText.Indentation(masked[header]).Length;
        var end = header + 1;
        if (end < masked.Count && masked[end].Trim() == "{" && CodeText.Indentation(masked[end]).Length == indent) end++;
        while (end < masked.Count && (masked[end].Trim().Length == 0 || CodeText.Indentation(masked[end]).Length > indent)) end++;
        return end;
    }

    /// <summary>The function a line is in, by indentation: its name, its parameters, and the lines of its body.</summary>
    private sealed record Function(string Name, IReadOnlyList<string> Parameters, int First, int End);

    private static Function? FunctionAround(IReadOnlyList<string> masked, int at, Regex function)
    {
        for (var line = at - 1; line >= 0; line--)
        {
            if (function.Match(masked[line]) is not { Success: true } header) continue;

            if (CodeText.Indentation(masked[at]).Length <= CodeText.Indentation(masked[line]).Length) return null;

            var end = BodyEnd(masked, line);

            return new Function(header.Groups["name"].Value, Split(header.Groups["parameters"].Value), line + 1, end);
        }

        return null;
    }

    /// <summary>A function's parameters, or a call's arguments, by its top-level commas - each written as it is, its type taken off.</summary>
    private static List<string> Split(string list)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;

        for (var i = 0; i <= list.Length; i++)
        {
            if (i < list.Length && list[i] is '(' or '[' or '{' or '<') depth++;
            else if (i < list.Length && list[i] is ')' or ']' or '}' or '>') depth--;
            else if (i == list.Length || list[i] == ',' && depth == 0)
            {
                parts.Add(list[start..i].Trim());
                start = i + 1;
            }
        }

        return parts;
    }

    /// <summary>The search started again at the middle: binsearch(mid, end) for a function of (start, end).</summary>
    private static IEnumerable<LogicFinding> Recursing(string id, SourceFile source, IReadOnlyList<string> masked, Function function, int at, string mid, List<string> names)
    {
        var parameters = function.Parameters.Select(parameter => Regex.Match(parameter, @"([$]?[A-Za-z_]\w*)\s*(?::.*)?$").Groups[1].Value).ToList();
        var low = parameters.FindIndex(parameter => names.Contains(parameter));
        if (low < 0 || parameters.FindIndex(low + 1, parameter => names.Contains(parameter)) is not (> 0 and var high)) yield break;

        var call = new Regex($@"(?<![\w.$]){Regex.Escape(function.Name)}\s*\(");
        for (var line = at + 1; line < function.End; line++)
        {
            foreach (Match called in call.Matches(masked[line]))
            {
                var open = called.Index + called.Length - 1;
                if (Brackets.Closing(masked[line], open) is not (> 0 and var close)) continue;

                var arguments = Split(masked[line][(open + 1)..close]);
                if (arguments.Count != parameters.Count || arguments[low] != mid || arguments[high] != parameters[high]) continue;

                var text = source.Lines[line];
                var argumentAt = ArgumentStart(masked[line], open, low);
                var changed = text[..argumentAt] + mid + " + 1" + text[(argumentAt + mid.Length)..];
                yield return Finding(id, source, line, changed,
                    $"`{source.Lines[line][called.Index..(close + 1)]}` on line {line + 1} searches again from `{mid}`: `{mid}` is rounded down, so once " +
                    $"`{parameters[high]}` is `{parameters[low]} + 1`, `{mid}` is `{parameters[low]}` - the call is the very one it was made from, and it never ends.",
                    $"Search again from {mid} + 1",
                    $"`{mid} + 1` starts past the position just looked at, so each call has fewer to look through.");
            }

            foreach (Match called in Regex.Matches(masked[line], $@"(?<![\w.$-]){Regex.Escape(function.Name)}\s+(?<arguments>\$[^|;()]*?)\s*$", RegexOptions.IgnoreCase))
            {
                var arguments = called.Groups["arguments"];
                var each = Regex.Matches(arguments.Value, @"\S+").ToList();
                if (each.Count != parameters.Count || each[low].Value != mid || each[high].Value != parameters[high]) continue;

                var text = source.Lines[line];
                var argumentAt = arguments.Index + each[low].Index;
                var changed = text[..argumentAt] + $"({mid} + 1)" + text[(argumentAt + mid.Length)..];
                yield return Finding(id, source, line, changed,
                    $"`{text[called.Index..(called.Index + called.Length)].Trim()}` on line {line + 1} searches again from `{mid}`: `{mid}` is rounded down, so once " +
                    $"`{parameters[high]}` is `{parameters[low]} + 1`, `{mid}` is `{parameters[low]}` - the call is the very one it was made from, and it never ends.",
                    $"Search again from {mid} + 1",
                    $"`({mid} + 1)` starts past the position just looked at, so each call has fewer to look through.");
            }
        }
    }

    private static int ArgumentStart(string masked, int open, int index)
    {
        var depth = 0;
        var count = 0;
        for (var i = open + 1; i < masked.Length; i++)
        {
            if (count == index && !char.IsWhiteSpace(masked[i])) return i;
            if (masked[i] is '(' or '[' or '{') depth++;
            else if (masked[i] is ')' or ']' or '}') depth--;
            else if (masked[i] == ',' && depth == 0) count++;
        }

        return open + 1;
    }

    /// <summary>(lo + hi) / 2 with whole numbers that stop at 2 147 483 647: lo + hi can go past it before it is halved.</summary>
    private static LogicFinding? Overflows(SourceFile source, IReadOnlyList<string> masked, int at, string right, List<string> names)
    {
        if (names.Count != 2 || Regex.Match(right, $@"^\(\s*(?<a>{Regex.Escape(names[0])}|{Regex.Escape(names[1])})\s*\+\s*(?<b>{Regex.Escape(names[0])}|{Regex.Escape(names[1])})\s*\)\s*/\s*2\s*$") is not { Success: true } sum) return null;
        if (sum.Groups["a"].Value == sum.Groups["b"].Value) return null;

        // Only for whole numbers the file declares as int, which stop at 2 147 483 647.
        if (!names.All(each => masked.Any(line => Regex.IsMatch(line, $@"\bint\s+(?:[\w,\s=0-9.-]*,\s*)?{Regex.Escape(each)}\b")))) return null;

        var (low, high) = (sum.Groups["a"].Value, sum.Groups["b"].Value);
        var text = source.Lines[at];
        var start = text.IndexOf("(", StringComparison.Ordinal);
        var end = text.LastIndexOf('2');
        if (start < 0 || end < start) return null;
        var changed = text[..start] + $"{low} + ({high} - {low}) / 2" + text[(end + 1)..];

        return new LogicFinding(source.Path.EndsWith(".java", StringComparison.OrdinalIgnoreCase) ? "logic-java-binary-search" : "logic-binary-search", at + 1,
            $"`({low} + {high}) / 2` on line {at + 1} adds the two ends before halving them, and an int stops at 2147483647: for a list of more than about " +
            $"a thousand million items, `{low} + {high}` goes past what an int holds before it is halved.",
            LocalFix.ReplaceLine(source.Path.EndsWith(".java", StringComparison.OrdinalIgnoreCase) ? "logic-java-binary-search" : "logic-binary-search",
                $"Work the middle out without adding the ends: {low} + ({high} - {low}) / 2",
                $"`{high} - {low}` is never more than the list's length, so `{low} + ({high} - {low}) / 2` gives the same middle without going past what an int holds.",
                source.Path, at + 1, changed))
        {
            Severity = Severity.Suggestion,
            Confidence = Confidence.Possible,
        };
    }

    private static LogicFinding Moved(string id, SourceFile source, int line, string end, string mid, string step, string said, string why)
    {
        var text = source.Lines[line];
        var at = text.LastIndexOf(mid, StringComparison.Ordinal);
        var changed = text[..(at + mid.Length)] + " " + step + text[(at + mid.Length)..];
        return Finding(id, source, line, changed, said, $"Set {end} to {mid} {step}", why);
    }

    private static LogicFinding Finding(string id, SourceFile source, int line, string changed, string said, string title, string why) =>
        new(id, line + 1, said, LocalFix.ReplaceLine(id, title, why, source.Path, line + 1, changed))
        {
            Severity = Severity.Warning,
            Confidence = Confidence.Likely,
        };
}
