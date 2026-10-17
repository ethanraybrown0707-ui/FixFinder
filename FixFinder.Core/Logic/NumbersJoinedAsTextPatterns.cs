using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// Text and then two numbers joined with + - "Total: " + first + second - in Java, C# and JavaScript, where + works from left to
/// right: the text joins the first number, and that text then joins the second, so 70 and 80 are written side by side - each of
/// javac's, the C# compiler's and Node's own programs printed "Total: 7080", and "Next: " + count + 1 printed "Next: 31", with no
/// warning. Brackets add the numbers first.
/// </summary>
public static partial class NumbersJoinedAsTextPatterns
{
    private static readonly IReadOnlySet<string> Joining = CodePattern.Files(".java", ".cs", ".js", ".mjs", ".cjs");

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        new CodePattern("logic-numbers-joined-as-text", Joining, Syntax.CLike, NumbersJoined, Severity.Warning, Confidence.Likely),
    ];

    /// <summary>
    /// <c>"Total: " + first + second</c>: the end of a piece of text, then two values joined by + - not a * or / that would bind
    /// the second more tightly, and not a call or a member, whose value is not known here.
    /// </summary>
    [GeneratedRegex(@"""\s*\+\s*(?<first>[A-Za-z_$][\w$]*)\s*\+\s*(?<second>[A-Za-z_$][\w$]*|\d+(?:\.\d+)?)(?!\s*[*/%.(\[\w$])")]
    private static partial Regex TextThenTwoValues();

    /// <summary>A whole or decimal number's declaration in Java or C#: <c>int first</c>, <c>double price</c>, or <c>var first = 70</c>.</summary>
    private static Regex TypedNumber(string name) =>
        new($@"\b(?:(?:int|long|short|byte|double|float|decimal)\s+{Regex.Escape(name)}\b|var\s+{Regex.Escape(name)}\s*=\s*-?\d+(?:\.\d+)?\s*;)");

    /// <summary>A JavaScript name given a number: <c>const first = 70</c>, <c>let count = 3;</c> - TypeScript's types already taken out.</summary>
    private static Regex ScriptNumber(string name) =>
        new($@"\b(?:let|const|var)\s+{Regex.Escape(name)}\s*=\s*-?\d+(?:\.\d+)?\s*;?\s*$");

    private static IEnumerable<LogicFinding> NumbersJoined(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var script = Path.GetExtension(source.Path).ToLowerInvariant() is ".js" or ".mjs" or ".cjs" || Execution.TypeScriptCheck.IsTypeScript(source.Path);
        bool IsNumber(string value) =>
            char.IsDigit(value[0]) || masked.Any(line => (script ? ScriptNumber(value) : TypedNumber(value)).IsMatch(line));

        for (var index = 0; index < masked.Count; index++)
        {
            if (TextThenTwoValues().Match(masked[index]) is not { Success: true } joined) continue;

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
