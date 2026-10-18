using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A grid made by repeating one row, whose rows are then one list - each run with its language's own toolchain, which said
/// nothing: after grid[0][0] = 1, Python's [[0] * 3] * 3, JavaScript's new Array(3).fill(new Array(3).fill(0)), Ruby's
/// Array.new(3, Array.new(3, 0)) and Dart's List.filled(3, List.filled(3, 0)) each printed a 1 at the start of every row.
/// </summary>
public static partial class SharedRowPatterns
{
    /// <summary>
    /// How one language repeats a row - the whole expression, with the row and how many times - how a line changes a row of the
    /// grid, and the expression that makes each row its own.
    /// </summary>
    private sealed record Repeating(
        string Id, IReadOnlySet<string> Extensions, Syntax Syntax, Regex Repeated, Func<string, Regex> ChangesARow,
        Func<string, string, string> EachRowItsOwn);

    private static readonly Repeating[] Languages =
    [
        new("logic-python-rows-shared", CodePattern.Files(".py", ".pyw"), Syntax.Python,
            new Regex(@"(?<![\w$])(?<name>[A-Za-z_]\w*)\s*=\s*(?<grid>\[\s*(?<row>\[(?:[^\[\]]|\[[^\[\]]*\])*\](?:\s*\*\s*[\w.()]+)?)\s*\]\s*\*\s*(?<rows>[\w.()]+))"),
            name => new Regex($@"(?<![\w.]){name}\s*\[[^\]]+\]\s*(?:\[[^\]]+\]\s*[-+*/]?=(?!=)|\.\s*(?:append|extend|insert|remove|pop|sort|clear)\s*\()"),
            (row, rows) => $"[{row} for _ in range({rows})]"),
        new("logic-js-rows-shared", CodePattern.Files(".js", ".mjs", ".cjs"), Syntax.CLike,
            new Regex(@"(?<![\w$])(?<name>[A-Za-z_$][\w$]*)\s*=\s*(?<grid>(?:new\s+)?Array\(\s*(?<rows>[^()]+?)\s*\)\.fill\(\s*(?<row>\[[^\[\]]*\]|(?:new\s+)?Array\([^()]*\)(?:\.fill\([^()]*\))?)\s*\))"),
            name => new Regex($@"(?<![\w$.]){Regex.Escape(name)}\s*\[[^\]]+\]\s*(?:\[[^\]]+\]\s*[-+*/]?=(?!=)|\.\s*(?:push|pop|shift|unshift|splice|fill|sort)\s*\()"),
            (row, rows) => $"Array.from({{ length: {rows} }}, () => {row})"),
        new("logic-ruby-rows-shared", CodePattern.Files(".rb"), Syntax.Python,
            new Regex(@"(?<![\w$])(?<name>[A-Za-z_]\w*)\s*=\s*(?<grid>Array\.new\(\s*(?<rows>[^,()]+?)\s*,\s*(?<row>\[[^\[\]]*\]|Array\.new\([^()]*\))\s*\))"),
            name => new Regex($@"(?<![\w.]){name}\s*\[[^\]]+\]\s*(?:\[[^\]]+\]\s*[-+*/]?=(?!=)|<<|\.\s*(?:push|pop|shift|unshift|insert|delete|clear)\b)"),
            (row, rows) => $"Array.new({rows}) {{ {row} }}"),
        new("logic-dart-rows-shared", CodePattern.Files(".dart"), Syntax.CLike,
            new Regex(@"(?<![\w$])(?<name>[A-Za-z_]\w*)\s*=\s*(?<grid>List\.filled\(\s*(?<rows>[^,()]+?)\s*,\s*(?<row>(?:<[^<>]*>)?\[[^\[\]]*\]|List\.filled\([^()]*\))\s*\))"),
            name => new Regex($@"(?<![\w.]){name}\s*\[[^\]]+\]\s*(?:\[[^\]]+\]\s*[-+*/~]?=(?!=)|\.\s*(?:add|addAll|insert|remove|removeAt|removeLast|clear|sort)\s*\()"),
            (row, rows) => $"List.generate({rows}, (_) => {row})"),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
        Languages.Select(language => (ILogicPattern)new CodePattern(language.Id, language.Extensions, language.Syntax,
            (id, source, masked) => RowsShared(id, source, masked, language), Severity.Error, Confidence.Certain)).ToList();

    /// <summary>
    /// A grid made by repeating one row, then a row of it changed: every row is the one list the row was made as, so the change
    /// shows in all of them. The change makes each row a list of its own.
    /// </summary>
    private static IEnumerable<LogicFinding> RowsShared(string id, SourceFile source, IReadOnlyList<string> masked, Repeating language)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (language.Repeated.Match(masked[index]) is not { Success: true } made) continue;

            var name = made.Groups["name"].Value;
            var changes = language.ChangesARow(name);
            var changedAt = Enumerable.Range(index + 1, masked.Count - index - 1).FirstOrDefault(later => changes.IsMatch(masked[later]), -1);
            if (changedAt < 0) continue;

            var line = source.Lines[index];
            var grid = made.Groups["grid"];
            var row = line.Substring(made.Groups["row"].Index, made.Groups["row"].Length);
            var rows = line.Substring(made.Groups["rows"].Index, made.Groups["rows"].Length).Trim();
            var eachRowItsOwn = language.EachRowItsOwn(row, rows);

            yield return new LogicFinding(id, index + 1,
                $"this repeats one row, rather than making {rows} of them: every row of {name} is the same list, so changing one row - on line " +
                $"{changedAt + 1} - changes every row",
                LocalFix.ReplaceLine(id, $"Make each row its own: {eachRowItsOwn}",
                    $"{eachRowItsOwn} works the row out again for each row, so each is a new list, and changing one leaves the others as they were.",
                    source.Path, index + 1, line[..grid.Index] + eachRowItsOwn + line[(grid.Index + grid.Length)..]));
        }
    }
}
