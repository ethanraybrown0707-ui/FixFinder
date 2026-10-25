using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// Fortran's declarations and text used in a way that builds with nothing said by gfortran -Wall -Wextra, and runs wrong: a
/// name given a value where it is declared, in a subroutine or function, is saved - given the value once, when the program
/// starts, and kept from one call to the next - so a counter set to 0 that way printed 1 and then 2; and a character(len=10)
/// name holds ten characters, the text and spaces after it, so print *, 'Hello ', name, '!' printed "Hello Ada       !" and
/// len(name) was 10 for Ada.
/// </summary>
public static partial class FortranLibraryPatterns
{
    private static readonly IReadOnlySet<string> FortranFiles = CodePattern.Files(".f90", ".f95", ".f03", ".f08");

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        new CodePattern("logic-fortran-implicit-save", FortranFiles, Syntax.Fortran, ImplicitSave, Severity.Error),
        new CodePattern("logic-fortran-padded-text-printed", FortranFiles, Syntax.Fortran, PaddedTextPrinted),
        new CodePattern("logic-fortran-len-of-padded-text", FortranFiles, Syntax.Fortran, LenOfPaddedText, Severity.Warning, Confidence.Possible),
    ];

    [GeneratedRegex(@"^\s*(?:(?:pure|elemental|recursive|impure)\s+)*(?:(?:integer|real|logical|double\s+precision|character|complex)(?:\s*\([^)]*\))?\s+)?(?<kind>subroutine|function)\s+\w+", RegexOptions.IgnoreCase)]
    private static partial Regex UnitStart();

    [GeneratedRegex(@"^\s*end\s*(?:subroutine|function)\b", RegexOptions.IgnoreCase)]
    private static partial Regex UnitEnd();

    /// <summary>A declaration - a type, its attributes, then :: and the names - or use, implicit, or a blank line among them.</summary>
    [GeneratedRegex(@"^\s*(?:(?:integer|real|logical|double\s+precision|character|complex|type\s*\()[^!]*::|use\b|implicit\b|$)", RegexOptions.IgnoreCase)]
    private static partial Regex DeclarationOrSetup();

    /// <summary><c>integer :: count = 0</c> - one name, given a value where it is declared, with no parameter or save.</summary>
    [GeneratedRegex(@"^(?<declaration>\s*(?<type>(?:integer|real|logical|double\s+precision|character|complex)(?:\s*\([^)]*\))?)(?<attributes>(?:\s*,\s*[a-z]+(?:\([^)]*\))?)*)\s*::\s*(?<name>[a-z_]\w*))\s*=\s*(?<value>[^,!]+?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex InitialisedDeclaration();

    private static IEnumerable<LogicFinding> ImplicitSave(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var start = 0; start < masked.Count; start++)
        {
            if (UnitStart().Match(masked[start]) is not { Success: true } unit) continue;

            // The unit's declarations: the lines after its first that are declarations, until the first that is not.
            var lastDeclaration = start;
            for (var line = start + 1; line < masked.Count && DeclarationOrSetup().IsMatch(masked[line]); line++)
                if (masked[line].Trim().Length > 0) lastDeclaration = line;

            for (var line = start + 1; line <= lastDeclaration; line++)
            {
                if (InitialisedDeclaration().Match(masked[line]) is not { Success: true } declared) continue;
                var attributes = declared.Groups["attributes"].Value;
                if (Regex.IsMatch(attributes, @"\b(?:parameter|save|intent|pointer)\b", RegexOptions.IgnoreCase)) continue;

                var written = source.Lines[line];
                var name = declared.Groups["name"].Value;
                var value = written.Substring(declared.Groups["value"].Index, declared.Groups["value"].Length);
                var indent = CodeText.Indentation(written);
                var moved = source.Lines.Skip(line + 1).Take(lastDeclaration - line).ToList();
                moved.Add(written[..(declared.Groups["declaration"].Index + declared.Groups["declaration"].Length)]);
                moved.Add($"{indent}{name} = {value}");

                yield return new LogicFinding(id, line + 1,
                    $"A value given to {name} where it is declared, in a {unit.Groups["kind"].Value.ToLowerInvariant()}, gives it the save attribute: it is " +
                    $"given {value} once, when the program starts, and keeps what it held when the {unit.Groups["kind"].Value.ToLowerInvariant()} last ended - " +
                    "a counter set to 0 this way printed 1 and then 2 for two calls",
                    new LocalFix
                    {
                        RuleId = id, Title = $"Give it its value each call: {name} = {value}, after the declarations",
                        Explanation = $"An assignment runs every time the {unit.Groups["kind"].Value.ToLowerInvariant()} does, so {name} starts at {value} on every call.",
                        File = source.Path, StartLine = line + 1, RemoveCount = lastDeclaration - line + 1, NewLines = moved,
                    });
            }
        }
    }

    /// <summary>The names declared character(len=N) - a length written as a number, so each holds that many characters, spaces and all.</summary>
    private static HashSet<string> PaddedNames(IReadOnlyList<string> masked) =>
        masked.Select(line => Regex.Match(line, @"^\s*character\s*\(\s*(?:len\s*=\s*)?\d+\s*\)(?<attributes>[^:]*)::(?<names>[^!=]*)$", RegexOptions.IgnoreCase))
            .Where(declared => declared.Success && !Regex.IsMatch(declared.Groups["attributes"].Value, @"\b(?:parameter|allocatable)\b", RegexOptions.IgnoreCase))
            .SelectMany(declared => declared.Groups["names"].Value.Split(',').Select(name => Regex.Match(name, @"^\s*(?<name>[a-z_]\w*)\s*$", RegexOptions.IgnoreCase)))
            .Where(name => name.Success)
            .Select(name => name.Groups["name"].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<LogicFinding> PaddedTextPrinted(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var padded = PaddedNames(masked);
        if (padded.Count == 0) yield break;

        for (var index = 0; index < masked.Count; index++)
        {
            var printed = Regex.Match(masked[index], @"^\s*(?:print\s*\*\s*,|write\s*\(\s*\*\s*,\s*\*\s*\))(?<items>.*)$", RegexOptions.IgnoreCase);
            if (!printed.Success) continue;

            var items = printed.Groups["items"];
            // A padded name with more printed after it - its spaces show before what follows.
            foreach (Match item in Regex.Matches(items.Value, @"(?<![\w%(])(?<name>[a-z_]\w*)(?=\s*,)", RegexOptions.IgnoreCase))
            {
                var name = item.Groups["name"].Value;
                if (!padded.Contains(name)) continue;

                var line = source.Lines[index];
                var at = items.Index + item.Index;

                yield return new LogicFinding(id, index + 1,
                    $"{name} is declared with a length, and holds that many characters - the text, and spaces after it - so the spaces are " +
                    $"printed before what comes next: print *, 'Hello ', name, '!' printed \"Hello Ada       !\"",
                    LocalFix.ReplaceLine(id, $"Print it without its spaces: trim({name})",
                        $"trim gives {name} without the spaces after its text.",
                        source.Path, index + 1, line[..at] + $"trim({name})" + line[(at + name.Length)..]));
            }
        }
    }

    private static IEnumerable<LogicFinding> LenOfPaddedText(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var padded = PaddedNames(masked);
        if (padded.Count == 0) yield break;

        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match length in Regex.Matches(masked[index], @"(?<![\w%])(?<len>len)\s*\(\s*(?<name>[a-z_]\w*)\s*\)", RegexOptions.IgnoreCase))
            {
                var name = length.Groups["name"].Value;
                if (!padded.Contains(name)) continue;

                var line = source.Lines[index];
                var len = length.Groups["len"];

                yield return new LogicFinding(id, index + 1,
                    $"len({name}) is the length {name} is declared with, not the length of its text - {name} keeps spaces after its text up to that " +
                    "length, so len was 10 for Ada in a character(len=10)",
                    LocalFix.ReplaceLine(id, $"Count the text: len_trim({name})",
                        $"len_trim counts {name}'s characters up to the last one that is not a space.",
                        source.Path, index + 1, line[..len.Index] + "len_trim" + line[(len.Index + len.Length)..]));
            }
        }
    }
}
