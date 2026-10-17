using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A character meant as itself given to a method that reads a regular expression - run with javac and Scala 3.8.4, which said
/// nothing. "3.14".split(".") gave no parts at all, as . is any character; "a|b".split("|") split between every character,
/// a,|,b, as | on its own matches the empty text; "a$b".split("$") did not split, as $ is the end of the text; and
/// "3.14".replaceAll(".", ",") gave ,,,, and replaceFirst(".", ",") gave ,.14. Escaped - split("\\.") - or with replace,
/// which takes its text as it is, each gave what was meant.
/// </summary>
public static partial class RegexArgumentPatterns
{
    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        new CodePattern("logic-java-regex-character-as-text", CodePattern.Files(".java"), Syntax.CLike, RegexCharacterAsText, Severity.Error, Confidence.Certain),
        new CodePattern("logic-scala-regex-character-as-text", CodePattern.Files(".scala", ".sc"), Syntax.Scala, RegexCharacterAsText, Severity.Error, Confidence.Certain),
    ];

    /// <summary><c>.split("</c>, <c>.replaceAll("</c> or <c>.replaceFirst("</c> - the opening quote of the regular expression, which masking blanks.</summary>
    [GeneratedRegex(@"\.(?<method>split|replaceAll|replaceFirst)\s*\(\s*""")]
    private static partial Regex RegexMethod();

    /// <summary>What each character means in a regular expression, said for a beginner.</summary>
    private static string Means(char character) => character switch
    {
        '.' => ". means any character at all",
        '|' => "| means \"or\", and on its own it matches the empty text between every two characters",
        _ => "$ means the end of the text",
    };

    /// <summary>What the method did with it, as Java and Scala both ran it.</summary>
    private static string Did(string method, char character) => (method, character) switch
    {
        ("split", '.') => "\"3.14\".split(\".\") splits at every character and gives no parts at all",
        ("split", '|') => "\"a|b\".split(\"|\") splits between every character - a, |, b",
        ("split", _) => "\"a$b\".split(\"$\") never splits",
        ("replaceAll", '.') => "\"3.14\".replaceAll(\".\", \",\") replaces every character - ,,,,",
        ("replaceFirst", '.') => "\"3.14\".replaceFirst(\".\", \",\") replaces the first character - ,.14",
        _ => $"{method} does not look for the {character} itself",
    };

    /// <summary>
    /// split, replaceAll or replaceFirst given ".", "|" or "$" alone: they read a regular expression, where each means something
    /// else. The change escapes it - "\\." - for split and replaceFirst, and uses replace, which takes its text as it is, for replaceAll.
    /// </summary>
    private static IEnumerable<LogicFinding> RegexCharacterAsText(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match call in RegexMethod().Matches(masked[index]))
            {
                var line = source.Lines[index];
                var textStart = call.Index + call.Length;
                var close = line.IndexOf('"', textStart);
                if (close != textStart + 1 || line[textStart] is not ('.' or '|' or '$')) continue;

                var character = line[textStart];
                var method = call.Groups["method"].Value;
                var changed = method == "replaceAll"
                    ? line[..call.Groups["method"].Index] + "replace" + line[(call.Groups["method"].Index + method.Length)..]
                    : line[..textStart] + "\\\\" + line[textStart..];
                var title = method == "replaceAll" ? $"Replace the {character} itself: replace(\"{character}\", ...)" : $"Escape it: {method}(\"\\\\{character}\")";

                yield return new LogicFinding(id, index + 1,
                    $"{method} reads a regular expression, where {Means(character)} - so {Did(method, character)}",
                    LocalFix.ReplaceLine(id, title,
                        method == "replaceAll"
                            ? $"replace takes its text as it is, and replaces every {character} in it."
                            : $"\\\\ before the {character} makes it mean the {character} itself in the regular expression.",
                        source.Path, index + 1, changed));
                break;
            }
        }
    }
}
