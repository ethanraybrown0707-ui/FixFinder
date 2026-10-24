using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// Mistakes Lua programs make that Lua runs without a word - each run with Lua 5.4.6: a loop from 0 over a list, whose
/// first item is at 1, so <c>marks[0]</c> is nil; text compared with == to a number, which in Lua is never equal - "5" == 5
/// is false; a decimal worked out by arithmetic, compared exactly - 0.1 + 0.2 == 0.3 is false;
/// <c>table.remove(marks, i)</c> in a loop forward over marks, which skips the item after each one it removes; and # of a
/// table given only keyed entries - #ages of {Ada = 36, Bob = 20} is 0.
/// </summary>
public static partial class LuaLogicPatterns
{
    private static readonly IReadOnlySet<string> LuaFiles = CodePattern.Files(".lua");

    private static CodePattern Pattern(
        string id, Func<string, SourceFile, IReadOnlyList<string>, IEnumerable<LogicFinding>> find,
        Severity severity = Severity.Warning, Confidence confidence = Confidence.Likely) =>
        new(id, LuaFiles, Syntax.Lua, find, severity, confidence);

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        Pattern("logic-lua-loop-from-zero", LoopFromZero, Severity.Error),
        Pattern("logic-lua-text-compared-with-number", TextComparedWithNumber, Severity.Error, Confidence.Certain),
        Pattern("logic-lua-float-equality", FloatEquality),
        Pattern("logic-lua-remove-while-looping-forward", RemoveWhileLoopingForward, Severity.Error),
        Pattern("logic-lua-length-of-keyed-table", LengthOfKeyedTable, Severity.Error),
    ];

    /// <summary><c>for i = 0, #marks do</c>, or <c>for i = 0, #marks - 1 do</c>: counting a list's positions from 0.</summary>
    [GeneratedRegex(@"\bfor\s+(?<variable>[A-Za-z_]\w*)\s*=\s*(?<start>0)\s*,\s*(?<limit>#\s*(?<list>[A-Za-z_][\w.]*)(?:\s*-\s*1)?)\s*do\b")]
    private static partial Regex LoopFromZeroHeading();

    [GeneratedRegex(@"\b(?:function|if|do|repeat)\b")]
    private static partial Regex BlockOpener();

    [GeneratedRegex(@"\b(?:end|until)\b")]
    private static partial Regex BlockCloser();

    /// <summary>
    /// The lines inside the block a line opens, up to the end that closes it - counting what opens a block in Lua (function,
    /// if, do - which a for or while heading ends with - and repeat) against what closes one (end, and repeat's until).
    /// </summary>
    private static (int First, int End)? Body(IReadOnlyList<string> masked, int opening)
    {
        var depth = 0;

        for (var index = opening; index < masked.Count; index++)
        {
            depth += BlockOpener().Matches(masked[index]).Count - BlockCloser().Matches(masked[index]).Count;
            if (depth <= 0) return index > opening ? (opening + 1, index) : null;
        }

        return null;
    }

    private static IEnumerable<LogicFinding> LoopFromZero(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (LoopFromZeroHeading().Match(masked[index]) is not { Success: true } loop || Body(masked, index) is not { } body) continue;

            var variable = loop.Groups["variable"].Value;
            var list = loop.Groups["list"].Value;
            var position = new Regex($@"(?<![\w.]){Regex.Escape(list)}\s*\[\s*{Regex.Escape(variable)}\s*\]");
            if (!Enumerable.Range(body.First, body.End - body.First).Any(line => position.IsMatch(masked[line]))) continue;

            var line = source.Lines[index];
            var start = loop.Groups["start"];
            var limit = loop.Groups["limit"];
            var corrected = line[..start.Index] + "1" + line[(start.Index + start.Length)..limit.Index] + $"#{list}" + line[(limit.Index + limit.Length)..];

            yield return new LogicFinding(id, index + 1,
                $"A Lua list's first item is at 1, not 0, so on the loop's first pass `{list}[{variable}]` is `{list}[0]` - nil, with no " +
                $"error - {(limit.Value.Contains('-') ? "and stopping at #" + list + " - 1 misses the last item" : "and the loop takes one pass more than the list has items")}",
                LocalFix.ReplaceLine(id, $"Count from 1 to #{list}: for {variable} = 1, #{list}",
                    $"{list}'s items are at positions 1 to #{list}, so the loop goes from 1 to #{list}.",
                    source.Path, index + 1, corrected));
        }
    }

    /// <summary>A variable given text: a quoted string, or a line io.read reads, which is text unless it is asked for a number.</summary>
    [GeneratedRegex(@"^\s*(?:local\s+)?(?<name>[A-Za-z_]\w*)\s*=\s*(?<value>.+?)\s*$")]
    private static partial Regex Assignment();

    [GeneratedRegex(@"^(?:""[^""]*""|'[^']*'|io\.read\(\s*(?:""\*?[lL]""|'\*?[lL]')?\s*\))$")]
    private static partial Regex TextValue();

    [GeneratedRegex(@"(?<![\w.])(?<name>[A-Za-z_]\w*)\s*(?<op>==|~=)\s*(?<number>-?\d+(?:\.\d+)?)(?![\w.])")]
    private static partial Regex ComparedWithNumber();

    private static IEnumerable<LogicFinding> TextComparedWithNumber(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        // Read from the lines as written, as masking blanks the text inside quotes - and each name counts only if every value it
        // is given is text, so one later given a number is left alone.
        var givenText = new Dictionary<string, bool>(StringComparer.Ordinal);

        for (var index = 0; index < masked.Count; index++)
        {
            if (Assignment().Match(masked[index]) is not { Success: true } assignment) continue;

            var name = assignment.Groups["name"].Value;
            var written = source.Lines[index].Substring(assignment.Groups["value"].Index, assignment.Groups["value"].Length).Trim();
            // Text that reads as a number is still text: "5" == 5 is false in Lua too.
            givenText[name] = TextValue().IsMatch(written) && givenText.GetValueOrDefault(name, true);
        }

        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match compared in ComparedWithNumber().Matches(masked[index]))
            {
                var name = compared.Groups["name"].Value;
                if (!givenText.TryGetValue(name, out var onlyText) || !onlyText) continue;

                var op = compared.Groups["op"].Value;
                var number = compared.Groups["number"].Value;
                var line = source.Lines[index];
                var nameGroup = compared.Groups["name"];

                yield return new LogicFinding(id, index + 1,
                    $"`{name}` holds text, and Lua never counts text equal to a number - \"{number}\" {op} {number} is " +
                    $"{(op == "==" ? "false" : "true")} - so `{name} {op} {number}` is {(op == "==" ? "false" : "true")} every time, with no error",
                    LocalFix.ReplaceLine(id, $"Turn the text into a number first: tonumber({name}) {op} {number}",
                        $"tonumber turns text such as \"{number}\" into the number {number} - or nil, for text that is not a number - so the comparison is of two numbers.",
                        source.Path, index + 1, line[..nameGroup.Index] + $"tonumber({name})" + line[(nameGroup.Index + nameGroup.Length)..]));
            }
        }
    }

    [GeneratedRegex(@"(?<![\w.])(?<name>[A-Za-z_]\w*)\s*(?<op>==|~=)\s*(?<value>\d*\.\d*[1-9]\d*)(?![\w.])")]
    private static partial Regex ExactComparison();

    private static IEnumerable<LogicFinding> FloatEquality(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var worked = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in masked)
        {
            if (Assignment().Match(line) is not { Success: true } assignment) continue;

            var value = assignment.Groups["value"].Value;
            var byArithmetic = Regex.IsMatch(Regex.Replace(value, @"\d*\.\d+|\d+\.\d*", "0"), @"[-+*/]");
            if (byArithmetic && Regex.IsMatch(value, @"\d*\.\d+|\d+\.\d*")) worked.Add(assignment.Groups["name"].Value);
        }

        if (worked.Count == 0) yield break;

        for (var index = 0; index < masked.Count; index++)
        {
            if (ExactComparison().Match(masked[index]) is not { Success: true } compared || !worked.Contains(compared.Groups["name"].Value)) continue;

            var name = compared.Groups["name"].Value;
            var value = compared.Groups["value"].Value;
            var close = compared.Groups["op"].Value == "==" ? $"math.abs({name} - {value}) < 1e-9" : $"math.abs({name} - {value}) >= 1e-9";
            var line = source.Lines[index];

            yield return new LogicFinding(id, index + 1,
                $"`{name}` is a decimal worked out by arithmetic, compared with {value} exactly - and arithmetic on decimals is rarely " +
                "exact: 0.1 + 0.2 is 0.30000000000000004, though print shows it as 0.3",
                LocalFix.ReplaceLine(id, $"Compare within a tiny tolerance: {close}",
                    $"{close} is true when {name} is {value} to within a billionth, which arithmetic's tiny errors stay inside.",
                    source.Path, index + 1, line[..compared.Index] + close + line[(compared.Index + compared.Length)..]));
        }
    }

    /// <summary><c>for i = 1, #marks do</c>: a loop forward over a list's positions.</summary>
    [GeneratedRegex(@"\bfor\s+(?<variable>[A-Za-z_]\w*)\s*=\s*(?<range>1\s*,\s*#\s*(?<list>[A-Za-z_][\w.]*))\s*do\b")]
    private static partial Regex ForwardOverList();

    /// <summary>
    /// table.remove(marks, i) inside a loop forward over marks: each removal moves the items after it down a place, so the
    /// loop skips the one that moved into i. Going backwards, a removal only moves items the loop has been past.
    /// </summary>
    private static IEnumerable<LogicFinding> RemoveWhileLoopingForward(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (ForwardOverList().Match(masked[index]) is not { Success: true } loop || Body(masked, index) is not { } body) continue;

            var variable = loop.Groups["variable"].Value;
            var list = loop.Groups["list"].Value;
            var removal = new Regex($@"\btable\.remove\s*\(\s*{Regex.Escape(list)}\s*,\s*{Regex.Escape(variable)}\s*\)");
            if (!Enumerable.Range(body.First, body.End - body.First).Any(line => removal.IsMatch(masked[line]))) continue;

            var line = source.Lines[index];
            var range = loop.Groups["range"];

            yield return new LogicFinding(id, index + 1,
                $"table.remove moves every item after the one it removes down a place, so going forward the loop skips the item that " +
                $"moved into position {variable} - and #{list}, worked out once as the loop began, now reaches past the end",
                LocalFix.ReplaceLine(id, $"Loop backwards: for {variable} = #{list}, 1, -1",
                    "Going backwards, a removal only moves items the loop has already been past, so none is skipped.",
                    source.Path, index + 1, line[..range.Index] + $"#{list}, 1, -1" + line[(range.Index + range.Length)..]));
        }
    }

    /// <summary><c>local ages = {</c>: a table made where it is named, whose entries follow on this line or the next ones.</summary>
    [GeneratedRegex(@"^\s*(?:local\s+)?(?<name>[A-Za-z_]\w*)\s*=\s*\{")]
    private static partial Regex TableMade();

    /// <summary>An entry given a key of its own - <c>Ada = 36</c> or <c>["Ada"] = 36</c> - rather than the next position.</summary>
    [GeneratedRegex(@"^\s*(?<key>[A-Za-z_]\w*|\[\s*(?:""[^""]*""|'[^']*')\s*\])\s*=(?!=)")]
    private static partial Regex KeyedEntry();

    /// <summary>A statement a line can begin with, so lines can go before it: not elseif, else, until or end, which close a block.</summary>
    [GeneratedRegex(@"^\s*(?!(?:elseif|else|until|end)\b)(?:[A-Za-z_]|$)")]
    private static partial Regex StartsAStatement();

    /// <summary>
    /// The text between a table's { and the } that closes it, from the line it opens on - with the line it closes on - or null
    /// if it is never closed: as masked, to find its entries by, and as written, to show their keys from - in the same places,
    /// as masking only blanks.
    /// </summary>
    private static (string Inside, string Written, int Closing)? TableText(IReadOnlyList<string> masked, IReadOnlyList<string> lines, int opening, int braceIndex)
    {
        var depth = 0;
        var inside = new System.Text.StringBuilder();
        var written = new System.Text.StringBuilder();

        for (var index = opening; index < masked.Count; index++)
        {
            var line = masked[index];
            for (var position = index == opening ? braceIndex : 0; position < line.Length; position++)
            {
                var character = line[position];
                if (character == '{') depth++;
                if (character == '}' && --depth == 0) return (inside.ToString(), written.ToString(), index);
                if (depth > 0 && !(depth == 1 && character == '{'))
                {
                    inside.Append(character);
                    written.Append(lines[index][position]);
                }
            }
            inside.Append('\n');
            written.Append('\n');
        }

        return null;
    }

    /// <summary>
    /// Where each of a table's entries is in its text: split at the commas and semicolons between them - not those inside a
    /// bracket of an entry's own - and leaving out any that are only space.
    /// </summary>
    private static List<(int Start, int Length)> Entries(string inside)
    {
        var entries = new List<(int Start, int Length)>();
        var depth = 0;
        var start = 0;

        for (var position = 0; position <= inside.Length; position++)
        {
            var atEnd = position == inside.Length;
            var character = atEnd ? ' ' : inside[position];
            if (character is '{' or '(' or '[') depth++;
            if (character is '}' or ')' or ']') depth--;
            if (!atEnd && !(depth == 0 && character is ',' or ';')) continue;

            if (inside[start..position].Trim().Length > 0) entries.Add((start, position - start));
            start = position + 1;
        }

        return entries;
    }

    /// <summary>
    /// #ages where ages was made with only keyed entries - <c>{Ada = 36, Bob = 20}</c> - and nothing puts items at positions 1, 2
    /// and on. # counts the items from position 1 until the first gap, and such a table has nothing at 1, so #ages is 0. Left
    /// alone wherever the table is named again, given items by table.insert or by a position, or given a metatable - whose
    /// __len can change what # says. The change counts the keys with pairs on the lines before, where lines can go before it.
    /// </summary>
    private static IEnumerable<LogicFinding> LengthOfKeyedTable(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (TableMade().Match(masked[index]) is not { Success: true } made ||
                TableText(masked, source.Lines, index, made.Index + made.Length - 1) is not { } table) continue;

            var keyed = Entries(table.Inside).Select(entry => (entry.Start, Key: KeyedEntry().Match(table.Inside, entry.Start, entry.Length))).ToList();
            if (keyed.Count == 0 || !keyed.All(entry => entry.Key.Success)) continue;

            var name = made.Groups["name"].Value;
            var named = Regex.Escape(name);
            // Named again - given a new value, or as a function's parameter or a loop's variable - or given items at positions:
            // by table.insert, or by an index that is not quoted text.
            var changedElsewhere = new Regex(
                $@"^\s*(?:local\s+)?{named}\s*=(?!=)|\bfunction\b[^(]*\([^)]*(?<![\w.]){named}\b|\bfor\b[^=]*(?<![\w.]){named}\b[^=]*\bin\b|" +
                $@"\btable\.(?:insert|move)\s*\(\s*{named}\b|(?<![\w.]){named}\s*\[\s*(?![""'])[^\]]*\]\s*=(?!=)|\bsetmetatable\s*\(\s*{named}\b");
            if (Enumerable.Range(0, masked.Count).Any(line => (line < index || line > table.Closing) && changedElsewhere.IsMatch(masked[line]))) continue;

            var keys = keyed.Select(entry => table.Written.Substring(entry.Key.Groups["key"].Index, entry.Key.Groups["key"].Length)).ToList();
            var lengthOf = new Regex($@"#\s*{named}(?![\w.\[(])");
            var counted = $"{name}Count";
            var countNameFree = !masked.Any(line => Regex.IsMatch(line, $@"(?<![\w.]){Regex.Escape(counted)}(?!\w)"));

            for (var use = table.Closing + 1; use < masked.Count; use++)
            {
                if (lengthOf.Match(masked[use]) is not { Success: true } length) continue;

                var message = $"`{name}` was given only keyed entries ({string.Join(", ", keys.Take(2))}{(keys.Count > 2 ? ", ..." : "")}), " +
                              $"and # counts only the items at positions 1, 2, 3 and on - so #{name} is 0, with no error";

                var previous = Enumerable.Range(0, use).Reverse().Select(line => masked[line].TrimEnd()).FirstOrDefault(line => line.Length > 0) ?? "";
                var continuesALine = Regex.IsMatch(previous, @"(?:[,({=+\-*/<>~]|\.\.|\band|\bor|\bnot)$");
                if (!countNameFree || continuesALine || !StartsAStatement().IsMatch(masked[use]))
                {
                    yield return new LogicFinding(id, use + 1, message, null);
                    continue;
                }

                var line = source.Lines[use];
                var indent = line[..(line.Length - line.TrimStart().Length)];
                var countedInstead = line[..length.Index] + counted + line[(length.Index + length.Length)..];
                var countTheKeys = LocalFix.ReplaceLine(id, $"Count its keys with pairs: for _ in pairs({name}) do {counted} = {counted} + 1 end",
                    "pairs goes through every key a table has, whatever it is, so adding 1 for each counts them all.",
                    source.Path, use + 1, countedInstead);

                yield return new LogicFinding(id, use + 1, message, countTheKeys with
                {
                    NewLines = [$"{indent}local {counted} = 0", $"{indent}for _ in pairs({name}) do {counted} = {counted} + 1 end", countedInstead],
                });
            }
        }
    }
}
