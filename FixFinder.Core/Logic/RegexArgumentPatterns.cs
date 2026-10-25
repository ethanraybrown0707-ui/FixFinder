using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A character meant as itself given to a method that reads a regular expression - run with javac and Scala 3.8.4, which said
/// nothing. "3.14".split(".") gave no parts at all, as . is any character; "a|b".split("|") split between every character,
/// a,|,b, as | on its own matches the empty text; "a$b".split("$") did not split, as $ is the end of the text; and
/// "3.14".replaceAll(".", ",") gave ,,,, and replaceFirst(".", ",") gave ,.14. Escaped - split("\\.") - or with replace,
/// which takes its text as it is, each gave what was meant. Lua's patterns and Perl's split do the same, with Lua 5.4.6 and
/// perl -w: gsub(text, ".", ",") gave ,,,, and split(".", "3.14") no parts.
/// </summary>
public static partial class RegexArgumentPatterns
{
    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        new CodePattern("logic-java-regex-character-as-text", CodePattern.Files(".java"), Syntax.CLike, RegexCharacterAsText, Severity.Error, Confidence.Certain),
        new CodePattern("logic-scala-regex-character-as-text", CodePattern.Files(".scala", ".sc"), Syntax.Scala, RegexCharacterAsText, Severity.Error, Confidence.Certain),
        new CodePattern("logic-lua-pattern-character-as-text", CodePattern.Files(".lua"), Syntax.Lua, LuaPatternDot, Severity.Error, Confidence.Certain),
        new CodePattern("logic-perl-split-character-as-text", CodePattern.Files(".pl", ".pm"), Syntax.Perl, PerlSplitCharacter, Severity.Error, Confidence.Certain),
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

    /// <summary>
    /// <c>string.gsub(text, "</c> or <c>text:gsub("</c> - and find, match and gmatch - up to the opening quote of the pattern,
    /// which masking blanks.
    /// </summary>
    [GeneratedRegex(@"(?:\bstring\.(?<function>gsub|find|match|gmatch)\s*\(\s*[^,()]+,|:(?<function>gsub|find|match|gmatch)\s*\()\s*(?<quote>[""'])")]
    private static partial Regex LuaPatternCall();

    /// <summary>
    /// A Lua pattern of "." alone - in a pattern, . is any character, so gsub(".", ",") turned "3.14" into ",,,," and find(".")
    /// found the first character, whatever it was. Left alone where find is told to look for its text as it is, with true
    /// after the starting position. The change writes %., Lua's . itself.
    /// </summary>
    private static IEnumerable<LogicFinding> LuaPatternDot(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match call in LuaPatternCall().Matches(masked[index]))
            {
                var line = source.Lines[index];
                var dot = call.Index + call.Length;
                var quote = call.Groups["quote"].Value[0];
                if (dot + 1 >= line.Length || line[dot] != '.' || line[dot + 1] != quote) continue;

                var function = call.Groups["function"].Value;
                var rest = FormatCodePatterns.Arguments(masked[index], dot + 2, bracketed: true);
                if (function == "find" && rest is [_, _, var plain, ..] && masked[index][plain.Start..plain.End] == "true") continue;

                yield return new LogicFinding(id, index + 1,
                    $"{function} reads a pattern, where . means any character at all - so " +
                    (function == "gsub" ? "gsub(\".\", \",\") replaces every character: \"3.14\" becomes \",,,,\"" : $"{function}(\".\") finds the first character, whatever it is"),
                    LocalFix.ReplaceLine(id, $"Write the . itself: {function}(\"%.\")",
                        "% before a character makes it mean the character itself in a Lua pattern, so %. is a full stop.",
                        source.Path, index + 1, line[..dot] + "%" + line[dot..]));
            }
        }
    }

    /// <summary><c>split(".",</c> or <c>split "|",</c> - Perl's split given its pattern as quoted text, up to the opening quote.</summary>
    [GeneratedRegex(@"\bsplit\s*\(?\s*(?<quote>[""'])")]
    private static partial Regex PerlSplitCall();

    /// <summary>
    /// Perl's split given "." or "|" as quoted text: split reads it as a regular expression, where . is any character - so
    /// split(".", "3.14") gave no parts - and | alone matches the empty text, so split("|", "a|b") split between every
    /// character, a, |, b. perl -w said nothing of either. The change writes the pattern as one, with the character
    /// escaped: split(/\./, ...).
    /// </summary>
    private static IEnumerable<LogicFinding> PerlSplitCharacter(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match call in PerlSplitCall().Matches(masked[index]))
            {
                var line = source.Lines[index];
                var start = call.Index + call.Length - 1;
                var quote = line[start];
                if (start + 2 >= line.Length || line[start + 1] is not ('.' or '|') || line[start + 2] != quote) continue;

                var character = line[start + 1];
                yield return new LogicFinding(id, index + 1,
                    $"split reads its first argument as a regular expression, where {Means(character)} - so " +
                    (character == '.' ? "split(\".\", \"3.14\") splits at every character and gives no parts at all" : "split(\"|\", \"a|b\") splits between every character - a, |, b"),
                    LocalFix.ReplaceLine(id, $"Split on the {character} itself: split(/\\{character}/, ...)",
                        $"Written as a pattern, /\\{character}/, with a \\ before it, the {character} means the character itself.",
                        source.Path, index + 1, line[..start] + $"/\\{character}/" + line[(start + 3)..]));
            }
        }
    }
}
