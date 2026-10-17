using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A value compared with NaN, which is equal to nothing, not even itself - each run with its language's own toolchain, which
/// said nothing: value == NaN was false and value != NaN true for a value that was NaN, in Python, Go, Ruby, PHP, Scala, F#,
/// PowerShell, C and C++. (Dart and Rust warn of it themselves; JavaScript, Java and C# have the check in CLikeReviewPatterns.)
/// </summary>
public static partial class NaNComparisonPatterns
{
    /// <summary>
    /// How one language writes the comparison and the test that answers it: the value compared, its = and its not-equal, NaN
    /// itself, and the language's own test, as a format for the value - and, where a comparison is only one inside a condition,
    /// what comes before it.
    /// </summary>
    private sealed record NaNWay(
        string Id, IReadOnlySet<string> Extensions, Syntax Syntax, string Value, string Equal, string NotEqual, string NaN, string Test,
        string NotTest, string Before = "", RegexOptions Options = RegexOptions.None);

    private const string Name = @"[A-Za-z_][\w.]*(?:\[[^\]]*\])?";

    private static readonly NaNWay[] Ways =
    [
        new("logic-go-nan-comparison", CodePattern.Files(".go"), Syntax.CLike, Name, "==", "!=", @"math\.NaN\(\)", "math.IsNaN({0})", "!math.IsNaN({0})"),
        new("logic-ruby-nan-comparison", CodePattern.Files(".rb"), Syntax.Python, Name, "==", "!=", "Float::NAN", "{0}.nan?", "!{0}.nan?"),
        new("logic-php-nan-comparison", CodePattern.Files(".php"), Syntax.CLike, @"\$\w+(?:\[[^\]]*\])?", "===|==", "!==|!=|<>", "NAN", "is_nan({0})", "!is_nan({0})"),
        new("logic-scala-nan-comparison", CodePattern.Files(".scala", ".sc"), Syntax.Scala, Name, "==", "!=", @"(?:Double|Float)\.NaN", "{0}.isNaN", "!{0}.isNaN"),
        // F#'s = also gives a value its name - let value = nan - so only a comparison inside a condition or brackets is one.
        new("logic-fsharp-nan-comparison", CodePattern.Files(".fsx"), Syntax.FSharp, Name, "=", "<>", "nan", "System.Double.IsNaN {0}",
            "not (System.Double.IsNaN {0})", Before: @"(?<=(?:\b(?:if|elif|while|when)\s+|&&\s*|\|\|\s*|\(\s*))"),
        new("logic-powershell-nan-comparison", CodePattern.Files(".ps1"), Syntax.Python, @"\$\w+", "-eq", "-ne", @"\[double\]::NaN",
            "[double]::IsNaN({0})", "-not [double]::IsNaN({0})", Options: RegexOptions.IgnoreCase),
        new("logic-c-nan-comparison", CodePattern.Files(".c"), Syntax.CLike, Name, "==", "!=", "NAN", "isnan({0})", "!isnan({0})"),
        new("logic-cpp-nan-comparison", CodePattern.Files(".cpp", ".cc", ".cxx", ".c++"), Syntax.CLike, Name, "==", "!=",
            @"NAN|std::numeric_limits<\w+>::quiet_NaN\(\)", "std::isnan({0})", "!std::isnan({0})"),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        new CodePattern("logic-python-nan-comparison", CodePattern.Files(".py", ".pyw"), Syntax.Python, PythonNaNComparison, Severity.Error, Confidence.Certain),
        .. Ways.Select(way => new CodePattern(way.Id, way.Extensions, way.Syntax,
            (id, source, masked) => NaNComparison(id, source, masked, way), Severity.Error, Confidence.Certain)),
    ];

    private static string AlwaysIs(bool negated) => negated ? "true" : "false";

    private static string Message(string comparison, bool negated) =>
        $"NaN is not equal to anything, not even itself, so {comparison} is always {AlwaysIs(negated)} - even when the value is NaN";

    private static IEnumerable<LogicFinding> NaNComparison(string id, SourceFile source, IReadOnlyList<string> masked, NaNWay way)
    {
        var comparison = new Regex(
            $@"{way.Before}(?<![\w$.])(?<value>{way.Value})\s*(?<op>{way.NotEqual}|{way.Equal})\s*(?<nan>{way.NaN})(?![\w.(])", way.Options);

        for (var index = 0; index < masked.Count; index++)
        {
            if (comparison.Match(masked[index]) is not { Success: true } found) continue;

            var line = source.Lines[index];
            var value = found.Groups["value"].Value;
            var negated = Regex.IsMatch(found.Groups["op"].Value, $"^(?:{way.NotEqual})$", way.Options);
            var test = string.Format(negated ? way.NotTest : way.Test, value);
            var written = line.Substring(found.Index, found.Length);

            yield return new LogicFinding(id, index + 1, Message(written, negated),
                LocalFix.ReplaceLine(id, $"Test for NaN: {test}",
                    $"{string.Format(way.Test, value)} asks whether {value} is NaN - the one way to find it, as a comparison with NaN is never true.",
                    source.Path, index + 1, line[..found.Index] + test + line[(found.Index + found.Length)..]));
        }
    }

    /// <summary><c>value == float("</c>, <c>value != math.nan</c>, <c>value == np.nan</c> - Python's NaN, written three ways.</summary>
    [GeneratedRegex(@"(?<![\w.])(?<value>[A-Za-z_][\w.]*(?:\[[^\]]*\])?)\s*(?<op>==|!=)\s*(?<nan>float\(\s*(?<quote>[""'])|math\.nan\b|(?<numpy>np|numpy)\.nan\b)")]
    private static partial Regex PythonComparison();

    /// <summary>
    /// Python's comparison with NaN - float("nan"), math.nan or numpy's nan. The change tests with math.isnan where the file
    /// imports math, and with numpy's isnan for numpy's nan, which works for a single number and for an array alike; with
    /// float("nan") in a file that does not import math, the finding has no change, as the test needs import math at the top.
    /// </summary>
    private static IEnumerable<LogicFinding> PythonNaNComparison(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var importsMath = masked.Any(code => Regex.IsMatch(code, @"^\s*import\s+(?:[\w.]+\s*,\s*)*math\b"));

        for (var index = 0; index < masked.Count; index++)
        {
            if (PythonComparison().Match(masked[index]) is not { Success: true } found) continue;

            var line = source.Lines[index];
            var end = found.Index + found.Length;

            // float("nan"): the text between the quotes is read from the line, as masking blanks it.
            if (found.Groups["quote"].Success)
            {
                var close = line.IndexOf(found.Groups["quote"].Value, end, StringComparison.Ordinal);
                if (close < 0 || !line[end..close].Trim().Equals("nan", StringComparison.OrdinalIgnoreCase)) continue;

                var bracket = line.IndexOf(')', close);
                if (bracket < 0) continue;
                end = bracket + 1;
            }

            var value = found.Groups["value"].Value;
            var negated = found.Groups["op"].Value == "!=";
            var test = found.Groups["numpy"].Success ? $"{found.Groups["numpy"].Value}.isnan({value})" : importsMath ? $"math.isnan({value})" : null;
            var written = line[found.Index..end];

            var fix = test is null
                ? null
                : LocalFix.ReplaceLine(id, $"Test for NaN: {(negated ? "not " : "")}{test}",
                    $"{test} asks whether {value} is NaN - the one way to find it, as a comparison with NaN is never true.",
                    source.Path, index + 1, line[..found.Index] + (negated ? "not " : "") + test + line[end..]);

            yield return new LogicFinding(id, index + 1,
                Message(written, negated) + (test is null ? "; math.isnan(...) tests for it, with import math at the top of the file" : ""),
                fix);
        }
    }
}
