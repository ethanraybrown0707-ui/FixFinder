using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// backup = marks, taken as a copy of a list or an array, when it is the same list under a second name - each run with its
/// language's own toolchain, which said nothing: after marks.append(100), Python's backup printed [70, 80, 90, 100]; after
/// marks[0] = 100, Java's and C#'s backup[0] printed 100; after marks.push(100), JavaScript's backup printed [ 70, 80, 90, 100 ];
/// and Ruby, Dart, Go, Lua and PowerShell showed the change through the second name in the same way.
/// </summary>
public static partial class SharedListPatterns
{
    /// <summary>A list the code makes: whether it is a list - Java's List, C#'s List&lt;T&gt; - rather than an array, and the type of its items, where the code names one.</summary>
    private sealed record MadeList(bool IsList, string ItemType);

    /// <summary>
    /// How one language makes a list or an array, gives it a second name, and changes it - and the copy that keeps its values -
    /// with what the language calls its plain array, a list in Python, Ruby and Dart, and the sigil its variables are written
    /// with, PowerShell's $.
    /// </summary>
    private sealed record Sharing(
        string Id, IReadOnlySet<string> Extensions, Syntax Syntax, string ArrayIsCalled, Regex Made, Regex SecondName,
        Func<string, Regex> Changes, Func<string, MadeList, string> Copy, string Sigil = "");

    private static readonly Sharing[] Languages =
    [
        new("logic-python-list-shared", CodePattern.Files(".py", ".pyw"), Syntax.Python, "list",
            new Regex(@"^\s*(?<name>[A-Za-z_]\w*)\s*=\s*(?<list>\[|list\(|sorted\(|.+?\.split\()"),
            new Regex(@"^\s*(?<copy>[A-Za-z_]\w*)\s*=\s*(?<original>[A-Za-z_]\w*)\s*$"),
            name => new Regex($@"(?<![\w.]){name}\s*(?:\.\s*(?:append|extend|insert|remove|pop|sort|reverse|clear)\s*\(|\[[^\]]*\]\s*[-+*/]?=(?!=)|\+=)|\bdel\s+{name}\s*\["),
            (original, _) => $"{original}.copy()"),
        new("logic-java-array-shared", CodePattern.Files(".java"), Syntax.CLike, "array",
            new Regex(@"\b[A-Za-z_]\w*\s*\[\s*\]\s+(?<name>[A-Za-z_]\w*)\s*=|\b[A-Za-z_]\w*\s+(?<name>[A-Za-z_]\w*)\s*\[\s*\]\s*=|\b(?<list>List|ArrayList|LinkedList)\s*<[^<>]*>\s+(?<name>[A-Za-z_]\w*)\s*="),
            new Regex(@"^\s*(?:[A-Za-z_][\w<>\[\], ]*?\s+)?(?<copy>[A-Za-z_]\w*)\s*=\s*(?<original>[A-Za-z_]\w*)\s*;\s*$"),
            name => new Regex($@"(?<![\w.]){name}\s*(?:\[[^\]]*\]\s*(?:[-+*/%]?=(?!=)|\+\+|--)|\.\s*(?:add|addAll|remove|removeIf|set|clear|sort|replaceAll)\s*\()|\b(?:Arrays|Collections)\s*\.\s*(?:sort|fill|reverse|shuffle)\s*\(\s*{name}\b|(?:\+\+|--){name}\s*\["),
            (original, made) => made.IsList ? $"new java.util.ArrayList<>({original})" : $"{original}.clone()"),
        new("logic-js-array-shared", CodePattern.Files(".js", ".mjs", ".cjs"), Syntax.CLike, "array",
            new Regex(@"\b(?:const|let|var)\s+(?<name>[A-Za-z_$][\w$]*)\s*=\s*(?<list>\[|Array\.from\(|[^;\n]*?\.(?:split|map|filter|slice|concat)\()"),
            new Regex(@"^\s*(?:(?:const|let|var)\s+)?(?<copy>[A-Za-z_$][\w$]*)\s*=\s*(?<original>[A-Za-z_$][\w$]*)\s*;?\s*$"),
            name => new Regex($@"(?<![\w$.]){Regex.Escape(name)}\s*(?:\[[^\]]*\]\s*(?:[-+*/%]?=(?!=)|\+\+|--)|\.\s*(?:push|pop|shift|unshift|splice|sort|reverse|fill|copyWithin)\s*\()"),
            (original, _) => $"[...{original}]"),
        new("logic-csharp-array-shared", CodePattern.Files(".cs"), Syntax.CLike, "array",
            new Regex(@"\b[A-Za-z_]\w*\s*\[\s*\]\s+(?<name>[A-Za-z_]\w*)\s*=|\bvar\s+(?<name>[A-Za-z_]\w*)\s*=\s*new\s*(?:[A-Za-z_]\w*\s*)?\[|\b(?<list>List)\s*<[^<>]*>\s+(?<name>[A-Za-z_]\w*)\s*=|\bvar\s+(?<name>[A-Za-z_]\w*)\s*=\s*new\s+(?<list>List)\s*<"),
            new Regex(@"^\s*(?:[A-Za-z_][\w<>\[\], ]*?\s+)?(?<copy>[A-Za-z_]\w*)\s*=\s*(?<original>[A-Za-z_]\w*)\s*;\s*$"),
            name => new Regex($@"(?<![\w.]){name}\s*(?:\[[^\]]*\]\s*(?:[-+*/%]?=(?!=)|\+\+|--)|\.\s*(?:Add|AddRange|Insert|Remove|RemoveAt|RemoveAll|Clear|Sort|Reverse)\s*\()|\bArray\s*\.\s*(?:Sort|Reverse|Fill|Clear)\s*\(\s*{name}\b"),
            (original, made) => made.IsList ? $"{original}.ToList()" : $"{original}.ToArray()"),
        new("logic-ruby-array-shared", CodePattern.Files(".rb"), Syntax.Python, "array",
            new Regex(@"^\s*(?<name>[A-Za-z_]\w*)\s*=\s*(?<list>\[|Array\.new|.+?\.(?:split|map|select|to_a|sort)\b)"),
            new Regex(@"^\s*(?<copy>[A-Za-z_]\w*)\s*=\s*(?<original>[A-Za-z_]\w*)\s*$"),
            name => new Regex($@"(?<![\w.@$]){name}\s*(?:<<|\.(?:push|append|pop|shift|unshift|insert|delete|delete_at|clear|sort!|reverse!|map!|concat)(?![\w?])|\[[^\]]*\]\s*[-+*/]?=(?!=))"),
            (original, _) => $"{original}.dup"),
        new("logic-dart-list-shared", CodePattern.Files(".dart"), Syntax.CLike, "list",
            new Regex(@"\b(?:var|final|List\s*<[^<>]*>)\s+(?<name>[A-Za-z_]\w*)\s*=\s*(?<list>\[|<[^<>]*>\s*\[|List\s*\.)"),
            new Regex(@"^\s*(?:(?:var|final|List\s*<[^<>]*>)\s+)?(?<copy>[A-Za-z_]\w*)\s*=\s*(?<original>[A-Za-z_]\w*)\s*;\s*$"),
            name => new Regex($@"(?<![\w.]){name}\s*(?:\.\s*(?:add|addAll|insert|remove|removeAt|removeLast|clear|sort|shuffle)\s*\(|\[[^\]]*\]\s*(?:[-+*/~]?=(?!=)|\+\+|--))"),
            (original, _) => $"[...{original}]"),
        // A slice: changing an item through either name changes the one array both share. append may give a new array, so it is not counted.
        new("logic-go-slice-shared", CodePattern.Files(".go"), Syntax.CLike, "slice",
            new Regex(@"\b(?<name>[A-Za-z_]\w*)\s*:?=\s*\[\](?<type>[\w.]+)\s*\{|\bvar\s+(?<name>[A-Za-z_]\w*)\s+\[\](?<type>[\w.]+)"),
            new Regex(@"^\s*(?<copy>[A-Za-z_]\w*)\s*:?=\s*(?<original>[A-Za-z_]\w*)\s*$"),
            name => new Regex($@"(?<![\w.]){name}\s*\[[^\]]*\]\s*(?:[-+*/]?=(?!=)|\+\+|--)|\b(?:sort\.(?:Ints|Strings|Float64s)|slices\.(?:Sort|Reverse))\(\s*{name}\s*\)"),
            (original, made) => $"append([]{made.ItemType}{{}}, {original}...)"),
        // A table made as a list - {70, 80, 90}, not {name = "Ada"} - whose items table.unpack gives, in order.
        new("logic-lua-table-shared", CodePattern.Files(".lua"), Syntax.Lua, "table",
            new Regex(@"^\s*(?:local\s+)?(?<name>[A-Za-z_]\w*)\s*=\s*(?<list>\{(?!\s*[A-Za-z_]\w*\s*=))"),
            new Regex(@"^\s*(?:local\s+)?(?<copy>[A-Za-z_]\w*)\s*=\s*(?<original>[A-Za-z_]\w*)\s*$"),
            name => new Regex($@"(?<![\w.:]){name}\s*\[[^\]]*\]\s*=(?!=)|\btable\.(?:insert|remove|sort)\(\s*{name}\b"),
            (original, _) => $"{{table.unpack({original})}}"),
        new("logic-powershell-array-shared", CodePattern.Files(".ps1"), Syntax.Python, "array",
            new Regex(@"^\s*\$(?<name>[A-Za-z_]\w*)\s*=\s*(?<list>@\(|\d+\s*,)"),
            new Regex(@"^\s*\$(?<copy>[A-Za-z_]\w*)\s*=\s*\$(?<original>[A-Za-z_]\w*)\s*$"),
            name => new Regex($@"\${name}\s*\[[^\]]*\]\s*=(?!=)|\[(?:array|System\.Array)\]::(?:Sort|Reverse)\(\s*\${name}\b", RegexOptions.IgnoreCase),
            (original, _) => $"{original}.Clone()", Sigil: "$"),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
        Languages.Select(language => (ILogicPattern)new CodePattern(language.Id, language.Extensions, language.Syntax,
            (id, source, masked) => SharedList(id, source, masked, language), Severity.Error)).ToList();

    /// <summary>
    /// A list given a second name, then changed through one of them, and the other read after the change - as a copy that kept
    /// the old values would be. The change makes the second name a copy.
    /// </summary>
    private static IEnumerable<LogicFinding> SharedList(string id, SourceFile source, IReadOnlyList<string> masked, Sharing language)
    {
        var lists = new Dictionary<string, MadeList>(StringComparer.Ordinal);
        foreach (Match made in masked.SelectMany(code => language.Made.Matches(code)))
            lists[made.Groups["name"].Value] = new MadeList(made.Groups["list"].Value is "List" or "ArrayList" or "LinkedList", made.Groups["type"].Value);

        if (lists.Count == 0) yield break;

        var sigil = Regex.Escape(language.Sigil);

        for (var index = 0; index < masked.Count; index++)
        {
            if (language.SecondName.Match(masked[index]) is not { Success: true } named) continue;

            var copy = named.Groups["copy"].Value;
            var original = named.Groups["original"];
            if (!lists.TryGetValue(original.Value, out var made) || copy == original.Value) continue;

            // The first later line that changes the list through either name - before either is given another list.
            var givenAgain = new Regex($@"^\s*(?:[A-Za-z_][\w<>\[\], ]*?\s+)?{sigil}(?:{Regex.Escape(copy)}|{Regex.Escape(original.Value)})\s*:?=(?!=)");
            int? changedAt = null;
            var changedThrough = "";
            for (var later = index + 1; later < masked.Count && changedAt is null; later++)
            {
                if (givenAgain.IsMatch(masked[later])) break;

                foreach (var name in new[] { original.Value, copy })
                {
                    if (!language.Changes(name).IsMatch(masked[later])) continue;
                    changedAt = later;
                    changedThrough = name;
                    break;
                }
            }

            if (changedAt is not { } change) continue;

            // The other name read after the change - read from the lines as written, so f"{backup}" and `${backup}` count.
            var other = changedThrough == copy ? original.Value : copy;
            var otherRead = new Regex($@"(?<![\w$.]){sigil}{Regex.Escape(other)}\b(?!\s*=(?!=))");
            if (!Enumerable.Range(change + 1, masked.Count - change - 1).Any(after => otherRead.IsMatch(source.Lines[after]))) continue;

            var line = source.Lines[index];
            var kind = made.IsList ? "list" : language.ArrayIsCalled;
            string Shown(string name) => language.Sigil + name;
            var copied = language.Copy(original.Value, made);

            yield return new LogicFinding(id, index + 1,
                $"{Shown(copy)} = {Shown(original.Value)} does not copy the {kind}: both names are the same {kind}, so changing it through " +
                $"{Shown(changedThrough)} on line {change + 1} changes {Shown(other)} too - and {Shown(other)}, read after that, does not hold the " +
                "values it had",
                LocalFix.ReplaceLine(id, $"Make a copy: {Shown(copy)} = {language.Sigil}{copied}",
                    $"{language.Sigil}{copied} makes a new {kind} with the same items, so a change to one no longer shows in the other.",
                    source.Path, index + 1, line[..original.Index] + copied + line[(original.Index + original.Length)..]));
        }
    }
}
