using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Logic;

/// <summary>
/// Mistakes Rust programs make that still build, each run with rustc 1.98 - which said nothing of any of them: a loop
/// over <c>0..=v.len()</c> that reads <c>v[i]</c> panics on its last pass; <c>(total / count) as f64</c> of two whole
/// numbers has lost its fraction before it becomes a decimal - (3 / 2) as f64 is 1; and a decimal worked out by
/// arithmetic, compared exactly - 0.1 + 0.2 == 0.3 is false.
/// </summary>
public static partial class RustLogicPatterns
{
    private static readonly IReadOnlySet<string> RustFiles = CodePattern.Files(".rs");

    private static CodePattern Pattern(
        string id, Func<string, SourceFile, IReadOnlyList<string>, IEnumerable<LogicFinding>> find,
        Severity severity = Severity.Warning, Confidence confidence = Confidence.Likely) =>
        new(id, RustFiles, Syntax.CLike, find, severity, confidence);

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        Pattern("logic-rust-range-to-length", RangeToLength, Severity.Error),
        Pattern("logic-rust-integer-average", IntegerAverage, Severity.Error),
        Pattern("logic-rust-float-equality", FloatEquality),
    ];

    /// <summary>
    /// <c>for i in 0..=v.len()</c> using <c>v[i]</c>: ..= counts up to and including the length, so the last pass asks
    /// for a position one past the end, and the program panics with index out of bounds. The change is the crash rule's.
    /// </summary>
    private static IEnumerable<LogicFinding> RangeToLength(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (RustLoops.RangeToLength().Match(masked[index]) is not { Success: true } loop || BraceBlocks.Body(masked, index) is not { } body) continue;

            var variable = loop.Groups["variable"].Value;
            var collection = loop.Groups["collection"].Value;
            var position = new Regex($@"(?<![\w.]){Regex.Escape(collection)}\s*\[\s*{Regex.Escape(variable)}\s*\]");
            if (!Enumerable.Range(body.First, body.End - body.First).Any(line => position.IsMatch(masked[line]))) continue;

            yield return new LogicFinding(id, index + 1,
                $"`0..={collection}.len()` counts up to and including {collection}.len(), so on the loop's last pass `{collection}[{variable}]` " +
                $"asks for a position one past the end of {collection}, and the program panics with index out of bounds",
                RustLoops.ExclusiveFix(id, source, index + 1, loop));
        }
    }

    /// <summary><c>(total / count) as f64</c>: a division made a decimal afterwards.</summary>
    [GeneratedRegex(@"\(\s*(?<dividend>[A-Za-z_]\w*|\w+\.len\(\))\s*/\s*(?<divisor>[A-Za-z_]\w*|\w+\.len\(\))\s*\)\s*as\s+(?<float>f64|f32)\b")]
    private static partial Regex DecimalOfDivision();

    [GeneratedRegex(@"^-?\d[\d_]*(?:_?[iu](?:8|16|32|64|128|size))?$|\.len\(\)$|\bas\s+[iu](?:8|16|32|64|128|size)$")]
    private static partial Regex WholeNumberValue();

    /// <summary>
    /// A division of two whole numbers made a decimal: Rust divides the whole numbers first, dropping the fraction, and only
    /// then makes a decimal of what is left - so (3 / 2) as f64 is 1, not 1.5.
    /// </summary>
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

                var kind = division.Groups["float"].Value;
                var line = source.Lines[index];
                var corrected = line[..division.Index] + $"{dividend} as {kind} / {divisor} as {kind}" + line[(division.Index + division.Length)..];

                yield return new LogicFinding(id, index + 1,
                    $"`{dividend} / {divisor}` divides two whole numbers, which in Rust gives a whole number - the fraction is dropped " +
                    $"before as {kind} is applied, so (3 / 2) as {kind} is 1, not 1.5",
                    LocalFix.ReplaceLine(id, $"Make each number a {kind} before dividing",
                        $"Dividing {kind}s keeps the fraction, so each number is made an {kind} first.",
                        source.Path, index + 1, corrected));
            }
        }
    }

    /// <summary>
    /// Whether a value is surely a whole number: v.len() always is, and a name is when every let that makes it gives it
    /// a whole-number literal, a .len() or an as to a whole-number type, or a whole-number type - and there is one.
    /// </summary>
    private static bool IsWholeNumber(string value, string code)
    {
        if (value.EndsWith(".len()", StringComparison.Ordinal)) return true;

        var name = Regex.Escape(value);
        var made = Regex.Matches(code, $@"\blet\s+(?:mut\s+)?{name}\s*(?::\s*(?<type>\w+)\s*)?(?:=\s*(?<value>[^;\n]*))?;").ToList();
        var parameters = Regex.Matches(code, $@"[(,]\s*(?:mut\s+)?{name}\s*:\s*(?<type>\w+)\s*[,)]").ToList();
        if (made.Count + parameters.Count == 0) return false;

        bool WholeType(string type) => Regex.IsMatch(type, @"^[iu](?:8|16|32|64|128|size)$");

        return made.All(let => let.Groups["type"].Success ? WholeType(let.Groups["type"].Value) : WholeNumberValue().IsMatch(let.Groups["value"].Value.Trim()))
               && parameters.All(parameter => WholeType(parameter.Groups["type"].Value));
    }

    /// <summary>A let that works a decimal out by arithmetic: let total = 0.1 + 0.2;, or total += 0.1.</summary>
    [GeneratedRegex(@"^\s*(?:let\s+(?:mut\s+)?)?(?<name>[A-Za-z_]\w*)\s*(?::\s*(?<type>f(?:32|64))\s*)?(?<assign>[-+*/]?=)(?!=)\s*(?<value>[^;]+);")]
    private static partial Regex Assignment();

    [GeneratedRegex(@"(?<![\w.])(?<name>[A-Za-z_]\w*)\s*(?<op>==|!=)\s*(?<value>\d*\.\d*[1-9]\d*)(?:_?f(?:32|64))?(?![\w.])")]
    private static partial Regex ExactComparison();

    /// <summary>
    /// A decimal worked out by arithmetic with a decimal in it, compared exactly with a decimal: the arithmetic leaves a
    /// tiny error - 0.1 + 0.2 is 0.30000000000000004 - so the comparison is false when the two are equal for every practical
    /// purpose. One division of whole numbers is not counted: it is rounded once, to the decimal its literal is.
    /// </summary>
    private static IEnumerable<LogicFinding> FloatEquality(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var worked = new HashSet<string>(StringComparer.Ordinal);
        var typed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in masked)
        {
            if (Assignment().Match(line) is not { Success: true } assignment) continue;

            var name = assignment.Groups["name"].Value;
            if (assignment.Groups["type"].Success) typed.Add(name);

            var value = assignment.Groups["value"].Value;
            var byArithmetic = assignment.Groups["assign"].Value != "=" || Regex.IsMatch(Regex.Replace(value, @"\d*\.\d+|\d+\.\d*", "0"), @"[-+*/]");
            if (byArithmetic && Regex.IsMatch(value, @"\d*\.\d+|\d+\.\d*")) worked.Add(name);
        }

        if (worked.Count == 0) yield break;

        for (var index = 0; index < masked.Count; index++)
        {
            if (ExactComparison().Match(masked[index]) is not { Success: true } compared || !worked.Contains(compared.Groups["name"].Value)) continue;

            var name = compared.Groups["name"].Value;

            // A decimal whose type was never written is of no type yet when abs is called on it, which rustc refuses; _f64 gives
            // it the type Rust would have given it in the end.
            var value = compared.Groups["value"].Value;
            var literal = value + (typed.Contains(name) ? "" : "_f64");
            var close = compared.Groups["op"].Value == "==" ? $"({name} - {literal}).abs() < 1e-9" : $"({name} - {literal}).abs() >= 1e-9";
            var line = source.Lines[index];

            yield return new LogicFinding(id, index + 1,
                $"`{name}` is a decimal worked out by arithmetic, compared with {value} exactly - and arithmetic on decimals is rarely " +
                "exact: 0.1 + 0.2 is 0.30000000000000004",
                LocalFix.ReplaceLine(id, $"Compare within a tiny tolerance: {close}",
                    $"{close} is true when {name} is {value} to within a billionth, which arithmetic's tiny errors stay inside.",
                    source.Path, index + 1, line[..compared.Index] + close + line[(compared.Index + compared.Length)..]));
        }
    }
}
