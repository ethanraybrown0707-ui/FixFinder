using System.Text.RegularExpressions;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Logic;

/// <summary>Reading the blocks of brace languages - C, C++, Java, C#, JavaScript - from masked lines.</summary>
internal static partial class BraceBlocks
{
    [GeneratedRegex(@"\((?<parameters>[^()]*)\)\s*(?:const\s*)?(?:throws\s+[\w.,\s]+)?(?:=>|\{|$)")]
    private static partial Regex ParameterList();

    [GeneratedRegex(@"^\s*(?:(?:public|private|protected|internal|static|final|virtual|override|async|abstract|sealed|synchronized|explicit|inline|constexpr)\s+)*[\w<>\[\],.?:*&\s]*?\b(?<name>[A-Za-z_]\w*)\s*\([^;]*$")]
    private static partial Regex MethodHeader();

    /// <summary>The lines inside the block a header opens: 0-based first line and the index of the closing brace's line.</summary>
    public static (int First, int End)? Body(IReadOnlyList<string> masked, int header)
    {
        var start = masked[header].Contains('{') ? header : NextCode(masked, header + 1);
        if (start < 0) return null;

        if (!masked[start].TrimStart().StartsWith('{') && start != header)
            return (start, start + 1);

        return NativeCourse.BlockEnd(masked, start) is { } end ? (start + 1, end) : null;
    }

    /// <summary>One level of indentation as the file uses it: a tab, or the smallest step in spaces between neighbouring lines.</summary>
    public static string IndentStep(IReadOnlyList<string> lines)
    {
        if (lines.Any(line => line.StartsWith('\t'))) return "\t";

        var widths = lines.Where(line => line.Trim().Length > 0).Select(line => line.Length - line.TrimStart(' ').Length).ToList();
        var steps = widths.Zip(widths.Skip(1), (a, b) => Math.Abs(a - b)).Where(step => step > 0).ToList();

        return new string(' ', steps.Count > 0 ? Math.Min(steps.Min(), 8) : 4);
    }

    public static string Text(IReadOnlyList<string> masked, int first, int end) =>
        string.Join("\n", Enumerable.Range(first, Math.Max(0, end - first)).Select(k => masked[k]));

    public static int NextCode(IReadOnlyList<string> masked, int from)
    {
        for (var k = from; k < masked.Count; k++) if (masked[k].Trim().Length > 0) return k;
        return -1;
    }

    /// <summary>The names of the parameters of the method or constructor a line is inside, or none when that cannot be told.</summary>
    public static IReadOnlyList<string> EnclosingParameters(IReadOnlyList<string> masked, int line)
    {
        var depth = 0;

        for (var k = line; k >= 0; k--)
        {
            depth += masked[k].Count(c => c == '}') - masked[k].Count(c => c == '{');
            if (depth >= 0 || !MethodHeader().IsMatch(masked[k])) continue;
            if (Regex.IsMatch(masked[k], @"^\s*(?:if|for|while|switch|catch|else)\b")) continue;

            var header = masked[k];
            for (var more = k + 1; more < masked.Count && !header.Contains(')'); more++) header += " " + masked[more];

            if (ParameterList().Match(header) is not { Success: true } list) return [];

            return list.Groups["parameters"].Value
                .Split(',')
                .Select(p => Regex.Match(p.Split('=')[0].Trim(), @"(?<name>[A-Za-z_]\w*)\s*(?:\[\s*\])*$").Groups["name"].Value)
                .Where(n => n.Length > 0)
                .ToList();
        }

        return [];
    }
}
