using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A percentage worked out from two whole numbers by dividing first - score / total * 100. The division comes first, and two
/// whole numbers divide to a whole number, so 45 / 60 is 0, and 0 * 100 is 0. Each was run with the language's own
/// toolchain, and each printed 0 where 75 was meant, with nothing said by gcc and g++ -Wall -Wextra, javac -Xlint:all, the
/// C# compiler, go vet, rustc, Scala CLI, ruby -w, F# Interactive or gfortran -Wall. The change multiplies first -
/// score * 100 / total, which printed 75 in each. PercentDividedFirstLiveTests runs each mistake, and its change.
/// </summary>
public static partial class PercentDividedFirstPatterns
{
    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        Divided("logic-percent-divided-first", CodePattern.Files(".c", ".cpp", ".cc", ".cxx", ".c++", ".java", ".cs"), Syntax.CLike, CLikeWhole),
        Divided("logic-go-percent-divided-first", CodePattern.Files(".go"), Syntax.CLike, GoWhole),
        Divided("logic-rust-percent-divided-first", CodePattern.Files(".rs"), Syntax.CLike, RustWhole),
        Divided("logic-scala-percent-divided-first", CodePattern.Files(".scala", ".sc"), Syntax.Scala, ScalaWhole),
        Divided("logic-ruby-percent-divided-first", CodePattern.Files(".rb"), Syntax.Python, RubyWhole),
        Divided("logic-fsharp-percent-divided-first", CodePattern.Files(".fsx"), Syntax.FSharp, FSharpWhole),
        Divided("logic-fortran-percent-divided-first", CodePattern.Files(".f90", ".f95", ".f03", ".f08"), Syntax.Fortran, FortranWhole),
    ];

    private static CodePattern Divided(string id, IReadOnlySet<string> extensions, Syntax syntax, Func<string, IReadOnlyList<string>, bool> whole) =>
        new(id, extensions, syntax, (patternId, source, masked) => DividedFirst(patternId, source, masked, whole), Severity.Error, Confidence.Likely);

    /// <summary>
    /// <c>score / total * 100</c>, or <c>(score / total) * 100</c> - a name divided by a name or a count, then times 100 - with
    /// nothing multiplied or divided just before it, whose order would be another sum.
    /// </summary>
    [GeneratedRegex(@"(?<![*/%]\s*)(?<![\w.$])(?<open>\(\s*)?(?<part>[A-Za-z_]\w*)\s*/\s*(?<whole>[A-Za-z_]\w*(?:\.(?:length|size|count|Length|Count)(?:\(\))?|\.len\(\))?|len\(\s*[A-Za-z_]\w*\s*\))(?(open)\s*\))\s*\*\s*(?<hundred>100(?:\.0)?)(?![\w.])")]
    private static partial Regex DividedThenHundred();

    /// <summary>A count - xs.length, xs.size(), xs.Count, xs.len(), len(xs) - which is a whole number in every language here.</summary>
    [GeneratedRegex(@"\.(?:length|size|count|Length|Count)(?:\(\))?$|\.len\(\)$|^len\(")]
    private static partial Regex Count();

    private static Regex Declared(string pattern, string name, RegexOptions options = RegexOptions.None) =>
        new(pattern.Replace("NAME", Regex.Escape(name)), options);

    private static bool AnyLine(IReadOnlyList<string> masked, Regex declaration) => masked.Any(declaration.IsMatch);

    /// <summary>C, C++, Java and C#: a whole-number type before the name - int, long, size_t - or var given a whole number.</summary>
    private static bool CLikeWhole(string name, IReadOnlyList<string> masked) =>
        AnyLine(masked, Declared(@"\b(?:unsigned\s+|signed\s+)?(?:int|long|short|size_t|u?int\d*_t|byte|sbyte|ushort|uint|ulong)\s+(?:[A-Za-z_]\w*\s*(?:=[^,;]*)?,\s*)*NAME\b(?!\s*\()|\bvar\s+NAME\s*=\s*-?\d+\s*;", name)) &&
        !AnyLine(masked, Declared(@"\b(?:double|float|decimal)\s+(?:[A-Za-z_]\w*\s*(?:=[^,;]*)?,\s*)*NAME\b", name));

    /// <summary>Go: name := 45, name := len(xs), var name int, or a parameter name int.</summary>
    private static bool GoWhole(string name, IReadOnlyList<string> masked) =>
        AnyLine(masked, Declared(@"\bNAME\s*:=\s*(?:-?\d+(?![.\w])|len\()|\bvar\s+NAME\s+u?int(?:8|16|32|64)?\b|[(,]\s*NAME\s+u?int(?:8|16|32|64)?\b", name));

    /// <summary>Rust: let name = 45, let name: i32, a parameter name: usize, or let name = xs.len().</summary>
    private static bool RustWhole(string name, IReadOnlyList<string> masked) =>
        AnyLine(masked, Declared(@"\blet\s+(?:mut\s+)?NAME\s*(?::\s*[iu](?:8|16|32|64|128|size)\s*)?=\s*(?:-?\d+(?:_?[iu](?:8|16|32|64|128|size))?(?![.\w])|[A-Za-z_][\w.]*\.len\(\))|\blet\s+(?:mut\s+)?NAME\s*:\s*[iu](?:8|16|32|64|128|size)\b|[(,]\s*NAME\s*:\s*[iu](?:8|16|32|64|128|size)\b", name));

    /// <summary>Scala: val name = 45, var name: Int, or a parameter name: Int.</summary>
    private static bool ScalaWhole(string name, IReadOnlyList<string> masked) =>
        AnyLine(masked, Declared(@"\b(?:val|var)\s+NAME\s*(?::\s*(?:Int|Long|Short|Byte)\s*)?=\s*-?\d+L?(?![.\w])|\b(?:val|var)\s+NAME\s*:\s*(?:Int|Long|Short|Byte)\b|[(,]\s*NAME\s*:\s*(?:Int|Long)\b", name));

    /// <summary>Ruby: given a whole number - 45, gets.to_i, xs.length - and nothing that holds a fraction.</summary>
    private static bool RubyWhole(string name, IReadOnlyList<string> masked)
    {
        var given = masked.Select(line => Declared(@"^\s*NAME\s*=(?!=)\s*(?<value>.+?)\s*$", name).Match(line)).Where(match => match.Success).Select(match => match.Groups["value"].Value).ToList();
        return given.Count > 0 && given.All(value => Regex.IsMatch(value, @"^(?:-?\d+|.*\.to_i|[A-Za-z_][\w.]*\.(?:length|size|count))$"));
    }

    /// <summary>F#: let name = 45, let mutable name = 45, a name declared : int, or List.length.</summary>
    private static bool FSharpWhole(string name, IReadOnlyList<string> masked) =>
        AnyLine(masked, Declared(@"\blet\s+(?:mutable\s+)?NAME\s*(?::\s*int(?:64)?\s*)?=\s*(?:-?\d+L?(?![.\w])|(?:List|Array|Seq)\.length\b)|\(\s*NAME\s*:\s*int\b", name));

    /// <summary>Fortran: named in an integer declaration - integer :: score, total - in any case.</summary>
    private static bool FortranWhole(string name, IReadOnlyList<string> masked) =>
        AnyLine(masked, Declared(@"^\s*integer\b[^:]*::[^=]*?(?<![\w])NAME(?![\w])", name, RegexOptions.IgnoreCase));

    private static IEnumerable<LogicFinding> DividedFirst(string id, SourceFile source, IReadOnlyList<string> masked, Func<string, IReadOnlyList<string>, bool> whole)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match divided in DividedThenHundred().Matches(masked[index]))
            {
                var part = divided.Groups["part"].Value;
                var total = divided.Groups["whole"].Value;
                if (!whole(part, masked) || !(Count().IsMatch(total) || whole(total, masked))) continue;

                var line = source.Lines[index];
                var hundred = divided.Groups["hundred"].Value;
                var multiplied = $"{part} * {hundred} / {total}";

                yield return new LogicFinding(id, index + 1,
                    $"{part} / {total} is worked out first, and both are whole numbers, so the part below 1 is thrown away - 45 / 60 is 0 - and " +
                    $"0 * {hundred} is {(hundred == "100" ? "0" : "0.0")}: the percentage is 0 whenever {part} is less than {total}",
                    LocalFix.ReplaceLine(id, $"Multiply first: {multiplied}",
                        $"Multiplying {part} by {hundred} first keeps all of it in the sum, so only the last division drops a fraction - 45 * 100 / 60 " +
                        "is 75.",
                        source.Path, index + 1, line[..divided.Index] + multiplied + line[(divided.Index + divided.Length)..]));
            }
        }
    }
}
