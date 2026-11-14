using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// The best found so far - the longest, the largest, the smallest - written over inside a loop under a test that does not
/// make sure the new value is better: longest = length + 1 when length == longest or val &lt; arr[ends[length + 1]]. The first
/// half of that test makes length + 1 longer than longest; the second does not, so a shorter length can take the place of the
/// longest found, and the answer comes out too small. QuixBugs' lis is this mistake, in Python and in Java. The change keeps
/// the better of the two: longest = max(longest, length + 1), as QuixBugs' correct program has it.
/// </summary>
/// <remarks>
/// Said only for a name that says it is a best - longest, max_len, largest, highest, smallest, min_cost, best - that the file
/// compares somewhere, given a value that is not a number alone, not already the larger or smaller of two, and not written
/// from the name itself; not for a name of where the best is - max_index, best_item - which max() would not keep. The value is
/// better when a test around the line, inside the loop, says so in every one of its or's: the value against the name -
/// length + 1 &gt; longest - the value's measure against the name's - len(word) &gt; len(longest) - or the value one step on from
/// something no smaller - length == longest - or an else whose test says the value is no better, turned round.
/// </remarks>
public static partial class BestOverwrittenPatterns
{
    /// <summary>How one language is read, and how it writes the larger and the smaller of two - {B} and {X}.</summary>
    private sealed record Dialect(string Language, IReadOnlySet<string> Files, Syntax Syntax, string Larger, string Smaller,
        bool ColonEnds = false, bool IgnoreCase = false);

    private static readonly Dialect[] Dialects =
    [
        new("python", CodePattern.Files(".py", ".pyw"), Syntax.Python, "max({B}, {X})", "min({B}, {X})", ColonEnds: true),
        new("java", CodePattern.Files(".java"), Syntax.CLike, "Math.max({B}, {X})", "Math.min({B}, {X})"),
        new("csharp", CodePattern.Files(".cs"), Syntax.CLike, "Math.Max({B}, {X})", "Math.Min({B}, {X})"),
        new("js", CodePattern.Files(".js", ".mjs", ".cjs", ".ts", ".mts", ".cts"), Syntax.CLike, "Math.max({B}, {X})", "Math.min({B}, {X})"),
        new("c", CodePattern.Files(".c"), Syntax.CLike, "{X} > {B} ? {X} : {B}", "{X} < {B} ? {X} : {B}"),
        new("cpp", CodePattern.Files(".cpp", ".cc", ".cxx", ".c++"), Syntax.CLike, "std::max({B}, {X})", "std::min({B}, {X})"),
        new("go", CodePattern.Files(".go"), Syntax.CLike, "max({B}, {X})", "min({B}, {X})"),
        new("rust", CodePattern.Files(".rs"), Syntax.CLike, "{B}.max({X})", "{B}.min({X})"),
        new("ruby", CodePattern.Files(".rb"), Syntax.Python, "[{B}, {X}].max", "[{B}, {X}].min"),
        new("lua", CodePattern.Files(".lua"), Syntax.Lua, "math.max({B}, {X})", "math.min({B}, {X})"),
        new("php", CodePattern.Files(".php"), Syntax.CLike, "max({B}, {X})", "min({B}, {X})"),
        new("perl", CodePattern.Files(".pl", ".pm"), Syntax.Perl, "{X} > {B} ? {X} : {B}", "{X} < {B} ? {X} : {B}"),
        new("powershell", CodePattern.Files(".ps1"), Syntax.Python, "[math]::Max({B}, {X})", "[math]::Min({B}, {X})", IgnoreCase: true),
        new("dart", CodePattern.Files(".dart"), Syntax.CLike, "max({B}, {X})", "min({B}, {X})"),
        new("scala", CodePattern.Files(".scala", ".sc"), Syntax.Scala, "math.max({B}, {X})", "math.min({B}, {X})"),
        new("fortran", CodePattern.Files(".f90", ".f95", ".f03", ".f08", ".f"), Syntax.Fortran, "max({B}, {X})", "min({B}, {X})", IgnoreCase: true),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        .. Dialects.Select(dialect => new CodePattern($"logic-{dialect.Language}-best-overwritten", dialect.Files, dialect.Syntax,
            (id, source, masked) => BestsOverwritten(id, source, masked, dialect), Severity.Error, Confidence.Likely)),
    ];

    /// <summary>Which way a best goes: larger, smaller, or either, as best does.</summary>
    private enum Way { Larger, Smaller, Either }

    /// <summary>A name that says it is a best - its words, without _ and case: longest, maxlen, largestsofar, mincost, best.</summary>
    [GeneratedRegex(@"^(?:(?<larger>longest|largest|highest|biggest|greatest|tallest|maximum|max|most)|(?<smaller>shortest|smallest|lowest|least|fewest|cheapest|minimum|min)|(?<either>best))|(?:(?<larger>longest|largest|highest|biggest|greatest|tallest|maximum|max)|(?<smaller>shortest|smallest|lowest|least|fewest|cheapest|minimum|min)|(?<either>best))$")]
    private static partial Regex BestWords();

    /// <summary>A name of where a best is, or of what has it - max_index, best_item, longest_word - which max() would not keep.</summary>
    [GeneratedRegex(@"index|idx|pos|key|item|node|word|name|id$|at$|line|row|col|path|str|text|list|arr|set|map", RegexOptions.IgnoreCase)]
    private static partial Regex WhereOrWhat();

    /// <summary>A name given a value on a line of its own: longest = length + 1, $longest = ..., int best = ...; not +=, not ==.</summary>
    [GeneratedRegex(@"^\s*(?<declared>(?:let|var|const|local|my|our|val|auto|int|long|double|float|integer|usize|Integer|Long|Double)\s+(?:mut\s+)?)?(?<best>[$@]?[A-Za-z_]\w*)\s*(?<sign>:=|(?<![+\-*/%<>!=&|^.])=(?!=))\s*(?<value>[^;]*[^;\s])\s*(?:;\s*)?$")]
    private static partial Regex Given();

    /// <summary>What a best's name has in it, to pass over at once a line that gives no best a value.</summary>
    [GeneratedRegex(@"longest|largest|highest|biggest|greatest|tallest|max|most|shortest|smallest|lowest|least|fewest|cheapest|min|best", RegexOptions.IgnoreCase)]
    private static partial Regex BestStem();

    /// <summary>A test's word: if, elif, else if, elsif, elseif, unless.</summary>
    [GeneratedRegex(@"(?<![\w$.@-])(?:else\s+if|elseif|elsif|elif|if|unless)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TestWord();

    /// <summary>A line heading a loop: for, foreach, while, do, until, .each, .times, loop.</summary>
    [GeneratedRegex(@"^\s*(?:\}\s*)?(?:for|foreach|while|do|until|loop)\b|\.each(?:_with_index)?\b|\.times\b", RegexOptions.IgnoreCase)]
    private static partial Regex LoopHeader();

    /// <summary>A value that already takes the better of two, or chooses: max(, min(, .max, ::Max, MAX(, a ternary's ?.</summary>
    [GeneratedRegex(@"(?i:\bmax\w*\s*\(|\bmin\w*\s*\(|\.max\b|\.min\b|::max\b|::min\b)|\?")]
    private static partial Regex AlreadyChosen();

    /// <summary>A comparison: its two sides and its sign, in any language's words.</summary>
    [GeneratedRegex(@"^(?<a>.+?)\s*(?<sign><=|>=|==|!=|~=|<(?![<])|>(?![>])|-le\b|-ge\b|-lt\b|-gt\b|-eq\b|-ne\b|\.le\.|\.ge\.|\.lt\.|\.gt\.|\.eq\.|\.ne\.)\s*(?<b>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex Comparison();

    private static IEnumerable<LogicFinding> BestsOverwritten(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        var options = dialect.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
        for (var line = 0; line < masked.Count; line++)
        {
            if (!masked[line].Contains('=') || !BestStem().IsMatch(masked[line])) continue;
            if (Given().Match(masked[line]) is not { Success: true } given) continue;
            var best = given.Groups["best"].Value;
            if (WayOf(best) is not { } way) continue;

            // A name made on the line - int best = ..., let best = ..., best := ... - is new each time round, not a best kept.
            if (given.Groups["declared"].Success || given.Groups["sign"].Value == ":=") continue;
            var value = given.Groups["value"].Value.Trim();

            // Ruby's and Perl's test written after: longest = length + 1 if length > longest.
            string? afterTest = null;
            if (Regex.Match(value, @"^(?<value>.+?)\s+(?<word>if|unless)\s+(?<test>.+)$") is { Success: true } modifier)
            {
                value = modifier.Groups["value"].Value.Trim();
                afterTest = modifier.Groups["word"].Value == "unless" ? null : modifier.Groups["test"].Value;
                if (modifier.Groups["word"].Value == "unless") continue;
            }

            if (Regex.IsMatch(value, @"^[-+]?\d+(?:\.\d+)?$|^(?:None|null|nil|undef|\$null|true|false|float\(.*\)|-?inf\w*|[\w.]*(?:MAX|MIN)_?VALUE\w*|\w*Infinity)$", RegexOptions.IgnoreCase)) continue;
            if (AlreadyChosen().IsMatch(value) || Regex.IsMatch(value, $@"(?<![\w$@]){Regex.Escape(best)}(?!\w)", options)) continue;
            if (!Reaching.InALoop(masked, 0, line)) continue;
            if (!Compared(masked, best, options)) continue;

            // This time round's search starting from a first value, then bettered on the lines after it in the same loop.
            if (ComparedLaterThisTimeRound(masked, line, best, options)) continue;

            var guards = Guards(masked, line, dialect, afterTest);
            if (guards.Any(guard => MakesItBetter(guard.Condition, guard.TurnedRound, value, best, way, options))) continue;

            var larger = way != Way.Smaller;
            var told = guards.Count == 0
                ? $"Line {line + 1} gives `{best}` the value `{value}` each time round the loop, with no test of whether it is " +
                  $"{(larger ? "larger" : "smaller")} than `{best}`, so `{best}` ends as the last value given, not the {(larger ? "largest" : "smallest")}."
                : $"Line {line + 1} gives `{best}` the value `{value}` when `{guards[0].Condition}`, but that test does not make sure `{value}` " +
                  $"is {(larger ? "larger" : "smaller")} than `{best}`: where it holds for another reason, a {(larger ? "smaller" : "larger")} value takes " +
                  $"the place of the {(larger ? "largest" : "smallest")} found so far, and the answer can come out wrong.";
            yield return new LogicFinding(id, line + 1, told, KeepTheBetter(id, source, line, given, best, value, larger, dialect))
            {
                Severity = Severity.Error,
                Confidence = Confidence.Likely,
            };
        }
    }

    /// <summary>Which way the name's best goes, from its words - or null for a name that is not a best, or names where one is.</summary>
    private static Way? WayOf(string name)
    {
        var words = name.TrimStart('$', '@').Replace("_", "").ToLowerInvariant();
        if (WhereOrWhat().IsMatch(words)) return null;
        if (BestWords().Match(words) is not { Success: true } best) return null;
        return best.Groups["larger"].Success ? Way.Larger : best.Groups["smaller"].Success ? Way.Smaller : Way.Either;
    }

    /// <summary>Whether the file compares the name anywhere: longest &lt; x, x == longest, $longest -gt $x - the mark of a best kept.</summary>
    private static bool Compared(IReadOnlyList<string> masked, string best, RegexOptions options)
    {
        var name = Regex.Escape(best);
        var sign = @"(?:<=|>=|==|<(?![<=])|>(?![>=])|-le\b|-ge\b|-lt\b|-gt\b|-eq\b|\.le\.|\.ge\.|\.lt\.|\.gt\.|\.eq\.)";
        var compared = new Regex($@"(?<![\w$@.]){name}\s*{sign}|{sign}\s*{name}(?![\w(\[.])", options);
        return masked.Any(line => compared.IsMatch(line));
    }

    /// <summary>Whether a line after this one, before the loop around it ends, compares the name - the line starts this time round's search.</summary>
    private static bool ComparedLaterThisTimeRound(IReadOnlyList<string> masked, int line, string best, RegexOptions options)
    {
        // The loop around the line: the nearest line above, less indented, that heads one.
        var indent = CodeText.Indentation(masked[line]).Length;
        var loopIndent = -1;
        for (var above = line - 1; above >= 0 && indent > 0; above--)
        {
            if (masked[above].Trim().Length == 0 || masked[above].Trim() == "{") continue;
            var width = CodeText.Indentation(masked[above]).Length;
            if (width >= indent) continue;
            indent = width;
            if (LoopHeader().IsMatch(masked[above]))
            {
                loopIndent = width;
                break;
            }
        }

        if (loopIndent < 0) return false;
        var name = Regex.Escape(best);
        var sign = @"(?:<=|>=|==|<(?![<=])|>(?![>=])|-le\b|-ge\b|-lt\b|-gt\b|-eq\b|\.le\.|\.ge\.|\.lt\.|\.gt\.|\.eq\.)";
        var compared = new Regex($@"(?<![\w$@.]){name}\s*{sign}|{sign}\s*{name}(?![\w(\[.])", options);
        for (var below = line + 1; below < masked.Count; below++)
        {
            if (masked[below].Trim().Length == 0) continue;
            if (CodeText.Indentation(masked[below]).Length <= loopIndent) break;
            if (compared.IsMatch(masked[below])) return true;
        }

        return false;
    }

    /// <summary>A test around the line: its condition, and whether the line is where it does not hold - its else, or past it in a chain.</summary>
    private sealed record Guard(string Condition, bool TurnedRound);

    /// <summary>
    /// The tests around the line inside its loop, nearest first: each block's own test, and for a line in an else or an else
    /// if, the earlier tests of its chain turned round. A test written after the line, as Ruby's and Perl's may be, is one too.
    /// </summary>
    private static List<Guard> Guards(IReadOnlyList<string> masked, int line, Dialect dialect, string? afterTest)
    {
        var guards = new List<Guard>();
        if (afterTest is not null) guards.Add(new Guard(afterTest, false));

        // A test on the line itself: if (x > best) best = x;
        if (TestWord().Match(masked[line]) is { Success: true } ownTest && Reaching.Written(masked[line], ownTest.Index + ownTest.Length, dialect.ColonEnds) is { } own)
            guards.Add(new Guard(own.Condition, false));

        var indent = CodeText.Indentation(masked[line]).Length;
        for (var above = line - 1; above >= 0 && indent > 0; above--)
        {
            var text = masked[above];
            if (text.Trim().Length == 0 || text.Trim() == "{") continue;
            var width = CodeText.Indentation(text).Length;
            if (width >= indent) continue;
            indent = width;
            if (LoopHeader().IsMatch(text)) break;

            var isElse = Regex.IsMatch(text, @"^\s*(?:\}\s*)?(?:else\b|elif\b|elsif\b|elseif\b)", RegexOptions.IgnoreCase);
            if (TestWord().Match(text) is { Success: true } test && Reaching.Written(text, test.Index + test.Length, dialect.ColonEnds) is { } wording)
                guards.Add(new Guard(wording.Condition, false));

            // The earlier tests of an else's chain do not hold where the else runs.
            if (isElse)
            {
                for (var earlier = above - 1; earlier >= 0; earlier--)
                {
                    var chain = masked[earlier];
                    if (chain.Trim().Length == 0 || chain.Trim() is "{" or "}") continue;
                    var chainWidth = CodeText.Indentation(chain).Length;
                    if (chainWidth > width) continue;
                    if (chainWidth < width) break;
                    if (TestWord().Match(chain) is not { Success: true } chainTest ||
                        Reaching.Written(chain, chainTest.Index + chainTest.Length, dialect.ColonEnds) is not { } chainWording) break;
                    guards.Add(new Guard(chainWording.Condition, true));
                    if (!Regex.IsMatch(chain, @"^\s*(?:\}\s*)?(?:else|elif|elsif|elseif)\b", RegexOptions.IgnoreCase)) break;
                }
            }
        }

        return guards;
    }

    /// <summary>
    /// Whether a test makes sure the value is no worse than the best where the line runs: every one of its or's says so - in one
    /// of its and's - or, turned round, one of its or's turned round does.
    /// </summary>
    private static bool MakesItBetter(string condition, bool turnedRound, string value, string best, Way way, RegexOptions options)
    {
        var ors = Split(Unwrapped(condition), @"\|\||(?<![\w$@])or(?!\w)|-or\b|\.or\.");
        if (!turnedRound)
            return ors.All(each => Split(each, @"&&|(?<![\w$@])and(?!\w)|-and\b|\.and\.").Any(part => Says(part, false, value, best, way, options)));

        // not (a or b) is not a and not b: one of them turned round is enough; not (a and b) needs each turned round.
        return ors.Any(each => Split(each, @"&&|(?<![\w$@])and(?!\w)|-and\b|\.and\.").All(part => Says(part, true, value, best, way, options)));
    }

    /// <summary>Whether one comparison says the value is no worse than the best: directly, by their measures, or one step on.</summary>
    private static bool Says(string part, bool turnedRound, string value, string best, Way way, RegexOptions options)
    {
        if (Comparison().Match(Unwrapped(part)) is not { Success: true } compared) return false;
        var sign = Sign(compared.Groups["sign"].Value);
        if (turnedRound) sign = Opposite(sign);
        var (one, other) = (Spaceless(compared.Groups["a"].Value), Spaceless(compared.Groups["b"].Value));
        var (valueText, bestText) = (Spaceless(value), Spaceless(best));
        var comparer = options.HasFlag(RegexOptions.IgnoreCase) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        // The value's side first: value sign best.
        string? valueSign = null;
        if (string.Equals(one, valueText, comparer) && string.Equals(other, bestText, comparer)) valueSign = sign;
        else if (string.Equals(other, valueText, comparer) && string.Equals(one, bestText, comparer)) valueSign = Swapped(sign);
        else if (Measured(one, other, valueText, bestText, options)) valueSign = sign;
        else if (Measured(other, one, valueText, bestText, options)) valueSign = Swapped(sign);

        if (valueSign is not null)
            return valueSign == "==" || way != Way.Smaller && valueSign is ">" or ">=" || way != Way.Larger && valueSign is "<" or "<=";

        // One step on: value = something + k, with something no smaller than the best - length + 1 where length == longest.
        if (Regex.Match(valueText, @"^(?<from>.+?)(?<step>[+-])(?<by>\d+)$") is not { Success: true } stepped) return false;
        var from = stepped.Groups["from"].Value;
        string? fromSign = null;
        if (string.Equals(one, from, comparer) && string.Equals(other, bestText, comparer)) fromSign = sign;
        else if (string.Equals(other, from, comparer) && string.Equals(one, bestText, comparer)) fromSign = Swapped(sign);
        if (fromSign is null) return false;
        var up = stepped.Groups["step"].Value == "+";
        return up && way != Way.Smaller && fromSign is "==" or ">" or ">=" || !up && way != Way.Larger && fromSign is "==" or "<" or "<=";
    }

    /// <summary>Whether two sides are the same measure of the value and of the best: len(word) and len(longest), items[i] and items[top].</summary>
    private static bool Measured(string valueSide, string bestSide, string value, string best, RegexOptions options)
    {
        if (!Regex.IsMatch(value, @"^[$@]?[A-Za-z_]\w*$") || valueSide == value) return false;
        var asBest = Regex.Replace(valueSide, $@"(?<![\w$@]){Regex.Escape(value)}(?!\w)", _ => best, options);
        return asBest != valueSide && string.Equals(asBest, bestSide, options.HasFlag(RegexOptions.IgnoreCase) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static string Sign(string written) => written.ToLowerInvariant() switch
    {
        "-le" or ".le." => "<=",
        "-ge" or ".ge." => ">=",
        "-lt" or ".lt." => "<",
        "-gt" or ".gt." => ">",
        "-eq" or ".eq." => "==",
        "-ne" or ".ne." or "~=" => "!=",
        _ => written,
    };

    private static string Opposite(string sign) => sign switch
    {
        "<" => ">=",
        "<=" => ">",
        ">" => "<=",
        ">=" => "<",
        "==" => "!=",
        _ => "==",
    };

    private static string Swapped(string sign) => sign switch
    {
        "<" => ">",
        "<=" => ">=",
        ">" => "<",
        ">=" => "<=",
        _ => sign,
    };

    /// <summary>The text inside brackets that wrap all of it, and a leading not taken as a turn the other way is left in place.</summary>
    private static string Unwrapped(string text)
    {
        // What opens the block, if the test was read with it: Python's :, a {, then, do.
        var inner = Regex.Replace(text.Trim(), @"\s*(?::|\{|\bthen|\bdo)\s*$", "", RegexOptions.IgnoreCase);
        while (inner.StartsWith('(') && LocalFixes.Rules.Brackets.ClosingParenthesis(inner, 0) == inner.Length - 1) inner = inner[1..^1].Trim();
        return inner;
    }

    /// <summary>The pieces of a test between the words or signs that join them, at the top level of its brackets.</summary>
    private static List<string> Split(string text, string joiner)
    {
        var pieces = new List<string>();
        var depth = 0;
        var from = 0;
        var joins = new Regex($"^(?:{joiner})", RegexOptions.IgnoreCase);
        for (var at = 0; at < text.Length; at++)
        {
            if (text[at] is '(' or '[' or '{') depth++;
            else if (text[at] is ')' or ']' or '}') depth--;
            else if (depth == 0 && joins.Match(text[at..]) is { Success: true } join && (at == 0 || !char.IsLetterOrDigit(text[at - 1]) || !char.IsLetter(join.Value[0])))
            {
                pieces.Add(text[from..at]);
                at += join.Length - 1;
                from = at + 1;
            }
        }

        pieces.Add(text[from..]);
        return pieces.Select(piece => piece.Trim()).Where(piece => piece.Length > 0).ToList();
    }

    /// <summary>The change: the better of the two kept - longest = max(longest, length + 1) - in the language's own words.</summary>
    private static LocalFix? KeepTheBetter(string id, SourceFile source, int line, Match given, string best, string value, bool larger, Dialect dialect)
    {
        var text = source.Lines[line];
        var valueGroup = given.Groups["value"];
        var valueAt = text.IndexOf(value, valueGroup.Index, StringComparison.Ordinal);
        if (valueAt < 0) return null;

        var kept = (larger ? dialect.Larger : dialect.Smaller).Replace("{B}", best).Replace("{X}", value);
        var changed = text[..valueAt] + kept + text[(valueAt + value.Length)..];
        return LocalFix.ReplaceLine(id, $"Keep the {(larger ? "larger" : "smaller")}: {best} = {kept}",
            $"`{best}` keeps the {(larger ? "largest" : "smallest")} found so far only if a new value takes its place when it is {(larger ? "larger" : "smaller")}.",
            source.Path, line + 1, changed);
    }

    private static string Spaceless(string text) => Regex.Replace(text, @"\s+", "");
}
