using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Logic;

/// <summary>
/// Two sequences lined up item by item - the longest common subsequence, the longest common substring - whose match moves on
/// in one of them only. A match is the two items compared being equal, and it uses up both: the length goes on from the
/// entry, or the call, past both items - table[i - 1][j - 1] + 1, lcs(a[1:], b[1:]). Written table[i - 1][j] + 1, or
/// lcs(a[1:], b), it goes on from past one item only: the other, already matched, is still there to be matched again, and
/// the length comes out wrong. QuixBugs' lcs_length and longest_common_subsequence are this mistake, in Python and in Java.
/// The change moves the other on as well, written as the first is.
/// </summary>
/// <remarks>
/// Said only when the file shows all of it. The test asks only whether two items are equal - one from each of two different
/// sequences, each at its own place, the same step from it: first[i - 1] == second[j - 1], s.charAt(i) == t.charAt(j),
/// first:sub(i, i) == second:sub(j, j), substr($a, $i - 1, 1) eq substr($b, $j - 1, 1), first[0] == second[0]. For a
/// table: on the match's own lines, inside a loop, an entry of a table - or a name then put into one - at the same step from
/// both places is given the same table's entry one step on in one place only, alone or with a number added; nothing on
/// those lines reads the entry one step on in both, as the number of ways b is a subsequence of a does; and an else, if the
/// test has one, moves on in the other place somewhere - max(table[i - 1][j], table[i][j - 1]) - where a table counting how
/// often an item has come up so far only ever moves on in the one. For a call: the function's parameters are the two
/// sequences, or the two places; on the match's lines it calls itself with one of them moved on and the other as it was
/// given, and never with both moved on; and elsewhere it tries each moved on alone together - in one max, or one after the
/// other with no test choosing between them - as a lining up does where the items differ. A function that drops a matched
/// item and keeps the other to match again - taking every copy of b's items out of a - makes those two calls only as a test
/// chooses, and is left alone.
/// </remarks>
public static partial class AlignmentPatterns
{
    /// <summary>How one language is read: its comments and strings, whether a colon ends its tests, and whether its names' case counts.</summary>
    /// <param name="ParenIndex">Whether a table's entry is read with round brackets, table(i)(j), as Scala's and Fortran's are.</param>
    private sealed record Dialect(string Language, IReadOnlySet<string> Files, Syntax Syntax, bool ColonEnds = false, bool IgnoreCase = false, bool ParenIndex = false);

    private static readonly Dialect[] Dialects =
    [
        new("python", CodePattern.Files(".py", ".pyw"), Syntax.Python, ColonEnds: true),
        new("java", CodePattern.Files(".java"), Syntax.CLike),
        new("csharp", CodePattern.Files(".cs"), Syntax.CLike),
        new("js", CodePattern.Files(".js", ".mjs", ".cjs", ".ts", ".mts", ".cts"), Syntax.CLike),
        new("c", CodePattern.Files(".c"), Syntax.CLike),
        new("cpp", CodePattern.Files(".cpp", ".cc", ".cxx", ".c++"), Syntax.CLike),
        new("go", CodePattern.Files(".go"), Syntax.CLike),
        new("rust", CodePattern.Files(".rs"), Syntax.CLike),
        new("ruby", CodePattern.Files(".rb"), Syntax.Python),
        new("lua", CodePattern.Files(".lua"), Syntax.Lua),
        new("php", CodePattern.Files(".php"), Syntax.CLike),
        new("perl", CodePattern.Files(".pl", ".pm"), Syntax.Perl),
        new("powershell", CodePattern.Files(".ps1"), Syntax.Python, IgnoreCase: true),
        new("dart", CodePattern.Files(".dart"), Syntax.CLike),
        new("scala", CodePattern.Files(".scala", ".sc"), Syntax.Scala, ParenIndex: true),
        new("fortran", CodePattern.Files(".f90", ".f95", ".f03", ".f08", ".f"), Syntax.Fortran, IgnoreCase: true, ParenIndex: true),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        .. Dialects.Select(dialect => new CodePattern($"logic-{dialect.Language}-match-moves-one", dialect.Files, dialect.Syntax,
            (id, source, masked) => MatchesMovingOne(id, source, masked, dialect), Severity.Error, Confidence.Likely)),
    ];

    /// <summary>A test's word: if, elif, else if, elsif, elseif - not unless, whose block is what happens when the items differ.</summary>
    [GeneratedRegex(@"(?<![\w$.@-])(?:else\s+if|elseif|elsif|elif|if)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TestWord();

    /// <summary>A test's two sides when it asks only whether they are equal: ==, ===, eq, -eq, -ceq, .eq.</summary>
    [GeneratedRegex(@"^(?<left>[^=!<>]+?)\s*(?:===?|(?<=\s)eq(?=\s)|-c?eq(?!\w)|\.eq\.)\s*(?<right>[^=!<>]+)$", RegexOptions.IgnoreCase)]
    private static partial Regex EqualSides();

    /// <summary>What joins a test to another, or turns it round: and, or, not, &amp;&amp;, ||, -and, .or., a leading !.</summary>
    [GeneratedRegex(@"&&|\|\||(?<![\w$@])(?:and|or|not)(?!\w)|-(?:and|or|not|xor)\b|\.(?:and|or|not)\.|^\s*!", RegexOptions.IgnoreCase)]
    private static partial Regex Joined();

    /// <summary>
    /// One item of a sequence and nothing else: first[i - 1], s.charAt(i), first(i:i), first:sub(i, i), substr($a, $i - 1, 1),
    /// first[0] - the sequence, the place as a name with a step, or a number.
    /// </summary>
    [GeneratedRegex(@"^(?:substr\s*\(\s*(?<sequence>\$[A-Za-z_]\w*)\s*,\s*|(?<sequence>[$@]?[A-Za-z_]\w*)\s*(?:[.:]\s*(?:charAt|charCodeAt|codePointAt|at|sub|substring|Substring|slice|get)\s*)?[\[(]\s*)(?:(?<place>[$@]?[A-Za-z_]\w*)\s*(?:(?<sign>[+-])\s*(?<step>\d+))?|(?<number>\d+))\s*[\]),:][\w\s,:$@)\]]*$")]
    private static partial Regex ItemRead();

    private const string PlaceWritten = @"[$@]?[A-Za-z_]\w*(?:\s*[+-]\s*\d+)?";

    /// <summary>An entry of a table at two places, each a name with a step or none: table[i - 1][j], dp[i - 1, j], $table[$i][$j - 1], table(i - 1)(j), table(i - 1, j), dp.get(i-1).get(j).</summary>
    [GeneratedRegex(@"(?<![\w$@.])(?<table>[$@]?[A-Za-z_]\w*)\s*(?:->\s*)?(?:\[\s*(?<a>" + PlaceWritten + @")\s*\]\s*(?:->\s*)?\[\s*(?<b>" + PlaceWritten + @")\s*\]|\[\s*\(?\s*(?<a>" + PlaceWritten + @")\s*,\s*(?<b>" + PlaceWritten + @")\s*\)?\s*\]|\.get\(\s*(?<a>" + PlaceWritten + @")\s*\)\s*\.get\(\s*(?<b>" + PlaceWritten + @")\s*\)|(?<paren>)\(\s*(?<a>" + PlaceWritten + @")\s*\)\s*\(\s*(?<b>" + PlaceWritten + @")\s*\)|(?<paren>)\(\s*(?<a>" + PlaceWritten + @")\s*,\s*(?<b>" + PlaceWritten + @")\s*\))")]
    private static partial Regex TableEntry();

    /// <summary>A place as written: its name, and how many steps from it - i - 1, $j+1, k.</summary>
    [GeneratedRegex(@"^(?<name>[$@]?[A-Za-z_]\w*)(?:\s*(?<sign>[+-])\s*(?<step>\d+))?$")]
    private static partial Regex PlaceParts();

    /// <summary>A name or an entry given a value at the start of a line: table[i][j] = , int insert_value = , dp[i, j] = .</summary>
    [GeneratedRegex(@"^\s*(?:(?:let|var|const|final|val|local|my|our|auto|mut|int|long|double|float|integer)\s+|[\w.<>\[\]:]+\s+(?=[$@]?[A-Za-z_]\w*\s*(?::=|=(?!=))))?(?<target>[^=;]*?[^\s=:])\s*(?::=|=(?!=))\s*$")]
    private static partial Regex GivenTo();

    /// <summary>One item compared, as written: the sequence it is from, the place it is at - a name, or none for a number - and how many steps from that place.</summary>
    private sealed record Item(string Text, string Sequence, string Place, int Step);

    /// <summary>A test asking only whether two items are equal: one from each of two different sequences.</summary>
    private sealed record Compared(Item Left, Item Right);

    /// <summary>A place written in a table's entry or a call: its name, its steps from it, the text and where on the line it starts.</summary>
    private sealed record Place(string Name, int Step, string Text, int Start);

    private static IEnumerable<LogicFinding> MatchesMovingOne(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        var names = dialect.IgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        List<(int Header, string Name, List<string> Parameters, List<int> Body)>? functions = null;
        for (var line = 0; line < masked.Count; line++)
        {
            foreach (Match word in TestWord().Matches(masked[line]))
            {
                if (Reaching.Written(masked[line], word.Index + word.Length, dialect.ColonEnds) is not { } wording) continue;
                if (ComparedItems(wording.Condition, names) is not { } compared) continue;

                var block = MatchBlock(masked, line, wording);
                var finding = TableStep(id, source, masked, dialect, names, compared, block);
                if (finding is null && block.Count > 0)
                {
                    functions ??= SortingPatterns.FunctionsIn(masked, Path.GetExtension(source.Path).ToLowerInvariant());
                    finding = CallStep(id, source, masked, names, compared, line, block, functions);
                }

                if (finding is not null) yield return finding;
                break;
            }
        }
    }

    /// <summary>The two items a test asks only the equality of, one from each of two sequences, at two places the same step from each - or both at the same number.</summary>
    private static Compared? ComparedItems(string condition, StringComparer names)
    {
        var asked = condition.Trim();
        while (asked.StartsWith('(') && Brackets.ClosingParenthesis(asked, 0) == asked.Length - 1) asked = asked[1..^1].Trim();
        if (Joined().IsMatch(asked) || EqualSides().Match(asked) is not { Success: true } sides) return null;
        if (ItemOf(sides.Groups["left"].Value.Trim()) is not { } left || ItemOf(sides.Groups["right"].Value.Trim()) is not { } right) return null;
        if (names.Equals(left.Sequence, right.Sequence) || left.Step != right.Step) return null;
        if (left.Place.Length == 0 != (right.Place.Length == 0) || left.Place.Length > 0 && names.Equals(left.Place, right.Place)) return null;
        return new Compared(left, right);
    }

    private static Item? ItemOf(string side)
    {
        if (ItemRead().Match(side) is not { Success: true } read) return null;
        if (read.Groups["number"].Success) return new Item(side, read.Groups["sequence"].Value, "", int.Parse(read.Groups["number"].Value));
        var step = read.Groups["step"].Success ? int.Parse(read.Groups["step"].Value) * (read.Groups["sign"].Value == "-" ? -1 : 1) : 0;
        return new Item(side, read.Groups["sequence"].Value, read.Groups["place"].Value, step);
    }

    /// <summary>The match's own lines: what follows the test on its line, or the lines of its block - each with where on the line it starts.</summary>
    private static List<(int Line, int From)> MatchBlock(IReadOnlyList<string> masked, int line, Reaching.Wording wording)
    {
        if (wording.Rest.Length > 0)
        {
            var from = masked[line].IndexOf(wording.Rest, Math.Min(wording.RestStart, masked[line].Length), StringComparison.Ordinal);
            return from < 0 ? [] : [(line, from)];
        }

        var indent = CodeText.Indentation(masked[line]).Length;
        var block = new List<(int, int)>();
        for (var below = line + 1; below < masked.Count; below++)
        {
            var text = masked[below];
            if (text.Trim().Length == 0) continue;
            var width = CodeText.Indentation(text).Length;

            // A { on a line of its own, under the test, opens the block - as C# writes it.
            if (width == indent && block.Count == 0 && text.Trim() == "{") continue;
            if (width <= indent) break;
            block.Add((below, 0));
        }

        return block;
    }

    /// <summary>
    /// The lines of the test's else - what happens when the items differ: after the match's block, a line starting else (not
    /// else if), its own rest and the lines of its block - each with where on the line it starts; none when there is no else.
    /// </summary>
    private static List<(int Line, int From)> ElseBlock(IReadOnlyList<string> masked, List<(int Line, int From)> block)
    {
        if (block.Count == 0) return [];
        var next = block[^1].Line + 1;
        while (next < masked.Count && masked[next].Trim() is "" or "}") next++;
        if (next >= masked.Count) return [];

        var opening = Regex.Match(masked[next], @"^\s*(?:\}\s*)?else\b(?!\s*if\b)\s*(?:\{|:)?\s*");
        if (!opening.Success) return [];
        var lines = new List<(int, int)>();
        if (masked[next].Trim().Length > opening.Value.Trim().Length) lines.Add((next, opening.Length));

        var indent = CodeText.Indentation(masked[next]).Length;
        for (var below = next + 1; below < masked.Count; below++)
        {
            var text = masked[below];
            if (text.Trim().Length == 0) continue;
            var width = CodeText.Indentation(text).Length;
            if (width == indent && lines.Count == 0 && text.Trim() == "{") continue;
            if (width <= indent) break;
            lines.Add((below, 0));
        }

        return lines;
    }

    // ---- A table: the match's entry from one step on in one place only ---------------------------------------------

    private static LogicFinding? TableStep(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect, StringComparer names, Compared compared, List<(int Line, int From)> block)
    {
        if (compared.Left.Place.Length == 0) return null;
        foreach (var (line, from) in block)
        {
            var text = masked[line];
            foreach (Match read in TableEntry().Matches(text, from))
            {
                if (read.Groups["paren"].Success && !dialect.ParenIndex) continue;
                if (Placed(read, compared, names) is not { } places || places.Left.Step == places.Right.Step) continue;

                // The whole of what is given: the entry, with a number added or none.
                var before = Regex.Replace(text[from..read.Index], @"\d+\s*\+\s*$", "");
                var after = text[(read.Index + read.Length)..];
                if (!Regex.IsMatch(after, @"^\s*(?:[+-]\s*\d+\s*)?;?\s*$") || GivenTo().Match(before) is not { Success: true } given) continue;

                var table = read.Groups["table"].Value;
                if (WrittenAt(masked, block, line, given.Groups["target"].Value.Trim(), table, compared, names) is not { } written) continue;

                // One place where the entry is written, the other one step on from it.
                Place? moved = null, kept = null;
                if (places.Left.Step == written) (moved, kept) = (places.Right, places.Left);
                else if (places.Right.Step == written) (moved, kept) = (places.Left, places.Right);
                if (moved is null || kept is null || Math.Abs(moved.Step - written) != 1) continue;

                // Nothing on the match's lines reads the entry one step on in both - as a count of ways adds the two.
                var both = moved.Step;
                if (block.Any(each => TableEntry().Matches(masked[each.Line], each.From).Any(other =>
                        names.Equals(other.Groups["table"].Value, table) && Placed(other, compared, names) is { } otherPlaces &&
                        otherPlaces.Left.Step == both && otherPlaces.Right.Step == both))) continue;
                if (!Reaching.InALoop(masked, 0, line)) continue;

                // Where the items differ, a lining up tries moving on in the other place too - max(table[i - 1][j], table[i][j - 1]).
                // A table only ever moving on in the one place counts how often an item has come up so far, and is left alone.
                var otherwise = ElseBlock(masked, block).SelectMany(each => TableEntry().Matches(masked[each.Line], each.From))
                    .Where(other => names.Equals(other.Groups["table"].Value, table))
                    .Select(other => Placed(other, compared, names)).OfType<(Place Left, Place Right)>().ToList();
                var keptName = kept.Name;
                if (otherwise.Count > 0 && !otherwise.Any(other => (names.Equals(other.Left.Name, keptName) ? other.Left : other.Right).Step != written)) continue;

                var movedItem = names.Equals(moved.Name, compared.Left.Place) ? compared.Left : compared.Right;
                var keptItem = ReferenceEquals(movedItem, compared.Left) ? compared.Right : compared.Left;
                var entry = source.Lines[line].Substring(read.Index, read.Length);
                var told = $"Line {line + 1} goes on from `{entry}` when `{compared.Left.Text}` equals `{compared.Right.Text}`. A match uses up both " +
                           $"items, but `{entry}` is one step on in `{moved.Name}` only - past `{movedItem.Sequence}`'s item, not `{keptItem.Sequence}`'s - so " +
                           $"`{keptItem.Text}`, already matched, can be matched again, and the length comes out wrong.";
                return new LogicFinding(id, line + 1, told, BothMoved(id, source, line, kept, moved, entry, read.Index))
                {
                    Severity = Severity.Error,
                    Confidence = Confidence.Likely,
                };
            }
        }

        return null;
    }

    /// <summary>The entry's two places, as the two items' places: the one named as the left item's first.</summary>
    private static (Place Left, Place Right)? Placed(Match entry, Compared compared, StringComparer names)
    {
        if (PlaceOf(entry.Groups["a"]) is not { } a || PlaceOf(entry.Groups["b"]) is not { } b) return null;
        if (names.Equals(a.Name, compared.Left.Place) && names.Equals(b.Name, compared.Right.Place)) return (a, b);
        if (names.Equals(a.Name, compared.Right.Place) && names.Equals(b.Name, compared.Left.Place)) return (b, a);
        return null;
    }

    private static Place? PlaceOf(Group written)
    {
        if (PlaceParts().Match(written.Value) is not { Success: true } parts) return null;
        var step = parts.Groups["step"].Success ? int.Parse(parts.Groups["step"].Value) * (parts.Groups["sign"].Value == "-" ? -1 : 1) : 0;
        return new Place(parts.Groups["name"].Value, step, written.Value, written.Index);
    }

    /// <summary>
    /// The step from both places at which the match's entry is written: the target's own, when it is the table's entry at the
    /// same step from both; or, for a name put into a table on a later line of the match - internal_map.put(j, insert_value) -
    /// the step of the place it is put at. Null when it is written anywhere else.
    /// </summary>
    private static int? WrittenAt(IReadOnlyList<string> masked, List<(int Line, int From)> block, int line, string target, string table, Compared compared, StringComparer names)
    {
        if (TableEntry().Match(target) is { Success: true } entry && entry.Index == 0 && entry.Length == target.Length)
        {
            if (!names.Equals(entry.Groups["table"].Value, table) || Placed(entry, compared, names) is not { } places) return null;
            return places.Left.Step == places.Right.Step ? places.Left.Step : null;
        }

        if (!Regex.IsMatch(target, @"^[$@]?[A-Za-z_]\w*$")) return null;
        var name = Regex.Escape(target);
        foreach (var (later, _) in block.Where(each => each.Line > line))
        {
            if (Regex.Match(masked[later], $@"^\s*[$@]?[A-Za-z_][\w.]*\s*\.\s*put\s*\(\s*(?<at>{PlaceWritten})\s*,\s*{name}\s*\)\s*;?\s*$") is { Success: true } put &&
                PlaceOf(put.Groups["at"]) is { } at && (names.Equals(at.Name, compared.Left.Place) || names.Equals(at.Name, compared.Right.Place)))
            {
                return at.Step;
            }

            if (Regex.Match(masked[later], $@"^\s*(?<into>.+?)\s*(?::=|=(?!=))\s*{name}\s*;?\s*$") is { Success: true } putInto &&
                TableEntry().Match(putInto.Groups["into"].Value) is { Success: true } into && Placed(into, compared, names) is { } places)
            {
                return places.Left.Step == places.Right.Step ? places.Left.Step : null;
            }
        }

        return null;
    }

    /// <summary>The change: the place left where it was written one step on as well, written as the other is - table[i - 1][j] to table[i - 1][j - 1].</summary>
    private static LocalFix? BothMoved(string id, SourceFile source, int line, Place kept, Place moved, string entry, int entryAt)
    {
        var text = source.Lines[line];
        if (kept.Start + kept.Text.Length > text.Length || text.Substring(kept.Start, kept.Text.Length) != kept.Text) return null;
        var movedOn = kept.Name + moved.Text[moved.Name.Length..];
        var changed = text[..kept.Start] + movedOn + text[(kept.Start + kept.Text.Length)..];
        var changedEntry = changed.Substring(entryAt, entry.Length + movedOn.Length - kept.Text.Length);
        return LocalFix.ReplaceLine(id, $"Move past both items on a match: {changedEntry}",
            $"A match uses up an item of each sequence, so the length goes on from the entry one step on in both places: `{changedEntry}`.",
            source.Path, line + 1, changed);
    }

    // ---- A function calling itself: the match moves on in one of its two sequences only ------------------------------

    private static LogicFinding? CallStep(string id, SourceFile source, IReadOnlyList<string> masked, StringComparer names, Compared compared, int line,
        List<(int Line, int From)> block, List<(int Header, string Name, List<string> Parameters, List<int> Body)> functions)
    {
        var function = functions.FirstOrDefault(each => each.Body.Contains(line));
        if (function.Name is null) return null;

        // The items are of two of its parameters: the two sequences, at the same number, or the two places.
        var parameters = function.Parameters;
        var (firstName, secondName) = compared.Left.Place.Length == 0
            ? (compared.Left.Sequence, compared.Right.Sequence)
            : (compared.Left.Place, compared.Right.Place);
        var first = parameters.FindIndex(parameter => names.Equals(parameter, firstName));
        var second = parameters.FindIndex(parameter => names.Equals(parameter, secondName));
        if (first < 0 || second < 0) return null;

        var inBlock = block.SelectMany(each => SelfCalls(masked[each.Line], each.From, function.Name).Select(call => (each.Line, Call: call))).ToList();
        var blockLines = block.Select(each => each.Line).ToHashSet();
        var elsewhere = function.Body.Where(each => !blockLines.Contains(each))
            .SelectMany(each => SelfCalls(masked[each], 0, function.Name).Select(call => (Line: each, Call: call))).ToList();

        string Shape(List<(string Text, int Start)> arguments)
        {
            if (arguments.Count != parameters.Count) return "";
            for (var at = 0; at < arguments.Count; at++)
            {
                if (at != first && at != second && !names.Equals(arguments[at].Text.Trim(), parameters[at])) return "";
            }

            var firstMoved = MovedOn(arguments[first].Text, parameters[first], parameters[second], names);
            var secondMoved = MovedOn(arguments[second].Text, parameters[second], parameters[first], names);
            var firstKept = names.Equals(arguments[first].Text.Trim(), parameters[first]);
            var secondKept = names.Equals(arguments[second].Text.Trim(), parameters[second]);
            return firstMoved && secondMoved ? "both" : firstMoved && secondKept ? "first" : secondMoved && firstKept ? "second" : "";
        }

        // On a match it never moves both on; elsewhere it moves each on alone, trying the two together - max(f(a, b[1:]),
        // f(a[1:], b)), or one after the other with nothing choosing between them - not one or the other as a test decides.
        if (inBlock.Any(each => Shape(each.Call.Arguments) == "both")) return null;
        var firstAlone = elsewhere.Where(each => Shape(each.Call.Arguments) == "first").Select(each => each.Line).ToList();
        var secondAlone = elsewhere.Where(each => Shape(each.Call.Arguments) == "second").Select(each => each.Line).ToList();
        if (!firstAlone.Any(firstLine => secondAlone.Any(secondLine => TriedTogether(masked, firstLine, secondLine)))) return null;

        foreach (var (callLine, call) in inBlock)
        {
            var shape = Shape(call.Arguments);
            if (shape is not ("first" or "second")) continue;
            var (movedAt, keptAt) = shape == "first" ? (first, second) : (second, first);
            var moved = call.Arguments[movedAt];
            var kept = call.Arguments[keptAt];

            var text = source.Lines[callLine];
            var called = text.Substring(call.Start, call.End - call.Start + 1);
            var keptText = kept.Text.Trim();
            var keptStart = kept.Start + kept.Text.IndexOf(keptText, StringComparison.Ordinal);
            var movedOn = Regex.Replace(moved.Text.Trim(), $@"(?<![\w$@]){Regex.Escape(parameters[movedAt])}(?!\w)", _ => parameters[keptAt],
                names == StringComparer.OrdinalIgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
            var changed = text[..keptStart] + movedOn + text[(keptStart + keptText.Length)..];
            var changedCall = changed.Substring(call.Start, call.End - call.Start + 1 + movedOn.Length - keptText.Length);

            var told = $"Line {callLine + 1} goes on with `{called}` when `{compared.Left.Text}` equals `{compared.Right.Text}`. A match uses up " +
                       $"both items, but the call moves past `{parameters[movedAt]}`'s item only - `{parameters[keptAt]}` is given again as it was, so its " +
                       "item, already matched, can be matched again, and the answer comes out wrong. Where the items differ, the function tries moving " +
                       "past each in turn; on a match it moves past both.";
            var fix = LocalFix.ReplaceLine(id, $"Move past both items on a match: {changedCall}",
                $"A match uses up an item of each, so the call goes on with both moved past it: `{changedCall}`.", source.Path, callLine + 1, changed);
            return new LogicFinding(id, callLine + 1, told, fix)
            {
                Severity = Severity.Error,
                Confidence = Confidence.Likely,
            };
        }

        return null;
    }

    /// <summary>
    /// Whether two calls are both made each time either is: on one line that chooses nothing - no test, no ? - or on lines one
    /// after another at the same indentation, none of them, nor any between, choosing.
    /// </summary>
    private static bool TriedTogether(IReadOnlyList<string> masked, int one, int other)
    {
        bool Chooses(int line) => TestWord().IsMatch(masked[line]) || masked[line].Contains('?') || Regex.IsMatch(masked[line], @"^\s*(?:\}\s*)?else\b");
        var (from, to) = one <= other ? (one, other) : (other, one);
        var indent = CodeText.Indentation(masked[from]).Length;
        for (var line = from; line <= to; line++)
        {
            if (masked[line].Trim().Length == 0) continue;
            if (Chooses(line) || CodeText.Indentation(masked[line]).Length < indent) return false;
        }

        return CodeText.Indentation(masked[to]).Length == indent;
    }

    /// <summary>A call's argument moved on from the parameter it is given as: not the parameter itself, but written from it - a[1:], i + 1, first.substring(1) - and not from the other.</summary>
    private static bool MovedOn(string argument, string parameter, string other, StringComparer names)
    {
        var written = argument.Trim();
        var options = names == StringComparer.OrdinalIgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
        return !names.Equals(written, parameter) &&
               Regex.IsMatch(written, $@"(?<![\w$@]){Regex.Escape(parameter)}(?!\w)", options) &&
               !Regex.IsMatch(written, $@"(?<![\w$@]){Regex.Escape(other)}(?!\w)", options);
    }

    /// <summary>A call the function makes of itself on the line, from where it is read: where it starts and ends, and each argument with where it starts.</summary>
    private sealed record SelfCall(int Start, int End, List<(string Text, int Start)> Arguments);

    private static IEnumerable<SelfCall> SelfCalls(string text, int from, string name)
    {
        foreach (Match call in Regex.Matches(text[from..], $@"(?<![\w$.@])(?:(?:self|this)\s*\.\s*)?{Regex.Escape(name)}\s*\("))
        {
            var open = from + call.Index + call.Length - 1;
            if (Brackets.ClosingParenthesis(text, open) is not { } close) continue;
            yield return new SelfCall(from + call.Index, close, Arguments(text, open + 1, close));
        }
    }

    /// <summary>The arguments between a call's brackets, split at its commas at the top level, each with where it starts.</summary>
    private static List<(string Text, int Start)> Arguments(string text, int start, int end)
    {
        var arguments = new List<(string, int)>();
        var depth = 0;
        var from = start;
        for (var at = start; at < end; at++)
        {
            if (text[at] is '(' or '[' or '{') depth++;
            else if (text[at] is ')' or ']' or '}') depth--;
            else if (text[at] == ',' && depth == 0)
            {
                arguments.Add((text[from..at], from));
                from = at + 1;
            }
        }

        if (text[from..end].Trim().Length > 0 || arguments.Count > 0) arguments.Add((text[from..end], from));
        return arguments;
    }
}
