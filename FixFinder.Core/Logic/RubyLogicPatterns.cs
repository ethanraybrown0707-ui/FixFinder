using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// Mistakes Ruby programs make that run without a word - each run with Ruby 3.3, and ruby -w said nothing of any of them:
/// <c>(total / count).to_f</c> of two whole numbers has lost its fraction before it becomes a decimal - (3 / 2).to_f is
/// 1.0; a decimal worked out by arithmetic, compared exactly - 0.1 + 0.2 == 0.3 is false; and a method that gives back a
/// changed copy - name.upcase, marks.sort - called on a line of its own, which leaves the value as it was.
/// </summary>
public static partial class RubyLogicPatterns
{
    private static readonly IReadOnlySet<string> RubyFiles = CodePattern.Files(".rb");

    private static CodePattern Pattern(
        string id, Func<string, SourceFile, IReadOnlyList<string>, IEnumerable<LogicFinding>> find,
        Severity severity = Severity.Warning, Confidence confidence = Confidence.Likely) =>
        new(id, RubyFiles, Syntax.Python, find, severity, confidence);

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        Pattern("logic-ruby-integer-average", IntegerAverage, Severity.Error),
        Pattern("logic-ruby-float-equality", FloatEquality),
        Pattern("logic-ruby-result-discarded", ResultDiscarded),
    ];

    /// <summary><c>(total / count).to_f</c>: a division made a decimal afterwards.</summary>
    [GeneratedRegex(@"\(\s*(?<dividend>[a-z_]\w*|\w+\.(?:length|size|count))\s*/\s*(?<divisor>[a-z_]\w*|\w+\.(?:length|size|count))\s*\)\.to_f\b")]
    private static partial Regex DecimalOfDivision();

    [GeneratedRegex(@"^-?\d[\d_]*$|\.(?:length|size|count|to_i)$|^\w+\.(?:sum|length|size|count)$")]
    private static partial Regex WholeNumberValue();

    private static IEnumerable<LogicFinding> IntegerAverage(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var code = string.Join("\n", masked);

        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match division in DecimalOfDivision().Matches(masked[index]))
            {
                var dividend = division.Groups["dividend"].Value;
                var divisor = division.Groups["divisor"].Value;
                if (!IsWholeNumber(dividend, code) || !IsWholeNumber(divisor, code)) continue;

                var line = source.Lines[index];
                var corrected = line[..division.Index] + $"{dividend}.to_f / {divisor}" + line[(division.Index + division.Length)..];

                yield return new LogicFinding(id, index + 1,
                    $"`{dividend} / {divisor}` divides two whole numbers, which in Ruby gives a whole number - the fraction is dropped " +
                    "before .to_f is applied, so (3 / 2).to_f is 1.0, not 1.5",
                    LocalFix.ReplaceLine(id, "Make the first number a decimal before dividing",
                        "A decimal divided by a whole number keeps the fraction, so the first number is made a decimal first.",
                        source.Path, index + 1, corrected));
            }
        }
    }

    /// <summary>
    /// Whether a value is surely a whole number: a count - .length, .size, .count - always is, and a name is when every line
    /// that gives it a value gives it a whole-number literal, a count, a .to_i or a .sum of something - and there is one.
    /// </summary>
    private static bool IsWholeNumber(string value, string code)
    {
        if (Regex.IsMatch(value, @"\.(?:length|size|count)$")) return true;

        var given = Regex.Matches(code, $@"^\s*{Regex.Escape(value)}\s*=(?!=)\s*(?<value>[^\n#]+)", RegexOptions.Multiline)
            .Select(made => made.Groups["value"].Value.Trim())
            .ToList();

        return given.Count > 0 && given.All(made => WholeNumberValue().IsMatch(made));
    }

    [GeneratedRegex(@"^\s*(?<name>[a-z_]\w*)\s*(?<assign>[-+*/]?=)(?!=)\s*(?<value>[^\n#]+)")]
    private static partial Regex Assignment();

    [GeneratedRegex(@"(?<![\w.])(?<name>[a-z_]\w*)\s*(?<op>==|!=)\s*(?<value>\d*\.\d*[1-9]\d*)(?![\w.])")]
    private static partial Regex ExactComparison();

    /// <summary>
    /// A decimal worked out by arithmetic with a decimal in it - or .to_f - compared exactly with a decimal: the arithmetic
    /// leaves a tiny error, so the comparison is false when the two are equal for every practical purpose.
    /// </summary>
    private static IEnumerable<LogicFinding> FloatEquality(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var worked = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in masked)
        {
            if (Assignment().Match(line) is not { Success: true } assignment) continue;

            var value = assignment.Groups["value"].Value;
            var byArithmetic = assignment.Groups["assign"].Value != "=" || Regex.IsMatch(Regex.Replace(value, @"\d*\.\d+|\d+\.\d*", "0"), @"[-+*/]");
            if (byArithmetic && Regex.IsMatch(value, @"\d*\.\d+|\d+\.\d*|\.to_f\b")) worked.Add(assignment.Groups["name"].Value);
        }

        if (worked.Count == 0) yield break;

        for (var index = 0; index < masked.Count; index++)
        {
            if (ExactComparison().Match(masked[index]) is not { Success: true } compared || !worked.Contains(compared.Groups["name"].Value)) continue;

            var name = compared.Groups["name"].Value;
            var value = compared.Groups["value"].Value;
            var close = compared.Groups["op"].Value == "==" ? $"({name} - {value}).abs < 1e-9" : $"({name} - {value}).abs >= 1e-9";
            var line = source.Lines[index];

            yield return new LogicFinding(id, index + 1,
                $"`{name}` is a decimal worked out by arithmetic, compared with {value} exactly - and arithmetic on decimals is rarely " +
                "exact: 0.1 + 0.2 is 0.30000000000000004",
                LocalFix.ReplaceLine(id, $"Compare within a tiny tolerance: {close}",
                    $"{close} is true when {name} is {value} to within a billionth, which arithmetic's tiny errors stay inside.",
                    source.Path, index + 1, line[..compared.Index] + close + line[(compared.Index + compared.Length)..]));
        }
    }

    /// <summary>The methods that give back a changed copy and leave the value as it was - each of which has a ! form that changes it.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)(?<name>[a-z_]\w*)\.(?<method>upcase|downcase|capitalize|swapcase|strip|lstrip|rstrip|chomp|chop|squeeze|reverse|sort|uniq|compact|flatten|shuffle)\s*$")]
    private static partial Regex CopyOnItsOwn();

    /// <summary>
    /// name.upcase on a line of its own: upcase gives back a new, changed copy and leaves name as it was, so the line changes
    /// nothing. The change keeps the copy in the name.
    /// </summary>
    private static IEnumerable<LogicFinding> ResultDiscarded(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (CopyOnItsOwn().Match(masked[index]) is not { Success: true } call) continue;

            var name = call.Groups["name"].Value;
            var method = call.Groups["method"].Value;

            yield return new LogicFinding(id, index + 1,
                $"{method} gives back a new, changed copy and leaves {name} as it was, so on a line of its own it changes nothing - " +
                $"{method}! changes {name} itself",
                LocalFix.ReplaceLine(id, $"Keep the copy in {name}",
                    $"{name}.{method} gives back the changed copy; giving it to {name} keeps it.",
                    source.Path, index + 1, $"{call.Groups["indent"].Value}{name} = {name}.{method}"));
        }
    }
}
