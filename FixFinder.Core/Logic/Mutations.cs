using System.Text.RegularExpressions;
using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// One small change to one line, to be tried against the expected output. An edit that adds a line beside this one, rather
/// than changing it, says where - <paramref name="Placement"/> is "before" or "after" - and its NewText holds both lines.
/// </summary>
public sealed record Mutation(int Line, string NewText, string From, string To, string Kind, int Cost, string? Placement = null);

/// <summary>The small edits that correct most one-line logic mistakes, generated for a line without knowing what it means.</summary>
public static partial class Mutations
{
    [GeneratedRegex(@"(?<![<>=!+\-*/%&|^:])(?<op><=|>=|==|!=|<|>)(?![<>=\-])")]
    private static partial Regex Relational();

    [GeneratedRegex(@"(?<=[\w)\]]\s*)(?<op>\+|-|\*|//|/)(?![+\-=/*>])(?=\s*[\w(\[.])")]
    private static partial Regex Arithmetic();

    [GeneratedRegex(@"(?<op>\+=|-=|\*=|//=|/=)")]
    private static partial Regex Compound();

    [GeneratedRegex(@"(?<op>\+\+|--)")]
    private static partial Regex Step();

    [GeneratedRegex(@"(?<![\w.$])(?<number>\d+)(?![\w.])")]
    private static partial Regex Integer();

    [GeneratedRegex(@"\s*(?<op>[-+])\s*1(?![\w.])")]
    private static partial Regex PlusMinusOne();

    [GeneratedRegex(@"(?<![\w.$:])(?:(?:Math|math|std|Mathf)\s*(?:\.|::)\s*)?(?<name>max|min|Max|Min)(?=\s*\()")]
    private static partial Regex MaxMin();

    [GeneratedRegex(@"(?<![\w.$])(?<value>True|False|true|false)(?![\w$])")]
    private static partial Regex Boolean();

    [GeneratedRegex(@"^\s*(?:#|//|/\*|\*|import\b|from\s+\S+\s+import\b|#include\b|package\b|using\s+[\w.]+\s*;|namespace\b|@)")]
    private static partial Regex NotCode();

    public static IReadOnlyList<Mutation> For(SourceFile source, int number)
    {
        if (source.Line(number) is not { } line) return [];

        var python = Path.GetExtension(source.Path).Equals(".py", StringComparison.OrdinalIgnoreCase);
        var syntax = python ? Syntax.Python : Syntax.CLike;
        var maskedLines = CodeText.MaskAll(source.Lines, syntax);
        var masked = maskedLines[number - 1];

        if (masked.Trim().Length == 0 || NotCode().IsMatch(line)) return [];
        if (python && Regex.IsMatch(masked, @"^\s*(?:async\s+)?(?:def|class)\s")) return [];

        var found = new List<Mutation>();

        void Add(int index, int length, string replacement, string kind, int cost)
        {
            var from = line.Substring(index, length);
            if (from == replacement) return;
            found.Add(new Mutation(number, line[..index] + replacement + line[(index + length)..], from.Trim(), replacement.Trim(), kind, cost));
        }

        var clike = !python;

        var decides = Regex.IsMatch(masked, @"\b(?:if|elif|while|for|return)\b|\?");
        var generic = clike && Regex.IsMatch(masked, @"\b[A-Z]\w*\s*<[\w\s,<>?\[\]]*>");

        foreach (Match m in Relational().Matches(masked))
        {
            var op = m.Groups["op"].Value;

            if (op is "<" or ">" && generic && !(m.Index > 0 && masked[m.Index - 1] == ' ' && m.Index + 1 < masked.Length && masked[m.Index + 1] == ' ')) continue;
            if (op == ">" && m.Index > 0 && masked[m.Index - 1] is '-' or '=') continue;

            var (near, far) = op switch
            {
                "<" => ("<=", ">"), "<=" => ("<", ">="), ">" => (">=", "<"), ">=" => (">", "<="), "==" => ("!=", (string?)null), "!=" => ("==", null),
                _ => (null, null),
            };

            if (near is not null) Add(m.Index, op.Length, near, "a comparison off by one, or the wrong way round", op is "==" or "!=" ? 2 : decides ? 0 : 1);
            if (far is not null) Add(m.Index, op.Length, far, "a comparison the wrong way round", 3);
        }

        foreach (Match m in Arithmetic().Matches(masked))
        {
            var op = m.Groups["op"].Value;
            if (clike && op == "/" && m.Index + 1 < masked.Length && masked[m.Index + 1] == '/') continue;

            foreach (var (to, cost) in op switch
            {
                "+" => new[] { ("-", 2) },
                "-" => [("+", 2)],
                "*" => [("/", 3), ("+", 4)],
                "/" when python => [("//", 2), ("*", 3)],
                "//" => [("/", 2)],
                "/" => [("*", 3)],
                _ => [],
            })
            {
                Add(m.Index, op.Length, to, "the wrong arithmetic operator", cost);
            }
        }

        foreach (Match m in Compound().Matches(masked))
        {
            var op = m.Groups["op"].Value;
            var to = op switch { "+=" => "-=", "-=" => "+=", "*=" => "+=", "/=" when python => "//=", "//=" => "/=", _ => null };
            if (to is not null) Add(m.Index, op.Length, to, "the wrong arithmetic operator", 2);
        }

        if (clike)
        {
            foreach (Match m in Step().Matches(masked))
                Add(m.Index, 2, m.Groups["op"].Value == "++" ? "--" : "++", "a count going the wrong way", 2);
        }

        foreach (Match m in Regex.Matches(masked, python ? @"(?<![\w.])(?<op>and|or)(?![\w])" : @"(?<op>&&|\|\|)"))
        {
            var op = m.Groups["op"].Value;
            var to = op switch { "and" => "or", "or" => "and", "&&" => "||", _ => "&&" };
            Add(m.Index, op.Length, to, "and where or was meant, or the reverse", 2);
        }

        foreach (Match m in Regex.Matches(masked, python ? @"(?<![\w.])not\s+" : @"!(?![=])(?=\s*[\w(])"))
            Add(m.Index, m.Length, "", "a condition negated by mistake", 3);

        foreach (Match m in PlusMinusOne().Matches(masked))
            Add(m.Index, m.Length, "", "a bound off by one", 1);

        foreach (var (start, length) in Bounds(masked, python))
        {
            var operand = line.Substring(start, length);
            Add(start, length, operand + " + 1", "a bound off by one", 2);
            Add(start, length, operand + " - 1", "a bound off by one", 2);
        }

        foreach (Match m in Integer().Matches(masked))
        {
            var value = long.Parse(m.Groups["number"].Value);
            if (value > 1_000_000) continue;

            Add(m.Index, m.Length, (value + 1).ToString(), "a constant off by one", value <= 1 ? 2 : 3);
            if (value > 0) Add(m.Index, m.Length, (value - 1).ToString(), "a constant off by one", value <= 1 ? 2 : 3);

            // A place value off by a digit - n // 100 where n // 10 was meant, to take the last digit off a number.
            if (IsPowerOfTen(value))
            {
                if (value >= 100) Add(m.Index, m.Length, (value / 10).ToString(), "a place value off by a digit", 3);
                Add(m.Index, m.Length, (value * 10).ToString(), "a place value off by a digit", 3);
            }
        }

        foreach (Match m in MaxMin().Matches(masked))
        {
            var name = m.Groups["name"].Value;
            var to = name switch { "max" => "min", "min" => "max", "Max" => "Min", _ => "Max" };
            Add(m.Groups["name"].Index, name.Length, to, "the largest where the smallest was meant, or the reverse", 3);
        }

        foreach (Match m in Boolean().Matches(masked))
        {
            var value = m.Groups["value"].Value;
            var to = value switch { "True" => "False", "False" => "True", "true" => "false", _ => "true" };
            if ((python && value is "True" or "False") || (clike && value is "true" or "false")) Add(m.Index, value.Length, to, "the opposite result", 3);
        }

        if (clike && !source.Path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) && !TypeScriptCheck.IsTypeScript(source.Path))
        {
            foreach (Match m in Regex.Matches(masked, @"(?<![\w.)\]])(?<left>[A-Za-z_]\w*)\s*/\s*(?<right>[A-Za-z_]\w*|\d+)(?![\w.(])"))
            {
                var left = m.Groups["left"];
                var cast = source.Path.EndsWith(".go", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : $"(double) {left.Value}";
                if (cast is not null) Add(left.Index, left.Length, cast, "a whole-number division where the fraction was wanted", 2);
            }
        }

        foreach (var call in Calls(masked))
        {
            // Two arguments in the wrong order - gcd(a % b, b) where gcd(b, a % b) was meant.
            for (var at = 0; at + 1 < call.Count && call.Count <= 4; at++)
            {
                var (first, second) = (call[at], call[at + 1]);
                var (firstText, secondText) = (line.Substring(first.Start, first.Length), line.Substring(second.Start, second.Length));
                if (firstText == secondText || IsNamedOrSpread(firstText, python) || IsNamedOrSpread(secondText, python)) continue;

                var swapped = line[..first.Start] + secondText + line[(first.Start + first.Length)..second.Start] + firstText + line[(second.Start + second.Length)..];
                found.Add(new Mutation(number, swapped, $"{firstText}, {secondText}", $"{secondText}, {firstText}", "arguments passed in the wrong order", 2));
            }

            // An argument off by one - binsearch(mid, end) where binsearch(mid + 1, end) was meant.
            foreach (var argument in call)
            {
                var text = line.Substring(argument.Start, argument.Length);
                if (!OneOffOperand().IsMatch(masked.Substring(argument.Start, argument.Length))) continue;
                Add(argument.Start, argument.Length, text + " + 1", "an argument off by one", 3);
                Add(argument.Start, argument.Length, text + " - 1", "an argument off by one", 3);
            }
        }

        // A part of an index off by one - dp[i - 1, j] where dp[i - 1, j - 1] was meant.
        foreach (var part in IndexParts(masked))
        {
            var text = line.Substring(part.Start, part.Length);
            Add(part.Start, part.Length, text + " + 1", "an index off by one", 3);
            Add(part.Start, part.Length, text + " - 1", "an index off by one", 3);
        }

        // A length off by one where it is worked with - (len(digits)) * [0] where (len(digits) - 1) * [0] was meant.
        foreach (Match m in (python ? PythonLength() : Length()).Matches(masked))
        {
            var text = line.Substring(m.Index, m.Length);
            Add(m.Index, m.Length, text + " - 1", "a length off by one", 3);
            Add(m.Index, m.Length, text + " + 1", "a length off by one", 3);
        }

        // any where all was meant, or the reverse - any(n % p > 0 for p in primes) where all(...) was.
        foreach (Match m in (python ? PythonAnyAll() : AnyAll()).Matches(masked))
        {
            var name = m.Groups["name"].Value;
            var to = name switch
            {
                "any" => "all", "all" => "any", "some" => "every", "every" => "some",
                "anyMatch" => "allMatch", "allMatch" => "anyMatch", "Any" => "All", _ => "Any",
            };
            Add(m.Groups["name"].Index, name.Length, to, "any where all was meant, or the reverse", 2);
        }

        // The wrong bitwise operator - n ^= n - 1 where n &= n - 1 was meant.
        foreach (Match m in BitwiseCompound().Matches(masked))
        {
            foreach (var to in new[] { "&=", "|=", "^=" }.Where(other => other != m.Value))
                Add(m.Index, m.Length, to, "the wrong bitwise operator", 2);
        }

        foreach (Match m in BitwiseBinary().Matches(masked))
        {
            if (m.Index > 0 && masked[m.Index - 1] == m.Value[0] || m.Index + 1 < masked.Length && masked[m.Index + 1] == m.Value[0]) continue;
            foreach (var to in new[] { "&", "|", "^" }.Where(other => other != m.Value))
                Add(m.Index, m.Length, to, "the wrong bitwise operator", 3);
        }

        // Added at the wrong end - result = result + digit where result = digit + result was meant.
        if (WrongEnd(line, masked) is { } reordered) found.Add(reordered with { Line = number });

        // A running best replaced rather than kept - longest = length + 1 where longest = max(longest, length + 1) was meant.
        foreach (var kept in KeptBest(source, number, line, masked, python)) found.Add(kept);

        // Taken from the wrong end of a list - pop(0), the first item, where pop(), the last, was meant, or the reverse: a
        // stack used as a queue, or a queue as a stack. JavaScript's shift and pop, and Java's removeFirst and removeLast.
        foreach (Match m in Regex.Matches(masked, python ? @"\.pop\(\s*(?<first>0)?\s*\)" : @"\.(?<end>shift|pop|removeFirst|removeLast|pollFirst|pollLast|getFirst|getLast|peekFirst|peekLast)\(\s*\)"))
        {
            var otherEnd = python
                ? m.Groups["first"].Success ? ".pop()" : ".pop(0)"
                : "." + m.Groups["end"].Value switch
                {
                    "shift" => "pop", "pop" => "shift",
                    var first when first.EndsWith("First", StringComparison.Ordinal) => first[..^5] + "Last",
                    var last => last[..^4] + "First",
                } + "()";
            Add(m.Index, m.Length, otherEnd, "taken from the wrong end of the list - the first item where the last was meant, or the reverse", 1);
        }

        // A base case that answers nothing - return [] where return [[]] was meant: the subsets of nothing are one, the empty one.
        if (Regex.Match(masked, python ? @"^\s*return\s*(?<empty>\[\s*\])\s*$" : @"^\s*return\s*(?<empty>\[\s*\])\s*;\s*$") is { Success: true } nothing)
            Add(nothing.Groups["empty"].Index, nothing.Groups["empty"].Length, "[[]]", "a base case that answers nothing where it should answer one empty answer", 3);

        // A base case one too small - len(arr) == 0 where len(arr) <= 1 was meant: one item is as finished as none, and
        // splitting it again never ends.
        foreach (Match m in SizeIsNothing().Matches(masked))
            Add(m.Groups["check"].Index, m.Groups["check"].Length, "<= 1", "a base case one too small", 3);

        // Finished without checking what was counted - return True where return depth == 0 was meant.
        foreach (var counted in CountNotChecked(source, number, line, masked, maskedLines, python)) found.Add(counted);

        if (python)
        {
            foreach (var wrong in WrongVariable(source, number, line, masked)) found.Add(wrong);
            foreach (var yielded in YieldedCall(source, number, line, masked)) found.Add(yielded);
            foreach (var given in GivenBack(source, number, line, masked)) found.Add(given);
            foreach (var shifted in ShiftedPastWhatWasLeftOut(source, number, line, masked, maskedLines)) found.Add(shifted);
            foreach (var emptied in NotStoppedWhenEmpty(source, number, line, masked, maskedLines)) found.Add(emptied);
            foreach (var shortened in ShortenedLikeTheOther(source, number, line, masked, maskedLines)) found.Add(shortened);
            foreach (var rest in LoopOverTheRest(source, number, line, masked, maskedLines)) found.Add(rest);
            foreach (var leftover in LeftoverAdded(source, number, line, masked, maskedLines)) found.Add(leftover);
            foreach (var pushed in PushedAfterPopping(source, number, line, maskedLines)) found.Add(pushed);
        }

        return found.GroupBy(f => f.NewText).Select(g => g.OrderBy(f => f.Cost).First()).OrderBy(f => f.Cost).ToList();
    }

    /// <summary>A size compared with nothing: len(arr) == 0, arr.length === 0, list.size() == 0, items.Count == 0.</summary>
    [GeneratedRegex(@"(?:\blen\s*\([^()]*\)|[\w)\]]\s*\.\s*(?:length|Length|Count|size\s*\(\s*\)))\s*(?<check>={2,3}\s*0)(?![\w.])")]
    private static partial Regex SizeIsNothing();

    private static int IndentOf(string text) => text.Length - text.TrimStart().Length;

    /// <summary>The names of a Python function's parameters, without defaults, types or stars.</summary>
    private static List<string> ParameterNames(string parameters) =>
        parameters.Split(',')
            .Select(parameter => Regex.Match(parameter, @"^\s*\**(?<name>[A-Za-z_]\w*)").Groups["name"].Value)
            .Where(name => name.Length > 0 && name != "self")
            .ToList();

    /// <summary>Whether a number is 10, 100, 1000 and so on: one place value of a decimal number.</summary>
    private static bool IsPowerOfTen(long value)
    {
        if (value < 10) return false;
        while (value % 10 == 0) value /= 10;
        return value == 1;
    }

    /// <summary>
    /// The counters a line's function counts up and down - depth += 1 and depth -= 1, or depth++ and depth-- - in Python the
    /// function the line is in, otherwise the 60 lines above it.
    /// </summary>
    private static IEnumerable<Mutation> CountNotChecked(
        SourceFile source, int number, string line, string masked, IReadOnlyList<string> maskedLines, bool python)
    {
        if (Regex.Match(masked, @"^(?<before>\s*return\s+)(?<value>True|true)(?<end>\s*;?)\s*$") is not { Success: true } m) yield break;

        var (first, last) = python
            ? Enclosing(source, number) is { } function ? (function.First, number - 1) : (-1, -1)
            : (Math.Max(0, number - 61), number - 1);
        if (first < 0) yield break;

        var body = string.Join("\n", maskedLines.Skip(first).Take(last - first));
        var up = Regex.Matches(body, @"(?<![\w.])(?<name>[A-Za-z_]\w*)\s*(?:\+=\s*1(?![\w.])|\+\+)|\+\+\s*(?<name>[A-Za-z_]\w*)").Select(c => c.Groups["name"].Value);
        var down = Regex.Matches(body, @"(?<![\w.])(?<name>[A-Za-z_]\w*)\s*(?:-=\s*1(?![\w.])|--)|--\s*(?<name>[A-Za-z_]\w*)").Select(c => c.Groups["name"].Value).ToHashSet();

        foreach (var counter in up.Distinct().Where(down.Contains))
        {
            var check = $"{counter} == 0";
            yield return new Mutation(number, line[..m.Groups["value"].Index] + check + line[(m.Groups["value"].Index + m.Groups["value"].Length)..],
                m.Groups["value"].Value, check, "finished without checking what was counted - every one opened has to be closed", 3);
        }
    }

    /// <summary>
    /// A base case that gives back nothing where it should give back what it was given: return [] where return [n] was
    /// meant - the factors of a prime are the prime itself.
    /// </summary>
    private static IEnumerable<Mutation> GivenBack(SourceFile source, int number, string line, string masked)
    {
        if (Regex.Match(masked, @"^\s*return\s*(?<empty>\[\s*\])\s*$") is not { Success: true } m) yield break;
        if (Enclosing(source, number) is not { } function) yield break;

        var empty = m.Groups["empty"];
        foreach (var parameter in ParameterNames(function.Parameters))
            yield return new Mutation(number, line[..empty.Index] + $"[{parameter}]" + line[(empty.Index + empty.Length)..], "[]", $"[{parameter}]",
                "a base case that gives back nothing where it should give back what it was given", 4);
    }

    /// <summary>
    /// An index passed on to the function's call of itself on part of a list, not shifted past the items left behind -
    /// kth(above, k) where kth(above, k - num_lessoreq) was meant: in the part, k counts from the part's own start. The
    /// shifts tried are the counts the function works out above the line, each a name given a len(...).
    /// </summary>
    private static IEnumerable<Mutation> ShiftedPastWhatWasLeftOut(SourceFile source, int number, string line, string masked, IReadOnlyList<string> maskedLines)
    {
        if (Enclosing(source, number) is not { } function) yield break;

        var parameters = ParameterNames(function.Parameters);
        var counts = Enumerable.Range(function.First, Math.Max(0, number - 1 - function.First))
            .Select(index => Regex.Match(maskedLines[index], @"^\s*(?<name>[A-Za-z_]\w*)\s*=(?!=).*(?<![\w.])len\s*\("))
            .Where(count => count.Success)
            .Select(count => count.Groups["name"].Value)
            .Where(name => !parameters.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (counts.Count == 0) yield break;

        foreach (Match call in Regex.Matches(masked, $@"(?<![\w.]){Regex.Escape(function.Name)}\s*\("))
        {
            if (Arguments(masked, call.Index + call.Length - 1) is not { } arguments) continue;

            foreach (var argument in arguments)
            {
                var passed = line.Substring(argument.Start, argument.Length).TrimEnd();
                if (!parameters.Contains(passed.Trim())) continue;

                foreach (var count in counts)
                {
                    yield return new Mutation(number, line[..argument.Start] + $"{passed} - {count}" + line[(argument.Start + argument.Length)..],
                        passed.Trim(), $"{passed.Trim()} - {count}", "an index not shifted past the items left behind", 3);
                }
            }
        }
    }

    /// <summary>
    /// The parameters a recursive function works through one item at a time - passes on as items[1:], or splits as first,
    /// *rest = items - so each needs a base case for when it is empty. None when the function never calls itself.
    /// </summary>
    private static List<string> WorkedThrough((string Name, int First, int Last, string Parameters) function, IReadOnlyList<string> maskedLines)
    {
        var body = string.Join("\n", maskedLines.Skip(function.First).Take(function.Last - function.First));
        if (!Regex.IsMatch(body, $@"(?<![\w.]){Regex.Escape(function.Name)}\s*\(")) return [];

        return ParameterNames(function.Parameters)
            .Where(parameter => Regex.IsMatch(body, $@"(?<![\w.]){Regex.Escape(parameter)}\s*\[\s*1\s*:\s*\]|\*\s*\w+\s*=\s*{Regex.Escape(parameter)}(?![\w.\[])"))
            .ToList();
    }

    /// <summary>
    /// A base case that does not stop when a list worked through is empty - if total &lt; 0: where if total &lt; 0 or not coins:
    /// was meant. Only on an if that returns straight away, in a function that calls itself on less of the list.
    /// </summary>
    private static IEnumerable<Mutation> NotStoppedWhenEmpty(SourceFile source, int number, string line, string masked, IReadOnlyList<string> maskedLines)
    {
        if (Regex.Match(masked, @"^(?<head>\s*(?:if|elif)\s+)(?<condition>.+?)\s*:\s*$") is not { Success: true } m) yield break;

        var next = number;
        while (next < source.Count && maskedLines[next].Trim().Length == 0) next++;
        if (next >= source.Count || IndentOf(maskedLines[next]) <= IndentOf(masked) || !Regex.IsMatch(maskedLines[next], @"^\s*return\b")) yield break;

        if (Enclosing(source, number) is not { } function) yield break;

        var condition = m.Groups["condition"];
        foreach (var items in WorkedThrough(function, maskedLines))
        {
            if (Regex.IsMatch(condition.Value, $@"(?<![\w.]){Regex.Escape(items)}(?![\w(])")) continue;

            var stopped = $"{line.Substring(condition.Index, condition.Length)} or not {items}";
            yield return new Mutation(number, line[..condition.Index] + stopped + line[(condition.Index + condition.Length)..],
                condition.Value.Trim(), stopped, "a base case that does not stop when the list it works through is empty", 4);
        }
    }

    /// <summary>
    /// A list passed on whole where it should be shortened like the one beside it - f(a[1:], b) where f(a[1:], b[1:]) was
    /// meant: when the first items matched, both are used up. Only in a function's call to itself, for its own parameters.
    /// </summary>
    private static IEnumerable<Mutation> ShortenedLikeTheOther(SourceFile source, int number, string line, string masked, IReadOnlyList<string> maskedLines)
    {
        if (Enclosing(source, number) is not { } function) yield break;

        var parameters = ParameterNames(function.Parameters);
        var body = string.Join("\n", maskedLines.Skip(function.First).Take(function.Last - function.First));

        foreach (Match call in Regex.Matches(masked, $@"(?<![\w.]){Regex.Escape(function.Name)}\s*\("))
        {
            if (Arguments(masked, call.Index + call.Length - 1) is not { } arguments) continue;

            var texts = arguments.Select(a => masked.Substring(a.Start, a.Length)).ToList();
            if (!texts.Any(text => Regex.IsMatch(text, @"^[A-Za-z_]\w*\s*\[\s*1\s*:\s*\]$"))) continue;

            foreach (var argument in arguments)
            {
                var name = masked.Substring(argument.Start, argument.Length);
                if (!parameters.Contains(name) || !Regex.IsMatch(body, $@"(?<![\w.]){Regex.Escape(name)}\s*\[")) continue;

                yield return new Mutation(number, line[..argument.Start] + $"{name}[1:]" + line[(argument.Start + argument.Length)..], name, $"{name}[1:]",
                    "a list passed on whole where it should be shortened like the one beside it", 3);
            }
        }
    }

    /// <summary>
    /// A loop over items already used - for x in arr: where for x in arr[k:]: was meant, when arr[:k] was taken above it.
    /// </summary>
    private static IEnumerable<Mutation> LoopOverTheRest(SourceFile source, int number, string line, string masked, IReadOnlyList<string> maskedLines)
    {
        if (Regex.Match(masked, @"^\s*for\s+[\w, ()]+?\s+in\s+(?<items>[A-Za-z_]\w*)\s*:\s*$") is not { Success: true } m) yield break;
        if (Enclosing(source, number) is not { } function) yield break;

        var items = m.Groups["items"];
        var above = string.Join("\n", maskedLines.Skip(function.First).Take(number - 1 - function.First));

        foreach (var taken in Regex.Matches(above, $@"(?<![\w.]){Regex.Escape(items.Value)}\s*\[\s*:\s*(?<count>[A-Za-z_]\w*|\d+)\s*\]").Select(t => t.Groups["count"].Value).Distinct())
        {
            var rest = $"{items.Value}[{taken}:]";
            yield return new Mutation(number, line[..items.Index] + rest + line[(items.Index + items.Length)..], items.Value, rest,
                "a loop over items already used - the first ones were taken before it", 3);
        }
    }

    /// <summary>
    /// The last piece never added - a loop adds each piece it finishes to a list, and the piece left when the loop ends is not:
    /// lines.append(text) missing before return lines. Tried with each name the loop works with.
    /// </summary>
    private static IEnumerable<Mutation> LeftoverAdded(SourceFile source, int number, string line, string masked, IReadOnlyList<string> maskedLines)
    {
        if (Regex.Match(masked, @"^(?<indent>\s*)return\s+(?<collection>[A-Za-z_]\w*)\s*$") is not { Success: true } m) yield break;
        if (Enclosing(source, number) is not { } function) yield break;

        var collection = m.Groups["collection"].Value;
        var indent = IndentOf(masked);

        // The loop the return comes after: the last for or while above it, no more indented than it.
        var loop = -1;
        for (var above = number - 2; above > function.First - 1; above--)
        {
            if (maskedLines[above].Trim().Length == 0 || IndentOf(maskedLines[above]) > indent) continue;
            if (Regex.IsMatch(maskedLines[above], @"^\s*(?:for|while)\b")) loop = above;
            break;
        }
        if (loop < 0) yield break;

        var body = Enumerable.Range(loop + 1, number - 2 - loop).Select(at => maskedLines[at]).Where(text => IndentOf(text) > IndentOf(maskedLines[loop])).ToList();
        if (!body.Any(text => Regex.IsMatch(text, $@"(?<![\w.]){Regex.Escape(collection)}\s*\.\s*append\s*\("))) yield break;

        var names = new List<string>();
        foreach (Match used in Regex.Matches(maskedLines[loop], @"(?<![\w.])[A-Za-z_]\w*(?![\w(])")) names.Add(used.Value);
        foreach (var text in body)
            if (Regex.Match(text, @"^\s*(?<targets>[A-Za-z_][\w, ]*?)\s*(?:=|\+=)(?!=)") is { Success: true } assigned)
                names.AddRange(assigned.Groups["targets"].Value.Split(',').Select(name => name.Trim()));

        foreach (var name in names.Distinct().Where(name => name.Length > 0 && name != collection && name != function.Name && !PythonWords.Contains(name)).Take(8))
        {
            var added = $"{collection}.append({name})";
            yield return new Mutation(number, $"{line[..indent]}{added}\n{line}", line.Trim(), added,
                "the last piece never added - the loop adds each piece it finishes, but not the one left when it ends", 4, "before");
        }
    }

    /// <summary>
    /// An item never pushed onto a stack after the stack was popped down to its place - while ops and ...: out.append(ops.pop())
    /// with no ops.append(token) after it, in a loop over each token. Added after the last line of the popping loop.
    /// </summary>
    private static IEnumerable<Mutation> PushedAfterPopping(SourceFile source, int number, string line, IReadOnlyList<string> maskedLines)
    {
        var indent = IndentOf(maskedLines[number - 1]);

        // The while loop this line is the last line of.
        var header = -1;
        for (var above = number - 2; above >= 0; above--)
        {
            if (maskedLines[above].Trim().Length == 0 || IndentOf(maskedLines[above]) >= indent) continue;
            header = above;
            break;
        }
        if (header < 0 || Regex.Match(maskedLines[header], @"^\s*while\s+(?<stack>[A-Za-z_]\w*)\b(?!\s*[.\[(])") is not { Success: true } loop) yield break;

        var next = number;
        while (next < source.Count && maskedLines[next].Trim().Length == 0) next++;
        if (next < source.Count && IndentOf(maskedLines[next]) > IndentOf(maskedLines[header])) yield break;

        var stack = loop.Groups["stack"].Value;
        if (stack == "not" || !Enumerable.Range(header + 1, number - 1 - header).Any(at => Regex.IsMatch(maskedLines[at], $@"(?<![\w.]){Regex.Escape(stack)}\s*\.\s*pop\s*\(\s*\)"))) yield break;

        // The loop over each item that holds the while loop.
        var besideTheLoop = source.Lines[header][..IndentOf(maskedLines[header])];
        var outer = besideTheLoop.Length;
        for (var above = header - 1; above >= 0; above--)
        {
            if (maskedLines[above].Trim().Length == 0 || IndentOf(maskedLines[above]) >= outer) continue;
            if (Regex.Match(maskedLines[above], @"^\s*for\s+(?<item>[A-Za-z_]\w*)\s+in\b") is { Success: true } each)
            {
                var pushed = $"{stack}.append({each.Groups["item"].Value})";
                yield return new Mutation(number, $"{line}\n{besideTheLoop}{pushed}", line.Trim(), pushed,
                    "an item never pushed onto the stack after the stack was popped down to its place", 4, "after");
                yield break;
            }
            if (Regex.IsMatch(maskedLines[above], @"^\s*(?:async\s+)?(?:def|class)\b")) yield break;
            outer = IndentOf(maskedLines[above]);
        }
    }

    /// <summary>A call's name: not a keyword that takes brackets, such as if, while or return.</summary>
    [GeneratedRegex(@"(?<![\w.$])(?<name>[A-Za-z_$][\w$.]*)\s*\(")]
    private static partial Regex CallStart();

    private static readonly HashSet<string> NotCalls = new(StringComparer.Ordinal)
    {
        "if", "elif", "while", "for", "switch", "catch", "return", "and", "or", "not", "in", "sizeof", "typeof", "def", "class",
        "lambda", "print", "printf", "println", "assert", "yield", "await", "new", "using", "lock", "foreach", "with",
    };

    /// <summary>Each call on the line whose brackets close on it: where each of its arguments is, in the order given.</summary>
    private static IEnumerable<List<(int Start, int Length)>> Calls(string masked)
    {
        foreach (Match m in CallStart().Matches(masked))
        {
            var name = m.Groups["name"].Value;
            if (NotCalls.Contains(name) || NotCalls.Contains(name.Split('.')[^1])) continue;
            if (Arguments(masked, m.Index + m.Length - 1) is { Count: > 0 } arguments && arguments.All(a => a.Length > 0)) yield return arguments;
        }
    }

    /// <summary>The top-level parts between the brackets that open at <paramref name="open"/> - or null when they do not close on the line.</summary>
    private static List<(int Start, int Length)>? Arguments(string masked, int open)
    {
        var parts = new List<(int Start, int Length)>();
        var depth = 0;
        var start = open + 1;

        for (var at = open; at < masked.Length; at++)
        {
            var c = masked[at];
            if (c is '(' or '[' or '{') { depth++; continue; }

            if (c is ')' or ']' or '}')
            {
                if (--depth == 0)
                {
                    if (at > start || parts.Count > 0) parts.Add(Trimmed(masked, start, at));
                    return parts;
                }

                continue;
            }

            if (c == ',' && depth == 1)
            {
                parts.Add(Trimmed(masked, start, at));
                start = at + 1;
            }
        }

        return null;
    }

    private static (int Start, int Length) Trimmed(string text, int from, int to)
    {
        while (from < to && char.IsWhiteSpace(text[from])) from++;
        while (to > from && char.IsWhiteSpace(text[to - 1])) to--;
        return (from, to - from);
    }

    /// <summary>A keyword argument - key=value - or one spread with *, which cannot change places or be counted on.</summary>
    private static bool IsNamedOrSpread(string argument, bool python) =>
        argument.StartsWith('*') || argument.StartsWith("...", StringComparison.Ordinal) ||
        python && Regex.IsMatch(argument, @"^[A-Za-z_]\w*\s*=(?!=)");

    /// <summary>What an argument off by one can be: a name, or a length.</summary>
    [GeneratedRegex(@"^(?:[A-Za-z_]\w*|len\([^()]*\)|[A-Za-z_][\w.]*\.(?:length|Length|Count|size\(\)))$")]
    private static partial Regex OneOffOperand();

    /// <summary>Each part of an index - [i], [i - 1, j] - that is a name on its own.</summary>
    private static IEnumerable<(int Start, int Length)> IndexParts(string masked)
    {
        for (var at = 1; at < masked.Length; at++)
        {
            if (masked[at] != '[' || !(char.IsLetterOrDigit(masked[at - 1]) || masked[at - 1] is '_' or ')' or ']')) continue;
            if (Arguments(masked, at) is not { } parts) continue;

            foreach (var part in parts.Where(p => p.Length > 0 && Regex.IsMatch(masked.Substring(p.Start, p.Length), @"^[A-Za-z_]\w*$")))
                yield return part;
        }
    }

    [GeneratedRegex(@"(?<![\w.])len\([^()]*\)(?!\s*[-+]\s*1(?![\w.]))")]
    private static partial Regex PythonLength();

    /// <summary>A length worked with - items.length, this.items.length, list.size() - not already one more or less.</summary>
    [GeneratedRegex(@"(?<![\w.$])[A-Za-z_$][\w$]*(?:\.[A-Za-z_$][\w$]*)*?(?:\.(?:length|Length|Count)(?![\w(])|\.size\(\))(?!\s*[-+]\s*1(?![\w.]))")]
    private static partial Regex Length();

    [GeneratedRegex(@"(?<![\w.])(?<name>any|all)(?=\s*\()")]
    private static partial Regex PythonAnyAll();

    [GeneratedRegex(@"\.(?<name>some|every|anyMatch|allMatch|Any|All)(?=\s*\()")]
    private static partial Regex AnyAll();

    [GeneratedRegex(@"\^=|&=|\|=")]
    private static partial Regex BitwiseCompound();

    [GeneratedRegex(@"(?<=[\w)\]]\s*)[\^&|](?![=\^&|])(?=\s*[\w(~-])")]
    private static partial Regex BitwiseBinary();

    /// <summary>name = a + b, or name += value, with nothing else added: the line, and where its two sides are.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)(?<target>[A-Za-z_$][\w$.]*)\s*(?<op>\+?=)(?!=)\s*(?<value>[^;]+?)\s*(?<end>;?)\s*$")]
    private static partial Regex Assignment();

    /// <summary>
    /// The value added at the wrong end: result = result + d made result = d + result, and result += d made result = d +
    /// result - text and lists are added in order, so which end matters.
    /// </summary>
    private static Mutation? WrongEnd(string line, string masked)
    {
        if (Assignment().Match(masked) is not { Success: true } m) return null;

        var target = m.Groups["target"].Value;
        var value = m.Groups["value"];
        var text = line.Substring(value.Index, value.Length);
        var before = line[..m.Groups["indent"].Length];
        var end = m.Groups["end"].Value;

        if (m.Groups["op"].Value == "+=")
            return Regex.IsMatch(text, @"^[^+]*$") ? new Mutation(0, $"{before}{target} = {text} + {target}{end}", $"{target} += {text}", $"{target} = {text} + {target}", "added at the wrong end", 3) : null;

        var plus = TopLevel(masked.Substring(value.Index, value.Length), '+');
        if (plus.Count != 1) return null;

        var (left, right) = (text[..plus[0]].Trim(), text[(plus[0] + 1)..].Trim());
        if (left != target && right != target) return null;

        return new Mutation(0, $"{before}{target} = {right} + {left}{end}", $"{left} + {right}", $"{right} + {left}", "added at the wrong end", 3);
    }

    /// <summary>Where <paramref name="sign"/> stands outside every bracket, as a binary operator: not ++, +=, or a sign before a number.</summary>
    private static List<int> TopLevel(string text, char sign)
    {
        var found = new List<int>();
        var depth = 0;

        for (var at = 0; at < text.Length; at++)
        {
            var c = text[at];
            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') depth--;
            else if (c == sign && depth == 0 && at > 0 && text[..at].Trim().Length > 0 && text[at - 1] != sign &&
                     (at + 1 >= text.Length || text[at + 1] is not ('=' or '+' or '-')))
                found.Add(at);
        }

        return found;
    }

    /// <summary>
    /// A running best replaced rather than kept, in a loop: name = value made name = max(name, value), name = min(name,
    /// value) and name = max(0, value) - the longest so far, and Kadane's best sum that never goes below nothing.
    /// </summary>
    private static IEnumerable<Mutation> KeptBest(SourceFile source, int number, string line, string masked, bool python)
    {
        if (Assignment().Match(masked) is not { Success: true } m || m.Groups["op"].Value != "=") yield break;

        var target = m.Groups["target"].Value;
        if (target.Contains('.') || !InALoop(source, number, python)) yield break;

        var value = line.Substring(m.Groups["value"].Index, m.Groups["value"].Length);
        if (Regex.IsMatch(value, @"^(?:Math\s*\.\s*)?(?:max|min|Max|Min)\s*\(") || !Regex.IsMatch(value, @"[+\-*]|\d")) yield break;

        var (largest, smallest) = Path.GetExtension(source.Path).ToLowerInvariant() switch
        {
            ".py" or ".pyw" => ("max", "min"),
            ".js" or ".mjs" or ".cjs" or ".ts" or ".java" => ("Math.max", "Math.min"),
            ".cs" => ("Math.Max", "Math.Min"),
            _ => (null, null),
        };
        if (largest is null) yield break;

        var before = line[..m.Groups["indent"].Length];
        var end = m.Groups["end"].Value;
        foreach (var (kept, to) in new[] { ($"{largest}({target}, {value})", "the largest so far"), ($"{smallest}({target}, {value})", "the smallest so far"), ($"{largest}(0, {value})", "never below nothing") })
            yield return new Mutation(number, $"{before}{target} = {kept}{end}", value, kept, $"a running best replaced rather than kept - {to}", 3);
    }

    /// <summary>Whether a line is inside a loop: below a for or while it is indented under, in Python; a for or while opened above it, otherwise.</summary>
    private static bool InALoop(SourceFile source, int number, bool python)
    {
        var line = source.Lines[number - 1];
        var indent = line.Length - line.TrimStart().Length;

        for (var above = number - 2; above >= 0 && above >= number - 40; above--)
        {
            var text = source.Lines[above];
            if (text.Trim().Length == 0) continue;
            var depth = text.Length - text.TrimStart().Length;

            if (python)
            {
                if (depth >= indent) continue;
                if (Regex.IsMatch(text, @"^\s*(?:async\s+)?(?:for|while)\b")) return true;
                if (Regex.IsMatch(text, @"^\s*(?:async\s+)?(?:def|class)\b")) return false;
                indent = depth;
            }
            else if (Regex.IsMatch(text, @"\b(?:for|while|foreach)\s*\(") && depth < indent)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The Python function a line is in: its name, its first line and its last.</summary>
    private static (string Name, int First, int Last, string Parameters)? Enclosing(SourceFile source, int number)
    {
        var line = source.Lines[number - 1];
        var indent = line.Length - line.TrimStart().Length;

        for (var above = number - 2; above >= 0; above--)
        {
            var text = source.Lines[above];
            if (text.Trim().Length == 0) continue;
            var depth = text.Length - text.TrimStart().Length;
            if (depth >= indent) continue;

            if (Regex.Match(text, @"^\s*(?:async\s+)?def\s+(?<name>\w+)\s*\((?<parameters>[^)]*)\)") is { Success: true } def)
            {
                var last = above + 1;
                while (last < source.Count && (source.Lines[last].Trim().Length == 0 || source.Lines[last].Length - source.Lines[last].TrimStart().Length > depth))
                    last++;
                return (def.Groups["name"].Value, above + 1, last, def.Groups["parameters"].Value);
            }

            indent = depth;
        }

        return null;
    }

    private static readonly HashSet<string> PythonWords = new(StringComparer.Ordinal)
    {
        "and", "or", "not", "in", "is", "if", "else", "elif", "for", "while", "return", "yield", "def", "class", "lambda", "None",
        "True", "False", "pass", "break", "continue", "import", "from", "as", "with", "try", "except", "finally", "raise", "del",
        "global", "nonlocal", "assert", "self", "await", "async",
    };

    /// <summary>
    /// The wrong variable: each of the function's own names on the line - its parameters, and what it assigns and loops
    /// over - changed for each of its others: enumerate(arr) where enumerate(counts) was meant. Not a name called, an
    /// attribute, or a keyword argument's name; on a line that assigns, only what it is given, not the name assigned.
    /// </summary>
    private static IEnumerable<Mutation> WrongVariable(SourceFile source, int number, string line, string masked)
    {
        if (Enclosing(source, number) is not { } function) yield break;

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in function.Parameters.Split(','))
        {
            var name = Regex.Match(parameter, @"^\s*\**(?<name>[A-Za-z_]\w*)").Groups["name"].Value;
            if (name.Length > 0) names.Add(name);
        }

        for (var at = function.First; at < function.Last && at < source.Count; at++)
        {
            var text = source.Lines[at];
            if (Regex.Match(text, @"^\s*(?<targets>[A-Za-z_][\w, ]*?)\s*(?:=|\+=|-=|\*=)(?!=)") is { Success: true } assigned)
                foreach (var name in assigned.Groups["targets"].Value.Split(',')) names.Add(name.Trim());
            if (Regex.Match(text, @"^\s*for\s+(?<targets>[\w, ()]+?)\s+in\b") is { Success: true } looped)
                foreach (var name in Regex.Matches(looped.Groups["targets"].Value, @"[A-Za-z_]\w*").Select(n => n.Value)) names.Add(name);
        }

        names.RemoveWhere(name => name.Length == 0 || PythonWords.Contains(name) || name == function.Name);
        if (names.Count is < 2 or > 12) yield break;

        var from = Assignment().Match(masked) is { Success: true } assignment && assignment.Groups["op"].Value == "=" ? assignment.Groups["value"].Index : 0;

        foreach (Match used in Regex.Matches(masked, @"(?<![\w.])[A-Za-z_]\w*(?![\w(])"))
        {
            if (used.Index < from || !names.Contains(used.Value)) continue;
            if (Regex.IsMatch(masked[(used.Index + used.Length)..], @"^\s*=(?!=)")) continue;

            foreach (var other in names.Where(other => other != used.Value).Order(StringComparer.Ordinal))
                yield return new Mutation(number, line[..used.Index] + other + line[(used.Index + used.Length)..], used.Value, other, "the wrong variable", 4);
        }
    }

    /// <summary>
    /// A generator's own call yielded as one item: yield flatten(x), which yields a generator, made yield from flatten(x) -
    /// its items - and yield x, what was given it.
    /// </summary>
    private static IEnumerable<Mutation> YieldedCall(SourceFile source, int number, string line, string masked)
    {
        if (Regex.Match(masked, @"^(?<before>\s*yield\s+)(?<name>\w+)\s*\(") is not { Success: true } m) yield break;
        if (Enclosing(source, number) is not { } function || function.Name != m.Groups["name"].Value) yield break;
        if (Arguments(masked, m.Index + m.Length - 1) is not { } arguments) yield break;

        var before = line[..m.Groups["before"].Length];
        var call = line[m.Groups["name"].Index..];
        yield return new Mutation(number, $"{before.TrimEnd()} from {call}", call, $"from {call}", "a generator's own call yielded as one item", 2);
        if (arguments.Count == 1)
            yield return new Mutation(number, before + line.Substring(arguments[0].Start, arguments[0].Length), call, line.Substring(arguments[0].Start, arguments[0].Length), "a generator's own call yielded as one item", 3);
    }

    private static IEnumerable<(int Start, int Length)> Bounds(string masked, bool python)
    {
        var operand = @"[A-Za-z_][\w.]*(?:\([^()]*\))?(?:\[[^\[\]]*\])?";

        foreach (Match m in Regex.Matches(masked, $@"(?:<=?|>=?)\s*(?<bound>{operand})(?=\s*(?:[;):,]|and\b|or\b|&&|\|\||$))"))
            yield return (m.Groups["bound"].Index, m.Groups["bound"].Length);

        if (python)
        {
            foreach (Match m in Regex.Matches(masked, $@"\brange\s*\((?:[^(),]+,\s*)?(?<bound>{operand})\s*[,)]"))
                yield return (m.Groups["bound"].Index, m.Groups["bound"].Length);
        }
    }
}
