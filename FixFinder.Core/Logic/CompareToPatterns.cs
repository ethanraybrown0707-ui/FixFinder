using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// What compareTo gives back, compared with 1 or -1. compareTo gives back a negative number, zero or a positive one - not
/// only -1 and 1: run with javac and Scala 3.8.4, "apple".compareTo("cherry") was -2, so == -1 was false, with nothing said
/// by javac -Xlint:all or Scala CLI. The change asks only for the sign: &lt; 0 for before, &gt; 0 for after.
/// </summary>
public static partial class CompareToPatterns
{
    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        new CodePattern("logic-java-compare-to-one", CodePattern.Files(".java"), Syntax.CLike, CompareToOne, Severity.Error, Confidence.Certain),
        new CodePattern("logic-scala-compare-to-one", CodePattern.Files(".scala", ".sc"), Syntax.Scala, CompareToOne, Severity.Error, Confidence.Certain),
    ];

    /// <summary><c>a.compareTo(b) == -1</c> - or compareToIgnoreCase - compared with 1 or -1 by == or !=.</summary>
    [GeneratedRegex(@"\.compareTo(?:IgnoreCase)?\s*\((?:[^()]|\([^()]*\))*\)\s*(?<comparison>(?<op>==|!=)\s*(?<one>-\s*1|1))(?![\w.])")]
    private static partial Regex ComparedWithOne();

    /// <summary>What each comparison was meant to ask, said with the sign alone.</summary>
    private static string BySign(string op, bool negative) => (op, negative) switch
    {
        ("==", true) => "< 0",
        ("==", false) => "> 0",
        ("!=", true) => ">= 0",
        _ => "<= 0",
    };

    private static IEnumerable<LogicFinding> CompareToOne(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match compared in ComparedWithOne().Matches(masked[index]))
            {
                var comparison = compared.Groups["comparison"];
                var op = compared.Groups["op"].Value;
                var negative = compared.Groups["one"].Value.StartsWith('-');
                var bySign = BySign(op, negative);
                var line = source.Lines[index];

                yield return new LogicFinding(id, index + 1,
                    $"compareTo gives back a negative number, zero or a positive number - not only -1 and 1: \"apple\".compareTo(\"cherry\") is -2 - " +
                    $"so {comparison.Value} can be {(op == "==" ? "false" : "true")} for text that does come {(negative ? "before" : "after")}",
                    LocalFix.ReplaceLine(id, $"Ask for the sign: {bySign}",
                        $"Below 0 means before and above 0 means after, whatever the number is, so {bySign} asks what was meant.",
                        source.Path, index + 1, line[..comparison.Index] + bySign + line[(comparison.Index + comparison.Length)..]));
            }
        }
    }
}
