using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A whole number written with a 0 in front - int code = 010 - which Java, C, C++ and Go read in octal, base 8: each printed
/// 8, with nothing said by javac -Xlint:all, gcc -Wall -Wextra or go vet. Left alone where a file mode is given, as in
/// chmod(path, 0755), where octal is meant, and where the number is the same either way - 07. The change writes the number
/// without its leading zeros.
/// </summary>
public static partial class OctalLiteralPatterns
{
    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        new CodePattern("logic-octal-literal", CodePattern.Files(".c", ".cpp", ".cc", ".cxx", ".c++", ".java"), Syntax.CLike, OctalLiteral, Severity.Error, Confidence.Likely),
        new CodePattern("logic-go-octal-literal", CodePattern.Files(".go"), Syntax.CLike, OctalLiteral, Severity.Error, Confidence.Likely),
    ];

    /// <summary><c>010</c> given to a name, in a list, or returned - a 0, then only the digits 0 to 7.</summary>
    [GeneratedRegex(@"(?<=(?:[=,{(]|:=|\breturn)\s*)(?<number>0(?<digits>[0-7]+))(?=\s*[;,})\n]|\s*$)")]
    private static partial Regex LeadingZero();

    /// <summary>A line that gives a file mode, where octal is meant: chmod, mkdir, umask, open, os.Chmod, os.WriteFile and their like.</summary>
    [GeneratedRegex(@"\b(?:chmod|fchmod|mkdir|umask|open|creat|Chmod|Mkdir|MkdirAll|OpenFile|WriteFile|FileMode|Perm)\b")]
    private static partial Regex FileMode();

    private static IEnumerable<LogicFinding> OctalLiteral(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (FileMode().IsMatch(masked[index])) continue;

            foreach (Match zero in LeadingZero().Matches(masked[index]))
            {
                var digits = zero.Groups["digits"].Value.TrimStart('0');
                if (digits.Length == 0) continue;

                var octal = Convert.ToInt64(digits, 8);
                if (octal.ToString() == digits) continue;

                var number = zero.Groups["number"];
                var line = source.Lines[index];

                yield return new LogicFinding(id, index + 1,
                    $"A whole number written with 0 in front is read in octal - base 8 - so {number.Value} is {octal}, not {digits}",
                    LocalFix.ReplaceLine(id, $"Write it without the 0: {digits}",
                        $"Without the 0 in front, {digits} is read as the number it says.",
                        source.Path, index + 1, line[..number.Index] + digits + line[(number.Index + number.Length)..]));
            }
        }
    }
}
