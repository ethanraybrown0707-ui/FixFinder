using System.Text.RegularExpressions;
using FixFinder.Core.Checking;

namespace FixFinder.Core.Analysis.Dynamic;

/// <summary>
/// The ifs, elifs, elses and whiles around a line of Python, in the function it is in - the decisions that had to go the way
/// they went for that line to run - read from the indentation, as Python reads it; and, from a run that recorded what the
/// call that stopped held as it reached each of them, the values their conditions read.
/// </summary>
public static partial class PythonBranches
{
    /// <summary>One decision around the line, before a run has said what its names held.</summary>
    /// <param name="WasTrue">What the line running means for it: true for the branch it is in, false for a condition above it in the same chain.</param>
    public sealed record Around(int Line, string Code, BranchKind Kind, bool WasTrue);

    /// <summary>
    /// The decisions around <paramref name="line"/> in the function it is in, outermost first, each chain's conditions in the
    /// order they are written. A header written over more than one line, and an else that is not an if's, are left out with
    /// the chain they belong to, as what was decided there is not read here.
    /// </summary>
    /// <param name="masked">The lines with strings and comments blanked.</param>
    public static IReadOnlyList<Around> Of(IReadOnlyList<string> lines, IReadOnlyList<string> masked, int line)
    {
        if (line < 1 || line > masked.Count) return [];

        var starts = StatementStarts(masked);
        var statement = line - 1;
        while (statement > 0 && !starts[statement]) statement--;

        var innermostFirst = new List<Around>();
        var inside = IndentOf(masked[statement]);

        for (var index = statement - 1; index >= 0 && inside > 0; index--)
        {
            if (!starts[index] || masked[index].Trim().Length == 0) continue;

            var indent = IndentOf(masked[index]);
            if (indent >= inside) continue;

            inside = indent;
            var header = masked[index].Trim();
            if (FunctionOrClass().IsMatch(header)) break;

            switch (KindOf(header))
            {
                case BranchKind.If or BranchKind.While:
                    innermostFirst.Add(new Around(index + 1, lines[index].Trim(), KindOf(header)!.Value, WasTrue: true));
                    break;

                case BranchKind.ElseIf or BranchKind.Else:
                    if (ChainAbove(lines, masked, starts, index, indent) is not { } above) break;
                    innermostFirst.Add(new Around(index + 1, lines[index].Trim(), KindOf(header)!.Value, WasTrue: true));
                    innermostFirst.AddRange(above);
                    break;
            }
        }

        innermostFirst.Reverse();
        return innermostFirst;
    }

    /// <summary>
    /// The decisions with what the names each condition reads held, from the values the call that stopped had as it reached
    /// each one - or null when it is not recorded as reaching one of them, as then the lines were not read the way it ran.
    /// </summary>
    public static IReadOnlyList<BranchTaken>? With(IReadOnlyList<Around> around, IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>> decided,
        IReadOnlyList<string> masked)
    {
        var taken = new List<BranchTaken>();

        foreach (var decision in around)
        {
            if (decision.Kind == BranchKind.Else)
            {
                taken.Add(new BranchTaken(decision.Line, decision.Code, decision.Kind, decision.WasTrue, []));
                continue;
            }

            if (!decided.TryGetValue(decision.Line, out var held)) return null;

            var values = NamesRead(masked[decision.Line - 1])
                .Where(held.ContainsKey)
                .Where(name => !AnAddress().IsMatch(held[name]))
                .Select(name => new HeldValue(name, held[name]))
                .ToList();
            taken.Add(new BranchTaken(decision.Line, decision.Code, decision.Kind, decision.WasTrue, values));
        }

        return taken;
    }

    /// <summary>The lines a run is to record the values at: every decision with a condition.</summary>
    public static IReadOnlyList<int> LinesToRecord(IReadOnlyList<Around> around) =>
        around.Where(decision => decision.Kind != BranchKind.Else).Select(decision => decision.Line).ToList();

    /// <summary>
    /// The conditions of the chain an elif or else at <paramref name="index"/> belongs to, above it - each false, as the branch
    /// below them ran - nearest first; null when the chain is not one of ifs and elifs written a line each.
    /// </summary>
    private static List<Around>? ChainAbove(IReadOnlyList<string> lines, IReadOnlyList<string> masked, bool[] starts, int index, int indent)
    {
        var above = new List<Around>();

        for (var earlier = index - 1; earlier >= 0; earlier--)
        {
            if (!starts[earlier] || masked[earlier].Trim().Length == 0) continue;

            var at = IndentOf(masked[earlier]);
            if (at > indent) continue;
            if (at < indent) return null;

            var header = masked[earlier].Trim();
            switch (KindOf(header))
            {
                case BranchKind.ElseIf:
                    above.Add(new Around(earlier + 1, lines[earlier].Trim(), BranchKind.ElseIf, WasTrue: false));
                    continue;
                case BranchKind.If:
                    above.Add(new Around(earlier + 1, lines[earlier].Trim(), BranchKind.If, WasTrue: false));
                    return above;
                default:
                    return null;
            }
        }

        return null;
    }

    /// <summary>The kind of decision a line written on one line is - if, elif, else or while, ending at its colon - or null for any other line.</summary>
    private static BranchKind? KindOf(string header) =>
        IfHeader().IsMatch(header) ? BranchKind.If
        : ElifHeader().IsMatch(header) ? BranchKind.ElseIf
        : ElseHeader().IsMatch(header) ? BranchKind.Else
        : WhileHeader().IsMatch(header) ? BranchKind.While
        : null;

    /// <summary>
    /// Which lines begin a statement: none inside brackets left open above, nor one after a line ending in a backslash -
    /// whose indentation says nothing about the blocks.
    /// </summary>
    private static bool[] StatementStarts(IReadOnlyList<string> masked)
    {
        var starts = new bool[masked.Count];
        var depth = 0;
        var carriedOn = false;

        for (var index = 0; index < masked.Count; index++)
        {
            starts[index] = depth == 0 && !carriedOn;

            foreach (var c in masked[index])
            {
                if (c is '(' or '[' or '{') depth++;
                else if (c is ')' or ']' or '}') depth = Math.Max(0, depth - 1);
            }

            var code = masked[index].TrimEnd();
            if (code.Length > 0) carriedOn = code.EndsWith('\\');
        }

        return starts;
    }

    private static int IndentOf(string line)
    {
        var indent = 0;
        while (indent < line.Length && line[indent] is ' ' or '\t') indent++;
        return indent;
    }

    /// <summary>The names a condition reads, in the order it reads them - not a function it calls, an attribute after a dot, or a word of Python's own.</summary>
    private static IEnumerable<string> NamesRead(string header) =>
        NameRead().Matches(header).Select(match => match.Value).Where(name => !Keywords.Contains(name)).Distinct(StringComparer.Ordinal);

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "if", "elif", "while", "and", "or", "not", "in", "is", "None", "True", "False", "lambda",
    };

    [GeneratedRegex(@"(?<![\w.])[A-Za-z_]\w*(?!\w|\s*\()")]
    private static partial Regex NameRead();

    [GeneratedRegex(@"^(?:async\s+)?(?:def|class)\b")]
    private static partial Regex FunctionOrClass();

    [GeneratedRegex(@"^if\b.*:$")]
    private static partial Regex IfHeader();

    [GeneratedRegex(@"^elif\b.*:$")]
    private static partial Regex ElifHeader();

    [GeneratedRegex(@"^else\s*:$")]
    private static partial Regex ElseHeader();

    [GeneratedRegex(@"^while\b.*:$")]
    private static partial Regex WhileHeader();

    /// <summary>How Python writes an object of a class with no repr of its own - &lt;__main__.Account object at 0x0000021F&gt; - which says nothing of what it holds.</summary>
    [GeneratedRegex(@"^<.+ at 0x[0-9A-Fa-f]+>$")]
    private static partial Regex AnAddress();
}
