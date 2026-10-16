using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Logic;

/// <summary>
/// Mistakes PHP programs make that it runs without stopping - each run with PHP 8.4, and neither php -l nor PHP itself
/// said anything of the last three: a loop to <c>$i &lt;= count($marks)</c> reads one key too many, which PHP only warns of,
/// carrying on with null; <c>if ($mark = 40)</c> stores 40 and is always true; a decimal worked out by arithmetic,
/// compared exactly - 0.1 + 0.2 == 0.3 is false; and <c>strpos($word, "a") == false</c>, true for a match at the very
/// start, as == counts 0 as false.
/// </summary>
public static partial class PhpLogicPatterns
{
    private static readonly IReadOnlySet<string> PhpFiles = CodePattern.Files(".php");

    private static CodePattern Pattern(
        string id, Func<string, SourceFile, IReadOnlyList<string>, IEnumerable<LogicFinding>> find,
        Severity severity = Severity.Warning, Confidence confidence = Confidence.Likely) =>
        new(id, PhpFiles, Syntax.CLike, find, severity, confidence);

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        Pattern("logic-php-loop-to-count", LoopToCount, Severity.Error),
        Pattern("logic-php-assignment-in-condition", AssignmentInCondition, Severity.Error, Confidence.Certain),
        Pattern("logic-php-float-equality", FloatEquality),
        Pattern("logic-php-search-compared-with-false", SearchComparedWithFalse, Severity.Error, Confidence.Certain),
    ];

    private static IEnumerable<LogicFinding> LoopToCount(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (PhpLoops.LoopToCount().Match(masked[index]) is not { Success: true } loop || BraceBlocks.Body(masked, index) is not { } body) continue;

            var variable = loop.Groups["variable"].Value;
            var array = loop.Groups["array"].Value;
            var position = new Regex($@"\${Regex.Escape(array)}\s*\[\s*\${Regex.Escape(variable)}\s*\]");
            if (!Enumerable.Range(body.First, body.End - body.First).Any(line => position.IsMatch(masked[line]))) continue;

            yield return new LogicFinding(id, index + 1,
                $"`${variable} <= count(${array})` lets ${variable} reach count(${array}), so on the loop's last pass `${array}[${variable}]` asks " +
                $"for a key one past the end - PHP warns, Undefined array key, and carries on with null in its place",
                PhpLoops.StrictlyBelowFix(id, source, index + 1, loop));
        }
    }

    /// <summary><c>if ($mark = 40)</c>: a value stored, not compared, with a literal - so the condition is the same every time.</summary>
    [GeneratedRegex(@"\b(?:if|elseif|while)\s*\(\s*\$(?<name>\w+)\s*(?<assign>=)(?![=>])\s*(?<value>-?\d+(?:\.\d+)?|'[^']*'|""[^""]*""|true|false)\s*\)")]
    private static partial Regex StoredInCondition();

    private static IEnumerable<LogicFinding> AssignmentInCondition(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (StoredInCondition().Match(source.Lines[index]) is not { Success: true } condition) continue;
            if (CodeText.Mask(source.Lines[index], Syntax.CLike)[condition.Groups["assign"].Index] != '=') continue;

            var name = condition.Groups["name"].Value;
            var value = condition.Groups["value"].Value;
            var assign = condition.Groups["assign"];
            var line = source.Lines[index];

            yield return new LogicFinding(id, index + 1,
                $"`${name} = {value}` stores {value} in ${name} rather than comparing them, so the condition is the same every time - and " +
                $"${name}'s own value is lost",
                LocalFix.ReplaceLine(id, $"Compare with ==: ${name} == {value}",
                    "== compares the two values; = stores the one on the right in the variable on the left.",
                    source.Path, index + 1, line[..assign.Index] + "==" + line[(assign.Index + assign.Length)..]));
        }
    }

    [GeneratedRegex(@"^\s*\$(?<name>\w+)\s*(?<assign>[-+*/]?=)(?!=)\s*(?<value>[^;]+);")]
    private static partial Regex Assignment();

    [GeneratedRegex(@"\$(?<name>\w+)\s*(?<op>===|==|!==|!=)\s*(?<value>\d*\.\d*[1-9]\d*)(?![\w.])")]
    private static partial Regex ExactComparison();

    private static IEnumerable<LogicFinding> FloatEquality(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var worked = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in masked)
        {
            if (Assignment().Match(line) is not { Success: true } assignment) continue;

            var value = assignment.Groups["value"].Value;
            var byArithmetic = assignment.Groups["assign"].Value != "=" || Regex.IsMatch(Regex.Replace(value, @"\d*\.\d+|\d+\.\d*", "0"), @"[-+*/]");
            if (byArithmetic && Regex.IsMatch(value, @"\d*\.\d+|\d+\.\d*")) worked.Add(assignment.Groups["name"].Value);
        }

        if (worked.Count == 0) yield break;

        for (var index = 0; index < masked.Count; index++)
        {
            if (ExactComparison().Match(masked[index]) is not { Success: true } compared || !worked.Contains(compared.Groups["name"].Value)) continue;

            var name = compared.Groups["name"].Value;
            var value = compared.Groups["value"].Value;
            var close = compared.Groups["op"].Value is "==" or "===" ? $"abs(${name} - {value}) < 1e-9" : $"abs(${name} - {value}) >= 1e-9";
            var line = source.Lines[index];

            yield return new LogicFinding(id, index + 1,
                $"`${name}` is a decimal worked out by arithmetic, compared with {value} exactly - and arithmetic on decimals is rarely " +
                "exact: 0.1 + 0.2 is 0.30000000000000004",
                LocalFix.ReplaceLine(id, $"Compare within a tiny tolerance: {close}",
                    $"{close} is true when ${name} is {value} to within a billionth, which arithmetic's tiny errors stay inside.",
                    source.Path, index + 1, line[..compared.Index] + close + line[(compared.Index + compared.Length)..]));
        }
    }

    /// <summary><c>strpos($word, "a") == false</c>: where a search found something, compared with false by ==, which counts 0 as false.</summary>
    [GeneratedRegex(@"\b(?<function>strpos|stripos|strrpos|array_search)\s*\([^()]*\)\s*(?<comparison>==|!=)(?!=)\s*false\b", RegexOptions.IgnoreCase)]
    private static partial Regex SearchComparedLoosely();

    private static IEnumerable<LogicFinding> SearchComparedWithFalse(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (SearchComparedLoosely().Match(masked[index]) is not { Success: true } search) continue;

            var comparison = search.Groups["comparison"];
            var strict = comparison.Value == "==" ? "===" : "!==";
            var line = source.Lines[index];

            yield return new LogicFinding(id, index + 1,
                $"{search.Groups["function"].Value} gives where it found what it looked for - 0 for the very start - or false when it found " +
                $"nothing, and {comparison.Value} counts 0 as equal to false, so a match at the very start reads as no match",
                LocalFix.ReplaceLine(id, $"Compare with {strict} false",
                    $"{strict} compares the type as well, so only false itself means nothing was found, and 0 is the very start.",
                    source.Path, index + 1, line[..comparison.Index] + strict + line[(comparison.Index + comparison.Length)..]));
        }
    }
}
