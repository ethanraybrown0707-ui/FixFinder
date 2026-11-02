using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// The largest - or smallest - of a list, kept as a loop goes over it, started at 0 rather than at one of the list's own
/// items: highest = 0, then if t > highest: highest = t. With temperatures of -5, -2 and -8 it stays 0, a value the list
/// does not have, and the program runs on without a word; a lowest started at 0 stays 0 over marks that are all above it.
/// The change starts it at the list's first item. Checked for Python and Java.
/// </summary>
/// <remarks>
/// A possible mistake, not a likely one: over marks or ages, which are never below 0, a largest started at 0 gives the right
/// answer. Over QuixBugs' 326 Python and Java programs it found nothing - none of them keeps a largest or smallest started
/// at 0, and their counters started at 0 (depth = 0, then if depth &lt; 0) are not taken for one.
/// </remarks>
public static partial class RunningBestPatterns
{
    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        new CodePattern("logic-python-best-starts-at-zero", CodePattern.Files(".py", ".pyw"), Syntax.Python, PythonBestStartsAtZero, Severity.Warning, Confidence.Possible),
        new CodePattern("logic-java-best-starts-at-zero", CodePattern.Files(".java"), Syntax.CLike, JavaBestStartsAtZero, Severity.Warning, Confidence.Possible),
    ];

    /// <summary>How many lines below where the best is started its loop is looked for.</summary>
    private const int MostLinesToTheLoop = 6;

    /// <summary>How many lines of the loop are looked through for the if that keeps the best.</summary>
    private const int MostLinesOfTheLoop = 8;

    [GeneratedRegex(@"^(?<indent>\s*)(?<name>[A-Za-z_]\w*)\s*=\s*0(?:\.0)?\s*$")]
    private static partial Regex PythonStartsAtZero();

    [GeneratedRegex(@"^\s*for\s+(?<item>[A-Za-z_]\w*)\s+in\s+(?<items>[A-Za-z_][\w.]*)\s*:\s*$")]
    private static partial Regex PythonLoopOverItems();

    [GeneratedRegex(@"^\s*for\s+(?<index>[A-Za-z_]\w*)\s+in\s+range\(\s*len\(\s*(?<items>[A-Za-z_][\w.]*)\s*\)\s*\)\s*:\s*$")]
    private static partial Regex PythonLoopOverPositions();

    [GeneratedRegex(@"^\s*if\s+(?<left>[\w.\[\]]+)\s*(?<op>>=?|<=?)\s*(?<right>[\w.\[\]]+)\s*:")]
    private static partial Regex PythonComparison();

    [GeneratedRegex(@"^(?<indent>\s*)(?<type>int|long|short|double|float)\s+(?<name>[A-Za-z_]\w*)\s*=\s*0(?:\.0)?[dDfFlL]?\s*;\s*$")]
    private static partial Regex JavaStartsAtZero();

    [GeneratedRegex(@"^\s*for\s*\(\s*(?:final\s+)?[\w.<>\[\]]+\s+(?<item>[A-Za-z_]\w*)\s*:\s*(?<items>[A-Za-z_][\w.]*)\s*\)")]
    private static partial Regex JavaLoopOverItems();

    [GeneratedRegex(@"^\s*for\s*\(\s*int\s+(?<index>[A-Za-z_]\w*)\s*=\s*0\s*;\s*\k<index>\s*<\s*(?<items>[A-Za-z_][\w.]*)\.length\s*;")]
    private static partial Regex JavaLoopOverPositions();

    [GeneratedRegex(@"\bif\s*\(\s*(?<left>[\w.\[\]]+)\s*(?<op>>=?|<=?)\s*(?<right>[\w.\[\]]+)\s*\)")]
    private static partial Regex JavaComparison();

    private static IEnumerable<LogicFinding> PythonBestStartsAtZero(string id, SourceFile source, IReadOnlyList<string> masked) =>
        BestStartsAtZero(id, source, masked, PythonStartsAtZero(), PythonLoopOverItems(), PythonLoopOverPositions(), PythonComparison(),
            (started, items) => $"{started.Groups["indent"].Value}{started.Groups["name"].Value} = {items}[0]", "a list");

    private static IEnumerable<LogicFinding> JavaBestStartsAtZero(string id, SourceFile source, IReadOnlyList<string> masked) =>
        BestStartsAtZero(id, source, masked, JavaStartsAtZero(), JavaLoopOverItems(), JavaLoopOverPositions(), JavaComparison(),
            (started, items) => $"{started.Groups["indent"].Value}{started.Groups["type"].Value} {started.Groups["name"].Value} = {items}[0];", "an array");

    /// <summary>
    /// A name started at 0, a loop over a list just below it, and in the loop an if that compares an item with the name and
    /// then gives the name that item: the largest when the item is kept for being bigger, the smallest for being smaller.
    /// </summary>
    private static IEnumerable<LogicFinding> BestStartsAtZero(
        string id, SourceFile source, IReadOnlyList<string> masked, Regex startsAtZero, Regex loopOverItems, Regex loopOverPositions, Regex comparison,
        Func<Match, string, string> startedAtFirstItem, string kindOfList)
    {
        for (var start = 0; start < masked.Count; start++)
        {
            if (startsAtZero.Match(masked[start]) is not { Success: true } started) continue;
            var name = started.Groups["name"].Value;

            for (var loopLine = start + 1; loopLine < Math.Min(masked.Count, start + 1 + MostLinesToTheLoop); loopLine++)
            {
                // Given another value on the way to the loop, it does not start the loop at 0.
                if (Regex.IsMatch(masked[loopLine], $@"(?<![\w.]){Regex.Escape(name)}\s*[-+*/]?=(?!=)")) break;

                var (items, item) = loopOverItems.Match(masked[loopLine]) is { Success: true } overItems
                    ? (overItems.Groups["items"].Value, overItems.Groups["item"].Value)
                    : loopOverPositions.Match(masked[loopLine]) is { Success: true } overPositions
                        ? (overPositions.Groups["items"].Value, $"{overPositions.Groups["items"].Value}[{overPositions.Groups["index"].Value}]")
                        : (null, null);
                if (items is null || item is null) continue;

                if (KeptBest(masked, loopLine, name, item, comparison) is not { } keeps) break;

                var corrected = startedAtFirstItem(started, items) + source.Lines[start][masked[start].TrimEnd().Length..];
                var (best, wrongWhen) = keeps == "largest" ? ("largest", "every one of them is below 0") : ("smallest", "every one of them is above 0");

                yield return new LogicFinding(id, start + 1,
                    $"`{name}` keeps the {best} item of `{items}`, and starts at 0 rather than at one of its items - so when {wrongWhen}, it " +
                    $"stays 0, a value `{items}` does not have",
                    LocalFix.ReplaceLine(id, $"Start {name} at {items}[0]",
                        $"Started at the first item, `{name}` is one of `{items}`'s values from the start, and the loop then keeps the {best} of " +
                        $"them. It needs {kindOfList} with at least one item.",
                        source.Path, start + 1, corrected));
                break;
            }
        }
    }

    /// <summary>"largest" or "smallest", for an if in the loop that compares the item with the name and then gives the name the item; null for none.</summary>
    private static string? KeptBest(IReadOnlyList<string> masked, int loopLine, string name, string item, Regex comparison)
    {
        for (var line = loopLine + 1; line < Math.Min(masked.Count, loopLine + 1 + MostLinesOfTheLoop); line++)
        {
            if (comparison.Match(masked[line]) is not { Success: true } compared) continue;

            var (left, op, right) = (compared.Groups["left"].Value, compared.Groups["op"].Value, compared.Groups["right"].Value);
            var bigger = (left == item && right == name && op.StartsWith('>')) || (left == name && right == item && op.StartsWith('<'));
            var smaller = (left == item && right == name && op.StartsWith('<')) || (left == name && right == item && op.StartsWith('>'));
            if (!bigger && !smaller) return null;

            // The item is kept, on the line under the if or on the same line after it.
            var kept = $@"(?<![\w.]){Regex.Escape(name)}\s*=(?!=)\s*{Regex.Escape(item)}\s*;?\s*(?:\}}\s*)?$";
            var keptOnIt = Regex.IsMatch(masked[line], kept);
            var keptUnderIt = Enumerable.Range(line + 1, Math.Min(2, masked.Count - line - 1)).Any(under => Regex.IsMatch(masked[under], kept));

            return keptOnIt || keptUnderIt ? bigger ? "largest" : "smallest" : null;
        }

        return null;
    }
}
