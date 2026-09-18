using System.Text.RegularExpressions;
using FixFinder.Core.Logic;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>What the guard rules share: finding a division on a line, and the language's way of writing "if it is zero, use 0".</summary>
internal static partial class Guards
{
    [GeneratedRegex(@"(?<a>[A-Za-z_][\w.]*(?:\([^()]*\)|\[[^\]]*\])?|\d+(?:\.\d+)?|\))\s*(?<op>//|/|%)(?![/=*])\s*(?<b>[A-Za-z_][\w.]*(?:\(\s*[^()]*\)|\[[^\]]*\])?)")]
    private static partial Regex Division();

    /// <summary>The line with its one division by a name or a call rewritten to give 0 when the divisor is 0, or null.</summary>
    public static string? GuardDivision(string line, Syntax syntax, bool python)
    {
        var masked = CodeText.Mask(line, syntax);
        var divisions = Division().Matches(masked).ToList();
        if (divisions.Count == 0 || divisions.Select(d => d.Groups["b"].Value).Distinct().Count() != 1) return null;

        var division = divisions[0];
        var start = division.Groups["a"].Index;

        if (division.Groups["a"].Value == ")")
        {
            start = Brackets.Opening(masked, division.Groups["a"].Index);
            if (start < 0) return null;
        }

        var end = division.Groups["b"].Index + division.Groups["b"].Length;
        var expression = line[start..end];
        var divisor = line.Substring(division.Groups["b"].Index, division.Groups["b"].Length);

        var guarded = python ? $"({expression} if {divisor} else 0)" : $"({divisor} == 0 ? 0 : {expression})";

        return line[..start] + guarded + line[end..];
    }

    private static readonly (string Wrong, string Right)[] Typography =
    [
        ("\u201C", "\""), ("\u201D", "\""), ("\u201E", "\""), ("\u2018", "'"), ("\u2019", "'"), ("\u201A", "'"),
        ("\u2013", "-"), ("\u2014", "-"), ("\u2212", "-"), ("\u2026", "..."), ("\u00A0", " "), ("\u200B", ""),
    ];

    /// <summary>The line with curly quotes, long dashes and hidden spaces from a word processor turned into the characters code uses.</summary>
    public static string? StraightenTypography(string line)
    {
        var straightened = Typography.Aggregate(line, (text, pair) => text.Replace(pair.Wrong, pair.Right, StringComparison.Ordinal));
        return straightened == line ? null : straightened;
    }

    public static LocalFix? StraightenFix(string id, SourceFile source, int number) =>
        source.Line(number) is { } line && StraightenTypography(line) is { } straightened
            ? LocalFix.ReplaceLine(id, "Retype the curly quotes and dashes as plain ones",
                "The line has characters that a word processor or a web page put in - curly quotes, a long dash or a hidden space. They look " +
                "like code but are different characters, and no compiler reads them. The plain keyboard versions are.",
                source.Path, number, straightened)
            : null;
}

/// <summary>What the base-case rules share: a function that calls itself with its number one smaller, and never stops.</summary>
internal static partial class Recursion
{
    /// <summary>The line of the function's header and the parameter it counts down, when the function calls itself as name(n - 1).</summary>
    public static (int Header, string Parameter)? CountingDown(IReadOnlyList<string> masked, int errorLine, Regex header, bool python)
    {
        for (var k = errorLine - 1; k >= 0; k--)
        {
            if (header.Match(masked[k]) is not { Success: true } match) continue;

            var name = Regex.Escape(match.Groups["name"].Value);
            var parameters = match.Groups["parameters"].Value.Split(',').Select(p => Regex.Match(p.Split('=', ':')[0].Trim(), @"(?<name>[A-Za-z_]\w*)\s*$").Groups["name"].Value).ToList();

            var end = python ? PythonBlocks.Body(masked, k).End : Brackets.BlockEnd(masked, k) ?? masked.Count;
            var body = string.Join("\n", Enumerable.Range(k + 1, Math.Max(0, end - k - 1)).Select(i => masked[i]));

            if (Regex.IsMatch(body, python ? @"\breturn\s+\S" : @"\breturn\s+[^;\s]")) return null;
            if (Regex.IsMatch(body, @"(?:^|\n)\s*if\b")) return null;

            foreach (var parameter in parameters.Where(p => p.Length > 0))
            {
                if (Regex.IsMatch(body, $@"(?<![\w.]){name}\s*\((?:[^()]*,\s*)?{Regex.Escape(parameter)}\s*-\s*1\s*[,)]")) return (k, parameter);
            }

            return null;
        }

        return null;
    }
}
