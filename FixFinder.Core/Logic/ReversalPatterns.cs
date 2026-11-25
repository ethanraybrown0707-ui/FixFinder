using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A linked list reversed by a loop that points each node back at the one before it, but never moves on what the one before
/// is: prevnode starts as nothing and stays nothing, so every node is pointed back at nothing and the reversed list that is
/// given back is empty. QuixBugs' reverse_linked_list is this mistake, in Python and in Java. The change moves it on before
/// the loop goes to the next node - prevnode = node - as QuixBugs' correct program does.
/// </summary>
/// <remarks>
/// Said only when the file shows all of it: inside a loop, the next node is kept - nextnode = node.successor, following =
/// next[index] - and then the node's link is pointed at a name - node.successor = prevnode, node.setSuccessor(prevnode),
/// node-&gt;next = previous, $node-&gt;{next} = $previous, next[index] = previous; that name starts as nothing before the loop -
/// None, null, nil, undef, NULL, 0 for a list kept in arrays, or declared with no value - and nothing in the loop gives it
/// a value. A loop pointing every node at a head or a sentinel it was given points them at something, and is left alone.
/// </remarks>
public static partial class ReversalPatterns
{
    /// <summary>How one language is read, and moves the one before on to the node: {P} = {N}; for Rust, {P} = Some({N}).</summary>
    private sealed record Dialect(string Language, IReadOnlySet<string> Files, Syntax Syntax, string MoveOn, bool IgnoreCase = false);

    private static readonly Dialect[] Dialects =
    [
        new("python", CodePattern.Files(".py", ".pyw"), Syntax.Python, "{P} = {N}"),
        new("java", CodePattern.Files(".java"), Syntax.CLike, "{P} = {N};"),
        new("csharp", CodePattern.Files(".cs"), Syntax.CLike, "{P} = {N};"),
        new("js", CodePattern.Files(".js", ".mjs", ".cjs", ".ts", ".mts", ".cts"), Syntax.CLike, "{P} = {N};"),
        new("c", CodePattern.Files(".c"), Syntax.CLike, "{P} = {N};"),
        new("cpp", CodePattern.Files(".cpp", ".cc", ".cxx", ".c++"), Syntax.CLike, "{P} = {N};"),
        new("go", CodePattern.Files(".go"), Syntax.CLike, "{P} = {N}"),
        new("rust", CodePattern.Files(".rs"), Syntax.CLike, "{P} = Some({N});"),
        new("ruby", CodePattern.Files(".rb"), Syntax.Python, "{P} = {N}"),
        new("lua", CodePattern.Files(".lua"), Syntax.Lua, "{P} = {N}"),
        new("php", CodePattern.Files(".php"), Syntax.CLike, "{P} = {N};"),
        new("perl", CodePattern.Files(".pl", ".pm"), Syntax.Perl, "{P} = {N};"),
        new("powershell", CodePattern.Files(".ps1"), Syntax.Python, "{P} = {N}", IgnoreCase: true),
        new("dart", CodePattern.Files(".dart"), Syntax.CLike, "{P} = {N};"),
        new("scala", CodePattern.Files(".scala", ".sc"), Syntax.Scala, "{P} = {N}"),
        new("fortran", CodePattern.Files(".f90", ".f95", ".f03", ".f08", ".f"), Syntax.Fortran, "{P} = {N}", IgnoreCase: true),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        .. Dialects.Select(dialect => new CodePattern($"logic-{dialect.Language}-previous-never-moved", dialect.Files, dialect.Syntax,
            (id, source, masked) => PreviousNeverMoved(id, source, masked, dialect), Severity.Error, Confidence.Likely)),
    ];

    private const string Name = @"[$@]?[A-Za-z_]\w*";

    /// <summary>
    /// A node's link pointed at a name, alone on its line: node.successor = prevnode, node-&gt;next = previous,
    /// node.setSuccessor(prevnode), $node-&gt;{next} = $previous, next[index] = previous, next(node) = previous.
    /// </summary>
    [GeneratedRegex(@"^\s*(?:(?<node>[$@]?[A-Za-z_]\w*)\s*(?:\.|->)\s*(?:\{\s*(?<link>\w+)\s*\}|(?<link>[A-Za-z_]\w*))\s*=(?!=)\s*(?<previous>[$@]?[A-Za-z_]\w*)|(?<node>[$@]?[A-Za-z_]\w*)\s*\.\s*set(?<link>[A-Z]\w*)\s*\(\s*(?<previous>[$@]?[A-Za-z_]\w*)\s*\)|(?<link>[A-Za-z_]\w*)\s*[\[(]\s*(?<node>[$@]?[A-Za-z_]\w*)\s*[\])]\s*=(?!=)\s*(?<previous>[$@]?[A-Za-z_]\w*))\s*;?\s*$")]
    private static partial Regex PointedBack();

    private static IEnumerable<LogicFinding> PreviousNeverMoved(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        var options = dialect.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
        for (var line = 0; line < masked.Count; line++)
        {
            if (PointedBack().Match(masked[line]) is not { Success: true } pointed) continue;
            var node = pointed.Groups["node"].Value;
            var link = pointed.Groups["link"].Value;
            var previous = pointed.Groups["previous"].Value;
            if (string.Equals(node, previous, StringComparison.OrdinalIgnoreCase) || !Reaching.InALoop(masked, 0, line)) continue;
            if (Regex.IsMatch(previous, @"^(?:None|null|nil|undef|NULL|nullptr|\$null|true|false|True|False)$")) continue;

            var loop = LoopLines(masked, line);
            if (loop.Count == 0) continue;

            // The next node kept first: following = node.successor, nextnode = node.getSuccessor(), following = next[index].
            var nodeName = Regex.Escape(node);
            var linkName = Regex.Escape(link);
            // Declared as the language declares - var, let, my, struct node *, Node*, const following: ListNode | null - or not at all.
            var keptNext = new Regex($@"^\s*(?:(?:let|var|const|final|val|local|my|auto|mut|struct)\s+)*(?:[\w.<>?:]+[\s*&]+(?=[$@]?[A-Za-z_]\w*\s*(?::|=)))?(?<kept>{Name})\s*(?::(?!=)\s*[^=]+?)?\s*(?::=|=(?!=))\s*(?:{nodeName}\s*(?:\.|->)\s*(?:\{{\s*{linkName}\s*\}}|{linkName}\b(?!\s*\())|{nodeName}\s*\.\s*get{linkName}\s*\(\s*\)|{linkName}\s*[\[(]\s*{nodeName}\s*[\])])\s*;?\s*$", options);
            var kept = loop.Where(each => each < line).Select(each => keptNext.Match(masked[each])).FirstOrDefault(match => match.Success);
            if (kept is null) continue;
            var nextNode = kept.Groups["kept"].Value;

            // The one before starts as nothing, and nothing in the loop gives it a value.
            var previousName = Regex.Escape(previous);
            if (!StartsAsNothing(masked, loop[0], previousName, options)) continue;
            var givenInLoop = new Regex($@"(?<![\w$@.]){previousName}\s*(?::=|=(?!=)|=>)|,\s*{previousName}\s*(?:,|\)?\s*=(?!=))|\(\s*{previousName}\s*,", options);
            if (loop.Any(each => givenInLoop.IsMatch(masked[each]))) continue;

            // The line going to the next node: whatever the loop asks about given the kept next node - node = following.
            var advance = loop.Where(each => each > line).FirstOrDefault(each =>
                Regex.IsMatch(masked[each], $@"^\s*{Name}\s*(?::=|=(?!=))\s*{Regex.Escape(nextNode)}\s*;?\s*$", options), -1);
            var told = $"Line {line + 1} points `{node}` back at `{previous}`, but nothing in the loop moves `{previous}` on to `{node}`: it stays " +
                       $"what it started as, so every node is pointed back at nothing, and the reversed list `{previous}` gives back is empty.";
            yield return new LogicFinding(id, line + 1, told, advance < 0 ? null : MoveOn(id, source, advance, previous, node, dialect))
            {
                Severity = Severity.Error,
                Confidence = Confidence.Likely,
            };
        }
    }

    /// <summary>
    /// Whether the name is given nothing before the loop - None, null, nil, undef, NULL, nullptr, $null, 0 for a list kept in
    /// arrays - or declared with no value: Go's var previous *node, Dart's Node? previous;.
    /// </summary>
    private static bool StartsAsNothing(IReadOnlyList<string> masked, int loopStart, string previousName, RegexOptions options)
    {
        var givenNothing = new Regex($@"(?<![\w$@.]){previousName}\s*(?::\s*[\w<>?|\[\]. ]+?)?\s*(?::=|=(?!=))\s*(?:None|null|nil|undef|undefined|\$null|nullptr|NULL|0)\s*;?\s*$|\bvar\s+{previousName}\s+\*?[\w.]+\s*$|^\s*[\w<>.]+\?\s+{previousName}\s*;\s*$", options);
        for (var above = loopStart - 1; above >= 0 && above >= loopStart - 15; above--)
        {
            if (givenNothing.IsMatch(masked[above])) return true;
        }

        return false;
    }

    /// <summary>The lines of the loop the line is in, by indentation - its header's block.</summary>
    private static List<int> LoopLines(IReadOnlyList<string> masked, int line)
    {
        var indent = CodeText.Indentation(masked[line]).Length;
        for (var above = line - 1; above >= 0 && indent > 0; above--)
        {
            if (masked[above].Trim().Length == 0 || masked[above].Trim() == "{") continue;
            var width = CodeText.Indentation(masked[above]).Length;
            if (width >= indent) continue;
            indent = width;
            if (!Regex.IsMatch(masked[above], @"^\s*(?:\}\s*)?(?:while|for|do|loop|until|foreach)\b", RegexOptions.IgnoreCase)) continue;

            var lines = new List<int>();
            for (var below = above + 1; below < masked.Count; below++)
            {
                if (masked[below].Trim().Length == 0) continue;
                if (CodeText.Indentation(masked[below]).Length <= width && masked[below].Trim() != "{") break;
                lines.Add(below);
            }

            return lines;
        }

        return [];
    }

    /// <summary>The change: the one before moved on to the node, before the loop goes to the next one - prevnode = node.</summary>
    private static LocalFix MoveOn(string id, SourceFile source, int advance, string previous, string node, Dialect dialect)
    {
        var moved = dialect.MoveOn.Replace("{P}", previous).Replace("{N}", node);
        return LocalFix.Insert(id, $"Move on the one before: {moved}",
            $"`{previous}` is the reversed list so far: once `{node}` points back at it, `{node}` is its new first node, and the next node points back at that.",
            source.Path, advance + 1, [CodeText.Indentation(source.Lines[advance]) + moved]);
    }
}
