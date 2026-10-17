using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A map on a line of its own, run for what its function does - printing each item - in a language whose map waits until its
/// result is used. Nothing uses it, so the function never runs: marks.map((mark) => print(mark)) in Dart, map(print, marks) in
/// Python and marks.Select(mark => { Console.WriteLine(mark); return mark; }) in C# each printed nothing for marks of 70, 80
/// and 90, and dart analyze, Python and dotnet build said nothing.
/// </summary>
public static partial class LazyMapPatterns
{
    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        new CodePattern("logic-dart-map-never-run", CodePattern.Files(".dart"), Syntax.CLike, DartMapNeverRun, Severity.Error, Confidence.Certain),
        new CodePattern("logic-python-map-never-run", CodePattern.Files(".py", ".pyw"), Syntax.Python, PythonMapNeverRun, Severity.Error, Confidence.Certain),
        new CodePattern("logic-csharp-select-never-run", CodePattern.Files(".cs"), Syntax.CLike, CSharpSelectNeverRun, Severity.Error, Confidence.Certain),
    ];

    /// <summary>What a mapped function does that needs it to run: calls something - print(mark) - or gives a variable a value.</summary>
    [GeneratedRegex(@"^(?:[A-Za-z_][\w.]*\s*\(|[A-Za-z_][\w.\[\]]*\s*(?:[-+*/]?=)(?!=))")]
    private static partial Regex DoesSomething();

    /// <summary><c>marks.map((mark) => print(mark));</c> - or with a block, <c>marks.map((mark) { print(mark); });</c>.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)(?<list>[A-Za-z_][\w.]*)\.map\(\s*\(\s*(?<item>[A-Za-z_]\w*)\s*\)\s*(?:=>\s*(?<expression>.+?)|(?<block>\{.*\}))\s*\)\s*;\s*$")]
    private static partial Regex DartMapStatement();

    /// <summary><c>map(print, marks)</c> or <c>map(lambda mark: print(mark), marks)</c> on a line of its own.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)map\(\s*(?:lambda\s+(?<item>[A-Za-z_]\w*)\s*:\s*(?<expression>.+?)|(?<function>[A-Za-z_][\w.]*))\s*,\s*(?<list>[A-Za-z_][\w.]*)\s*\)\s*$")]
    private static partial Regex PythonMapStatement();

    /// <summary><c>marks.Select(mark => { Console.WriteLine(mark); return mark; });</c> on a line of its own.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)(?<list>[A-Za-z_][\w.]*)\.Select\(\s*\(?\s*(?<item>[A-Za-z_]\w*)\s*\)?\s*=>\s*\{\s*(?<statements>.*?)\s*return\b[^;]*;\s*\}\s*\)\s*;\s*$")]
    private static partial Regex CSharpSelectStatement();

    /// <summary>Python's built-in functions that only work out a value from what they are given, and do nothing else.</summary>
    private static readonly HashSet<string> ValueOnlyBuiltIns =
    [
        "str", "int", "float", "bool", "len", "abs", "round", "sorted", "list", "tuple", "set", "dict", "repr", "type", "sum", "min",
        "max", "ord", "chr", "hex", "bin", "oct", "format",
    ];

    private static string Message(string what, string list) =>
        $"{what} waits until its result is used, and nothing uses it - so the function is never run for any item of {list}, and " +
        "nothing it does happens";

    private static IEnumerable<LogicFinding> DartMapNeverRun(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (DartMapStatement().Match(masked[index]) is not { Success: true } map) continue;

            var line = source.Lines[index];
            var list = map.Groups["list"].Value;
            var item = map.Groups["item"].Value;
            var block = map.Groups["block"];
            var body = block.Success ? line.Substring(block.Index, block.Length) : line.Substring(map.Groups["expression"].Index, map.Groups["expression"].Length) + ";";
            if (!block.Success && !DoesSomething().IsMatch(map.Groups["expression"].Value)) continue;

            yield return new LogicFinding(id, index + 1, Message($"{list}.map", list),
                LocalFix.ReplaceLine(id, $"Run it for each item: for (var {item} in {list}) ...",
                    $"A for loop runs its body for each item of {list} straight away. map only makes a list that works each item out when it is read.",
                    source.Path, index + 1, $"{map.Groups["indent"].Value}for (var {item} in {list}) {body}"));
        }
    }

    private static IEnumerable<LogicFinding> PythonMapNeverRun(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var namesInCode = masked.SelectMany(code => Regex.Matches(code, @"[A-Za-z_]\w*")).Select(found => found.Value).ToHashSet(StringComparer.Ordinal);

        for (var index = 0; index < masked.Count; index++)
        {
            if (PythonMapStatement().Match(masked[index]) is not { Success: true } map) continue;

            var line = source.Lines[index];
            var indent = map.Groups["indent"].Value;
            var list = map.Groups["list"].Value;
            string item, call;

            if (map.Groups["function"].Success)
            {
                // A built-in that only works a value out - map(str, marks) - does nothing when it runs: its result was the point.
                if (ValueOnlyBuiltIns.Contains(map.Groups["function"].Value)) continue;

                // map(print, marks): the loop's variable is the list's name without its s - mark for marks - or item, whichever the
                // code does not use already.
                var named = new[] { list.EndsWith('s') && list.Length > 1 && !list.Contains('.') ? list[..^1] : "", "item", "each" }
                    .FirstOrDefault(candidate => candidate.Length > 0 && !namesInCode.Contains(candidate));
                if (named is null) continue;
                item = named;
                call = $"{map.Groups["function"].Value}({item})";
            }
            else
            {
                if (!DoesSomething().IsMatch(map.Groups["expression"].Value)) continue;
                item = map.Groups["item"].Value;
                call = line.Substring(map.Groups["expression"].Index, map.Groups["expression"].Length);
            }

            yield return new LogicFinding(id, index + 1, Message("map", list),
                new LocalFix
                {
                    RuleId = id,
                    Title = $"Run it for each item: for {item} in {list}:",
                    Explanation = $"A for loop runs its body for each item of {list} straight away. map only makes an iterator that works each " +
                                  "item out when it is read.",
                    File = source.Path,
                    StartLine = index + 1,
                    RemoveCount = 1,
                    NewLines = [$"{indent}for {item} in {list}:", $"{indent}    {call}"],
                });
        }
    }

    private static IEnumerable<LogicFinding> CSharpSelectNeverRun(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (CSharpSelectStatement().Match(masked[index]) is not { Success: true } select) continue;

            var line = source.Lines[index];
            var list = select.Groups["list"].Value;
            var item = select.Groups["item"].Value;
            var statements = select.Groups["statements"];
            if (statements.Length == 0) continue;

            yield return new LogicFinding(id, index + 1, Message($"{list}.Select", list),
                LocalFix.ReplaceLine(id, $"Run it for each item: foreach (var {item} in {list}) {{ ... }}",
                    $"A foreach loop runs its body for each item of {list} straight away. Select only makes a sequence that works each item out " +
                    "when it is read, and the value the lambda gives back is not needed.",
                    source.Path, index + 1,
                    $"{select.Groups["indent"].Value}foreach (var {item} in {list}) {{ {line.Substring(statements.Index, statements.Length)} }}"));
        }
    }
}
