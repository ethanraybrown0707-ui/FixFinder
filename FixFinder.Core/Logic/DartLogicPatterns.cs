using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Logic;

/// <summary>
/// Mistakes Dart programs make that dart analyze says nothing of - each run with Dart 3.13: a loop to <c>i &lt;= marks.length</c>
/// reads one position past the end, which stops the program with RangeError; two lists compared with ==, which asks whether
/// they are the very same list, so [70, 80] == [70, 80] is false; a decimal worked out by arithmetic, compared exactly; and
/// removeAt(i) in a loop counting up through the same list, which skips the item after each one it removes.
/// </summary>
public static partial class DartLogicPatterns
{
    private static readonly IReadOnlySet<string> DartFiles = CodePattern.Files(".dart");

    private static CodePattern Pattern(
        string id, Func<string, SourceFile, IReadOnlyList<string>, IEnumerable<LogicFinding>> find,
        Severity severity = Severity.Warning, Confidence confidence = Confidence.Likely) =>
        new(id, DartFiles, Syntax.CLike, find, severity, confidence);

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        Pattern("logic-dart-loop-to-length", LoopToLength, Severity.Error),
        Pattern("logic-dart-lists-compared-with-equals", ListsComparedWithEquals, Severity.Error, Confidence.Certain),
        Pattern("logic-dart-float-equality", FloatEquality),
        Pattern("logic-dart-interpolated-property", InterpolatedProperty, Severity.Error, Confidence.Certain),
        Pattern("logic-dart-remove-while-counting-up", RemoveWhileCountingUp, Severity.Error),
    ];

    private static IEnumerable<LogicFinding> LoopToLength(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (DartLoops.LoopToLength().Match(masked[index]) is not { Success: true } loop || BraceBlocks.Body(masked, index) is not { } body) continue;

            var variable = loop.Groups["variable"].Value;
            var list = loop.Groups["list"].Value;
            var position = new Regex($@"\b{Regex.Escape(list)}\s*\[\s*{Regex.Escape(variable)}\s*\]");
            if (!Enumerable.Range(body.First, body.End - body.First).Any(line => position.IsMatch(masked[line]))) continue;

            yield return new LogicFinding(id, index + 1,
                $"`{variable} <= {list}.length` lets {variable} reach {list}.length, so on the loop's last pass `{list}[{variable}]` asks for a " +
                "position one past the end - which stops the program with RangeError",
                DartLoops.StrictlyBelowFix(id, source, index + 1, loop));
        }
    }

    /// <summary>A list made with a list literal: <c>var first = [70, 80];</c>, or <c>List&lt;int&gt; first = [...]</c>.</summary>
    [GeneratedRegex(@"\b(?:var|final|List<[^>]*>)\s+(?<name>\w+)\s*=\s*(?:<[^>]*>)?\[")]
    private static partial Regex ListDeclared();

    [GeneratedRegex(@"(?<![\w.])(?<first>\w+)\s*(?<op>==|!=)\s*(?<second>\w+)(?![\w.(\[])")]
    private static partial Regex Comparison();

    private static IEnumerable<LogicFinding> ListsComparedWithEquals(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var lists = masked.SelectMany(line => ListDeclared().Matches(line)).Select(list => list.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);
        if (lists.Count < 2) yield break;

        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match compared in Comparison().Matches(masked[index]))
            {
                var first = compared.Groups["first"].Value;
                var second = compared.Groups["second"].Value;
                if (first == second || !lists.Contains(first) || !lists.Contains(second)) continue;

                var same = compared.Groups["op"].Value == "==";
                var itemByItem = $"({first}.length == {second}.length && Iterable.generate({first}.length).every((i) => {first}[i] == {second}[i]))";
                var line = source.Lines[index];

                yield return new LogicFinding(id, index + 1,
                    $"== on two lists asks whether they are the very same list, not whether they hold the same items - so `{first} == {second}` " +
                    $"is {(same ? "false" : "true")} for two lists made apart, whatever they hold",
                    LocalFix.ReplaceLine(id, "Compare the lists item by item",
                        "The fixed comparison checks the two lists are as long as each other and that each item equals the one at the same position.",
                        source.Path, index + 1, line[..compared.Index] + (same ? itemByItem : "!" + itemByItem) + line[(compared.Index + compared.Length)..]));
            }
        }
    }

    [GeneratedRegex(@"\b(?:var|final|double|num)\s+(?<name>\w+)\s*(?<assign>[-+*/]?=)(?!=)\s*(?<value>[^;]+);")]
    private static partial Regex Assignment();

    [GeneratedRegex(@"(?<![\w.])(?<name>\w+)\s*(?<op>==|!=)\s*(?<value>\d*\.\d*[1-9]\d*)(?![\w.])")]
    private static partial Regex ExactComparison();

    private static IEnumerable<LogicFinding> FloatEquality(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var worked = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in masked)
        {
            if (Assignment().Match(line) is not { Success: true } assignment) continue;

            var value = assignment.Groups["value"].Value;
            var byArithmetic = assignment.Groups["assign"].Value != "=" || Regex.IsMatch(Regex.Replace(value, @"\d*\.\d+|\d+\.\d*", "0"), @"[-+*/]");
            if (byArithmetic && Regex.IsMatch(value, @"\d*\.\d+|\d+\.\d*")) worked.Add(assignment.Groups["name"].Value);
        }

        if (worked.Count == 0) yield break;

        for (var index = 0; index < masked.Count; index++)
        {
            if (ExactComparison().Match(masked[index]) is not { Success: true } compared || !worked.Contains(compared.Groups["name"].Value)) continue;

            var name = compared.Groups["name"].Value;
            var value = compared.Groups["value"].Value;
            var close = compared.Groups["op"].Value == "==" ? $"({name} - {value}).abs() < 1e-9" : $"({name} - {value}).abs() >= 1e-9";
            var line = source.Lines[index];

            yield return new LogicFinding(id, index + 1,
                $"`{name}` is a decimal worked out by arithmetic, compared with {value} exactly - and arithmetic on decimals is rarely " +
                "exact: 0.1 + 0.2 is 0.30000000000000004",
                LocalFix.ReplaceLine(id, $"Compare within a tiny tolerance: {close}",
                    $"{close} is true when {name} is {value} to within a billionth, which arithmetic's tiny errors stay inside.",
                    source.Path, index + 1, line[..compared.Index] + close + line[(compared.Index + compared.Length)..]));
        }
    }

    /// <summary><c>$marks.length</c> or <c>$marks[0]</c> inside text - a name with a property or a position after it, not in ${ }.</summary>
    [GeneratedRegex(@"(?<!\\)\$(?<name>[A-Za-z_]\w*)(?<after>\.[A-Za-z_]\w*|\[[^\]]+\])")]
    private static partial Regex InterpolatedWithMore();

    /// <summary>The text on a line, between ' or " - with \ escaping - and not raw text, r'...', where $ is only a $.</summary>
    private static IEnumerable<(int Start, int End)> TextIn(string line)
    {
        for (var index = 0; index < line.Length; index++)
        {
            if (line[index] == '/' && index + 1 < line.Length && line[index + 1] == '/') yield break;
            if (line[index] is not ('"' or '\'')) continue;

            var quote = line[index];
            var raw = index > 0 && line[index - 1] == 'r';
            var end = index + 1;
            while (end < line.Length && line[end] != quote) end += line[end] == '\\' ? 2 : 1;
            if (end >= line.Length) yield break;

            if (!raw) yield return (index + 1, end);
            index = end;
        }
    }

    /// <summary>
    /// '$marks.length' in text: Dart puts in the value of the name after $ alone - $marks - and writes .length after it as it is, so
    /// Dart 3.13 printed [70, 80, 90].length. ${ } puts in the value of the whole expression.
    /// </summary>
    private static IEnumerable<LogicFinding> InterpolatedProperty(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < source.Lines.Count; index++)
        {
            var line = source.Lines[index];
            var found = TextIn(line)
                .SelectMany(text => InterpolatedWithMore().Matches(line[text.Start..text.End]).Select(match => (Match: match, At: text.Start + match.Index)))
                .ToList();
            if (found.Count == 0) continue;

            var changed = line;
            foreach (var (match, at) in Enumerable.Reverse(found))
                changed = changed[..at] + "${" + match.Value[1..] + "}" + changed[(at + match.Length)..];

            var first = found[0].Match;
            yield return new LogicFinding(id, index + 1,
                $"in text Dart puts in the value of ${first.Groups["name"].Value} alone, and writes the {first.Groups["after"].Value} after it as it " +
                "is - so the text holds the whole of the value, and then the words",
                LocalFix.ReplaceLine(id, $"Put the whole expression in: ${{{first.Value[1..]}}}",
                    $"${{ }} puts in the value of everything between its braces - {first.Value[1..]}, not {first.Groups["name"].Value} alone.",
                    source.Path, index + 1, changed));
        }
    }

    /// <summary><c>for (var i = 0; i &lt; marks.length; i++) {</c> - a loop up through a list's positions.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)for\s*\(\s*(?:var|int)\s+(?<variable>[A-Za-z_]\w*)\s*=\s*0\s*;\s*\k<variable>\s*<\s*(?<list>[A-Za-z_]\w*)\.length\s*;\s*\k<variable>\+\+\s*\)\s*\{\s*$")]
    private static partial Regex CountingUpThroughList();

    /// <summary>
    /// removeAt(i) inside a loop counting up through the same list: each removal moves the items after it down a place, so
    /// the loop then skips the one that moved into position i - [70, 40, 40, 90] kept a 40. Counting down, a removal only
    /// moves items the loop has been past.
    /// </summary>
    private static IEnumerable<LogicFinding> RemoveWhileCountingUp(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (CountingUpThroughList().Match(masked[index]) is not { Success: true } loop || BraceBlocks.Body(masked, index) is not { } body) continue;

            var variable = loop.Groups["variable"].Value;
            var list = loop.Groups["list"].Value;
            var removal = new Regex($@"(?<![\w.]){Regex.Escape(list)}\s*\.\s*removeAt\s*\(\s*{Regex.Escape(variable)}\s*\)");
            if (!Enumerable.Range(body.First, body.End - body.First).Any(line => removal.IsMatch(masked[line]))) continue;

            var line = source.Lines[index];
            var counted = Regex.Match(line, @"\b(?:var|int)\b").Value;
            yield return new LogicFinding(id, index + 1,
                $"removeAt moves every item after the one it removes down a place, so counting up the loop skips the item that moved into " +
                $"position {variable} - two in a row to remove leave one behind",
                LocalFix.ReplaceLine(id, $"Count down instead: for ({counted} {variable} = {list}.length - 1; {variable} >= 0; {variable}--)",
                    "Counting down, a removal only moves items the loop has already been past, so none is skipped.",
                    source.Path, index + 1, $"{loop.Groups["indent"].Value}for ({counted} {variable} = {list}.length - 1; {variable} >= 0; {variable}--) {{"));
        }
    }
}
