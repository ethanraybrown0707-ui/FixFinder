using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// totl = total + mark, where total = total + mark was meant: in a language where giving a name a value makes the name, a
/// misspelt one makes a new variable, and the one meant is never changed. Run with Python, Lua, PHP, Node.js and PowerShell,
/// adding 5, 10 and 20, each printed 0 where 35 was meant, with nothing said; ruby -w warns of a variable given a value and
/// never used, so Ruby is not here, and a language that declares its names refuses one it does not know. Found when the name
/// given a value is used nowhere else in the file - not even in text - and a name one letter away, which the file also gives
/// values to, is read on that same line. The change gives the value to that name.
/// </summary>
public static partial class MisspeltNamePatterns
{
    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        Misspelt("logic-python-misspelt-name", CodePattern.Files(".py", ".pyw"), Syntax.Python, @"(?<name>[A-Za-z_]\w*)", "(?<![\\w.])NAME(?!\\w)", false, "([{", "a new variable"),
        Misspelt("logic-lua-misspelt-name", CodePattern.Files(".lua"), Syntax.Lua, @"(?<name>[A-Za-z_]\w*)", "(?<![\\w.:])NAME(?!\\w)", false, "([{", "a new global variable"),
        Misspelt("logic-js-misspelt-name", CodePattern.Files(".js", ".mjs", ".cjs"), Syntax.CLike, @"(?<name>[A-Za-z_$][\w$]*)", "(?<![\\w$.])NAME(?![\\w$])", false, "([",
            "a new global variable - or, in strict mode, stops with a ReferenceError"),
        Misspelt("logic-php-misspelt-name", CodePattern.Files(".php"), Syntax.CLike, @"(?<name>\$\w+)", "NAME(?!\\w)", false, "([", "a new variable"),
        Misspelt("logic-powershell-misspelt-name", CodePattern.Files(".ps1"), Syntax.Python, @"(?<name>\$\w+)", "NAME(?!\\w)", true, "([", "a new variable"),
    ];

    /// <summary>
    /// Whether each line starts inside brackets <paramref name="opening"/> left open - a call's keyword argument, colour=color,
    /// or a table's field on a line of its own, which is not a name given a value. Braces that hold a block - a function's,
    /// a loop's - are not counted where the language writes its blocks with them.
    /// </summary>
    private static bool[] InsideBrackets(IReadOnlyList<string> masked, string opening)
    {
        var inside = new bool[masked.Count];
        var depth = 0;
        for (var index = 0; index < masked.Count; index++)
        {
            inside[index] = depth > 0;
            foreach (var character in masked[index])
            {
                if (opening.Contains(character)) depth++;
                else if (depth > 0 && ")]}"[..opening.Length].Contains(character)) depth--;
            }
        }

        return inside;
    }

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "self", "this", "true", "false", "null", "nil", "None", "True", "False", "and", "or", "not", "local", "return", "function", "end",
    };

    private static CodePattern Misspelt(string id, IReadOnlySet<string> extensions, Syntax syntax, string name, string occurrence, bool ignoreCase, string opening,
        string made) =>
        new(id, extensions, syntax, (patternId, source, masked) => MisspeltName(patternId, source, masked,
                // A name given a value on a line of its own - not compared, not declared with local, let, const or var.
                new Regex($@"^(?<indent>\s*){name}\s*=(?![=>~])\s*(?<value>\S.*)$"), name, occurrence, ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None, opening, made),
            Severity.Error, Confidence.Likely);

    [GeneratedRegex(@"\b(?:let|var|const|local)\s+(?<name>[A-Za-z_$][\w$]*)\s*=")]
    private static partial Regex Declared();

    /// <summary>Whether two names are one letter apart - one added, taken away, changed or swapped with its neighbour - with neither a digit.</summary>
    internal static bool OneLetterApart(string first, string second)
    {
        if (first.Length < 4 || second.Length < 4 || Math.Abs(first.Length - second.Length) > 1 || first == second) return false;

        var start = 0;
        while (start < first.Length && start < second.Length && first[start] == second[start]) start++;
        var firstEnd = first.Length;
        var secondEnd = second.Length;
        while (firstEnd > start && secondEnd > start && first[firstEnd - 1] == second[secondEnd - 1])
        {
            firstEnd--;
            secondEnd--;
        }

        var firstDiffers = first[start..firstEnd];
        var secondDiffers = second[start..secondEnd];
        if ((firstDiffers + secondDiffers).Any(char.IsDigit)) return false;

        // One letter added or taken away, one changed, or two neighbours swapped.
        return (firstDiffers.Length + secondDiffers.Length == 1) ||
               (firstDiffers.Length == 1 && secondDiffers.Length == 1) ||
               (firstDiffers.Length == 2 && secondDiffers.Length == 2 && firstDiffers[0] == secondDiffers[1] && firstDiffers[1] == secondDiffers[0]);
    }

    private static IEnumerable<LogicFinding> MisspeltName(string id, SourceFile source, IReadOnlyList<string> masked, Regex assignment, string name, string occurrence,
        RegexOptions options, string opening, string made)
    {
        var inside = InsideBrackets(masked, opening);
        var given = masked.Select((line, index) => (Index: index, Match: assignment.Match(line))).Where(each => each.Match.Success && !inside[each.Index]).ToList();
        var comparison = options.HasFlag(RegexOptions.IgnoreCase) ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        // The names the file gives values to - and, in JavaScript and Lua, declares: let total = 0, local total = 0.
        var givenNames = given.Select(each => each.Match.Groups["name"].Value)
            .Concat(masked.SelectMany(line => Declared().Matches(line).Select(declared => declared.Groups["name"].Value)))
            .ToHashSet(comparison);
        Regex Occurrence(string word) => new(occurrence.Replace("NAME", Regex.Escape(word)), options);

        // How many times each name is written in the file - in its code and in any text - and given a value, each counted in one reading.
        var appearances = new Regex(occurrence.Replace("NAME", name.Replace("(?<name>", "(?<word>", StringComparison.Ordinal)), options)
            .Matches(string.Join("\n", source.Lines))
            .GroupBy(written => written.Groups["word"].Value, comparison)
            .ToDictionary(group => group.Key, group => group.Count(), comparison);
        var timesGiven = given.GroupBy(each => each.Match.Groups["name"].Value, comparison).ToDictionary(group => group.Key, group => group.Count(), comparison);

        foreach (var (index, assigned) in given)
        {
            var misspelt = assigned.Groups["name"].Value;
            if (Keywords.Contains(misspelt.TrimStart('$'))) continue;

            // Used nowhere but where it is given a value - in the code or in any text.
            if (appearances.GetValueOrDefault(misspelt) != timesGiven[misspelt]) continue;

            var value = assigned.Groups["value"];
            var meant = Regex.Matches(value.Value, name).Select(word => word.Groups["name"].Value)
                .FirstOrDefault(word => givenNames.Contains(word) && !comparison.Equals(word, misspelt) &&
                                        OneLetterApart(misspelt.TrimStart('$'), word.TrimStart('$')) && Occurrence(word).IsMatch(masked[index][value.Index..]));
            if (meant is null) continue;

            var line = source.Lines[index];
            var nameGroup = assigned.Groups["name"];

            yield return new LogicFinding(id, index + 1,
                $"{misspelt} is given a value here and used nowhere else, and {meant} - one letter away - is the name the rest of the file uses: " +
                $"giving {misspelt} a value makes {made}, and {meant} is left as it was",
                LocalFix.ReplaceLine(id, $"Give the value to {meant}",
                    $"{meant} is the name the rest of the program reads, so giving it the value is what changes the result.",
                    source.Path, index + 1, line[..nameGroup.Index] + meant + line[(nameGroup.Index + nameGroup.Length)..]));
        }
    }
}
