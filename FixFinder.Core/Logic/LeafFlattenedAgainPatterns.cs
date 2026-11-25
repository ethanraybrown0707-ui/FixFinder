using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A flattening of a list of items and lists that, for an item that is not a list, gives it to itself - the function written
/// for lists - instead of giving back the item: yield flatten(x) where yield x is meant. Python's flatten is a generator, so
/// calling it does nothing yet and five generator objects came out where 1 2 3 4 5 is right; in a function that goes
/// through what it is given, the item - or a list holding just it - goes back in for ever, until the stack runs out.
/// QuixBugs' flatten is this mistake, in Python and in Java. The change gives back the item, as QuixBugs' correct programs do.
/// </summary>
/// <remarks>
/// Said only when the file shows all of it: a test whether an item is a list - isinstance(x, list), x instanceof ArrayList,
/// item is List&lt;object&gt;, Array.isArray(item), item.([]any), item.is_a?(Array), type(item) ==, is_array($item), ref $item
/// eq, $item -is [array], item is List, case inner: List[...], item when is_list(item), [Item | Rest]) when is_list(Item) -
/// and in its else, or the next case, a call of the function the test is in given that item: flatten(x), flatten([item]),
/// Flatten(new List&lt;object&gt; { item }), ...flatten([item]), (Get-Flat @(,$item)), flatten([]any{item}).
/// </remarks>
public static partial class LeafFlattenedAgainPatterns
{
    /// <summary>How one language tests that an item is a list - the item in the group named item - and whether its other items are a case or clause of their own rather than an else.</summary>
    private sealed record Dialect(string Language, IReadOnlySet<string> Files, Syntax Syntax, string Token, Regex IsAList, bool Cases = false, bool IgnoreCase = false);

    private static readonly Dialect[] Dialects =
    [
        new("python", CodePattern.Files(".py", ".pyw"), Syntax.Python, "isinstance", new(@"\bisinstance\(\s*(?<item>[A-Za-z_]\w*)\s*,\s*list\s*\)")),
        new("java", CodePattern.Files(".java"), Syntax.CLike, "instanceof", new(@"(?<item>[A-Za-z_]\w*)\s+instanceof\s+(?:Array|Linked)?List\b")),
        new("csharp", CodePattern.Files(".cs"), Syntax.CLike, " is ", new(@"(?<item>[A-Za-z_]\w*)\s+is\s+(?:List|IList|IEnumerable)\s*<")),
        new("js", CodePattern.Files(".js", ".mjs", ".cjs", ".ts", ".mts", ".cts"), Syntax.CLike, "isArray", new(@"\bArray\.isArray\(\s*(?<item>[A-Za-z_$][\w$]*)\s*\)")),
        new("go", CodePattern.Files(".go"), Syntax.CLike, ".(", new(@"(?<item>[A-Za-z_]\w*)\.\(\s*\[\]\s*(?:any|interface\{\})\s*\)")),
        new("ruby", CodePattern.Files(".rb"), Syntax.Python, "_", new(@"(?<item>[A-Za-z_]\w*)\.(?:is_a\?|kind_of\?)\(?\s*Array\b")),
        new("lua", CodePattern.Files(".lua"), Syntax.Lua, "type(", new(@"\btype\(\s*(?<item>[A-Za-z_]\w*)\s*\)\s*==")),
        new("php", CodePattern.Files(".php"), Syntax.CLike, "is_array", new(@"\bis_array\(\s*(?<item>\$[A-Za-z_]\w*)\s*\)")),
        new("perl", CodePattern.Files(".pl", ".pm"), Syntax.Perl, "ref", new(@"\bref\s*\(?\s*(?<item>\$[A-Za-z_]\w*)\s*\)?\s*eq\b")),
        new("powershell", CodePattern.Files(".ps1"), Syntax.Python, "-is", new(@"(?<item>\$[A-Za-z_]\w*)\s+-is\s+\[(?:array|object\[\]|System\.Array)\]", RegexOptions.IgnoreCase), IgnoreCase: true),
        new("dart", CodePattern.Files(".dart"), Syntax.CLike, " is ", new(@"(?<item>[A-Za-z_]\w*)\s+is\s+(?:List|Iterable)\b")),
        new("scala", CodePattern.Files(".scala", ".sc"), Syntax.Scala, "case", new(@"\bcase\s+(?<item>[A-Za-z_]\w*)\s*:\s*(?:List|Seq)\["), Cases: true),
        new("elixir", CodePattern.Files(".ex", ".exs"), Syntax.Python, "is_list", new(@"(?<item>[a-z_]\w*)\s+when\s+is_list\(\s*\k<item>\s*\)\s*->"), Cases: true),
        new("erlang", CodePattern.Files(".erl"), Syntax.Erlang, "is_list", new(@"\[\s*(?<item>[A-Z_]\w*)\s*\|\s*[A-Z_]\w*\s*\]\s*\)\s*when\s+is_list\(\s*\k<item>\s*\)"), Cases: true),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        .. Dialects.Select(dialect => new CodePattern($"logic-{dialect.Language}-leaf-flattened-again", dialect.Files, dialect.Syntax,
            (id, source, masked) => LeavesFlattenedAgain(id, source, masked, dialect), Severity.Error, Confidence.Likely)),
    ];

    private static IEnumerable<LogicFinding> LeavesFlattenedAgain(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        var options = dialect.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
        for (var test = 0; test < masked.Count; test++)
        {
            // The test's own word on the line first - a regex starting at every name is slow over a whole file.
            if (!masked[test].Contains(dialect.Token, StringComparison.OrdinalIgnoreCase)) continue;
            if (dialect.IsAList.Match(masked[test]) is not { Success: true } listTest) continue;
            if (FunctionAt(masked, test) is not { } function) continue;

            // The item one the loop goes through - for x in arr, for (Object x : narr), |item| - not what the function was
            // given: QuixBugs' Java gives anything not a list back to itself, in its correct program too, where it never comes.
            if (!dialect.Cases && !ALoopsItem(masked, test, listTest.Groups["item"].Value, options)) continue;

            // The lines for an item that is not a list, and the name it has there.
            var (lines, item) = dialect.Cases ? NextCase(masked, test, dialect) : ElseOf(masked, test, listTest.Groups["item"].Value);
            if (item is null) continue;

            foreach (var line in lines)
            {
                if (GivenToItself(masked[line], function, item, options) is not { } call) continue;
                var written = source.Lines[line];
                var changed = written[..call.Index] + item + written[(call.Index + call.Length)..];
                var told = $"Line {line + 1} gives `{item}`, which is not a list, to `{function}` again - the function written for lists - where the " +
                           $"item itself belongs in the flat list. Python's flatten is a generator, and calling it gives back a generator object, " +
                           "not the item; where the function goes through what it is given, the item goes back in for ever, until the stack runs out.";
                yield return new LogicFinding(id, line + 1, told,
                    LocalFix.ReplaceLine(id, $"Give back the item itself: {item}",
                        $"An item that is not a list is already flat: it goes into the result as it is.",
                        source.Path, line + 1, changed));
                break;
            }
        }
    }

    /// <summary>Whether a line just above the test goes through items under the item's name: for x in, for (Object x :, for (const item of, foreach (var item in, |item|, for my $item, range, as $item.</summary>
    private static bool ALoopsItem(IReadOnlyList<string> masked, int test, string item, RegexOptions options)
    {
        var named = new Regex($@"(?<![\w$@.]){Regex.Escape(item)}(?!\w)", options);
        var loop = new Regex(@"\b(?:for|foreach|each|flat_map|map|range|ipairs)\b|\|\s*[A-Za-z_]\w*\s*\|", options);
        return Enumerable.Range(Math.Max(0, test - 4), test - Math.Max(0, test - 4)).Any(at => named.IsMatch(masked[at]) && loop.IsMatch(masked[at]));
    }

    /// <summary>The name of the function a line is in, from the nearest heading at or above it.</summary>
    private static string? FunctionAt(IReadOnlyList<string> masked, int line)
    {
        for (var above = line; above >= 0; above--)
        {
            var heading = Regex.Match(masked[above], @"\b(?:def|defp|function\*?|func|fn|sub)\s+(?<name>[A-Za-z_][\w-]*)|^\s*(?:(?:public|private|protected|static)\s+)*[\w<>\[\],]+\s+(?<name>[A-Za-z_]\w*)\s*\([^)]*\)\s*\{?\s*$|^(?<name>[a-z_]\w*)\s*\(");
            if (heading.Success && !Regex.IsMatch(heading.Groups["name"].Value, @"^(?:if|for|while|switch|return|foreach|catch)$")) return heading.Groups["name"].Value;
        }

        return null;
    }

    /// <summary>The lines of the else after the test - at the test's own indent - and the item's name there, the test's own.</summary>
    private static (List<int> Lines, string? Item) ElseOf(IReadOnlyList<string> masked, int test, string item)
    {
        var indent = CodeText.Indentation(masked[test]).Length;
        for (var at = test + 1; at < masked.Count; at++)
        {
            var text = masked[at];
            if (text.Trim().Length == 0) continue;
            var width = CodeText.Indentation(text).Length;
            if (width < indent) break;
            if (width > indent) continue;

            // A brace on a line of its own, as C# writes them, at the test's indent.
            if (text.Trim() is "{" or "}") continue;
            if (!Regex.IsMatch(text, @"^\s*(?:\}\s*)?else\b(?!\s*if)", RegexOptions.IgnoreCase)) break;

            var lines = new List<int>();
            for (var below = at + 1; below < masked.Count; below++)
            {
                if (masked[below].Trim().Length == 0) continue;
                if (masked[below].Trim() == "{" && CodeText.Indentation(masked[below]).Length == indent && lines.Count == 0) continue;
                if (CodeText.Indentation(masked[below]).Length <= indent) break;
                lines.Add(below);
            }

            return (lines, item);
        }

        return ([], null);
    }

    /// <summary>
    /// The case or clause after the list's - case item =&gt;, item -&gt;, flatten([Item | Rest]) -&gt; - on the next line or the one after,
    /// and the item's name in it.
    /// </summary>
    private static (List<int> Lines, string? Item) NextCase(IReadOnlyList<string> masked, int test, Dialect dialect)
    {
        for (var at = test + 1; at < Math.Min(masked.Count, test + 3); at++)
        {
            var other = dialect.Language switch
            {
                "scala" => Regex.Match(masked[at], @"\bcase\s+(?<item>[A-Za-z_]\w*)\s*=>"),
                "elixir" => Regex.Match(masked[at], @"^\s*(?<item>[a-z_]\w*)\s*->"),
                _ => Regex.Match(masked[at], @"\(\s*\[\s*(?<item>[A-Z_]\w*)\s*\|\s*[A-Z_]\w*\s*\]\s*\)\s*->"),
            };
            if (other.Success) return ([at], other.Groups["item"].Value);
        }

        return ([], null);
    }

    /// <summary>
    /// A call of the function given the item - bare, or in a list of its own - with a spread or brackets around it:
    /// flatten(x), flatten([item]), Flatten(new List&lt;object&gt; { item }), ...flatten([item]), (Get-Flat @(,$item)),
    /// flatten([]any{item}), flatten(List(item)), flatten({ item }).
    /// </summary>
    private static Match? GivenToItself(string text, string function, string item, RegexOptions options)
    {
        var name = Regex.Escape(item);
        var wrapped = $@"(?:{name}|\[\s*{name}\s*\]|\{{\s*{name}\s*\}}|new\s+List\s*<\s*\w+\s*>\s*\{{\s*{name}\s*\}}|@\(\s*,?\s*{name}\s*\)|List\(\s*{name}\s*\)|\[\]\s*(?:any|interface\{{\}})\s*\{{\s*{name}\s*\}})";
        // A spread before the call is taken away with it; one dot before the name, something.flatten(...), is another function's.
        var call = new Regex($@"(?:\.\.\.)?(?<![\w$-])(?<![^.]\.){Regex.Escape(function)}\s*\(\s*{wrapped}\s*\)|\(\s*{Regex.Escape(function)}\s+{wrapped}\s*\)", options);
        return call.Match(text) is { Success: true } found ? found : null;
    }
}
