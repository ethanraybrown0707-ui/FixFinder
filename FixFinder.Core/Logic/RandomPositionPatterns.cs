using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A random position that can be one past the end - run with Python, which said nothing: randint gives a number from its first
/// to its last, both included, so 1,000 calls of random.randint(0, len(marks)) for three marks gave 0, 1, 2 and 3, and
/// marks[random.randint(0, len(marks))] stopped with IndexError - on the runs that drew 3, not on the others.
/// </summary>
public static partial class RandomPositionPatterns
{
    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        new CodePattern("logic-python-randint-past-end", CodePattern.Files(".py", ".pyw"), Syntax.Python, RandintPastEnd, Severity.Error, Confidence.Certain),
    ];

    /// <summary><c>marks[random.randint(0, len(marks))]</c> - a random position, up to the length itself, used in the same list.</summary>
    [GeneratedRegex(@"(?<list>[A-Za-z_]\w*)\s*\[\s*(?<call>(?:random\s*\.\s*)?randint)\s*\(\s*0\s*,\s*(?<length>len\(\s*\k<list>\s*\))\s*\)\s*\]")]
    private static partial Regex PositionUsedAtOnce();

    /// <summary><c>pick = random.randint(0, len(marks))</c> - a random position, up to the length itself, given a name.</summary>
    [GeneratedRegex(@"^\s*(?<name>[A-Za-z_]\w*)\s*=\s*(?<call>(?:random\s*\.\s*)?randint)\s*\(\s*0\s*,\s*(?<length>len\(\s*(?<list>[A-Za-z_]\w*)\s*\))\s*\)\s*$")]
    private static partial Regex PositionGivenAName();

    private static LogicFinding Found(string id, SourceFile source, int index, string list, Group length) =>
        new(id, index + 1,
            $"randint gives a number from its first to its last, both included - so randint(0, len({list})) can give len({list}), one past the " +
            $"last position in {list}, and {list}[...] then stops with IndexError: not every run, only the ones that draw it",
            LocalFix.ReplaceLine(id, $"Stop at the last position: randint(0, len({list}) - 1)",
                $"len({list}) - 1 is the last position in {list}, so randint gives only positions that are there.",
                source.Path, index + 1, source.Lines[index][..(length.Index + length.Length)] + " - 1" + source.Lines[index][(length.Index + length.Length)..]));

    private static IEnumerable<LogicFinding> RandintPastEnd(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (PositionUsedAtOnce().Match(masked[index]) is { Success: true } atOnce)
            {
                yield return Found(id, source, index, atOnce.Groups["list"].Value, atOnce.Groups["length"]);
                continue;
            }

            if (PositionGivenAName().Match(masked[index]) is not { Success: true } named) continue;

            // The name used as a position in the same list on a later line.
            var list = named.Groups["list"].Value;
            var usedAsPosition = new Regex($@"(?<![\w.]){list}\s*\[\s*{named.Groups["name"].Value}\s*\]");
            if (masked.Skip(index + 1).Any(usedAsPosition.IsMatch)) yield return Found(id, source, index, list, named.Groups["length"]);
        }
    }
}
