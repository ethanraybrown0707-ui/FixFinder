using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// The largest - or smallest - of a list, kept as a loop goes over it, started at 0 rather than at one of the list's own
/// items: highest = 0, then if t > highest: highest = t. With temperatures of -5, -2 and -8 it stays 0, a value the list
/// does not have, and the program runs on without a word; a lowest started at 0 stays 0 over marks that are all above it.
/// The change starts it at the list's first item. One check, read in each language's own words from the table below, and in
/// Elixir's Enum.reduce and Erlang's lists:foldl, whose 0 is where the fold starts.
/// </summary>
/// <remarks>
/// A possible mistake, not a likely one: over marks or ages, which are never below 0, a largest started at 0 gives the right
/// answer. Over QuixBugs' 326 Python and Java programs it found nothing - none of them keeps a largest or smallest started
/// at 0, and their counters started at 0 (depth = 0, then if depth &lt; 0) are not taken for one.
/// </remarks>
public static partial class RunningBestPatterns
{
    /// <summary>How one language writes what this check reads.</summary>
    /// <param name="Id">The check's id for the language.</param>
    /// <param name="StartsAtZero">A name given 0 - its indent, its name and the 0 itself, the part the change replaces.</param>
    /// <param name="OverItems">A loop over a list's items: the item's name and the list's.</param>
    /// <param name="OverPositions">A loop over a list's positions from its first: the position's name.</param>
    /// <param name="FirstItem">The list's first item, as the language writes it, from the list's name in its loop.</param>
    /// <param name="FirstPosition">0, or 1 for a language whose positions start at 1.</param>
    /// <param name="KindOfList">What the language calls the list, for the change's explanation.</param>
    private sealed record Dialect(
        string Id, IReadOnlySet<string> Files, Syntax Syntax, Regex StartsAtZero, Regex OverItems, Regex OverPositions,
        Func<string, string> FirstItem, int FirstPosition, string KindOfList);

    private static readonly Dialect[] Dialects =
    [
        new("logic-python-best-starts-at-zero", CodePattern.Files(".py", ".pyw"), Syntax.Python,
            new(@"^(?<indent>\s*)(?<name>[A-Za-z_]\w*)\s*=\s*(?<zero>0(?:\.0)?)\s*$"),
            new(@"^\s*for\s+(?<item>[A-Za-z_]\w*)\s+in\s+(?<items>[A-Za-z_][\w.]*)\s*:\s*$"),
            new(@"^\s*for\s+(?<index>[A-Za-z_]\w*)\s+in\s+range\(\s*len\(\s*[A-Za-z_][\w.]*\s*\)\s*\)\s*:\s*$"),
            items => $"{items}[0]", 0, "a list"),
        new("logic-java-best-starts-at-zero", CodePattern.Files(".java"), Syntax.CLike,
            new(@"^(?<indent>\s*)(?:int|long|short|double|float)\s+(?<name>[A-Za-z_]\w*)\s*=\s*(?<zero>0(?:\.0)?[dDfFlL]?)\s*;\s*$"),
            new(@"^\s*for\s*\(\s*(?:final\s+)?[\w.<>\[\]]+\s+(?<item>[A-Za-z_]\w*)\s*:\s*(?<items>[A-Za-z_][\w.]*)\s*\)"),
            new(@"^\s*for\s*\(\s*int\s+(?<index>[A-Za-z_]\w*)\s*=\s*0\s*;"),
            items => $"{items}[0]", 0, "an array"),
        new("logic-best-starts-at-zero", CodePattern.Files(".c", ".cpp", ".cc", ".cxx", ".c++", ".cs", ".js", ".mjs", ".cjs"), Syntax.CLike,
            new(@"^(?<indent>\s*)(?:const\s+)?(?:int|long|short|double|float|decimal|auto|var|let)\s+(?<name>[A-Za-z_]\w*)(?:\s*:\s*number)?\s*=\s*(?<zero>0(?:\.0)?[fFlLmM]?)\s*;?\s*$"),
            new(@"^\s*(?:for\s*\(\s*(?:const\s+)?(?:auto|int|long|double|float)\s*&?\s*(?<item>[A-Za-z_]\w*)\s*:\s*(?<items>[A-Za-z_][\w.]*)\s*\)" +
                @"|foreach\s*\(\s*(?:var|int|long|double|float|decimal)\s+(?<item>[A-Za-z_]\w*)\s+in\s+(?<items>[A-Za-z_][\w.]*)\s*\)" +
                @"|for\s*\(\s*(?:const|let|var)\s+(?<item>[A-Za-z_]\w*)\s+of\s+(?<items>[A-Za-z_][\w.]*)\s*\))"),
            new(@"^\s*for\s*\(\s*(?:(?:int|long|size_t|unsigned|var|let|auto)\s+)?(?<index>[A-Za-z_]\w*)\s*=\s*0\s*;"),
            items => $"{items}[0]", 0, "an array"),
        new("logic-go-best-starts-at-zero", CodePattern.Files(".go"), Syntax.CLike,
            new(@"^(?<indent>\s*)(?:var\s+)?(?<name>[A-Za-z_]\w*)(?:\s+(?:int|int64|float64))?\s*:?=\s*(?<zero>0(?:\.0)?)\s*$"),
            new(@"^\s*for\s+\w+\s*,\s*(?<item>[A-Za-z_]\w*)\s*:=\s*range\s+(?<items>[A-Za-z_][\w.]*)\s*\{"),
            new(@"^\s*for\s+(?<index>[A-Za-z_]\w*)\s*:=\s*0\s*;"),
            items => $"{items}[0]", 0, "a slice"),
        new("logic-rust-best-starts-at-zero", CodePattern.Files(".rs"), Syntax.CLike,
            new(@"^(?<indent>\s*)let\s+mut\s+(?<name>[A-Za-z_]\w*)(?:\s*:\s*\w+)?\s*=\s*(?<zero>0(?:\.0)?(?:_?[iuf]\d+)?)\s*;\s*$"),
            new(@"^\s*for\s+&?(?<item>[A-Za-z_]\w*)\s+in\s+&?(?<items>[A-Za-z_]\w*)(?:\.iter\(\))?\s*\{"),
            new(@"^\s*for\s+(?<index>[A-Za-z_]\w*)\s+in\s+0\s*\.\."),
            items => $"{items}[0]", 0, "a vector"),
        new("logic-ruby-best-starts-at-zero", CodePattern.Files(".rb"), Syntax.Python,
            new(@"^(?<indent>\s*)(?<name>[a-z_]\w*)\s*=\s*(?<zero>0(?:\.0)?)\s*$"),
            new(@"^\s*(?:(?<items>[a-z_][\w.]*)\.each\s+do\s*\|\s*(?<item>[a-z_]\w*)\s*\||for\s+(?<item>[a-z_]\w*)\s+in\s+(?<items>[a-z_][\w.]*))"),
            new(@"^\s*\(\s*0\s*\.{2,3}[^)]*\)\.each\s+do\s*\|\s*(?<index>[a-z_]\w*)\s*\|"),
            items => $"{items}[0]", 0, "an array"),
        new("logic-php-best-starts-at-zero", CodePattern.Files(".php"), Syntax.CLike,
            new(@"^(?<indent>\s*)(?<name>\$[A-Za-z_]\w*)\s*=\s*(?<zero>0(?:\.0)?)\s*;\s*$"),
            new(@"^\s*foreach\s*\(\s*(?<items>\$[A-Za-z_]\w*)\s+as\s+(?<item>\$[A-Za-z_]\w*)\s*\)"),
            new(@"^\s*for\s*\(\s*(?<index>\$[A-Za-z_]\w*)\s*=\s*0\s*;"),
            items => $"{items}[0]", 0, "an array"),
        new("logic-lua-best-starts-at-zero", CodePattern.Files(".lua"), Syntax.Lua,
            new(@"^(?<indent>\s*)(?:local\s+)?(?<name>[A-Za-z_]\w*)\s*=\s*(?<zero>0(?:\.0)?)\s*$"),
            new(@"^\s*for\s+\w+\s*,\s*(?<item>[A-Za-z_]\w*)\s+in\s+i?pairs\(\s*(?<items>[A-Za-z_][\w.]*)\s*\)\s*do\b"),
            new(@"^\s*for\s+(?<index>[A-Za-z_]\w*)\s*=\s*1\s*,"),
            items => $"{items}[1]", 1, "a table"),
        new("logic-perl-best-starts-at-zero", CodePattern.Files(".pl", ".pm"), Syntax.Perl,
            new(@"^(?<indent>\s*)(?:my\s+)?(?<name>\$[A-Za-z_]\w*)\s*=\s*(?<zero>0(?:\.0)?)\s*;\s*$"),
            new(@"^\s*(?:foreach|for)\s+my\s+(?<item>\$[A-Za-z_]\w*)\s*\(\s*(?<items>@[A-Za-z_]\w*)\s*\)"),
            new(@"^\s*(?:for\s*\(\s*my\s+(?<index>\$[A-Za-z_]\w*)\s*=\s*0\s*;|(?:foreach|for)\s+my\s+(?<index>\$[A-Za-z_]\w*)\s*\(\s*0\s*\.\.)"),
            items => $"${items[1..]}[0]", 0, "an array"),
        new("logic-dart-best-starts-at-zero", CodePattern.Files(".dart"), Syntax.CLike,
            new(@"^(?<indent>\s*)(?:var|int|double|num)\s+(?<name>[A-Za-z_]\w*)\s*=\s*(?<zero>0(?:\.0)?)\s*;\s*$"),
            new(@"^\s*for\s*\(\s*(?:var|final|int|double|num)\s+(?<item>[A-Za-z_]\w*)\s+in\s+(?<items>[A-Za-z_][\w.]*)\s*\)"),
            new(@"^\s*for\s*\(\s*(?:var|int)\s+(?<index>[A-Za-z_]\w*)\s*=\s*0\s*;"),
            items => $"{items}[0]", 0, "a list"),
        new("logic-fortran-best-starts-at-zero", CodePattern.Files(".f90", ".f95", ".f03", ".f08"), Syntax.Fortran,
            new(@"^(?<indent>\s*)(?<name>[A-Za-z_]\w*)\s*=\s*(?<zero>0(?:\.0)?(?:[dD]0)?)\s*$"),
            new(@"(?!)"),
            new(@"^\s*do\s+(?<index>[A-Za-z_]\w*)\s*=\s*1\s*,", RegexOptions.IgnoreCase),
            items => $"{items}(1)", 1, "an array"),
        new("logic-powershell-best-starts-at-zero", CodePattern.Files(".ps1"), Syntax.Python,
            new(@"^(?<indent>\s*)(?<name>\$[A-Za-z_]\w*)\s*=\s*(?<zero>0(?:\.0)?)\s*$"),
            new(@"^\s*foreach\s*\(\s*(?<item>\$[A-Za-z_]\w*)\s+in\s+(?<items>\$[A-Za-z_]\w*)\s*\)", RegexOptions.IgnoreCase),
            new(@"^\s*for\s*\(\s*(?<index>\$[A-Za-z_]\w*)\s*=\s*0\s*;", RegexOptions.IgnoreCase),
            items => $"{items}[0]", 0, "an array"),
        new("logic-fsharp-best-starts-at-zero", CodePattern.Files(".fsx", ".fs"), Syntax.FSharp,
            new(@"^(?<indent>\s*)let\s+mutable\s+(?<name>[a-z_]\w*)\s*=\s*(?<zero>0(?:\.0)?)\s*$"),
            new(@"^\s*for\s+(?<item>[a-z_]\w*)\s+in\s+(?<items>[a-z_][\w.]*)\s+do\s*$"),
            new(@"^\s*for\s+(?<index>[a-z_]\w*)\s+in\s+0\s*\.\."),
            items => $"{items}.[0]", 0, "a list"),
        new("logic-scala-best-starts-at-zero", CodePattern.Files(".scala", ".sc"), Syntax.Scala,
            new(@"^(?<indent>\s*)var\s+(?<name>[a-z_]\w*)(?:\s*:\s*\w+)?\s*=\s*(?<zero>0(?:\.0)?)\s*$"),
            new(@"^\s*for\s*\(?\s*(?<item>[a-z_]\w*)\s*<-\s*(?<items>[a-z_][\w.]*)\s*\)?\s*(?:do\b|\{|$)"),
            new(@"^\s*for\s*\(?\s*(?<index>[a-z_]\w*)\s*<-\s*0\s+(?:until|to)\b"),
            items => $"{items}(0)", 0, "a list"),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        .. Dialects.Select(dialect => new CodePattern(dialect.Id, dialect.Files, dialect.Syntax,
            (id, source, masked) => BestStartsAtZero(id, source, masked, dialect), Severity.Warning, Confidence.Possible)),
        new CodePattern("logic-elixir-best-starts-at-zero", CodePattern.Files(".exs", ".ex"), Syntax.Python,
            (id, source, masked) => FoldStartsAtZero(id, source, masked, ElixirReduce(), "Enum.reduce"), Severity.Warning, Confidence.Possible),
        new CodePattern("logic-erlang-best-starts-at-zero", CodePattern.Files(".erl"), Syntax.Erlang,
            (id, source, masked) => FoldStartsAtZero(id, source, masked, ErlangFoldl(), "lists:foldl"), Severity.Warning, Confidence.Possible),
    ];

    /// <summary>How many lines below where the best is started its loop is looked for.</summary>
    private const int MostLinesToTheLoop = 6;

    /// <summary>How many lines of the loop are looked through for the if that keeps the best.</summary>
    private const int MostLinesOfTheLoop = 8;

    /// <summary>
    /// An if comparing two things - t > highest, $t -gt $highest, temperatures(i) .gt. highest, *t > highest - in any of the
    /// languages: a name, its fields, an item at a position, and Rust's * before a borrowed item.
    /// </summary>
    [GeneratedRegex(@"\bif\s*\(?\s*(?<left>\*?[$@]?[A-Za-z_]\w*(?:\.\w+)*(?:\.?\[\s*\$?\w+\s*\]|\(\s*\$?\w+\s*\))?)\s*(?<op>>=|<=|>|<|-[gl][te]\b|\.[gl][te]\.)\s*(?<right>\*?[$@]?[A-Za-z_]\w*(?:\.\w+)*(?:\.?\[\s*\$?\w+\s*\]|\(\s*\$?\w+\s*\))?)", RegexOptions.IgnoreCase)]
    private static partial Regex Comparison();

    /// <summary>Elixir's Enum.reduce(temperatures, 0, fn t, best -> if t > best, do: t, else: best end).</summary>
    [GeneratedRegex(@"Enum\.reduce\(\s*(?<items>[a-z_]\w*)\s*,\s*(?<zero>0(?:\.0)?)\s*,\s*fn\s+(?<item>[a-z_]\w*)\s*,\s*(?<best>[a-z_]\w*)\s*->\s*if\s+(?<left>[a-z_]\w*)\s*(?<op>>=?|<=?)\s*(?<right>[a-z_]\w*)\s*,\s*do:\s*(?<kept>[a-z_]\w*)")]
    private static partial Regex ElixirReduce();

    /// <summary>Erlang's lists:foldl(fun(T, Best) when T > Best -> T; (_, Best) -> Best end, 0, Temperatures).</summary>
    [GeneratedRegex(@"lists:foldl\(\s*fun\s*\(\s*(?<item>[A-Z]\w*)\s*,\s*(?<best>[A-Z]\w*)\s*\)\s*when\s+(?<left>[A-Z]\w*)\s*(?<op>>=?|=<|<)\s*(?<right>[A-Z]\w*)\s*->\s*(?<kept>[A-Z]\w*)\s*;.*?\bend\s*,\s*(?<zero>0(?:\.0)?)\s*,\s*(?<items>[A-Z]\w*)\s*\)")]
    private static partial Regex ErlangFoldl();

    /// <summary>
    /// A name started at 0, a loop over a list just below it, and in the loop an if that compares an item with the name and
    /// then gives the name that item: the largest when the item is kept for being bigger, the smallest for being smaller.
    /// </summary>
    private static IEnumerable<LogicFinding> BestStartsAtZero(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        for (var start = 0; start < masked.Count; start++)
        {
            if (dialect.StartsAtZero.Match(masked[start]) is not { Success: true } started) continue;
            var name = started.Groups["name"].Value;

            for (var loopLine = start + 1; loopLine < Math.Min(masked.Count, start + 1 + MostLinesToTheLoop); loopLine++)
            {
                // Given another value on the way to the loop, it does not start the loop at 0.
                if (Regex.IsMatch(masked[loopLine], $@"(?<![\w$.]){Regex.Escape(name)}\s*(?:[-+*/]?=|<-|:=)(?!=)")) break;

                var overItems = dialect.OverItems.Match(masked[loopLine]);
                var overPositions = dialect.OverPositions.Match(masked[loopLine]);
                if (!overItems.Success && !overPositions.Success) continue;

                // Over items, the list is the loop's; over positions, it is the one the item at the position is taken from.
                var kept = overItems.Success
                    ? KeptBest(masked, loopLine, name, overItems.Groups["item"].Value, index: null) is { } keptFromItems
                        ? keptFromItems with { Items = overItems.Groups["items"].Value, FirstItem = dialect.FirstItem(overItems.Groups["items"].Value) }
                        : null
                    : KeptBest(masked, loopLine, name, item: null, index: overPositions.Groups["index"].Value) is { } keptAtPosition
                        ? keptAtPosition with { FirstItem = FirstAt(keptAtPosition.Item, overPositions.Groups["index"].Value, dialect.FirstPosition) }
                        : null;

                if (kept is not null) yield return Finding(id, source, start, started.Groups["zero"], name, kept, dialect.KindOfList);
                break;
            }
        }
    }

    /// <summary>What the loop keeps: "largest" or "smallest", the item it is kept from, the list, and the list's first item.</summary>
    private sealed record Kept(string Best, string Item, string Items, string FirstItem);

    /// <summary>
    /// The if in the loop that compares the item with the name and then gives the name the item - an item named in the
    /// loop, or, for a loop over positions, the item at that position - or null for none.
    /// </summary>
    private static Kept? KeptBest(IReadOnlyList<string> masked, int loopLine, string name, string? item, string? index)
    {
        for (var line = loopLine + 1; line < Math.Min(masked.Count, loopLine + 1 + MostLinesOfTheLoop); line++)
        {
            if (Comparison().Match(masked[line]) is not { Success: true } compared) continue;

            var (left, op, right) = (compared.Groups["left"].Value.TrimStart('*'), compared.Groups["op"].Value.ToLowerInvariant(), compared.Groups["right"].Value.TrimStart('*'));
            var other = left == name ? right : right == name ? left : null;
            if (other is null) return null;

            // The other side is the loop's item, or the item at the loop's position: temperatures[i], $marks[$i], scores(i).
            string items;
            if (item is not null)
            {
                if (other != item) return null;
                items = "";
            }
            else if (Regex.Match(other, $@"^(?<items>[$@]?[A-Za-z_]\w*(?:\.\w+)*)\.?(?:\[\s*{Regex.Escape(index!)}\s*\]|\(\s*{Regex.Escape(index!)}\s*\))$") is { Success: true } element)
            {
                items = element.Groups["items"].Value;
            }
            else return null;

            var itemIsBigger = op.Contains('>') || op is "-gt" or "-ge" or ".gt." or ".ge.";
            var bigger = other == left ? itemIsBigger : !itemIsBigger;

            // The item is kept, on the line under the if or on the same line - after it, or before it as Ruby's modifier puts it.
            var kept = $@"(?<![\w$.]){Regex.Escape(name)}\s*(?:=|<-|:=)(?!=)\s*\*?{Regex.Escape(other)}(?=\s*(?:;|\}}|$|\bif\b|\bend\b))";
            var keptOnIt = Regex.IsMatch(masked[line], kept);
            var keptUnderIt = Enumerable.Range(line + 1, Math.Min(2, masked.Count - line - 1)).Any(under => Regex.IsMatch(masked[under], kept));

            return keptOnIt || keptUnderIt ? new Kept(bigger ? "largest" : "smallest", other, items, "") : null;
        }

        return null;
    }

    /// <summary>The item at a list's first position, from the item at the loop's: temperatures[i] gives temperatures[0], scores(i) scores(1).</summary>
    private static string FirstAt(string itemAtPosition, string index, int firstPosition) =>
        Regex.Replace(itemAtPosition, $@"(?<=[\[(]\s*){Regex.Escape(index)}(?=\s*[\])])", firstPosition.ToString(), RegexOptions.None, TimeSpan.FromSeconds(1));

    private static LogicFinding Finding(string id, SourceFile source, int start, Group zero, string name, Kept kept, string kindOfList)
    {
        var items = kept.Items.Length > 0 ? kept.Items : Regex.Replace(kept.Item, @"\.?[\[(].*$", "");
        var line = source.Lines[start];
        var corrected = line[..zero.Index] + kept.FirstItem + line[(zero.Index + zero.Length)..];
        var wrongWhen = kept.Best == "largest" ? "every one of them is below 0" : "every one of them is above 0";

        return new LogicFinding(id, start + 1,
            $"`{name}` keeps the {kept.Best} item of `{items}`, and starts at 0 rather than at one of its items - so when {wrongWhen}, it " +
            $"stays 0, a value `{items}` does not have",
            LocalFix.ReplaceLine(id, $"Start {name} at {kept.FirstItem}",
                $"Started at the first item, `{name}` is one of `{items}`'s values from the start, and the loop then keeps the {kept.Best} of " +
                $"them. It needs {kindOfList} with at least one item.",
                source.Path, start + 1, corrected));
    }

    /// <summary>
    /// A fold that keeps the largest or smallest item and starts from 0 rather than from the list's first item:
    /// Enum.reduce(temperatures, 0, fn t, best -> if t > best ...) or lists:foldl(fun(T, Best) when T > Best -> T ...).
    /// The change starts it from hd(temperatures), the list's first item.
    /// </summary>
    private static IEnumerable<LogicFinding> FoldStartsAtZero(string id, SourceFile source, IReadOnlyList<string> masked, Regex fold, string foldName)
    {
        for (var line = 0; line < masked.Count; line++)
        {
            if (fold.Match(masked[line]) is not { Success: true } folded) continue;

            var (item, best, left, right, keptName) = (folded.Groups["item"].Value, folded.Groups["best"].Value, folded.Groups["left"].Value,
                folded.Groups["right"].Value, folded.Groups["kept"].Value);
            if (keptName != item) continue;

            var itemIsBigger = folded.Groups["op"].Value.Contains('>');
            bool? bigger = left == item && right == best ? itemIsBigger : left == best && right == item ? !itemIsBigger : null;
            if (bigger is not { } keepsBigger) continue;

            var items = folded.Groups["items"].Value;
            var zero = folded.Groups["zero"];
            var text = source.Lines[line];
            var firstItem = $"hd({items})";
            var kept = keepsBigger ? "largest" : "smallest";
            var wrongWhen = keepsBigger ? "every one of them is below 0" : "every one of them is above 0";

            yield return new LogicFinding(id, line + 1,
                $"{foldName} keeps the {kept} item of `{items}`, and starts from 0 rather than from one of its items - so when {wrongWhen}, " +
                $"it gives 0, a value `{items}` does not have",
                LocalFix.ReplaceLine(id, $"Start the fold from {firstItem}",
                    $"Started from the first item, the fold has one of `{items}`'s values from the start, and keeps the {kept} of them. It " +
                    "needs a list with at least one item.",
                    source.Path, line + 1, text[..zero.Index] + firstItem + text[(zero.Index + zero.Length)..]));
        }
    }
}
