using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A loop over one list that names each item after another: <c>for height in widths:</c> in a file that has a list called
/// heights. The item's name says which list was meant - often a loop copied from the one above it, with its item renamed and
/// its list left as it was - and the loop goes through the other one. The change goes through the list the name says. One
/// check, read in each language's own way of going through a list's items, from the table below.
/// </summary>
/// <remarks>
/// A possible mistake, not a likely one: a list of names may be gone through as students on purpose. It is said only when the
/// list the item's name says - its plural, heights for height, classes for class, cities for city - is a name the file has,
/// the list gone through is not named for the item at all, and an earlier loop over the same list says why: it named its
/// items for that list - the loop this one was copied from - or it filled the list the item's name says, as QuixBugs'
/// BUCKETSORT fills counts while going through arr and then goes through arr again for each count. A name matching a list
/// alone is not enough: over FixFinder's own code it matched more than forty loops that go through the list they mean.
/// </remarks>
public static partial class WrongCollectionPatterns
{
    /// <summary>How one language goes through a list's items: each item's name as "item", the list's as "items".</summary>
    private sealed record Dialect(string Id, IReadOnlySet<string> Files, Syntax Syntax, Regex Loop);

    private static readonly Dialect[] Dialects =
    [
        new("logic-python-wrong-list", CodePattern.Files(".py", ".pyw"), Syntax.Python,
            new(@"^\s*(?:async\s+)?for\s+(?<item>[A-Za-z_]\w*)\s+in\s+(?<items>[A-Za-z_]\w*)\s*:\s*$" +
                @"|^\s*for\s+[A-Za-z_]\w*\s*,\s*(?<item>[A-Za-z_]\w*)\s+in\s+enumerate\(\s*(?<items>[A-Za-z_]\w*)\s*\)\s*:\s*$")),
        new("logic-java-wrong-list", CodePattern.Files(".java"), Syntax.CLike,
            new(@"\bfor\s*\(\s*(?:final\s+)?[\w.<>\[\]]+\s+(?<item>[A-Za-z_]\w*)\s*:\s*(?<items>[A-Za-z_]\w*)\s*\)")),
        new("logic-wrong-list", CodePattern.Files(".cpp", ".cc", ".cxx", ".c++", ".cs", ".js", ".mjs", ".cjs"), Syntax.CLike,
            new(@"\bfor\s*\(\s*(?:const\s+)?(?:auto|[\w:<>]+)\s*&{0,2}\s*(?<item>[A-Za-z_]\w*)\s*:\s*(?<items>[A-Za-z_]\w*)\s*\)" +
                @"|\bforeach\s*\(\s*(?:var|[\w<>\[\]]+)\s+(?<item>[A-Za-z_]\w*)\s+in\s+(?<items>[A-Za-z_]\w*)\s*\)" +
                @"|\bfor\s*\(\s*(?:const|let|var)\s+(?<item>[A-Za-z_$][\w$]*)\s+of\s+(?<items>[A-Za-z_$][\w$]*)\s*\)")),
        new("logic-go-wrong-list", CodePattern.Files(".go"), Syntax.CLike,
            new(@"\bfor\s+\w+\s*,\s*(?<item>[A-Za-z_]\w*)\s*:=\s*range\s+(?<items>[A-Za-z_]\w*)\s*\{")),
        new("logic-rust-wrong-list", CodePattern.Files(".rs"), Syntax.CLike,
            new(@"\bfor\s+&?(?<item>[A-Za-z_]\w*)\s+in\s+&?(?<items>[A-Za-z_]\w*)(?:\.iter\(\))?\s*\{")),
        new("logic-ruby-wrong-list", CodePattern.Files(".rb"), Syntax.Python,
            new(@"(?<items>[a-z_]\w*)\.each\s+do\s*\|\s*(?<item>[a-z_]\w*)\s*\||^\s*for\s+(?<item>[a-z_]\w*)\s+in\s+(?<items>[a-z_]\w*)\b")),
        new("logic-php-wrong-list", CodePattern.Files(".php"), Syntax.CLike,
            new(@"\bforeach\s*\(\s*\$(?<items>[A-Za-z_]\w*)\s+as\s+\$(?<item>[A-Za-z_]\w*)\s*\)")),
        new("logic-lua-wrong-list", CodePattern.Files(".lua"), Syntax.Lua,
            new(@"\bfor\s+\w+\s*,\s*(?<item>[A-Za-z_]\w*)\s+in\s+i?pairs\(\s*(?<items>[A-Za-z_]\w*)\s*\)\s*do\b")),
        new("logic-perl-wrong-list", CodePattern.Files(".pl", ".pm"), Syntax.Perl,
            new(@"\b(?:foreach|for)\s+my\s+\$(?<item>[A-Za-z_]\w*)\s*\(\s*@(?<items>[A-Za-z_]\w*)\s*\)")),
        new("logic-dart-wrong-list", CodePattern.Files(".dart"), Syntax.CLike,
            new(@"\bfor\s*\(\s*(?:final|var|[\w<>]+)\s+(?<item>[A-Za-z_]\w*)\s+in\s+(?<items>[A-Za-z_]\w*)\s*\)")),
        new("logic-scala-wrong-list", CodePattern.Files(".scala", ".sc"), Syntax.Scala,
            new(@"\bfor\s*\(\s*(?<item>[A-Za-z_]\w*)\s*<-\s*(?<items>[A-Za-z_]\w*)\s*\)")),
        new("logic-powershell-wrong-list", CodePattern.Files(".ps1"), Syntax.Python,
            new(@"\bforeach\s*\(\s*\$(?<item>[A-Za-z_]\w*)\s+in\s+\$(?<items>[A-Za-z_]\w*)\s*\)", RegexOptions.IgnoreCase)),
        new("logic-fsharp-wrong-list", CodePattern.Files(".fs", ".fsx"), Syntax.FSharp,
            new(@"\bfor\s+(?<item>[A-Za-z_]\w*)\s+in\s+(?<items>[A-Za-z_]\w*)\s+do\b")),
        new("logic-elixir-wrong-list", CodePattern.Files(".ex", ".exs"), Syntax.Python,
            new(@"\bEnum\.\w+\(\s*(?<items>[a-z_]\w*)\s*,\s*(?:[^,()]+,\s*)?fn\s+(?<item>[a-z_]\w*)\s*(?:,|->)|\bfor\s+(?<item>[a-z_]\w*)\s*<-\s*(?<items>[a-z_]\w*)\s*(?:,|do\b)")),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
        [.. Dialects.Select(dialect => new CodePattern(dialect.Id, dialect.Files, dialect.Syntax,
            (id, source, masked) => WrongLists(id, source, masked, dialect.Loop), Severity.Warning, Confidence.Possible))];

    private static IEnumerable<LogicFinding> WrongLists(string id, SourceFile source, IReadOnlyList<string> masked, Regex loop)
    {
        for (var line = 0; line < masked.Count; line++)
        {
            if (loop.Match(masked[line]) is not { Success: true } going) continue;

            var item = going.Groups["item"].Value;
            var items = going.Groups["items"];
            if (item.Length < 3 || items.Value.Contains(item, StringComparison.OrdinalIgnoreCase)) continue;

            // The list the item's name says, when the file has it: heights for height.
            var meant = Plurals(item).FirstOrDefault(plural => plural != items.Value && masked.Where((_, other) => other != line).Any(text => Names(text, plural)));
            if (meant is null || Plurals(SingularOf(items.Value)).Contains(item)) continue;

            // And why it was meant, from an earlier loop over the same list: one that named its items for the list - the loop this
            // one was copied from - or one that filled the list the item's name says, counts from arr.
            var earlier = EarlierLoops(masked, line, loop).Where(each => each.Items == items.Value).ToList();
            var copied = earlier.Any(each => Plurals(each.Item).Contains(items.Value));
            var filled = earlier.Any(each => Enumerable.Range(each.Header + 1, each.End - each.Header - 1).Any(body => Names(masked[body], meant)));
            if (!copied && !filled) continue;

            var text = source.Lines[line];
            var changed = text[..items.Index] + meant + text[(items.Index + items.Length)..];
            yield return new LogicFinding(id, line + 1,
                $"The loop on line {line + 1} goes through `{items.Value}` but names each item `{item}` - and the file has `{meant}`. If it should go " +
                $"through `{meant}`, as each item's name says, it reads `{changed.Trim()}`.",
                LocalFix.ReplaceLine(id, $"Go through {meant}, as each item's name says",
                    $"Each item is called {item}, and {meant} is the file's list of them; the loop goes through {items.Value} instead. If {items.Value} is " +
                    "the list meant, leave it as it is.",
                    source.Path, line + 1, changed))
            {
                Severity = Severity.Warning,
                Confidence = Confidence.Possible,
            };
        }
    }

    /// <summary>A loop above the line: its first line, where its body ends, and the names of its item and its list.</summary>
    private sealed record EarlierLoop(int Header, int End, string Item, string Items);

    private static IEnumerable<EarlierLoop> EarlierLoops(IReadOnlyList<string> masked, int before, Regex loop)
    {
        for (var header = 0; header < before; header++)
        {
            if (loop.Match(masked[header]) is not { Success: true } going) continue;

            // Its body, by indentation: what is further in than its first line - after a { on a line of its own, as C# writes it.
            var indent = CodeText.Indentation(masked[header]).Length;
            var end = header + 1;
            if (end < masked.Count && masked[end].Trim() == "{" && CodeText.Indentation(masked[end]).Length == indent) end++;
            while (end < masked.Count && (masked[end].Trim().Length == 0 || CodeText.Indentation(masked[end]).Length > indent)) end++;
            if (end > before) continue;

            yield return new EarlierLoop(header, end, going.Groups["item"].Value, going.Groups["items"].Value);
        }
    }

    /// <summary>What a list of the item is called: height's heights, class's classes, city's cities.</summary>
    private static IEnumerable<string> Plurals(string item)
    {
        yield return item + "s";
        if (Regex.IsMatch(item, "(?:s|x|z|ch|sh)$")) yield return item + "es";
        if (Regex.IsMatch(item, "[^aeiou]y$")) yield return item[..^1] + "ies";
    }

    private static string SingularOf(string items) =>
        items.EndsWith("ies", StringComparison.Ordinal) ? items[..^3] + "y"
        : Regex.IsMatch(items, "(?:s|x|z|ch|sh)es$") ? items[..^2]
        : items.EndsWith('s') ? items[..^1]
        : items;

    /// <summary>Whether the line has the name as a name of its own - not a part of another, and not in a string.</summary>
    private static bool Names(string masked, string name) =>
        Regex.IsMatch(masked, $@"(?<![\w.$@])[$@]?{Regex.Escape(name)}(?!\w)");
}
