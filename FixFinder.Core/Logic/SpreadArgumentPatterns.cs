using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// An array given where its items were meant, one by one - run with Node.js, which said nothing: for marks of 70, 85 and 90,
/// Math.max(marks) and Math.min(marks) were NaN, as the array became the text "70,85,90", which is no number; an array of one
/// item, [70], happened to give 70. Math.max(...marks) gave 90.
/// </summary>
public static partial class SpreadArgumentPatterns
{
    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        new CodePattern("logic-js-math-max-of-array", CodePattern.Files(".js", ".mjs", ".cjs"), Syntax.CLike, MathOfArray, Severity.Error, Confidence.Certain),
    ];

    /// <summary>The names the code makes an array: [70, 85, 90], Array.from(...), or a method that gives one - split, map, filter.</summary>
    [GeneratedRegex(@"\b(?:const|let|var)\s+(?<name>[A-Za-z_$][\w$]*)\s*=\s*(?:\[|Array\.from\(|[^;\n]*?\.(?:split|map|filter|slice|concat)\()")]
    private static partial Regex ArrayMade();

    /// <summary><c>Math.max(marks)</c> or <c>Math.min(marks)</c> - one name, and nothing else, given to it.</summary>
    [GeneratedRegex(@"\bMath\s*\.\s*(?<method>max|min)\s*\(\s*(?<name>[A-Za-z_$][\w$]*)\s*\)")]
    private static partial Regex MathOfOneName();

    /// <summary>
    /// Math.max or Math.min given an array: they take numbers, one by one, so the array is turned into one number - from its
    /// text, "70,85,90" - which is NaN. The change spreads the array into its items with ...
    /// </summary>
    private static IEnumerable<LogicFinding> MathOfArray(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var arrays = masked.SelectMany(code => ArrayMade().Matches(code)).Select(found => found.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);
        if (arrays.Count == 0) yield break;

        for (var index = 0; index < masked.Count; index++)
        {
            if (MathOfOneName().Matches(masked[index]).FirstOrDefault(found => arrays.Contains(found.Groups["name"].Value)) is not { } call) continue;

            var line = source.Lines[index];
            var name = call.Groups["name"];
            var method = call.Groups["method"].Value;

            yield return new LogicFinding(id, index + 1,
                $"Math.{method} takes numbers one by one, and {name.Value} is an array: it is turned into one number from its text - such as " +
                "\"70,85,90\", which is no number - so the answer is NaN (an array of one item happens to work)",
                LocalFix.ReplaceLine(id, $"Give it the items: Math.{method}(...{name.Value})",
                    $"... spreads {name.Value} into its items, so Math.{method} is given each number on its own.",
                    source.Path, index + 1, line[..name.Index] + "..." + line[name.Index..]));
        }
    }
}
