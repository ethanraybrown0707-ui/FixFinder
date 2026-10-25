using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// Two arrays compared with equals or == in Java and C#: both ask whether the two are the very same array, not whether they
/// hold the same items - two arrays of 70 and 80 printed false for equals and for ==, with nothing said by javac -Xlint:all
/// or the C# compiler. The change compares the items: Arrays.equals in Java, SequenceEqual in C#, which printed true.
/// </summary>
public static partial class ArrayEqualityPatterns
{
    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        new CodePattern("logic-java-array-equals", CodePattern.Files(".java"), Syntax.CLike, JavaArrayEquals, Severity.Error, Confidence.Certain),
        new CodePattern("logic-csharp-array-equals", CodePattern.Files(".cs"), Syntax.CLike, CSharpArrayEquals, Severity.Error, Confidence.Certain),
    ];

    /// <summary><c>first.equals(second)</c> or <c>first.Equals(second)</c>, or <c>first == second</c> and <c>!=</c> - two names compared.</summary>
    [GeneratedRegex(@"(?<![\w.])(?<first>[A-Za-z_]\w*)\s*(?:\.\s*(?<method>equals|Equals)\s*\(\s*(?<second>[A-Za-z_]\w*)\s*\)|(?<op>==|!=)\s*(?<second>[A-Za-z_]\w*)(?![\w.\[(]))")]
    private static partial Regex TwoNamesCompared();

    /// <summary>A name declared as an array: <c>int[] first</c>, <c>String first[]</c>, or given one with new - <c>new int[3]</c>.</summary>
    private static Regex ArrayDeclared(string name) =>
        new($@"\b[\w<>.]+\s*\[\]\s+{Regex.Escape(name)}\b|\b[\w<>.]+\s+{Regex.Escape(name)}\s*\[\]|\bvar\s+{Regex.Escape(name)}\s*=\s*new\s+[\w<>.]+\s*\[");

    private static IEnumerable<LogicFinding> JavaArrayEquals(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var imported = masked.Any(line => Regex.IsMatch(line, @"^\s*import\s+java\.util\.(?:Arrays|\*)\s*;"));
        return Compared(id, source, masked, (first, second) => $"{(imported ? "" : "java.util.")}Arrays.equals({first}, {second})",
            imported ? "Arrays.equals" : "java.util.Arrays.equals");
    }

    private static IEnumerable<LogicFinding> CSharpArrayEquals(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var linq = masked.Any(line => Regex.IsMatch(line, @"^\s*(?:global\s+)?using\s+System\.Linq\s*;"));
        return Compared(id, source, masked, (first, second) => linq ? $"{first}.SequenceEqual({second})" : $"System.Linq.Enumerable.SequenceEqual({first}, {second})",
            linq ? "SequenceEqual" : "System.Linq.Enumerable.SequenceEqual");
    }

    /// <summary>
    /// Each comparison of two names that are both arrays. The change compares their items - written whole where the file does
    /// not bring the method in by name, so it builds as it is.
    /// </summary>
    private static IEnumerable<LogicFinding> Compared(string id, SourceFile source, IReadOnlyList<string> masked, Func<string, string, string> sameItems, string method)
    {
        bool IsArray(string name) => masked.Any(line => ArrayDeclared(name).IsMatch(line));

        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match compared in TwoNamesCompared().Matches(masked[index]))
            {
                var first = compared.Groups["first"].Value;
                var second = compared.Groups["second"].Value;
                if (first == second || !IsArray(first) || !IsArray(second)) continue;

                var line = source.Lines[index];
                var negated = compared.Groups["op"].Value == "!=";
                var asked = compared.Groups["method"].Success ? $"{compared.Groups["method"].Value}" : "==";
                var changed = (negated ? "!" : "") + sameItems(first, second);

                yield return new LogicFinding(id, index + 1,
                    $"{asked} asks whether {first} and {second} are the very same array, not whether they hold the same items - so two arrays " +
                    $"holding the same items are {(negated ? "not equal by ==, and != is true" : "not equal")}",
                    LocalFix.ReplaceLine(id, $"Compare the items: {changed}",
                        $"{method} compares the two arrays item by item, and is true when they hold the same items in the same order.",
                        source.Path, index + 1, line[..compared.Index] + changed + line[(compared.Index + compared.Length)..]));
            }
        }
    }
}
