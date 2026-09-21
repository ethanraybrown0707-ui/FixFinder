using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>Logic mistakes in Python that the code itself shows.</summary>
public static partial class PythonLogicPatterns
{
    private static readonly IReadOnlySet<string> Python = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".py" };

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        new Pattern("logic-python-is-literal", IsLiteral),
        new Pattern("logic-python-assert-tuple", AssertTuple, confidence: Confidence.Certain),
        new Pattern("logic-python-mutable-default", MutableDefault),
        new Pattern("logic-python-result-discarded", ResultDiscarded),
        new Pattern("logic-python-return-in-loop", ReturnInLoop),
        new Pattern("logic-python-reset-in-loop", ResetInLoop),
        new Pattern("logic-python-comparison-statement", ComparisonStatement),
        new Pattern("logic-python-loop-never-advances", LoopNeverAdvances, Severity.Error),
    ];

    private sealed class Pattern(
        string id,
        Func<string, SourceFile, IReadOnlyList<string>, IEnumerable<LogicFinding>> find,
        Severity severity = Severity.Warning,
        Confidence confidence = Confidence.Likely,
        FindingKind kind = FindingKind.Logic) : ILogicPattern
    {
        public string Id => id;
        public IReadOnlySet<string> Extensions => Python;

        public IEnumerable<LogicFinding> Find(SourceFile source) =>
            find(id, source, CodeText.MaskAll(source.Lines, Syntax.Python))
                .Select(finding => finding with { Severity = severity, Confidence = confidence, Kind = kind });
    }

    private static string Indent(string line) => CodeText.Indentation(line);

    private static (int First, int End) Body(IReadOnlyList<string> lines, int header)
    {
        var indent = Indent(lines[header]).Length;
        var end = header + 1;

        while (end < lines.Count && (lines[end].Trim().Length == 0 || Indent(lines[end]).Length > indent)) end++;
        while (end > header + 1 && lines[end - 1].Trim().Length == 0) end--;

        return (header + 1, end);
    }

    private static IEnumerable<int> Statements(IReadOnlyList<string> masked, int first, int end) =>
        Enumerable.Range(first, Math.Max(0, end - first)).Where(i => masked[i].Trim().Length > 0);

    private static LocalFix Replace(string id, string title, string explanation, SourceFile source, int line, string text) =>
        LocalFix.ReplaceLine(id, title, explanation, source.Path, line, text);

    [GeneratedRegex(@"(?<=[\w)\]'""]\s*)\bis(?<not>\s+not)?\s+(?=(?:-?\d+(?:\.\d+)?|""[^""]*""|'[^']*')(?![\w.]))")]
    private static partial Regex IsLiteralRegex();

    private static IEnumerable<LogicFinding> IsLiteral(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var values = masked
            .Select(l => Regex.Match(l, @"^\s*(?<name>[A-Za-z_]\w*)\s*=\s*(?:-?\d+(?:\.\d+)?\s*$|""|'|(?:int|float|str|input|len)\s*\()"))
            .Where(m => m.Success)
            .Select(m => m.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        for (var i = 0; i < masked.Count; i++)
        {
            var hits = IsLiteralRegex().Matches(masked[i]).ToList();

            hits.AddRange(Regex.Matches(masked[i], @"(?<=(?<![\w.])(?<a>[A-Za-z_]\w*)\s+)\bis(?<not>\s+not)?\s+(?=(?<b>[A-Za-z_]\w*)(?![\w.(]))")
                .Where(m => values.Contains(m.Groups["a"].Value) && values.Contains(m.Groups["b"].Value) && m.Groups["b"].Value is not ("None" or "True" or "False")));

            if (hits.Count == 0) continue;

            var line = source.Lines[i];
            var corrected = hits.OrderByDescending(h => h.Index)
                .Aggregate(line, (text, hit) => text[..hit.Index] + (hit.Groups["not"].Success ? "!= " : "== ") + text[(hit.Index + hit.Length)..]);

            yield return new LogicFinding(id, i + 1,
                "`is` compares whether two things are the same object, not whether they are equal, and a number or a string can be equal without being the same object",
                Replace(id, "Compare values with ==, not is",
                    "`is` asks whether two names point at the very same object in memory. Small numbers and short strings are often shared, " +
                    "so it can happen to work - and then stop working for a bigger number or text built at run time. Python warns about " +
                    "exactly this. `==` compares the values.",
                    source, i + 1, corrected));
        }
    }

    [GeneratedRegex(@"^(?<lead>\s*)assert\s*\((?<inner>.*)\)\s*$")]
    private static partial Regex AssertRegex();

    private static IEnumerable<LogicFinding> AssertTuple(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var i = 0; i < masked.Count; i++)
        {
            if (AssertRegex().Match(masked[i]) is not { Success: true } assert) continue;

            var inner = assert.Groups["inner"];
            var comma = LastTopLevelComma(masked[i], inner.Index, inner.Index + inner.Length);
            if (comma < 0) continue;

            var line = source.Lines[i];
            var message = line[(comma + 1)..(inner.Index + inner.Length)].Trim();
            if (!Regex.IsMatch(message, @"^[rRfFbBuU]{0,2}(?:""[^""]*""|'[^']*')$")) continue;

            var condition = line[inner.Index..comma].Trim();

            yield return new LogicFinding(id, i + 1,
                "the brackets make the assert check a tuple, which is never empty, so it always passes",
                Replace(id, "Check the condition, not a tuple: assert condition, message",
                    "`assert (condition, message)` checks a tuple of two things, and a tuple with anything in it counts as true - so the " +
                    "assert passes whatever the condition is, and would never catch the mistake it was written for. Without the brackets, " +
                    "the condition is checked and the message is shown when it fails.",
                    source, i + 1, $"{assert.Groups["lead"].Value}assert {condition}, {message}"));
        }
    }

    private static int LastTopLevelComma(string masked, int from, int to)
    {
        var depth = 0;
        var last = -1;

        for (var k = from; k < to; k++)
        {
            if (masked[k] is '(' or '[' or '{') depth++;
            else if (masked[k] is ')' or ']' or '}') depth--;
            else if (masked[k] == ',' && depth == 0) last = k;
        }

        return last;
    }

    [GeneratedRegex(@"^(?<lead>\s*)(?:async\s+)?def\s+\w+\s*\((?<parameters>.*)\)\s*(?:->[^:]*)?:\s*$")]
    private static partial Regex DefRegex();

    private static IEnumerable<LogicFinding> MutableDefault(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var lines = source.Lines;

        for (var i = 0; i < masked.Count; i++)
        {
            if (DefRegex().Match(masked[i]) is not { Success: true } def) continue;

            var defaults = Regex.Matches(masked[i], @"(?<![\w.])(?<name>[A-Za-z_]\w*)\s*(?::\s*[^=,()]+)?=\s*(?<value>\[\s*\]|\{\s*\}|set\(\s*\)|list\(\s*\)|dict\(\s*\))(?=\s*[,)])").ToList();
            if (defaults.Count != 1) continue;

            var name = defaults[0].Groups["name"].Value;
            var value = defaults[0].Groups["value"];
            var (first, end) = Body(lines, i);
            var body = string.Join("\n", Enumerable.Range(first, end - first).Select(k => masked[k]));
            var n = Regex.Escape(name);

            if (!Regex.IsMatch(body, $@"(?<![\w.]){n}\s*(?:\.\s*(?:append|extend|insert|add|update|pop|remove|clear|setdefault|discard)\s*\(|\[[^\]]*\]\s*=(?!=)|\+=)")) continue;
            if (Regex.IsMatch(body, $@"(?<![\w.]){n}\s*=(?!=)")) continue;

            var at = FirstStatement(lines, first, end);
            var inner = at < lines.Count && at < end ? Indent(lines[at]) : Indent(lines[i]) + "    ";
            var empty = lines[i].Substring(value.Index, value.Length).Replace(" ", "", StringComparison.Ordinal) switch { "{}" or "dict()" => "{}", "set()" => "set()", _ => "[]" };

            var header = lines[i][..value.Index] + "None" + lines[i][(value.Index + value.Length)..];

            yield return new LogicFinding(id, i + 1,
                $"the default `{name}` is made once and shared by every call, so what one call adds to it is still there in the next",
                new LocalFix
                {
                    RuleId = id,
                    Title = $"Make a new {empty} for each call, not one shared by all",
                    Explanation =
                        $"A default value is created once, when the function is defined, and every call that leaves out `{name}` gets that " +
                        $"same object. This function adds to it, so each call sees what the calls before it added. `None` as the default, " +
                        $"and a new `{empty}` made inside the function, gives every call its own.",
                    File = source.Path, StartLine = i + 1, RemoveCount = at - i,
                    NewLines = [header, .. lines.Skip(i + 1).Take(at - i - 1), $"{inner}if {name} is None:", $"{inner}    {name} = {empty}"],
                });
        }
    }

    private static int FirstStatement(IReadOnlyList<string> lines, int first, int end)
    {
        while (first < end && lines[first].Trim().Length == 0) first++;
        if (first >= end) return first;

        var opening = lines[first].TrimStart();
        var quote = opening.StartsWith("\"\"\"", StringComparison.Ordinal) ? "\"\"\"" : opening.StartsWith("'''", StringComparison.Ordinal) ? "'''" : null;
        if (quote is null) return first;
        if (opening.Length > 3 && opening[3..].Contains(quote, StringComparison.Ordinal)) return first + 1;

        var close = first + 1;
        while (close < end && !lines[close].Contains(quote, StringComparison.Ordinal)) close++;
        return close + 1;
    }

    [GeneratedRegex(@"^(?<lead>\s*)(?<name>[A-Za-z_]\w*)\.(?<method>upper|lower|strip|lstrip|rstrip|title|capitalize|replace|swapcase|casefold|zfill|center|ljust|rjust|removeprefix|removesuffix)\((?<arguments>.*)\)\s*$")]
    private static partial Regex StringMethodStatement();

    [GeneratedRegex(@"^(?<lead>\s*)(?<function>sorted|reversed)\((?<name>[A-Za-z_]\w*)(?<rest>(?:\s*,.*)?)\)\s*$")]
    private static partial Regex SortedStatement();

    private static IEnumerable<LogicFinding> ResultDiscarded(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var lines = source.Lines;

        for (var i = 0; i < masked.Count; i++)
        {
            if (StringMethodStatement().Match(masked[i]) is { Success: true } call)
            {
                var name = call.Groups["name"].Value;
                var n = Regex.Escape(name);

                var isText = lines.Take(i).Any(l => Regex.IsMatch(l, $@"^\s*{n}\s*=\s*(?:input\s*\(|str\s*\(|[rRfFbBuU]{{0,2}}[""']|{n}\.\w+\()"));
                if (!isText) continue;

                var method = call.Groups["method"].Value;
                var original = lines[i];
                var lead = call.Groups["lead"].Value;

                yield return new LogicFinding(id, i + 1,
                    $"`{name}.{method}(...)` makes a new string and nothing keeps it, so `{name}` does not change",
                    Replace(id, $"Keep the result: {name} = {name}.{method}(...)",
                        $"Strings in Python never change. `{method}` does not alter `{name}` - it returns a new string, and on a line of its own " +
                        $"that new string is thrown away. Storing it back in `{name}` keeps it.",
                        source, i + 1, $"{lead}{name} = {original.TrimStart()}"));

                continue;
            }

            if (SortedStatement().Match(masked[i]) is { Success: true } sorted)
            {
                var function = sorted.Groups["function"].Value;
                var name = sorted.Groups["name"].Value;
                var lead = sorted.Groups["lead"].Value;
                var rest = lines[i].Substring(sorted.Groups["rest"].Index, sorted.Groups["rest"].Length);

                yield return new LogicFinding(id, i + 1,
                    $"`{function}({name})` makes a new, {(function == "sorted" ? "sorted" : "reversed")} sequence and nothing keeps it, so `{name}` does not change",
                    Replace(id, function == "sorted" ? $"Keep the sorted list: {name} = sorted({name})" : $"Keep the reversed list: {name} = list(reversed({name}))",
                        $"`{function}` leaves `{name}` as it was and returns a new sequence, which on a line of its own is thrown away. Storing it " +
                        $"back in `{name}` keeps it.",
                        source, i + 1, function == "sorted" ? $"{lead}{name} = sorted({name}{rest})" : $"{lead}{name} = list(reversed({name}))"));
            }
        }
    }

    [GeneratedRegex(@"^\s*(?:for\s+(?<variables>.+?)\s+in\s+.+|while\s+.+):\s*$")]
    private static partial Regex LoopRegex();

    private static IEnumerable<LogicFinding> ReturnInLoop(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var lines = source.Lines;

        for (var header = 0; header < masked.Count; header++)
        {
            if (LoopRegex().Match(masked[header]) is not { Success: true } loop) continue;

            var (first, end) = Body(lines, header);
            var statements = Statements(masked, first, end).ToList();
            if (statements.Count < 4) continue;

            var (ifLine, returnA, elseLine, returnB) = (statements[^4], statements[^3], statements[^2], statements[^1]);
            var branch = Indent(lines[first]).Length;

            if (Indent(lines[ifLine]).Length != branch || !Regex.IsMatch(masked[ifLine], @"^\s*if\s.+:\s*$")) continue;
            if (Indent(lines[elseLine]).Length != branch || !Regex.IsMatch(masked[elseLine], @"^\s*else\s*:\s*$")) continue;
            if (!Regex.IsMatch(masked[returnA], @"^\s*return\b") || !Regex.IsMatch(masked[returnB], @"^\s*return\b")) continue;
            if (Indent(lines[returnA]).Length <= branch || Indent(lines[returnB]).Length <= branch) continue;

            var variables = loop.Groups["variables"].Success
                ? Regex.Matches(loop.Groups["variables"].Value, @"[A-Za-z_]\w*").Select(m => m.Value).ToList()
                : [];
            if (variables.Any(v => Regex.IsMatch(masked[returnB], $@"(?<![\w.]){Regex.Escape(v)}(?!\w)"))) continue;

            var returned = lines[returnB].Trim();

            yield return new LogicFinding(id, elseLine + 1,
                $"the loop returns on its first pass whichever way the `if` goes, so it never looks past the first item",
                new LocalFix
                {
                    RuleId = id,
                    Title = $"Only {returned} once every item has been checked",
                    Explanation =
                        $"Both branches return, so the loop stops on its first pass: if the first item does not match, `{returned}` ends the " +
                        "search before any other item is looked at. The `else` belongs after the loop - reached only when no item matched.",
                    File = source.Path, StartLine = elseLine + 1, RemoveCount = returnB - elseLine + 1,
                    NewLines = [$"{Indent(lines[header])}{returned}"],
                });
        }
    }

    private static IEnumerable<LogicFinding> ResetInLoop(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var lines = source.Lines;

        for (var header = 0; header < masked.Count; header++)
        {
            if (!LoopRegex().IsMatch(masked[header])) continue;

            var (first, end) = Body(lines, header);
            var statements = Statements(masked, first, end).ToList();
            if (statements.Count < 2) continue;

            if (Regex.Match(masked[statements[0]], @"^\s*(?<name>[A-Za-z_]\w*)\s*=\s*(?<value>0|0\.0|1|""\s*""|'\s*'|\[\s*\]|\{\s*\})\s*$") is not { Success: true } reset) continue;

            var name = reset.Groups["name"].Value;
            var n = Regex.Escape(name);

            var builds = statements.Skip(1).Any(s => Regex.IsMatch(masked[s], $@"(?<![\w.]){n}\s*(?:\+=|-=|\*=|=\s*{n}\s*[+\-*])|(?<![\w.]){n}\.(?:append|add|extend|update)\s*\("));
            var setAgain = statements.Skip(1).Any(s => Regex.IsMatch(masked[s], $@"^\s*{n}\s*=(?!=)(?!\s*{n}\b)"));
            if (!builds || setAgain) continue;

            var headerIndent = Indent(lines[header]).Length;
            var usedAfter = false;

            for (var k = end; k < masked.Count && !usedAfter; k++)
            {
                if (masked[k].Trim().Length == 0) continue;
                if (Indent(lines[k]).Length < headerIndent) break;
                usedAfter = Regex.IsMatch(masked[k], $@"(?<![\w.]){n}(?!\w)");
            }

            if (!usedAfter) continue;

            var assignment = Indent(lines[header]) + lines[statements[0]].Trim();

            yield return new LogicFinding(id, statements[0] + 1,
                $"`{name}` is set back to {reset.Groups["value"].Value} at the start of every pass, so after the loop it only holds the last item's part",
                new LocalFix
                {
                    RuleId = id,
                    Title = $"Start {name} once, before the loop",
                    Explanation =
                        $"`{lines[statements[0]].Trim()}` is inside the loop, so every pass throws away what the passes before it added and starts " +
                        $"again. Set once before the loop, `{name}` keeps building up across every pass.",
                    File = source.Path, StartLine = header + 1, RemoveCount = statements[0] - header + 1,
                    NewLines = [assignment, lines[header], .. lines.Skip(header + 1).Take(statements[0] - header - 1)],
                });
        }
    }

    private static IEnumerable<LogicFinding> ComparisonStatement(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var depth = 0;

        for (var i = 0; i < masked.Count; i++)
        {
            var atStart = depth;
            depth += masked[i].Count(c => c is '(' or '[' or '{') - masked[i].Count(c => c is ')' or ']' or '}');

            if (atStart != 0 || (i > 0 && source.Lines[i - 1].TrimEnd().EndsWith('\\'))) continue;
            if (Regex.Match(masked[i], @"^(?<lead>\s*)(?<target>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s*==\s*(?<value>[^=].*)$") is not { Success: true } statement) continue;
            if (Regex.IsMatch(masked[i], @"\b(?:and|or|if|else|for|lambda)\b")) continue;

            var line = source.Lines[i];
            var at = statement.Groups["target"].Index + statement.Groups["target"].Length;
            var operatorAt = line.IndexOf("==", at, StringComparison.Ordinal);
            var target = statement.Groups["target"].Value;

            yield return new LogicFinding(id, i + 1,
                $"`{target} == ...` on a line of its own compares and throws the answer away, so `{target}` is never set",
                Replace(id, $"Set {target} with =, not ==",
                    $"`==` asks whether `{target}` is equal to the value and gives back True or False, which a line on its own discards. " +
                    $"`=` stores the value in `{target}`.",
                    source, i + 1, line[..operatorAt] + "=" + line[(operatorAt + 2)..]));
        }
    }

    private static IEnumerable<LogicFinding> LoopNeverAdvances(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var lines = source.Lines;

        for (var header = 0; header < masked.Count; header++)
        {
            if (Regex.Match(masked[header], @"^(?<lead>\s*)while\s+(?<name>[A-Za-z_]\w*)\s*(?<op><=|<|>=|>)\s*(?<bound>[^:]+):\s*$") is not { Success: true } loop) continue;

            var name = loop.Groups["name"].Value;
            var n = Regex.Escape(name);
            var (first, end) = Body(lines, header);
            if (end <= first) continue;

            var body = Enumerable.Range(first, end - first).Select(k => masked[k]).ToList();

            if (body.Any(l => Regex.IsMatch(l, $@"(?<![\w.]){n}\s*(?:[+\-*/%]|//)?=(?!=)|\bfor\s+(?:[\w\s,]*\b)?{n}\b|\b(?:break|return|raise|exit|quit)\b|\bglobal\b|\bnonlocal\b"))) continue;

            var boundNames = Regex.Matches(loop.Groups["bound"].Value, @"(?<![\w.])[A-Za-z_]\w*").Select(m => m.Value).Where(w => w is not ("len" or "and" or "or" or "not")).ToList();
            if (boundNames.Any(b => body.Any(l => Regex.IsMatch(l, $@"(?<![\w.]){Regex.Escape(b)}\s*(?:[+\-*/%]|//)?=(?!=)|(?<![\w.]){Regex.Escape(b)}\.(?:append|pop|remove|insert|extend|clear)\s*\(")))) continue;

            if (!Enumerable.Range(0, header).Any(k => Regex.IsMatch(masked[k], $@"^\s*{n}\s*=\s*-?\d+\s*$"))) continue;

            var step = loop.Groups["op"].Value.StartsWith('<') ? "+= 1" : "-= 1";
            var inner = Indent(lines[first]);

            yield return new LogicFinding(id, header + 1,
                $"nothing in the loop changes `{name}`, so once `{lines[header].Trim().TrimEnd(':')}` is true it stays true and the loop never ends",
                LocalFix.Insert(id, $"Move {name} on at the end of each pass: {name} {step}",
                    $"The loop keeps going while `{name} {loop.Groups["op"].Value} {loop.Groups["bound"].Value.Trim()}`, and nothing inside it changes " +
                    $"`{name}` or the bound, so it can only stop by never starting. `{name} {step}` at the end of each pass moves it towards the end.",
                    source.Path, end + 1, [$"{inner}{name} {step}"]));
        }
    }
}
