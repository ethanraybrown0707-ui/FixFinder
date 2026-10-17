using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// Two mistakes that run without a word in every language they are checked in, each seen by running it: a decimal worked
/// out by arithmetic and then compared exactly - in Python 0.1 + 0.2 == 0.3 is False, and in JavaScript 0.1 + 0.2 === 0.3
/// is false - and a search's position compared with > 0, which is false when what was looked for is at the very start:
/// "apple".indexOf("a") is 0 in Java and JavaScript, "apple".IndexOf("a") in C#, and "apple".find("a") in Python. A search's
/// position taken as true or false is wrong both ways: in Node, if (marks.indexOf(70)) took 70 at the start for no match and
/// if (marks.indexOf(99)) took 99, not there, for a match, and Python's if name.find("A"): did the same.
/// DecimalAndSearchLiveTests runs each mistake, and its change, in each of these languages.
/// </summary>
public static partial class DecimalAndSearchPatterns
{
    private static readonly IReadOnlySet<string> Python = CodePattern.Files(".py", ".pyw");
    private static readonly IReadOnlySet<string> Script = CodePattern.Files(".js", ".mjs", ".cjs");
    private static readonly IReadOnlySet<string> Searching = CodePattern.Files(".java", ".cs", ".js", ".mjs", ".cjs");

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        new CodePattern("logic-python-float-equality", Python, Syntax.Python, PythonDecimalCompared, Severity.Warning, Confidence.Likely),
        new CodePattern("logic-js-float-equality", Script, Syntax.CLike, ScriptDecimalCompared, Severity.Warning, Confidence.Likely),
        new CodePattern("logic-index-of-above-zero", Searching, Syntax.CLike, IndexOfAboveZero, Severity.Warning, Confidence.Possible),
        new CodePattern("logic-python-find-above-zero", Python, Syntax.Python, FindAboveZero, Severity.Warning, Confidence.Possible),
        new CodePattern("logic-js-search-as-condition", Script, Syntax.CLike, ScriptSearchAsCondition, Severity.Error, Confidence.Certain),
        new CodePattern("logic-python-find-as-condition", Python, Syntax.Python, PythonFindAsCondition, Severity.Error, Confidence.Certain),
    ];

    /// <summary>A decimal with a fraction that is not 0 - 0.3, 2.75 - and nothing after it that makes it part of a name.</summary>
    private const string Decimal = @"(?<value>\d*\.\d*[1-9]\d*)(?![\w.])";

    /// <summary>A line that gives a name its value: total = 0.1 + 0.2, total += 0.1, price = 2.5.</summary>
    [GeneratedRegex(@"^\s*(?:(?:const|let|var)\s+)?(?<name>[A-Za-z_]\w*)\s*(?<assign>[-+*/]?=)(?!=)\s*(?<value>.+)$")]
    private static partial Regex Assignment();

    [GeneratedRegex(@"\d*\.\d+|\d+\.\d*|\bfloat\(|\bparseFloat\(")]
    private static partial Regex DecimalInIt();

    [GeneratedRegex(@"[-+*/]")]
    private static partial Regex Arithmetic();

    /// <summary>
    /// The names a file works out as decimals by arithmetic - every name some line gives a value with an operator and a
    /// decimal or a conversion to a float in it, or adds a decimal to with +=. A name only ever given a decimal as it is,
    /// such as price = 2.5, compares exactly with that decimal, and so does one division of two whole numbers - 5 / 2 is
    /// rounded once, to the very decimal 2.5 is - so neither is one of them.
    /// </summary>
    private static HashSet<string> DecimalsWorkedOut(IReadOnlyList<string> masked)
    {
        var worked = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in masked)
        {
            if (Assignment().Match(line) is not { Success: true } assignment) continue;

            var value = assignment.Groups["value"].Value;
            var byArithmetic = assignment.Groups["assign"].Value != "=" || Arithmetic().IsMatch(Regex.Replace(value, @"\d*\.\d+|\d+\.\d*", "0"));
            if (byArithmetic && DecimalInIt().IsMatch(value)) worked.Add(assignment.Groups["name"].Value);
        }

        return worked;
    }

    [GeneratedRegex(@"(?<![\w.])(?<name>[A-Za-z_]\w*)\s*(?<op>==|!=)\s*" + Decimal)]
    private static partial Regex PythonComparison();

    [GeneratedRegex(@"(?<![\w.])(?<name>[A-Za-z_]\w*)\s*(?<op>===|==|!==|!=)\s*" + Decimal)]
    private static partial Regex ScriptComparison();

    private static IEnumerable<LogicFinding> PythonDecimalCompared(string id, SourceFile source, IReadOnlyList<string> masked) =>
        DecimalCompared(id, source, masked, PythonComparison(), "abs", "an exact == or !=");

    private static IEnumerable<LogicFinding> ScriptDecimalCompared(string id, SourceFile source, IReadOnlyList<string> masked) =>
        DecimalCompared(id, source, masked, ScriptComparison(), "Math.abs", "an exact === or ==");

    /// <summary>
    /// A decimal worked out by arithmetic, compared exactly with a decimal: the arithmetic leaves a tiny error - 0.1 + 0.2
    /// is 0.30000000000000004 - so the comparison is false when the two are equal for every practical purpose. The
    /// change compares the difference with a tiny tolerance.
    /// </summary>
    private static IEnumerable<LogicFinding> DecimalCompared(
        string id, SourceFile source, IReadOnlyList<string> masked, Regex comparison, string absolute, string exactly)
    {
        var worked = DecimalsWorkedOut(masked);
        if (worked.Count == 0) yield break;

        for (var index = 0; index < masked.Count; index++)
        {
            if (comparison.Match(masked[index]) is not { Success: true } compared || !worked.Contains(compared.Groups["name"].Value)) continue;

            var name = compared.Groups["name"].Value;
            var value = compared.Groups["value"].Value;
            var equal = compared.Groups["op"].Value is "==" or "===";
            var close = equal ? $"{absolute}({name} - {value}) < 1e-9" : $"{absolute}({name} - {value}) >= 1e-9";
            var line = source.Lines[index];

            yield return new LogicFinding(id, index + 1,
                $"`{name}` is a decimal worked out by arithmetic, compared with {value} by {exactly} - and arithmetic on decimals is " +
                "rarely exact: 0.1 + 0.2 is 0.30000000000000004",
                LocalFix.ReplaceLine(id, $"Compare within a tiny tolerance: {close}",
                    $"{close} is true when {name} is {value} to within a billionth, which arithmetic's tiny errors stay inside.",
                    source.Path, index + 1, line[..compared.Index] + close + line[(compared.Index + compared.Length)..]));
        }
    }

    /// <summary><c>text.indexOf("a") &gt; 0</c>, for the searches that give -1 when nothing is found.</summary>
    [GeneratedRegex(@"\.(?<method>indexOf|lastIndexOf|IndexOf|LastIndexOf|findIndex|findLastIndex|search)\((?:[^()]|\([^()]*\))*\)\s*(?<comparison>>)\s*0(?![\w.])")]
    private static partial Regex SearchAboveZero();

    [GeneratedRegex(@"\.(?<method>find|rfind)\((?:[^()]|\([^()]*\))*\)\s*(?<comparison>>)\s*0(?![\w.])")]
    private static partial Regex PythonFindAboveZero();

    private static IEnumerable<LogicFinding> IndexOfAboveZero(string id, SourceFile source, IReadOnlyList<string> masked) =>
        AboveZero(id, source, masked, SearchAboveZero());

    private static IEnumerable<LogicFinding> FindAboveZero(string id, SourceFile source, IReadOnlyList<string> masked) =>
        AboveZero(id, source, masked, PythonFindAboveZero());

    /// <summary>
    /// <c>if (marks.indexOf(70))</c>, <c>while (!text.indexOf("x"))</c> or <c>marks.indexOf(70) ? ... : ...</c> - a search's
    /// position as the whole of a condition.
    /// </summary>
    [GeneratedRegex(@"(?:\b(?:if|while)\s*\(\s*(?<not>!\s*)?(?<subject>[A-Za-z_$][\w$.]*(?:\[[^\]]*\])?)\.(?<method>indexOf|lastIndexOf)\((?<arguments>(?:[^()]|\([^()]*\))*)\)(?=\s*\))|(?<![\w$.!])(?<not>!\s*)?(?<subject>[A-Za-z_$][\w$.]*(?:\[[^\]]*\])?)\.(?<method>indexOf|lastIndexOf)\((?<arguments>(?:[^()]|\([^()]*\))*)\)(?=\s*\?))")]
    private static partial Regex ScriptSearchCondition();

    /// <summary><c>if name.find("A"):</c> or <c>while not text.find("x"):</c>, with the one argument in can take.</summary>
    [GeneratedRegex(@"\b(?:if|elif|while)\s+(?<not>not\s+)?(?<subject>[A-Za-z_][\w.]*(?:\[[^\]]*\])?)\.(?<method>find|rfind)\((?<arguments>[^(),]+)\)\s*:")]
    private static partial Regex PythonFindCondition();

    private static string AsConditionMessage(string method) =>
        $"`{method}` gives a position - 0 when what it looks for is at the very start, -1 when it is not there at all - and as a condition 0 " +
        "is false and -1 is true: the condition is false for a match at the start and true when nothing is found";

    /// <summary>
    /// A search's position taken as true or false: a match at the start, position 0, is false, and no match at all, -1, is true.
    /// The change asks whether it is there with includes, which arrays and text both have.
    /// </summary>
    private static IEnumerable<LogicFinding> ScriptSearchAsCondition(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match found in ScriptSearchCondition().Matches(masked[index]))
            {
                var line = source.Lines[index];
                var not = found.Groups["not"];
                var subject = found.Groups["subject"].Value;
                var arguments = line.Substring(found.Groups["arguments"].Index, found.Groups["arguments"].Length);
                var start = not.Success ? not.Index : found.Groups["subject"].Index;
                var end = found.Groups["arguments"].Index + found.Groups["arguments"].Length + 1;
                var asked = $"{(not.Success ? "!" : "")}{subject}.includes({arguments})";

                yield return new LogicFinding(id, index + 1, AsConditionMessage(found.Groups["method"].Value),
                    LocalFix.ReplaceLine(id, $"Ask whether it is there: {asked}",
                        "includes is true when what it looks for is anywhere - at the very start too - and false only when it is not there.",
                        source.Path, index + 1, line[..start] + asked + line[end..]));
                break;
            }
        }
    }

    /// <summary>
    /// Python's find taken as true or false, as JavaScript's indexOf is: 0, a match at the start, is false, and -1 is true. The
    /// change asks with in.
    /// </summary>
    private static IEnumerable<LogicFinding> PythonFindAsCondition(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (PythonFindCondition().Match(masked[index]) is not { Success: true } found) continue;

            var line = source.Lines[index];
            var not = found.Groups["not"];
            var subject = found.Groups["subject"].Value;
            var looked = line.Substring(found.Groups["arguments"].Index, found.Groups["arguments"].Length).Trim();
            var start = not.Success ? not.Index : found.Groups["subject"].Index;
            var end = found.Groups["arguments"].Index + found.Groups["arguments"].Length + 1;
            var asked = $"{looked} {(not.Success ? "not in" : "in")} {subject}";

            yield return new LogicFinding(id, index + 1, AsConditionMessage(found.Groups["method"].Value),
                LocalFix.ReplaceLine(id, $"Ask whether it is there: {asked}",
                    "in is True when what it looks for is anywhere in the text - at the very start too - and False only when it is not there.",
                    source.Path, index + 1, line[..start] + asked + line[end..]));
        }
    }

    /// <summary>
    /// A search's position compared with &gt; 0: positions start at 0, so a match at the very start counts as not found.
    /// Wanting a match anywhere but the start is a reason to write it, which is why this is only a possible mistake.
    /// </summary>
    private static IEnumerable<LogicFinding> AboveZero(string id, SourceFile source, IReadOnlyList<string> masked, Regex search)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match found in search.Matches(masked[index]))
            {
                var method = found.Groups["method"].Value;
                var comparison = found.Groups["comparison"];
                var line = source.Lines[index];

                yield return new LogicFinding(id, index + 1,
                    $"`{method}` gives 0 when what it looks for is at the very start, and -1 when it is not there at all - so `> 0` " +
                    "takes a match at the start for not found",
                    LocalFix.ReplaceLine(id, "Count a match at the start: >= 0",
                        $"`>= 0` is true wherever the match is, the start included, and false only for -1.",
                        source.Path, index + 1, line[..comparison.Index] + ">=" + line[(comparison.Index + comparison.Length)..]));
            }
        }
    }
}
