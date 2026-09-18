using System.Text.RegularExpressions;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// Changes to the order of an if / else-if chain: each later branch moved to the front.
/// </summary>
/// <remarks>
/// A chain stops at the first condition that is true, so a condition that is a special case of an earlier one never gets its
/// turn - FizzBuzz testing <c>n % 3</c> before <c>n % 15</c> prints "Fizz" for 15. No single-token edit fixes that; moving the
/// branch does. Only chains of plain conditions are reordered - Python's <c>if</c>/<c>elif</c>, and the C-like
/// <c>} else if (...) {</c> layout - and, like every other change, only one that makes every run right is offered.
/// </remarks>
public static partial class BranchOrder
{
    private sealed record Branch(string? Condition, int Header, int End);

    [GeneratedRegex(@"^(?<lead>\s*)if\s+(?<condition>.+):\s*$")]
    private static partial Regex PythonIf();

    [GeneratedRegex(@"^(?<lead>\s*)(?:(?<keyword>elif)\s+(?<condition>.+)|(?<keyword>else)\s*):\s*$")]
    private static partial Regex PythonNext();

    [GeneratedRegex(@"^(?<lead>\s*)if\s*\((?<condition>.*)\)\s*\{\s*$")]
    private static partial Regex CLikeIf();

    [GeneratedRegex(@"^\s*\}\s*else\s+if\s*\((?<condition>.*)\)\s*\{\s*$")]
    private static partial Regex CLikeElseIf();

    [GeneratedRegex(@"^\s*\}\s*else\s*\{\s*$")]
    private static partial Regex CLikeElse();

    /// <summary>Every reordering of every chain that touches one of <paramref name="lines"/>.</summary>
    public static IEnumerable<LocalFix> For(SourceFile source, IReadOnlySet<int> lines)
    {
        var python = Path.GetExtension(source.Path).Equals(".py", StringComparison.OrdinalIgnoreCase);
        var masked = CodeText.MaskAll(source.Lines, python ? Syntax.Python : Syntax.CLike);

        for (var i = 0; i < masked.Count; i++)
        {
            var chain = python ? PythonChain(source.Lines, masked, i) : CLikeChain(source.Lines, masked, i);
            if (chain is not { } found) continue;

            var (lead, branches) = found;
            var conditional = branches.Where(b => b.Condition is not null).ToList();
            if (conditional.Count < 2) continue;

            var start = branches[0].Header;
            var end = branches[^1].End;
            if (!Enumerable.Range(start + 1, end - start + 1).Any(lines.Contains)) continue;

            for (var move = 1; move < conditional.Count; move++)
            {
                var order = conditional.Take(move).Prepend(conditional[move]).Concat(conditional.Skip(move + 1)).ToList();
                var rebuilt = python ? RebuildPython(source.Lines, lead, order, branches) : RebuildCLike(source.Lines, lead, order, branches);
                var condition = conditional[move].Condition!;

                yield return new LocalFix
                {
                    RuleId = "logic-edit",
                    Title = $"Test {condition.Trim()} first",
                    Explanation =
                        $"The branch for `{condition.Trim()}` comes after one whose condition is also true whenever it is, and a chain stops at the " +
                        "first true condition - so it never gets its turn. Tested first, the more particular case is reached.",
                    File = source.Path, StartLine = start + 1, RemoveCount = end - start + 1, NewLines = rebuilt,
                };
            }
        }
    }

    // ------------------------------------------------------------------ Python

    private static (string Lead, List<Branch> Branches)? PythonChain(IReadOnlyList<string> lines, IReadOnlyList<string> masked, int at)
    {
        if (PythonIf().Match(masked[at]) is not { Success: true } first) return null;

        var lead = first.Groups["lead"].Value;
        var branches = new List<Branch>();
        var header = at;
        string? condition = lines[at].Substring(first.Groups["condition"].Index, first.Groups["condition"].Length);

        while (true)
        {
            var end = header + 1;
            while (end < lines.Count && (masked[end].Trim().Length == 0 || CodeText.Indentation(masked[end]).Length > lead.Length)) end++;
            var last = end - 1;
            while (last > header && masked[last].Trim().Length == 0) last--;

            branches.Add(new Branch(condition, header, last));

            if (condition is null || end >= lines.Count || CodeText.Indentation(masked[end]) != lead) break;
            if (PythonNext().Match(masked[end]) is not { Success: true } next) break;

            header = end;
            condition = next.Groups["keyword"].Value == "elif"
                ? lines[end].Substring(next.Groups["condition"].Index, next.Groups["condition"].Length)
                : null;
        }

        return (lead, branches);
    }

    private static List<string> RebuildPython(IReadOnlyList<string> lines, string lead, IReadOnlyList<Branch> order, IReadOnlyList<Branch> branches)
    {
        var result = new List<string>();

        for (var i = 0; i < order.Count; i++)
        {
            result.Add($"{lead}{(i == 0 ? "if" : "elif")} {order[i].Condition!.Trim()}:");
            result.AddRange(Enumerable.Range(order[i].Header + 1, order[i].End - order[i].Header).Select(k => lines[k]));
        }

        foreach (var otherwise in branches.Where(b => b.Condition is null))
        {
            result.Add($"{lead}else:");
            result.AddRange(Enumerable.Range(otherwise.Header + 1, otherwise.End - otherwise.Header).Select(k => lines[k]));
        }

        return result;
    }

    // ------------------------------------------------------------------ C, C++, Java, C#, JavaScript, Go-style braces

    private static (string Lead, List<Branch> Branches)? CLikeChain(IReadOnlyList<string> lines, IReadOnlyList<string> masked, int at)
    {
        if (CLikeIf().Match(masked[at]) is not { Success: true } first) return null;

        // Not the else-if of a chain that started further up.
        if (at > 0 && Regex.IsMatch(masked[at], @"^\s*\}")) return null;

        var lead = first.Groups["lead"].Value;
        var branches = new List<Branch>();
        var header = at;
        string? condition = lines[at].Substring(first.Groups["condition"].Index, first.Groups["condition"].Length);

        while (true)
        {
            if (Close(masked, header) is not { } close) return null;

            branches.Add(new Branch(condition, header, close));

            if (CLikeElseIf().Match(masked[close]) is { Success: true } elseIf)
            {
                header = close;
                condition = lines[close].Substring(elseIf.Groups["condition"].Index, elseIf.Groups["condition"].Length);
                continue;
            }

            if (CLikeElse().IsMatch(masked[close]))
            {
                header = close;
                condition = null;
                continue;
            }

            // The chain ends at a line holding only the closing brace; anything else there is a layout this does not rebuild.
            if (masked[close].Trim() != "}") return null;
            break;
        }

        // Each branch's range runs to the line that closes it, which is the next branch's header; the body is in between.
        return (lead, branches);
    }

    /// <summary>The line whose brace closes the block opened at the end of <paramref name="header"/>.</summary>
    private static int? Close(IReadOnlyList<string> masked, int header)
    {
        var depth = 1;

        for (var i = header + 1; i < masked.Count; i++)
        {
            foreach (var c in masked[i])
            {
                if (c == '{') depth++;
                else if (c == '}' && --depth == 0) return i;
            }
        }

        return null;
    }

    private static List<string> RebuildCLike(IReadOnlyList<string> lines, string lead, IReadOnlyList<Branch> order, IReadOnlyList<Branch> branches)
    {
        var result = new List<string>();

        for (var i = 0; i < order.Count; i++)
        {
            result.Add(i == 0 ? $"{lead}if ({order[i].Condition!.Trim()}) {{" : $"{lead}}} else if ({order[i].Condition!.Trim()}) {{");
            result.AddRange(Enumerable.Range(order[i].Header + 1, order[i].End - order[i].Header - 1).Select(k => lines[k]));
        }

        foreach (var otherwise in branches.Where(b => b.Condition is null))
        {
            result.Add($"{lead}}} else {{");
            result.AddRange(Enumerable.Range(otherwise.Header + 1, otherwise.End - otherwise.Header - 1).Select(k => lines[k]));
        }

        result.Add(lines[branches[^1].End]);
        return result;
    }
}
