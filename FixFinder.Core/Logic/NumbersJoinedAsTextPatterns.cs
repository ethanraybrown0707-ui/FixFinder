using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// Text and then two numbers joined with + - "Total: " + first + second - in Java, C#, JavaScript, Scala and PowerShell, where
/// + works from left to right: the text joins the first number, and that text then joins the second, so 70 and 80 are written
/// side by side - each of javac's, the C# compiler's, Node's, Scala CLI's and PowerShell's own programs printed "Total: 7080",
/// and "Next: " + count + 1 printed "Next: 31", with no warning. Brackets add the numbers first.
/// </summary>
public static partial class NumbersJoinedAsTextPatterns
{
    private static readonly IReadOnlySet<string> Joining = CodePattern.Files(".java", ".cs", ".js", ".mjs", ".cjs");

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        new CodePattern("logic-numbers-joined-as-text", Joining, Syntax.CLike, NumbersJoined, Severity.Warning, Confidence.Likely),
        new CodePattern("logic-scala-numbers-joined-as-text", CodePattern.Files(".scala", ".sc"), Syntax.Scala, ScalaNumbersJoined, Severity.Warning, Confidence.Likely),
        new CodePattern("logic-powershell-numbers-joined-as-text", CodePattern.Files(".ps1"), Syntax.Python, PowerShellNumbersJoined, Severity.Warning, Confidence.Likely),
    ];

    /// <summary>
    /// <c>"Total: " + first + second</c>: the end of a piece of text, then two values joined by + - not a * or / that would bind
    /// the second more tightly, and not a call or a member, whose value is not known here.
    /// </summary>
    [GeneratedRegex(@"""\s*\+\s*(?<first>[A-Za-z_$][\w$]*)\s*\+\s*(?<second>[A-Za-z_$][\w$]*|\d+(?:\.\d+)?)(?!\s*[*/%.(\[\w$])")]
    private static partial Regex TextThenTwoValues();

    /// <summary>The same in PowerShell, whose names start with $: <c>"Total: " + $first + $second</c>.</summary>
    [GeneratedRegex(@"[""']\s*\+\s*(?<first>\$\w+)\s*\+\s*(?<second>\$\w+|\d+(?:\.\d+)?)(?!\s*[*/%.(\[\w$])")]
    private static partial Regex TextThenTwoDollarValues();

    /// <summary>A whole or decimal number's declaration in Java or C#: <c>int first</c>, <c>double price</c>, or <c>var first = 70</c>.</summary>
    private static Regex TypedNumber(string name) =>
        new($@"\b(?:(?:int|long|short|byte|double|float|decimal)\s+{Regex.Escape(name)}\b|var\s+{Regex.Escape(name)}\s*=\s*-?\d+(?:\.\d+)?\s*;)");

    /// <summary>A JavaScript name given a number: <c>const first = 70</c>, <c>let count = 3;</c> - TypeScript's types already taken out.</summary>
    private static Regex ScriptNumber(string name) =>
        new($@"\b(?:let|const|var)\s+{Regex.Escape(name)}\s*=\s*-?\d+(?:\.\d+)?\s*;?\s*$");

    /// <summary>A Scala name that holds a number: <c>val first = 70</c>, or one declared with a number's type, <c>var count: Int</c>.</summary>
    private static Regex ScalaNumber(string name) =>
        new($@"\b(?:val|var)\s+{Regex.Escape(name)}\s*(?:=\s*-?\d+(?:\.\d+)?[LlDdFf]?\s*;?\s*$|:\s*(?:Int|Long|Short|Byte|Double|Float)\b)");

    /// <summary>A PowerShell variable given a number, <c>$first = 70</c>, or declared as one, <c>[int]$count</c> - in any case.</summary>
    private static Regex PowerShellNumber(string name) =>
        new($@"^\s*{Regex.Escape(name)}\s*=\s*-?\d+(?:\.\d+)?\s*$|\[(?:int|long|double|decimal|single|float)\]\s*{Regex.Escape(name)}\b", RegexOptions.IgnoreCase);

    private static IEnumerable<LogicFinding> NumbersJoined(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var script = Path.GetExtension(source.Path).ToLowerInvariant() is ".js" or ".mjs" or ".cjs" || Execution.TypeScriptCheck.IsTypeScript(source.Path);
        return Joined(id, source, masked, TextThenTwoValues(), script ? ScriptNumber : TypedNumber);
    }

    private static IEnumerable<LogicFinding> ScalaNumbersJoined(string id, SourceFile source, IReadOnlyList<string> masked) =>
        Joined(id, source, masked, TextThenTwoValues(), ScalaNumber);

    private static IEnumerable<LogicFinding> PowerShellNumbersJoined(string id, SourceFile source, IReadOnlyList<string> masked) =>
        Joined(id, source, masked, TextThenTwoDollarValues(), PowerShellNumber);

    /// <summary>
    /// Each line that joins text and then two values with +, where both values are numbers - written as numbers, or names the
    /// file gives numbers. The change brackets the two values, so they are added first.
    /// </summary>
    private static IEnumerable<LogicFinding> Joined(string id, SourceFile source, IReadOnlyList<string> masked, Regex textThenTwoValues, Func<string, Regex> number)
    {
        bool IsNumber(string value) => char.IsDigit(value[0]) || masked.Any(line => number(value).IsMatch(line));

        for (var index = 0; index < masked.Count; index++)
        {
            if (textThenTwoValues.Match(masked[index]) is not { Success: true } joined) continue;

            var first = joined.Groups["first"].Value;
            var second = joined.Groups["second"].Value;
            if (!IsNumber(first) || !IsNumber(second)) continue;

            var start = joined.Groups["first"].Index;
            var end = joined.Groups["second"].Index + second.Length;
            var line = source.Lines[index];
            var added = line[start..end];

            yield return new LogicFinding(id, index + 1,
                $"+ works from left to right: the text joins {first} first, and that text then joins {second} - so the two are written side " +
                $"by side as text, not added, with no error",
                LocalFix.ReplaceLine(id, $"Add them first: ({added})",
                    $"Brackets are worked out first, so {first} and {second} are added as numbers, and their sum joins the text.",
                    source.Path, index + 1, line[..start] + "(" + added + ")" + line[end..]));
        }
    }
}
