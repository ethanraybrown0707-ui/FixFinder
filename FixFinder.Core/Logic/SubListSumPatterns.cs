using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// The largest sum of a run of items in a list, worked out in one pass - Kadane's way - that never starts the run again. The
/// pass keeps the sum of the best run ending at each item, max_ending_here, and the largest of those, max_so_far. A run whose
/// sum has fallen below nothing only lowers whatever follows it, so the best run ending at the next item starts after it:
/// max_ending_here = max(0, max_ending_here + x). Adding each item and never starting again, max_ending_here is the sum of
/// every item so far, and max_so_far the largest sum of the list's first items - not of any run of them: 4 for 4, -5, 2, 1,
/// -1, 3, where the run 2, 1, -1, 3 makes 5. QuixBugs' max_sublist_sum is this mistake, in Python and in Java.
/// </summary>
/// <remarks>
/// Said only when the file shows all of it: a line in a loop adds the loop's item to a sum - E = E + x, E += items[i] -
/// another line of that loop keeps the largest of the sum - B = max(B, E), or B = E under a test of E against B - and
/// nothing in the loop, or in a loop around it, gives the sum another value or asks whether it is below nothing. The highest
/// point of a running balance is written the same way, and is right, so the names must say what is worked out: the
/// function's or one of the two, max_sublist_sum, maxSubArray, max_ending_here, kadane, contiguous. Trying every start in a
/// loop around, with the sum made 0 for each, is left alone.
/// </remarks>
public static partial class SubListSumPatterns
{
    /// <summary>
    /// How one language is read, and how its change starts the run again: a line with the language's max put in its place,
    /// or lines after it that make the sum 0 when it is below nothing - for a language without a max of its own.
    /// </summary>
    /// <param name="Maximum">The adding line rewritten, with {sum}, {item} and {zero} in it - or null.</param>
    /// <param name="Restart">The lines put after the adding line, with {sum} in them, when there is no max.</param>
    private sealed record Dialect(string Language, IReadOnlySet<string> Files, Syntax Syntax, string? Maximum, string[] Restart, bool Semicolons, bool IgnoreCase = false);

    private static readonly Dialect[] Dialects =
    [
        new("python", CodePattern.Files(".py", ".pyw"), Syntax.Python, "{sum} = max(0, {sum} + {item})", [], Semicolons: false),
        new("java", CodePattern.Files(".java"), Syntax.CLike, "{sum} = Math.max(0, {sum} + {item});", [], Semicolons: true),
        new("csharp", CodePattern.Files(".cs"), Syntax.CLike, "{sum} = Math.Max(0, {sum} + {item});", [], Semicolons: true),
        new("js", CodePattern.Files(".js", ".mjs", ".cjs", ".ts", ".mts", ".cts"), Syntax.CLike, "{sum} = Math.max(0, {sum} + {item});", [], Semicolons: true),

        // C has no max; C++'s std::max is <algorithm>'s and wants both of one type; Go's came with Go 1.21.
        new("c", CodePattern.Files(".c"), Syntax.CLike, null, ["if ({sum} < 0) {sum} = 0;"], Semicolons: true),
        new("cpp", CodePattern.Files(".cpp", ".cc", ".cxx", ".c++"), Syntax.CLike, null, ["if ({sum} < 0) {sum} = 0;"], Semicolons: true),
        new("go", CodePattern.Files(".go"), Syntax.CLike, null, ["if {sum} < 0 {", "\t{sum} = 0", "}"], Semicolons: false),
        new("rust", CodePattern.Files(".rs"), Syntax.CLike, "{sum} = ({sum} + {item}).max({zero});", [], Semicolons: true),
        new("ruby", CodePattern.Files(".rb"), Syntax.Python, "{sum} = [0, {sum} + {item}].max", [], Semicolons: false),
        new("lua", CodePattern.Files(".lua"), Syntax.Lua, "{sum} = math.max(0, {sum} + {item})", [], Semicolons: false),
        new("php", CodePattern.Files(".php"), Syntax.CLike, "{sum} = max(0, {sum} + {item});", [], Semicolons: true),

        // Perl's max is List::Util's, and Dart's dart:math's - each to be brought in first.
        new("perl", CodePattern.Files(".pl", ".pm"), Syntax.Perl, null, ["{sum} = 0 if {sum} < 0;"], Semicolons: true),
        new("powershell", CodePattern.Files(".ps1"), Syntax.Python, "{sum} = [Math]::Max(0, {sum} + {item})", [], Semicolons: false, IgnoreCase: true),
        new("dart", CodePattern.Files(".dart"), Syntax.CLike, null, ["if ({sum} < 0) {sum} = 0;"], Semicolons: true),
        new("scala", CodePattern.Files(".scala", ".sc"), Syntax.Scala, "{sum} = math.max(0, {sum} + {item})", [], Semicolons: false),

        // Fortran's max wants both of one type - gfortran refused max(0, total + 2.0) for a real total - so the sum is started
        // again by a test, whatever its type.
        new("fortran", CodePattern.Files(".f90", ".f95", ".f03", ".f08", ".f"), Syntax.Fortran, null, ["if ({sum} < 0) {sum} = 0"], Semicolons: false, IgnoreCase: true),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        .. Dialects.Select(dialect => new CodePattern($"logic-{dialect.Language}-sum-never-started-again", dialect.Files, dialect.Syntax,
            (id, source, masked) => SumsNeverStartedAgain(id, source, masked, dialect), Severity.Error, Confidence.Likely)),
    ];

    /// <summary>A name saying a run of items is summed: sublist, sub_array, subseq, kadane, contiguous, ending_here.</summary>
    [GeneratedRegex(@"sub_?(?:list|array|arr|seq|sequence|range|segment|vector)|kadane|contiguous|ending_?here", RegexOptions.IgnoreCase)]
    private static partial Regex RunNamed();

    /// <summary>A line giving a name a value, =, := or +=: the name, and where what it is given starts.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)(?:(?:let|var|val|local|my)\s+)?(?:mut\s+)?(?<sum>[$@]?[A-Za-z_]\w*)\s*(?<op>\+=|:=|=)(?!=)\s*")]
    private static partial Regex Given();

    /// <summary>An item: a name, or one picked from a list by a name - items[i], items(i), $items[$i].</summary>
    [GeneratedRegex(@"^[$@]?[A-Za-z_]\w*(?:\s*[\[(]\s*[$@]?[A-Za-z_]\w*\s*[\])])?$")]
    private static partial Regex Item();

    /// <summary>A max: max(...), Math.max, std::max, math.max, [Math]::Max, .max(...), [a, b].max.</summary>
    [GeneratedRegex(@"\bmax\b", RegexOptions.IgnoreCase)]
    private static partial Regex Maximum();

    /// <summary>A comparison: &lt;, &gt;, &lt;=, &gt;=, -gt, -lt, .gt., .lt. - not -&gt;, =&gt;, &lt;&lt; or &gt;&gt;.</summary>
    [GeneratedRegex(@"(?<![-=<>])[<>](?![<>])|-[gl][te]\b|\.[gl][te]\.", RegexOptions.IgnoreCase)]
    private static partial Regex Compares();

    private static IEnumerable<LogicFinding> SumsNeverStartedAgain(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        if (!masked.Any(line => RunNamed().IsMatch(line))) yield break;
        var options = dialect.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
        var functions = SortingPatterns.FunctionsIn(masked, Path.GetExtension(source.Path).ToLowerInvariant());

        for (var line = 0; line < masked.Count; line++)
        {
            if (Adding(masked[line], options) is not { } adding) continue;
            if (Reaching.InnermostLoop(masked, 0, line) is not { } loop) continue;
            var body = BlockUnder(masked, loop.Opening);

            // The item is the loop's: its name, or the name picking it, is in the loop's heading.
            var heading = masked[loop.Heading];
            if (!Regex.Matches(adding.Item, @"[A-Za-z_]\w*").Any(word => Named(word.Value, options).IsMatch(heading))) continue;

            var sum = Named(adding.Sum, options);
            if (Kept(masked, body, line, adding.Sum, sum, options) is not { } best) continue;
            if (StartedAgain(masked, body, line, adding.Sum, sum, options)) continue;
            if (GivenInAnOuterLoop(masked, line, body, adding.Sum, options)) continue;

            var function = functions.FirstOrDefault(each => each.Body.Contains(line));
            if (!RunNamed().IsMatch(adding.Sum) && !RunNamed().IsMatch(best) && !RunNamed().IsMatch(function.Name ?? "")) continue;

            var told = $"Line {line + 1} adds each item to `{adding.Sum}`, the sum of the best run of items ending at this one, and `{best}` keeps the " +
                       $"largest of it - but `{adding.Sum}` is never started again. A run whose sum has fallen below nothing only lowers what follows " +
                       $"it, so the best run ending at the next item starts after it; as written `{adding.Sum}` is the sum of every item so far, and " +
                       $"`{best}` the largest sum of the list's first items, not of any run of them.";
            yield return new LogicFinding(id, line + 1, told, StartAgain(id, source, masked, dialect, line, adding));
        }
    }

    /// <summary>The line adding an item to a sum - E = E + x, E = x + E, E += x: the sum and the item, with where the item is on the line.</summary>
    private sealed record Addition(string Indent, string Sum, string Item, int ItemAt);

    private static Addition? Adding(string text, RegexOptions options)
    {
        if (!text.Contains('+') || Given().Match(text) is not { Success: true } given) return null;
        var sum = given.Groups["sum"].Value;
        var rest = text[given.Length..].TrimEnd();
        if (rest.EndsWith(';')) rest = rest[..^1].TrimEnd();
        var restAt = given.Length;

        if (given.Groups["op"].Value == "+=")
            return Item().IsMatch(rest) && !Same(rest, sum, options) ? new Addition(given.Groups["indent"].Value, sum, rest, restAt) : null;

        var sumFirst = Regex.Match(rest, $@"^{Regex.Escape(sum)}\s*\+\s*", options);
        if (sumFirst.Success && Item().IsMatch(rest[sumFirst.Length..]) && !Same(rest[sumFirst.Length..], sum, options))
            return new Addition(given.Groups["indent"].Value, sum, rest[sumFirst.Length..], restAt + sumFirst.Length);

        var sumLast = Regex.Match(rest, $@"\s*\+\s*{Regex.Escape(sum)}$", options);
        if (sumLast.Success && Item().IsMatch(rest[..sumLast.Index]) && !Same(rest[..sumLast.Index], sum, options))
            return new Addition(given.Groups["indent"].Value, sum, rest[..sumLast.Index], restAt);

        return null;
    }

    private static bool Same(string first, string second, RegexOptions options) =>
        string.Equals(first, second, options.HasFlag(RegexOptions.IgnoreCase) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>The name where it is written whole: not part of another name, nor a member of something - current, not current_best or self.current.</summary>
    private static Regex Named(string name, RegexOptions options) =>
        new($@"(?<![\w$@.])[$@]*{Regex.Escape(name.TrimStart('$', '@'))}(?!\w)", options);

    /// <summary>The lines of the block under a heading - those more indented than it, until one that is not.</summary>
    private static List<int> BlockUnder(IReadOnlyList<string> masked, int opening)
    {
        var lines = new List<int>();
        var indent = CodeText.Indentation(masked[opening]).Length;
        for (var at = opening + 1; at < masked.Count; at++)
        {
            if (masked[at].Trim().Length == 0) continue;
            if (CodeText.Indentation(masked[at]).Length <= indent) break;
            lines.Add(at);
        }

        return lines;
    }

    /// <summary>
    /// The name keeping the largest of the sum in the loop - best = max(best, current), or best = current under a test of the
    /// two, on its line or the one above - or null.
    /// </summary>
    private static string? Kept(IReadOnlyList<string> masked, List<int> body, int adding, string sumName, Regex sum, RegexOptions options)
    {
        foreach (var line in body.Where(line => line != adding))
        {
            var text = masked[line];
            if (!sum.IsMatch(text)) continue;

            // best = max(best, current), best = best.max(current), best = std::max(best, current).
            if (Given().Match(text) is { Success: true } given && given.Groups["op"].Value == "=" && !Same(given.Groups["sum"].Value, sumName, options))
            {
                var value = text[given.Length..];
                var best = given.Groups["sum"].Value;
                if (Maximum().IsMatch(value) && sum.IsMatch(value) && Named(best, options).IsMatch(value)) return best;
            }

            // best = current under a test of the two: if current > best: best = current, $best = $current if $current > $best.
            foreach (Match kept in Regex.Matches(text, $@"(?<![\w$@.])(?<best>[$@]?[A-Za-z_]\w*)\s*=(?!=)\s*[$@]?{Regex.Escape(sumName.TrimStart('$', '@'))}\s*(?:;|\}}|$|\bif\b)", options))
            {
                var best = kept.Groups["best"].Value;
                if (Same(best, sumName, options)) continue;
                var above = line - 1;
                while (above >= 0 && masked[above].Trim() is "" or "{") above--;
                if (Tests(text, best, sum, options) || above >= 0 && body.Contains(above) && Tests(masked[above], best, sum, options)) return best;
            }
        }

        return null;
    }

    /// <summary>Whether the text compares the sum with the best: current &gt; best, $current -gt $best.</summary>
    private static bool Tests(string text, string best, Regex sum, RegexOptions options) =>
        Compares().IsMatch(text) && sum.IsMatch(text) && Named(best, options).IsMatch(text) && Regex.IsMatch(text, @"(?<![\w$@.-])(?:if|unless)\b", RegexOptions.IgnoreCase);

    /// <summary>Whether the loop gives the sum another value besides the adding - current = 0, current = max(...) - or asks whether it is below nothing.</summary>
    private static bool StartedAgain(IReadOnlyList<string> masked, List<int> body, int adding, string sumName, Regex sum, RegexOptions options)
    {
        var givenAgain = new Regex($@"(?<![\w$@.])[$@]?{Regex.Escape(sumName.TrimStart('$', '@'))}\s*(?:[-+*/]?=|:=)(?!=)", options);
        var belowNothing = new Regex($@"(?<![\w$@.])[$@]?{Regex.Escape(sumName.TrimStart('$', '@'))}\s*(?:<=?|-l[te]\b|\.l[te]\.)\s*0(?![\w.])|(?<![\w.])0\s*(?:>=?|-g[te]\b|\.g[te]\.)\s*[$@]?{Regex.Escape(sumName.TrimStart('$', '@'))}(?!\w)", options);
        return body.Any(line => line != adding && sum.IsMatch(masked[line]) && (givenAgain.IsMatch(masked[line]) || belowNothing.IsMatch(masked[line])));
    }

    /// <summary>
    /// Whether a line outside the loop, inside a loop around it, gives the sum a value - each start tried, int current = 0
    /// before the loop over the run - or declares it there.
    /// </summary>
    private static bool GivenInAnOuterLoop(IReadOnlyList<string> masked, int adding, List<int> body, string sumName, RegexOptions options)
    {
        var given = new Regex($@"(?<![\w$@.])[$@]?{Regex.Escape(sumName.TrimStart('$', '@'))}\s*(?::=|=)(?!=)", options);
        for (var line = 0; line < masked.Count; line++)
        {
            if (line == adding || body.Contains(line) || !given.IsMatch(masked[line])) continue;
            if (Reaching.InALoop(masked, 0, line)) return true;
        }

        return false;
    }

    /// <summary>The change: the run started again when its sum falls below nothing - with the language's max, or a test after the adding.</summary>
    private static LocalFix StartAgain(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect, int line, Addition adding)
    {
        var text = source.Lines[line];
        var item = text.Substring(adding.ItemAt, adding.Item.Length);
        var zero = Regex.IsMatch(string.Join("\n", masked), $@"(?<![\w$@.]){Regex.Escape(adding.Sum)}\s*(?::\s*f(?:32|64)\s*)?=\s*-?\d+\.\d|(?<![\w$@.]){Regex.Escape(adding.Sum)}\s*:\s*f(?:32|64)\b") ? "0.0" : "0";

        if (dialect.Maximum is { } maximum)
        {
            var written = maximum.Replace("{sum}", adding.Sum).Replace("{item}", item).Replace("{zero}", zero);
            return LocalFix.ReplaceLine(id, $"Start the run again when it falls below nothing: {written.TrimEnd(';')}",
                $"A run whose sum is below nothing only lowers what follows it; starting again at nothing - an empty run - keeps `{adding.Sum}` the sum " +
                "of the best run ending at each item.",
                source.Path, line + 1, adding.Indent + written);
        }

        var restart = dialect.Restart.Select(each => adding.Indent + each.Replace("{sum}", adding.Sum)).ToList();
        return new LocalFix
        {
            RuleId = id,
            Title = $"Start the run again when it falls below nothing: {restart[0].Trim()}",
            Explanation = $"A run whose sum is below nothing only lowers what follows it; starting again at nothing - an empty run - keeps `{adding.Sum}` " +
                          "the sum of the best run ending at each item.",
            File = source.Path,
            StartLine = line + 1,
            RemoveCount = 1,
            NewLines = [text, .. restart],
        };
    }
}
