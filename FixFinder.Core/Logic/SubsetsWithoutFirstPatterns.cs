using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A function making every subset of a list by calling itself for the rest of the list, then putting the first item in front
/// of each subset that gives back - which gives back only those, and leaves out the rest's subsets as they are, the subsets
/// without the first item. Each subset is made from one of the rest's, so each call gives back as many as the call below it,
/// and the call at the bottom gives back one, the empty set: the function gives back a single subset, the whole list, where
/// a list of n items has 2^n. QuixBugs' powerset is this mistake, in Python. The change gives back the rest's subsets too.
/// </summary>
/// <remarks>
/// Said only when the file shows all of it: the function gives back the list holding the empty list - return [[]], {{}},
/// vec![vec![]], List(Nil), ([]), ,@(,@()), [ [] ], a list given new ArrayList&lt;&gt;() - and keeps what a call of itself gives
/// back, made once and not in a loop, under a name: rest_subsets = powerset(rest). That name is used once after: either in
/// what is given back - a map of it, [[first] + subset for subset in rest_subsets], restSubsets.map(...),
/// List.map (...) restSubsets, Enum.map(rest_subsets, ...), map { ... } @rest_subsets, [... || Subset &lt;- RestSubsets] - or as
/// what a loop goes through, whose one line adds to a list that starts empty, nothing else adds to, and is given back.
/// A one-to-one map or loop gives back one subset for each of the rest's: a flatMap, two adds or a nested loop do not.
/// </remarks>
public static partial class SubsetsWithoutFirstPatterns
{
    /// <summary>How one language writes the parts of the mistake and of its change.</summary>
    /// <param name="Token">Text each base case's line holds, looked for before its regex is tried.</param>
    /// <param name="JustTheEmptySet">The list holding the empty list, given back by the base case.</param>
    /// <param name="Joined">The rest's subsets put in front of a map of them, given both as written - or null where only a loop is read.</param>
    /// <param name="EmptyList">A list made empty under a name, before a loop: the name in the group named out, the empty list in the group named empty.</param>
    /// <param name="Copied">What takes the empty list's place to start the list with the rest's subsets, given the empty list and the rest's name.</param>
    private sealed record Dialect(
        string Language, IReadOnlySet<string> Files, Syntax Syntax, string Token, Regex JustTheEmptySet,
        Func<string, string, string>? Joined, Regex? EmptyList, Func<string, string, string>? Copied, bool IgnoreCase = false);

    private static readonly Dialect[] Dialects =
    [
        new("python", CodePattern.Files(".py", ".pyw"), Syntax.Python, "[[", new(@"\[\s*\[\s*\]\s*\]"),
            (rest, map) => $"{rest} + {map}", new(@"^\s*(?<out>[A-Za-z_]\w*)\s*=\s*(?<empty>\[\s*\])\s*$"), (_, rest) => $"list({rest})"),
        new("java", CodePattern.Files(".java"), Syntax.CLike, "List", new(@"\.add\(\s*new\s+(?:Array|Linked)List\s*(?:<[^()]*>)?\s*\(\s*\)\s*\)|List\.of\(\s*List\.of\(\s*\)\s*\)"),
            null, new(@"^\s*(?:final\s+)?[\w<>\[\],.?\s]+?\s+(?<out>[A-Za-z_]\w*)\s*=\s*(?<empty>new\s+(?:Array|Linked)List\s*(?:<[^()]*>)?\s*\(\s*\))\s*;\s*$"),
            (empty, rest) => $"{empty[..^1].TrimEnd()}{rest})"),
        new("csharp", CodePattern.Files(".cs"), Syntax.CLike, "List", new(@"\{\s*new\s+List\s*<\s*\w+\s*>\s*\(\s*\)\s*\}|\.Add\(\s*new\s+List\s*<\s*\w+\s*>\s*\(\s*\)\s*\)"),
            (rest, map) => $"{rest}.Concat({WithoutTheEnd(map, ".ToList()")}).ToList()",
            new(@"^\s*(?:var|List\s*<.+>)\s+(?<out>[A-Za-z_]\w*)\s*=\s*(?<empty>new\s*(?:List\s*<.+>)?\s*\(\s*\))\s*;\s*$"),
            (empty, rest) => $"{empty[..^1].TrimEnd()}{rest})"),
        new("js", CodePattern.Files(".js", ".mjs", ".cjs", ".ts", ".mts", ".cts"), Syntax.CLike, "[[", new(@"\[\s*\[\s*\]\s*\]"),
            (rest, map) => $"[...{rest}, ...{map}]",
            new(@"^\s*(?:const|let|var)\s+(?<out>[A-Za-z_$][\w$]*)\s*(?::\s*[^=]+)?=\s*(?<empty>\[\s*\])\s*;?\s*$"), (_, rest) => $"[...{rest}]"),
        new("go", CodePattern.Files(".go"), Syntax.CLike, "{{", new(@"\[\]\[\]\w+\{\s*\{\s*\}\s*\}"),
            null, new(@"^\s*(?<out>[A-Za-z_]\w*)\s*:=\s*(?<empty>\[\]\[\]\w+\{\s*\})\s*$"), (empty, rest) => $"append({empty}, {rest}...)"),
        new("ruby", CodePattern.Files(".rb"), Syntax.Python, "[[", new(@"\[\s*\[\s*\]\s*\]"),
            (rest, map) => $"{rest} + {map}", new(@"^\s*(?<out>[a-z_]\w*)\s*=\s*(?<empty>\[\s*\])\s*$"), (_, rest) => $"{rest}.dup"),
        new("lua", CodePattern.Files(".lua"), Syntax.Lua, "{}", new(@"\{\s*\{\s*\}\s*\}"),
            null, new(@"^\s*local\s+(?<out>[A-Za-z_]\w*)\s*=\s*(?<empty>\{\s*\})\s*$"), (_, rest) => $"{{ table.unpack({rest}) }}"),
        new("php", CodePattern.Files(".php"), Syntax.CLike, "[[", new(@"\[\s*\[\s*\]\s*\]"),
            (rest, map) => $"array_merge({rest}, {map})", new(@"^\s*(?<out>\$[A-Za-z_]\w*)\s*=\s*(?<empty>\[\s*\]|array\(\s*\))\s*;\s*$"), (_, rest) => rest),
        new("perl", CodePattern.Files(".pl", ".pm"), Syntax.Perl, "([", new(@"\(\s*\[\s*\]\s*\)"),
            (rest, map) => $"({rest}, {map})", new(@"^\s*my\s+(?<out>@[A-Za-z_]\w*)\s*(?<empty>(?:=\s*\(\s*\))?)\s*;\s*$"),
            (empty, rest) => empty.Length == 0 ? $" = {rest}" : $"= {rest}"),
        new("powershell", CodePattern.Files(".ps1"), Syntax.Python, "@(", new(@"@\(\s*,\s*@\(\s*\)\s*\)", RegexOptions.IgnoreCase),
            null, new(@"^\s*(?<out>\$[A-Za-z_]\w*)\s*=\s*(?<empty>@\(\s*\))\s*$", RegexOptions.IgnoreCase), (_, rest) => $"@({rest})", IgnoreCase: true),
        new("dart", CodePattern.Files(".dart"), Syntax.CLike, "[", new(@"\[\s*(?:<\w+>\s*)?\[\s*\]\s*\]"),
            (rest, map) => $"[...{rest}, ...{WithoutTheEnd(map, ".toList()")}]",
            new(@"^\s*(?:final|var|List\s*<.+>)\s+(?<out>[A-Za-z_]\w*)\s*=\s*(?<empty>(?:<.+>)?\[\s*\])\s*;\s*$"), (_, rest) => $"[...{rest}]"),
        new("scala", CodePattern.Files(".scala", ".sc"), Syntax.Scala, "List(", new(@"\bList\(\s*(?:Nil|List\(\s*\))\s*\)"),
            (rest, map) => $"{rest} ++ {map}", null, null),
        new("elixir", CodePattern.Files(".ex", ".exs"), Syntax.Python, "[[", new(@"\[\s*\[\s*\]\s*\]"),
            (rest, map) => $"{rest} ++ {map}", null, null),
        new("erlang", CodePattern.Files(".erl"), Syntax.Erlang, "[[", new(@"\[\s*\[\s*\]\s*\]"),
            (rest, map) => $"{rest} ++ {map}", null, null),
        new("fsharp", CodePattern.Files(".fs", ".fsx"), Syntax.FSharp, "[]", new(@"\[\s*\[\s*\]\s*\]"),
            (rest, map) => $"{rest} @ {map}", null, null),
        new("ocaml", CodePattern.Files(".ml"), Syntax.OCaml, "[]", new(@"\[\s*\[\s*\]\s*\]"),
            (rest, map) => $"{rest} @ {map}", null, null),
        new("rust", CodePattern.Files(".rs"), Syntax.CLike, "vec!", new(@"vec!\[\s*(?:vec!\[\s*\]|Vec::new\(\))\s*\]"),
            null, new(@"^\s*let\s+mut\s+(?<out>[A-Za-z_]\w*)\s*(?::\s*[^=]+)?=\s*(?<empty>Vec::new\(\)|vec!\[\s*\])\s*;\s*$"), (_, rest) => $"{rest}.clone()"),
        new("cpp", CodePattern.Files(".cpp", ".cc", ".cxx", ".c++"), Syntax.CLike, "{", new(@"\{\s*\{\s*\}\s*\}"),
            null, new(@"^\s*(?:std::)?vector\s*<.+>\s+(?<out>[A-Za-z_]\w*)\s*(?<empty>(?:\{\s*\}|=\s*\{\s*\})?)\s*;\s*$"),
            (empty, rest) => empty.StartsWith('=') ? $"= {rest}" : $" = {rest}"),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        .. Dialects.Select(dialect => new CodePattern($"logic-{dialect.Language}-subsets-without-first-dropped", dialect.Files, dialect.Syntax,
            (id, source, masked) => SubsetsWithoutFirstDropped(id, source, masked, dialect), Severity.Error, Confidence.Likely)),
    ];

    private const string WhyBoth =
        "Every subset either leaves the first item out - one of the rest's subsets as it is - or holds it - one of them with the item put in front - so both belong in what is given back.";

    private static IEnumerable<LogicFinding> SubsetsWithoutFirstDropped(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        var options = dialect.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
        var reported = new HashSet<int>();
        for (var bottom = 0; bottom < masked.Count; bottom++)
        {
            // The base case's own text on the line first - a regex tried on every line is slow over a whole file.
            if (!masked[bottom].Contains(dialect.Token, StringComparison.Ordinal) || !dialect.JustTheEmptySet.IsMatch(masked[bottom])) continue;
            if (FunctionAt(masked, bottom) is not { } function) continue;

            // The call of itself whose subsets are kept under a name - in this function, or a clause of it beside the base case's.
            // A type after the name - TypeScript's restSubsets: number[][] - holds no :, so C++'s std:: is not read as one.
            var callsItself = new Regex(
                $@"(?<![\w$@.:])(?<kept>[$@]?[A-Za-z_]\w*)(?!\s*::)\s*(?::(?!:)[^=;:]+?)?\s*:?=\s*{Regex.Escape(function.Name)}\s*(?:\(|\s+[\w@$(\[])", options);
            for (var kept = Math.Max(0, bottom - 40); kept < Math.Min(masked.Count, bottom + 40); kept++)
            {
                if (reported.Contains(kept) || !masked[kept].Contains(function.Name, dialect.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) continue;
                if (callsItself.Match(masked[kept]) is not { Success: true } call) continue;
                if (FunctionAt(masked, kept) is not { } caller || !caller.Name.Equals(function.Name, StringComparison.OrdinalIgnoreCase)) continue;

                // Called in a loop it is called many times, and its subsets are more than one call's.
                if (Reaching.InALoop(masked, caller.Heading, kept)) continue;

                var rest = call.Groups["kept"].Value;
                if (Read(id, source, masked, dialect, function.Name, caller.Heading, kept, rest, options) is not { } finding) continue;
                reported.Add(kept);
                yield return finding;
            }
        }
    }

    /// <summary>The finding for the rest's subsets kept on line <paramref name="kept"/>, if the function gives back only those made from them.</summary>
    private static LogicFinding? Read(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect, string function, int heading, int kept, string rest, RegexOptions options)
    {
        var restName = rest.TrimStart('$', '@');
        var named = new Regex($@"(?<![\w$@%]){(rest.StartsWith('$') ? @"\$" : @"[$@%]?")}{Regex.Escape(restName)}(?!\w)", options);
        var end = EndOf(masked, heading, kept);

        // Used once after it is kept: more uses keep the rest's subsets too, as rest_subsets + [...] does.
        var uses = Enumerable.Range(kept + 1, end - kept - 1).SelectMany(line => named.Matches(masked[line]).Select(use => (Line: line, Use: use))).ToList();
        if (uses.Count != 1) return null;
        var (at, found) = uses[0];
        var text = masked[at];
        var before = text[..found.Index];
        var after = text[(found.Index + found.Length)..];

        if (Regex.IsMatch(text, @"^\s*(?:for|foreach)\b", options) && Regex.IsMatch(before, @"(?:\bin|:|\bof|\brange|\bforeach\s*\(|ipairs\s*\()\s*&?\s*$", options))
            return dialect.EmptyList is null ? null : ReadLoop(id, source, masked, dialect, function, heading, at, end, rest, options);

        return dialect.Joined is null ? null : ReadMap(id, source, masked, dialect, function, at, end, found, before, after, rest, options);
    }

    /// <summary>
    /// The rest's subsets gone through by a map given back: R.map(...), R.Select(...), [... for s in R], List.map (...) R,
    /// Enum.map(R, ...), map { [...] } @R, [... || S &lt;- R] - one subset made from each of them, and nothing else given back.
    /// </summary>
    private static LogicFinding? ReadMap(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect, string function, int at, int end, Match found, string before, string after, string rest, RegexOptions options)
    {
        var text = masked[at];
        int start;
        if (Regex.IsMatch(after, @"^\s*\.\s*(?:map|Select)\s*[({]")) start = found.Index;
        else if (Regex.Match(before, @"(?:\b(?:List|Seq|Array)\.map\b|\bEnum\.map\s*\(|\bmap\s*\{)[^\n]*$") is { Success: true } mapped) start = mapped.Index;
        else if (Regex.Match(before, @"\[[^\[\]]*(?:\[[^\[\]]*\][^\[\]]*)*(?:\bfor\s+[A-Za-z_]\w*\s+in|\|\|\s*[A-Z_]\w*\s*<-)\s*$") is { Success: true } comprehension) start = comprehension.Index;
        else return null;

        // What is given back: return E, or E as the branch's last line where the last expression is the value.
        var given = Regex.Match(text, @"^\s*return\s+", options);
        if (given.Success ? start != given.Length : text.Length - text.TrimStart().Length != start || !LastOfItsBranch(masked, at, end)) return null;

        var expression = text[start..].TrimEnd();
        expression = Regex.Replace(expression, @"\s*[;.,]$", "");
        if (!OneForEach(expression, found.Index - start, found.Length, dialect)) return null;

        var written = source.Lines[at];
        var mapWritten = written.Substring(start, expression.Length);
        var joined = dialect.Joined!(source.Lines[at].Substring(found.Index, found.Length), mapWritten);
        var told = $"Line {at + 1} gives back one subset for each of `{rest}` - each with an item put in front - and leaves out `{rest}` itself, the subsets without " +
                   $"that item, which are subsets too. Each call of `{function}` gives back as many as the call below it, and the one at the bottom gives back one, " +
                   "the empty set - so it gives back a single subset, the whole list, where a list of n items has 2^n.";
        return new LogicFinding(id, at + 1, told,
            LocalFix.ReplaceLine(id, $"Give back {rest} as well: {joined}", WhyBoth, source.Path, at + 1,
                written[..start] + joined + written[(start + expression.Length)..]));
    }

    /// <summary>
    /// The rest's subsets gone through by a loop - not inside another - whose one line adds one subset to a list that starts
    /// empty above it, which nothing else adds to and which is given back after the loop.
    /// </summary>
    private static LogicFinding? ReadLoop(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect, string function, int heading, int loop, int end, string rest, RegexOptions options)
    {
        if (Reaching.InALoop(masked, heading, loop)) return null;
        var (body, after) = BodyOf(masked, loop, end);

        // Given back after the loop: return subsets, return ,$subsets, return @subsets, or subsets as the last line.
        var next = after;
        while (next < end && masked[next].Trim() is "" or "}" or "end" or "end;") next++;
        if (next >= end || Regex.Match(masked[next], @"^\s*(?:return\s+,?\s*)?(?<out>[$@]?[A-Za-z_]\w*)\s*;?\s*$", options) is not { Success: true } given) return null;
        var output = given.Groups["out"].Value;

        // Made empty above the loop, under that name.
        var made = -1;
        Match? empty = null;
        for (var line = heading + 1; line < loop; line++)
        {
            if (dialect.EmptyList!.Match(masked[line]) is { Success: true } list && list.Groups["out"].Value.Equals(output, StringComparison.OrdinalIgnoreCase))
                (made, empty) = (line, list);
        }

        if (empty is null) return null;

        // Named once between, on a line of the loop's own that adds one subset to it.
        var outputNamed = new Regex($@"(?<![\w$@%]){Regex.Escape(output)}(?!\w)", options);
        var naming = Enumerable.Range(made + 1, next - made - 1).Where(line => outputNamed.IsMatch(masked[line])).ToList();
        if (naming.Count != 1 || !body.Contains(naming[0])) return null;
        if (Reaching.InnermostLoop(masked, heading, naming[0]) is not { } innermost || innermost.Heading != loop) return null;
        if (!AddsOne(masked[naming[0]], output, options)) return null;

        var written = source.Lines[made];
        var group = empty.Groups["empty"];
        var replacement = dialect.Copied!(written.Substring(group.Index, group.Length), rest);
        var told = $"`{output}` starts empty on line {made + 1}, and the loop over `{rest}` adds one subset to it for each of `{rest}` - each with an item put in front - " +
                   $"so `{function}` gives back only those, and leaves out `{rest}` itself, the subsets without that item, which are subsets too. Each call gives back as many " +
                   "as the call below it, and the one at the bottom gives back one, the empty set - so it gives back a single subset, the whole list, where a list of n items has 2^n.";
        return new LogicFinding(id, made + 1, told,
            LocalFix.ReplaceLine(id, $"Start {output} with {rest}'s subsets: {replacement.Trim().TrimStart('=').Trim()}", WhyBoth, source.Path, made + 1,
                written[..group.Index] + replacement + written[(group.Index + group.Length)..]));
    }

    /// <summary>
    /// Whether a line adds one subset to the list: subsets.append(x), .add(x), .Add(x), .push(x), .push_back(x), subsets =
    /// append(subsets, x), subsets[#subsets + 1] = x, table.insert(subsets, x), $subsets[] = x, push @subsets, x, $subsets += ,x
    /// - one item, not two, and not every item of a list spread in.
    /// </summary>
    private static bool AddsOne(string text, string output, RegexOptions options)
    {
        var name = Regex.Escape(output);
        if (Regex.IsMatch(text, $@"{name}\s*\[\s*(?:#\s*{name}\s*\+\s*1\s*)?\]\s*=(?!=)|{name}\s*\+=\s*,", options)) return true;

        var call = Regex.Match(text, $@"(?:{name}\s*\.\s*(?:append|add|Add|push|push_back|emplace_back)\s*\(|{name}\s*=\s*append\s*\(\s*{name}\s*,|\btable\.insert\s*\(\s*{name}\s*,|\barray_push\s*\(\s*{name}\s*,)", options);
        if (call.Success)
        {
            var arguments = ArgumentsFrom(text, call.Index + call.Length);
            return arguments.Count == 1 && !arguments[0].TrimEnd().EndsWith("...", StringComparison.Ordinal) && !arguments[0].TrimStart().StartsWith("...", StringComparison.Ordinal);
        }

        var pushed = Regex.Match(text, $@"\bpush\s*\(?\s*{name}\s*,", options);
        return pushed.Success && ArgumentsFrom(text + ")", pushed.Index + pushed.Length) is { Count: 1 } items && !items[0].Contains('@');
    }

    /// <summary>The arguments of a call from just after its ( - or after the list's name and comma - to the ) that closes it, split at its own commas.</summary>
    private static List<string> ArgumentsFrom(string text, int from)
    {
        var arguments = new List<string>();
        var depth = 0;
        var started = from;
        for (var index = from; index < text.Length; index++)
        {
            var character = text[index];
            if (character is '(' or '[' or '{') depth++;
            else if (character is ')' or ']' or '}')
            {
                if (depth == 0)
                {
                    arguments.Add(text[started..index]);
                    return arguments;
                }

                depth--;
            }
            else if (character == ',' && depth == 0)
            {
                arguments.Add(text[started..index]);
                started = index + 1;
            }
            else if (character == ';' && depth == 0)
            {
                arguments.Add(text[started..index]);
                return arguments;
            }
        }

        arguments.Add(text[started..]);
        return arguments;
    }

    /// <summary>
    /// Whether the map gives back one subset for each of the rest's and nothing beside them: its brackets close at its end, it
    /// holds no + ++ @ , or |&gt; outside them, a comprehension goes through one list, a Perl map's block gives one list, and
    /// after a .map(...) comes nothing but .toList(), .ToList(), .to_a or .collect().
    /// </summary>
    private static bool OneForEach(string expression, int restAt, int restLength, Dialect dialect)
    {
        var depth = 0;
        for (var index = 0; index < expression.Length; index++)
        {
            var character = expression[index];
            if (character is '(' or '[' or '{') depth++;
            else if (character is ')' or ']' or '}')
            {
                depth--;
                if (depth < 0) return false;
            }
            else if (depth == 0 && (character is '+' or ',' or '?' || character == '@' && index + 1 < expression.Length && char.IsWhiteSpace(expression[index + 1]) ||
                                    character == '|' && index + 1 < expression.Length && expression[index + 1] == '>'))
                return false;
        }

        if (depth != 0) return false;

        if (expression.StartsWith('['))
        {
            // One list gone through, and the comprehension's ] is the expression's last.
            if (Regex.Matches(expression, @"\bfor\b|\|\||<-").Count != (dialect.Language == "erlang" ? 2 : 1)) return false;
            return ClosingOf(expression, 0) == expression.Length - 1;
        }

        if (dialect.Language == "perl") return Regex.IsMatch(expression, @"^map\s*\{\s*\[[^{}]*\]\s*\}\s*@\w+$");
        if (Regex.IsMatch(expression, @"^(?:List|Seq|Array)\.map\b")) return restAt + restLength == expression.Length;
        if (expression.StartsWith("Enum.map", StringComparison.Ordinal)) return ClosingOf(expression, expression.IndexOf('(')) == expression.Length - 1;

        // R.map(...) or R.Select(...), then only what makes a list of it.
        var opening = expression.IndexOfAny(['(', '{'], restAt + restLength);
        if (opening < 0) return false;
        var closing = ClosingOf(expression, opening);
        return closing > 0 && Regex.IsMatch(expression[(closing + 1)..], @"^(?:\s*\.\s*(?:toList|ToList|ToArray|toArray|to_a|collect(?:::<[^>]*>)?)\s*(?:\(\s*\))?)*\s*$");
    }

    /// <summary>The index of the bracket closing the one at <paramref name="opening"/>, or -1.</summary>
    private static int ClosingOf(string text, int opening)
    {
        var depth = 0;
        for (var index = opening; index < text.Length; index++)
        {
            if (text[index] is '(' or '[' or '{') depth++;
            else if (text[index] is ')' or ']' or '}' && --depth == 0) return index;
        }

        return -1;
    }

    /// <summary>A map's text without a .toList() or .ToList() at its end, which the list it is spread or joined into makes needless.</summary>
    private static string WithoutTheEnd(string map, string end) => map.EndsWith(end, StringComparison.Ordinal) ? map[..^end.Length] : map;

    /// <summary>Whether a line is the last of its branch: the next line with code in the function is further out, or closes a block.</summary>
    private static bool LastOfItsBranch(IReadOnlyList<string> masked, int line, int end)
    {
        var indent = CodeText.Indentation(masked[line]).Length;
        for (var next = line + 1; next < end; next++)
        {
            var text = masked[next].Trim();
            if (text.Length == 0) continue;
            return CodeText.Indentation(masked[next]).Length < indent || text is "}" or "end" or "end;" or "end." or ";;";
        }

        return true;
    }

    /// <summary>The lines of a loop's body - further in than its heading, after a { on a line of its own - and the line after it.</summary>
    private static (List<int> Lines, int After) BodyOf(IReadOnlyList<string> masked, int loop, int end)
    {
        var indent = CodeText.Indentation(masked[loop]).Length;
        var lines = new List<int>();
        var at = loop + 1;
        if (at < end && masked[at].Trim() == "{" && CodeText.Indentation(masked[at]).Length == indent) at++;
        for (; at < end; at++)
        {
            if (masked[at].Trim().Length == 0) continue;
            if (CodeText.Indentation(masked[at]).Length <= indent) break;
            lines.Add(at);
        }

        return (lines, at);
    }

    /// <summary>The line after the function whose heading is given: the first line with code no further in than the heading, after the line kept.</summary>
    private static int EndOf(IReadOnlyList<string> masked, int heading, int kept)
    {
        var indent = CodeText.Indentation(masked[heading]).Length;
        for (var line = kept + 1; line < masked.Count; line++)
        {
            if (masked[line].Trim().Length > 0 && CodeText.Indentation(masked[line]).Length <= indent) return line;
        }

        return masked.Count;
    }

    /// <summary>The function a line is in - its name and its heading's line - from the nearest heading at or above it.</summary>
    private static (string Name, int Heading)? FunctionAt(IReadOnlyList<string> masked, int line)
    {
        for (var above = line; above >= 0; above--)
        {
            var heading = Heading().Match(masked[above]);
            if (!heading.Success || Regex.IsMatch(heading.Groups["name"].Value, @"^(?:if|for|while|switch|return|catch|foreach)$")) continue;
            return (heading.Groups["name"].Value, above);
        }

        return null;
    }

    [GeneratedRegex(@"\b(?:def|defp|function|func|fn|sub|let\s+rec)\s+(?<name>[A-Za-z_][\w-]*[?!]?)|^\s*(?:(?:public|private|protected|static|internal)\s+)*[\w<>\[\]:,\s]+?\s+(?<name>[A-Za-z_]\w*)\s*\([^)]*\)\s*\{?\s*$|^(?<name>[a-z_]\w*)\s*\(.*->")]
    private static partial Regex Heading();
}
