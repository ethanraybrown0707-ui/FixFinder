using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// Mistakes Fortran programs make that gfortran builds without a word - each run with gfortran 13.2: two whole numbers
/// divided and kept in a real, which keeps the whole-number answer - 7 / 2 gives 3.0; a program with no implicit none, where
/// a misspelt name is a new variable rather than an error; a running total added to before it is given its first value,
/// which Fortran does not give it; and a double precision decimal worked out by arithmetic, compared exactly -
/// 0.1d0 + 0.2d0 == 0.3d0 is false.
/// </summary>
public static partial class FortranLogicPatterns
{
    private static readonly IReadOnlySet<string> FortranFiles = CodePattern.Files(".f90", ".f95", ".f03", ".f08");

    private static CodePattern Pattern(
        string id, Func<string, SourceFile, IReadOnlyList<string>, IEnumerable<LogicFinding>> find,
        Severity severity = Severity.Warning, Confidence confidence = Confidence.Likely) =>
        new(id, FortranFiles, Syntax.Fortran, find, severity, confidence);

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        Pattern("logic-fortran-whole-number-division", WholeNumberDivision, Severity.Error, Confidence.Certain),
        Pattern("logic-fortran-no-implicit-none", NoImplicitNone),
        Pattern("logic-fortran-total-not-started", TotalNotStarted),
        Pattern("logic-fortran-float-equality", FloatEquality),
        Pattern("logic-fortran-single-precision-constant", SinglePrecisionConstant, Severity.Error, Confidence.Certain),
    ];

    /// <summary><c>rate = 0.1</c>, or <c>double precision :: rate = 0.1</c> - a decimal written with no d0 or kind after it.</summary>
    [GeneratedRegex(@"(?:^\s*(?<name>[A-Za-z]\w*)\s*=|::.*?(?<name>[A-Za-z]\w*)\s*=)\s*(?<value>[-+]?(?:\d+\.\d*|\.\d+))(?![\w.])\s*(?:!.*)?$")]
    private static partial Regex GivenADecimal();

    /// <summary>
    /// A double precision name given a decimal written with no d0: such a decimal is a default real, single precision, and
    /// is rounded to about seven digits before it is stored - rate = 0.1 printed 0.10000000149011612, with nothing said by
    /// gfortran -Wall. Left alone for a decimal single precision holds exactly, such as 0.5. The change writes it as a double,
    /// 0.1d0, which printed 0.10000000000000001.
    /// </summary>
    private static IEnumerable<LogicFinding> SinglePrecisionConstant(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var declared = Declared(masked);

        for (var index = 0; index < masked.Count; index++)
        {
            if (GivenADecimal().Match(masked[index]) is not { Success: true } given) continue;

            var name = given.Groups["name"].Value;
            if (!declared.TryGetValue(name, out var kind) || kind.Type != "double precision") continue;

            var value = given.Groups["value"];
            var exact = double.Parse(value.Value, System.Globalization.CultureInfo.InvariantCulture);
            if ((double)(float)exact == exact) continue;

            var single = ((double)(float)exact).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            var line = source.Lines[index];

            yield return new LogicFinding(id, index + 1,
                $"{value.Value} with no d0 is a default real - single precision, about seven digits - so {name} is given {single}, not {value.Value}",
                LocalFix.ReplaceLine(id, $"Write it as a double: {value.Value}d0",
                    $"d0 after the number makes it double precision, so {name} is given {value.Value} to the sixteen digits a double holds.",
                    source.Path, index + 1, line[..(value.Index + value.Length)] + "d0" + line[(value.Index + value.Length)..]));
        }
    }

    /// <summary>A declaration: its type - integer, real, double precision, real(8) - and the names after ::.</summary>
    [GeneratedRegex(@"^\s*(?<type>integer|real|double\s+precision)\b[^:!]*::\s*(?<names>[^!]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Declaration();

    /// <summary>The names a declaration makes, each with what it is given: scores(3) = [70, 80, 90], total = 0, count.</summary>
    [GeneratedRegex(@"(?<name>[A-Za-z]\w*)\s*(?<dimensions>\([^()]*\))?\s*(?<initial>=\s*(?:\[[^\]]*\]|\([^()]*\)|[^,]+))?")]
    private static partial Regex DeclaredName();

    /// <summary>Every declared name in the file, with its type and whether it was given a value where it was declared.</summary>
    private static Dictionary<string, (string Type, bool Started, bool Array)> Declared(IReadOnlyList<string> masked)
    {
        var declared = new Dictionary<string, (string Type, bool Started, bool Array)>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in masked)
        {
            if (Declaration().Match(line) is not { Success: true } declaration) continue;

            var type = Regex.Replace(declaration.Groups["type"].Value.ToLowerInvariant(), @"\s+", " ");
            var kind = Regex.IsMatch(line, @"^\s*real\s*\(\s*(?:kind\s*=\s*)?(?:8|dp|real64)\s*\)", RegexOptions.IgnoreCase) ? "double precision" : type;

            foreach (Match name in DeclaredName().Matches(declaration.Groups["names"].Value))
                declared.TryAdd(name.Groups["name"].Value, (kind, name.Groups["initial"].Success, name.Groups["dimensions"].Success));
        }

        return declared;
    }

    /// <summary><c>average = total / count</c>, or <c>average = sum(scores) / size(scores)</c>: a real given one whole number over another.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)(?<target>[A-Za-z]\w*)\s*=\s*(?<left>[A-Za-z]\w*(?:\([^()]*\))?|\d+)\s*/\s*(?<right>[A-Za-z]\w*(?:\([^()]*\))?|\d+)\s*$")]
    private static partial Regex Division();

    private static IEnumerable<LogicFinding> WholeNumberDivision(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var declared = Declared(masked);

        bool IsWhole(string expression)
        {
            if (Regex.IsMatch(expression, @"^\d+$")) return true;
            if (Regex.Match(expression, @"^(?<function>sum|size|count|len|maxval|minval)\s*\(\s*(?<argument>\w+)") is { Success: true } call)
                return call.Groups["function"].Value.ToLowerInvariant() is "size" or "count" or "len" ||
                       declared.TryGetValue(call.Groups["argument"].Value, out var argument) && argument.Type == "integer";

            return declared.TryGetValue(expression, out var variable) && variable.Type == "integer" && !variable.Array;
        }

        for (var index = 0; index < masked.Count; index++)
        {
            if (Division().Match(masked[index]) is not { Success: true } division) continue;
            if (!declared.TryGetValue(division.Groups["target"].Value, out var target) || target.Type == "integer") continue;

            var left = division.Groups["left"].Value;
            var right = division.Groups["right"].Value;
            if (!IsWhole(left) || !IsWhole(right)) continue;

            var line = source.Lines[index];
            var leftGroup = division.Groups["left"];

            yield return new LogicFinding(id, index + 1,
                $"{left} and {right} are both whole numbers, so {left} / {right} is worked out as a whole number - the fraction dropped - " +
                $"before it is kept in the {target.Type} {division.Groups["target"].Value}: 7 / 2 gives 3.0, not 3.5",
                LocalFix.ReplaceLine(id, $"Divide as decimals: real({left}) / {right}",
                    $"real({left}) turns the first number into a decimal, so the division keeps its fraction.",
                    source.Path, index + 1, line[..leftGroup.Index] + $"real({left})" + line[(leftGroup.Index + leftGroup.Length)..]));
        }
    }

    [GeneratedRegex(@"^\s*program\s+\w+", RegexOptions.IgnoreCase)]
    private static partial Regex ProgramStart();

    [GeneratedRegex(@"\bimplicit\s+none\b", RegexOptions.IgnoreCase)]
    private static partial Regex ImplicitNone();

    private static IEnumerable<LogicFinding> NoImplicitNone(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        if (masked.Any(line => ImplicitNone().IsMatch(line))) yield break;

        var start = Enumerable.Range(0, masked.Count).FirstOrDefault(index => ProgramStart().IsMatch(masked[index]), -1);
        if (start < 0) yield break;

        var indent = CodeText.Indentation(source.Lines.Skip(start + 1).FirstOrDefault(line => line.Trim().Length > 0) ?? "  ");

        yield return new LogicFinding(id, start + 1,
            "the program has no implicit none, so Fortran makes any name it has not seen a new variable - one starting with i to n a " +
            "whole number, any other a real - and a misspelt name is a new, empty variable rather than an error",
            LocalFix.Insert(id, "Add implicit none",
                "implicit none makes gfortran refuse any name that was not declared, so a misspelling is found before the program runs. " +
                "Each variable then needs a declaration of its own.",
                source.Path, start + 2, [string.IsNullOrEmpty(indent) ? "  implicit none" : indent + "implicit none"]));
    }

    [GeneratedRegex(@"^\s*do\b", RegexOptions.IgnoreCase)]
    private static partial Regex DoStart();

    [GeneratedRegex(@"^\s*end\s*do\b", RegexOptions.IgnoreCase)]
    private static partial Regex DoEnd();

    [GeneratedRegex(@"^\s*(?:program|subroutine|function|(?:integer|real|logical|double\s+precision)\s+function|recursive\s+\w+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex UnitStart();

    private static IEnumerable<LogicFinding> TotalNotStarted(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var declared = Declared(masked);
        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var loopLine = 0; loopLine < masked.Count; loopLine++)
        {
            if (!DoStart().IsMatch(masked[loopLine])) continue;

            var unitStart = Enumerable.Range(0, loopLine + 1).Reverse().FirstOrDefault(index => UnitStart().IsMatch(masked[index]), 0);

            for (var index = loopLine + 1; index < masked.Count && !DoEnd().IsMatch(masked[index]); index++)
            {
                if (Regex.Match(masked[index], @"^\s*(?<name>[A-Za-z]\w*)\s*=\s*\k<name>\s*(?<operator>[-+*])", RegexOptions.IgnoreCase) is not { Success: true } running) continue;

                var name = running.Groups["name"].Value;
                if (!declared.TryGetValue(name, out var variable) || variable.Started || variable.Array || !reported.Add(name)) continue;

                var given = new Regex($@"^\s*{Regex.Escape(name)}\s*=(?!=)|\bread\s*\([^)]*\)\s*.*\b{Regex.Escape(name)}\b", RegexOptions.IgnoreCase);
                if (Enumerable.Range(unitStart, loopLine - unitStart).Any(line => given.IsMatch(masked[line]))) continue;

                var start = running.Groups["operator"].Value == "*" ? "1" : variable.Type == "integer" ? "0" : "0.0";

                yield return new LogicFinding(id, index + 1,
                    $"{name} is added to on every pass of the loop, but given no value before it - and Fortran gives a variable no starting " +
                    $"value of its own: it holds whatever was in that memory, which can be {start} by chance and something else next time",
                    LocalFix.Insert(id, $"Start the total before the loop: {name} = {start}",
                        $"{name} = {start} gives the total its starting value, so every pass adds to a known number.",
                        source.Path, loopLine + 1, [CodeText.Indentation(source.Lines[loopLine]) + $"{name} = {start}"]));
            }
        }
    }

    [GeneratedRegex(@"^\s*(?<name>[A-Za-z]\w*)\s*=\s*(?<value>[^!]+)$")]
    private static partial Regex Assignment();

    [GeneratedRegex(@"(?<![\w.])(?<name>[A-Za-z]\w*)\s*(?<op>==|/=|\.eq\.|\.ne\.)\s*(?<value>\d*\.\d*[1-9]\d*(?:[dD][+-]?\d+|_dp|_8)?)(?![\w.])", RegexOptions.IgnoreCase)]
    private static partial Regex ExactComparison();

    private static IEnumerable<LogicFinding> FloatEquality(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        // Only double precision: in default real, 0.1 + 0.2 rounds to the same as 0.3, so the comparison is true, and saying
        // otherwise would be wrong.
        var declared = Declared(masked);
        var worked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in masked)
        {
            if (Assignment().Match(line) is not { Success: true } assignment) continue;
            if (!declared.TryGetValue(assignment.Groups["name"].Value, out var variable) || variable.Type != "double precision") continue;

            var value = assignment.Groups["value"].Value;
            var byArithmetic = Regex.IsMatch(Regex.Replace(value, @"\d*\.\d+(?:[dDeE][+-]?\d+)?|\d+\.\d*(?:[dDeE][+-]?\d+)?", "0"), @"[-+*/]");
            if (byArithmetic && Regex.IsMatch(value, @"\d*\.\d+|\d+\.\d*")) worked.Add(assignment.Groups["name"].Value);
        }

        if (worked.Count == 0) yield break;

        for (var index = 0; index < masked.Count; index++)
        {
            if (ExactComparison().Match(masked[index]) is not { Success: true } compared || !worked.Contains(compared.Groups["name"].Value)) continue;

            var name = compared.Groups["name"].Value;
            var value = compared.Groups["value"].Value;
            var same = compared.Groups["op"].Value is "==" or ".eq." or ".EQ.";
            var close = same ? $"abs({name} - {value}) < 1d-9" : $"abs({name} - {value}) >= 1d-9";
            var line = source.Lines[index];

            yield return new LogicFinding(id, index + 1,
                $"{name} is a decimal worked out by arithmetic, compared with {value} exactly - and arithmetic on decimals is rarely exact: " +
                "0.1d0 + 0.2d0 is 0.30000000000000004",
                LocalFix.ReplaceLine(id, $"Compare within a tiny tolerance: {close}",
                    $"{close} is true when {name} is {value} to within a billionth, which arithmetic's tiny errors stay inside.",
                    source.Path, index + 1, line[..compared.Index] + close + line[(compared.Index + compared.Length)..]));
        }
    }
}
