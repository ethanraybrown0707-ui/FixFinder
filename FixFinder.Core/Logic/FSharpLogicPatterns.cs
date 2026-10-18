using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Logic;

/// <summary>
/// Mistakes F# scripts make that run on and give the wrong answer - each run with F# Interactive 15.2 (F# 10), which said
/// nothing of any of them: <c>total = total + 70</c> at the script's top level, which compares and changes nothing; a list
/// function such as <c>List.sort marks</c> on a line of its own there, whose new list is lost; <c>float (total / count)</c> of
/// two whole numbers, divided before they become decimals; a loop to an array's Length that reads the array at the loop's
/// number; a decimal worked out by arithmetic and then compared exactly; and a line read with ReadLine compared with a number
/// written as text, which compares the two as text. Inside a function or a loop, F# warns of the first two (FS0020), and its
/// warning's rule makes the same change.
/// </summary>
public static partial class FSharpLogicPatterns
{
    private static readonly IReadOnlySet<string> FSharpFiles = CodePattern.Files(".fsx");

    private static CodePattern Pattern(
        string id, Func<string, SourceFile, IReadOnlyList<string>, IEnumerable<LogicFinding>> find,
        Severity severity = Severity.Warning, Confidence confidence = Confidence.Likely) =>
        new(id, FSharpFiles, Syntax.FSharp, find, severity, confidence);

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        Pattern("logic-fsharp-comparison-for-assignment", ComparisonForAssignment, Severity.Error, Confidence.Certain),
        Pattern("logic-fsharp-result-discarded", ResultDiscarded, Severity.Error),
        Pattern("logic-fsharp-whole-number-division", WholeNumberDivision, Severity.Error),
        Pattern("logic-fsharp-loop-to-length", LoopToLength, Severity.Error),
        Pattern("logic-fsharp-float-equality", FloatEquality),
        Pattern("logic-fsharp-input-compared-as-text", InputComparedAsText, Severity.Error),
    ];

    /// <summary><c>let mutable total = 0</c> - and the name it declares.</summary>
    [GeneratedRegex(@"^\s*let\s+mutable\s+(?<name>[A-Za-z_][\w']*)\b")]
    private static partial Regex MutableDeclaration();

    /// <summary><c>total = total + 70</c>, at the start of a line of the script's top level.</summary>
    [GeneratedRegex(@"^(?<name>[a-z_][\w']*)\s*=(?!=)\s*\S")]
    private static partial Regex TopLevelComparison();

    /// <summary>
    /// A mutable value "given" a new value with = on a line of the script's own: = compares, and at the top level of a script
    /// F# throws the true or false away without a word, so the value keeps its old one. The change is the one the rule on F#'s
    /// warning makes inside a function: &lt;-.
    /// </summary>
    private static IEnumerable<LogicFinding> ComparisonForAssignment(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        // Where each mutable value is first declared, read once: asking every line above each line again took a 5,000-line
        // script longer than twenty seconds.
        var declaredOn = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < masked.Count; index++)
            if (MutableDeclaration().Match(masked[index]) is { Success: true } declared)
                declaredOn.TryAdd(declared.Groups["name"].Value, index);

        for (var index = 0; index < masked.Count; index++)
        {
            if (TopLevelComparison().Match(masked[index]) is not { Success: true } compared) continue;

            var name = compared.Groups["name"].Value;
            if (name is "let" or "do" || !declaredOn.TryGetValue(name, out var declaredAt) || declaredAt >= index) continue;
            if (FSharpAssignments.ArrowFix(id, source, index + 1, name) is not { } fix) continue;

            yield return new LogicFinding(id, index + 1,
                $"= compares: {name} = ... works out whether {name} equals the value on the right, and on a line of the script's own F# " +
                $"throws the true or false away without a word - so {name} keeps its old value",
                fix);
        }
    }

    /// <summary><c>List.sort marks</c> at the start of a line of the script's top level: a function that gives back a new list.</summary>
    [GeneratedRegex(@"^(?<call>(?<module>List|Array|Seq|Set|Map|String)\.(?<function>sort|sortDescending|rev|distinct|filter|map|append|add|remove)\b.*?\s(?<name>[a-z_][\w']*))\s*$")]
    private static partial Regex CopyOnItsOwn();

    /// <summary>What the new list is called, by what made it: sortedMarks, reversedMarks.</summary>
    private static readonly IReadOnlyDictionary<string, string> NewNameFor = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["sort"] = "sorted", ["sortDescending"] = "sorted", ["rev"] = "reversed", ["distinct"] = "distinct", ["filter"] = "filtered",
        ["map"] = "mapped", ["append"] = "joined", ["add"] = "updated", ["remove"] = "updated",
    };

    /// <summary>
    /// A function that gives back a new list - or set, map or text - on a line of the script's own: F#'s lists never change, so
    /// List.sort marks makes a sorted copy, and on a line of its own F# throws the copy away without a word. A value given with let
    /// cannot be given again at a script's top level, so the change keeps the copy under a name of its own, and the lines after
    /// read that name - unless one of them gives the old name a meaning of its own, when the finding comes with no change.
    /// </summary>
    private static IEnumerable<LogicFinding> ResultDiscarded(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (CopyOnItsOwn().Match(masked[index]) is not { Success: true } copy) continue;

            var name = copy.Groups["name"].Value;
            var function = $"{copy.Groups["module"].Value}.{copy.Groups["function"].Value}";
            var reads = new Regex($@"(?<![\w.']){Regex.Escape(name)}(?![\w'])");
            var lastRead = Enumerable.Range(index + 1, masked.Count - index - 1).LastOrDefault(line => reads.IsMatch(masked[line]), -1);
            if (lastRead < 0) continue;

            var message = $"F#'s lists never change: {function} gives back a new one and leaves {name} as it was - and on a line of the " +
                          "script's own F# throws the new one away without a word";

            var newName = NewNameFor[copy.Groups["function"].Value] + char.ToUpperInvariant(name[0]) + name[1..];
            var givenAgain = new Regex($@"\b(?:let|fun|for)\b[^=]*(?<![\w.']){Regex.Escape(name)}(?![\w'])|\(\s*{Regex.Escape(name)}\s*:");
            var renamable = !Enumerable.Range(index + 1, lastRead - index).Any(line => givenAgain.IsMatch(masked[line])) &&
                            !masked.Any(line => Regex.IsMatch(line, $@"(?<![\w.']){Regex.Escape(newName)}(?![\w'])"));

            if (!renamable)
            {
                yield return new LogicFinding(id, index + 1, message, null);
                continue;
            }

            var call = source.Lines[index].Substring(copy.Groups["call"].Index, copy.Groups["call"].Length);
            var changed = new List<string> { $"let {newName} = {call}" };

            for (var line = index + 1; line <= lastRead; line++)
            {
                var written = source.Lines[line];
                foreach (var read in reads.Matches(masked[line]).Reverse())
                    written = written[..read.Index] + newName + written[(read.Index + read.Length)..];
                changed.Add(written);
            }

            yield return new LogicFinding(id, index + 1, message, new LocalFix
            {
                RuleId = id,
                Title = $"Keep the new list: let {newName} = {function} ...",
                Explanation = $"let keeps what {function} gives back under a name of its own - F# will not give {name} a second value at the " +
                              $"top of a script - and the lines after it read {newName}.",
                File = source.Path,
                StartLine = index + 1,
                RemoveCount = lastRead - index + 1,
                NewLines = changed,
            });
        }
    }

    /// <summary><c>float (total / count)</c>: a division turned into a decimal after it is worked out.</summary>
    [GeneratedRegex(@"\bfloat\s*\(\s*(?<dividend>[a-z_][\w']*)\s*/\s*(?<divisor>[a-z_][\w']*)\s*\)")]
    private static partial Regex DividedThenFloat();

    /// <summary>A name given a whole number: <c>let count = 2</c>, <c>let count = List.length marks</c>, <c>let total = marks.Length</c>.</summary>
    private static Regex WholeNumber(string name) =>
        new($@"^\s*let\s+(?:mutable\s+)?{Regex.Escape(name)}\s*(?::\s*int\s*)?=\s*(?:-?\d+\s*$|(?:List|Array|Seq|String)\.length\b|[\w.]+\.(?:Length|Count)\s*$|int\b)");

    /// <summary>
    /// float (total / count) of two whole numbers: / divides two whole numbers as whole numbers, dropping what is left over, and
    /// float only then makes the answer a decimal - 7 / 2 is 3, and float 3 is 3.0. The change makes each a decimal first.
    /// </summary>
    private static IEnumerable<LogicFinding> WholeNumberDivision(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (DividedThenFloat().Match(masked[index]) is not { Success: true } division) continue;

            var dividend = division.Groups["dividend"].Value;
            var divisor = division.Groups["divisor"].Value;
            if (!masked.Any(line => WholeNumber(dividend).IsMatch(line)) || !masked.Any(line => WholeNumber(divisor).IsMatch(line))) continue;

            var line = source.Lines[index];

            yield return new LogicFinding(id, index + 1,
                $"both {dividend} and {divisor} are whole numbers, so {dividend} / {divisor} is worked out as a whole number, dropping what is left " +
                $"over, and float only then makes it a decimal - 7 / 2 is 3, and float 3 is 3.0",
                LocalFix.ReplaceLine(id, $"Make each a decimal first: float {dividend} / float {divisor}",
                    $"float {dividend} / float {divisor} divides two decimals, so nothing is dropped: 7.0 / 2.0 is 3.5.",
                    source.Path, index + 1, line[..division.Index] + $"float {dividend} / float {divisor}" + line[(division.Index + division.Length)..]));
        }
    }

    /// <summary>
    /// A loop to an array's Length - for index in 0 .. marks.Length do, or for index = 0 to marks.Length do - that reads the
    /// array at the loop's number: both count to Length itself, one past the last position, and reading there stops the script
    /// with System.IndexOutOfRangeException. The change is the one the crash's rule makes.
    /// </summary>
    private static IEnumerable<LogicFinding> LoopToLength(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (FSharpLoops.ToLength().Match(masked[index]) is not { Success: true } loop) continue;

            var array = loop.Groups["array"].Value;
            var variable = loop.Groups["variable"].Value;
            var position = new Regex($@"(?<![\w.']){Regex.Escape(array)}\.?\[\s*{Regex.Escape(variable)}\s*\]");
            var indent = CodeText.Indentation(masked[index]).Length;

            var body = Enumerable.Range(index + 1, masked.Count - index - 1)
                .TakeWhile(line => masked[line].Trim().Length == 0 || CodeText.Indentation(masked[line]).Length > indent);
            if (!body.Any(line => position.IsMatch(masked[line])) && !position.IsMatch(masked[index])) continue;

            yield return new LogicFinding(id, index + 1,
                $"the loop counts to {array}.Length itself, and {array}'s last position is {array}.Length - 1 - so on the last pass " +
                $"{array}.[{variable}] is one past the end, and the script stops with System.IndexOutOfRangeException",
                FSharpLoops.LastPositionFix(id, source, index + 1, loop));
        }
    }

    /// <summary>A value given a decimal worked out by arithmetic: <c>let price = 0.1 + 0.2</c>, <c>total &lt;- total + 0.1</c>.</summary>
    [GeneratedRegex(@"^\s*(?:let\s+(?:mutable\s+)?)?(?<name>[a-z_][\w']*)\s*(?:=|<-)\s*(?<value>.*\d+\.\d+.*)$")]
    private static partial Regex DecimalWorkedOut();

    /// <summary><c>if price = 0.3 then</c>: a value compared exactly with a decimal, in a condition.</summary>
    [GeneratedRegex(@"\b(?:if|elif|while|when)\b.*?(?<![\w.'<>=!])(?<name>[a-z_][\w']*)\s*(?<op><>|=)(?![=>])\s*(?<value>\d+\.\d*[1-9]\d*)(?![\w.])")]
    private static partial Regex ComparedWithDecimal();

    /// <summary>
    /// A decimal worked out by arithmetic, compared exactly with a decimal: the arithmetic leaves a tiny error - 0.1 + 0.2 is
    /// 0.30000000000000004 - so = is false when the two are equal for every practical purpose. The change compares the
    /// difference with a tiny tolerance.
    /// </summary>
    private static IEnumerable<LogicFinding> FloatEquality(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var workedOut = masked
            .Select(line => DecimalWorkedOut().Match(line))
            .Where(given => given.Success && Regex.IsMatch(Regex.Replace(given.Groups["value"].Value, @"\d+\.\d+", "0"), @"[-+*/]"))
            .Select(given => given.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        for (var index = 0; index < masked.Count; index++)
        {
            if (ComparedWithDecimal().Match(masked[index]) is not { Success: true } compared || !workedOut.Contains(compared.Groups["name"].Value)) continue;

            var name = compared.Groups["name"].Value;
            var value = compared.Groups["value"].Value;
            var equal = compared.Groups["op"].Value == "=";
            var close = equal ? $"abs ({name} - {value}) < 1e-9" : $"abs ({name} - {value}) >= 1e-9";
            var start = compared.Groups["name"].Index;
            var end = compared.Groups["value"].Index + compared.Groups["value"].Length;
            var line = source.Lines[index];

            yield return new LogicFinding(id, index + 1,
                $"the arithmetic that gives {name} its value leaves a tiny error - 0.1 + 0.2 is 0.30000000000000004 - so comparing {name} " +
                $"exactly with {value} is {(equal ? "false" : "true")} even when the two are equal for every practical purpose",
                LocalFix.ReplaceLine(id, $"Compare within a tiny tolerance: {close}",
                    "The difference between the two is compared with a tiny tolerance, which allows for the error the arithmetic left.",
                    source.Path, index + 1, line[..start] + close + line[end..]));
        }
    }

    /// <summary><c>let answer = stdin.ReadLine()</c> or <c>Console.ReadLine()</c>: a line read as text.</summary>
    [GeneratedRegex(@"^\s*let\s+(?:mutable\s+)?(?<name>[a-z_][\w']*)\s*=\s*(?:stdin\.ReadLine|(?:System\.)?Console\.ReadLine)\s*\(\s*\)\s*$")]
    private static partial Regex ReadLine();

    /// <summary>
    /// A line read with ReadLine, compared by &lt;, &gt;, &lt;= or &gt;= with a number written as text: ReadLine gives text, and
    /// text is compared letter by letter, so "7" &gt; "10" is true, as "7" comes after "1". The change turns the line into a
    /// number with int, and compares it with the number itself.
    /// </summary>
    private static IEnumerable<LogicFinding> InputComparedAsText(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (ReadLine().Match(masked[index]) is not { Success: true } read) continue;

            var name = read.Groups["name"].Value;
            var compared = new Regex($@"(?<![\w.']){Regex.Escape(name)}\s*(?<op>>=|<=|>|<)(?![-|>])\s*""(?<number>\d+)""");

            for (var later = index + 1; later < masked.Count; later++)
            {
                var line = source.Lines[later];
                if (compared.Match(line) is not { Success: true } comparison || masked[later].Substring(comparison.Index, name.Length) != name) continue;

                var number = comparison.Groups["number"].Value;
                var op = comparison.Groups["op"].Value;

                yield return new LogicFinding(id, later + 1,
                    $"ReadLine gives {name} as text, and \"{number}\" is text too, so {op} compares them letter by letter, as words are put in " +
                    $"order - \"7\" > \"10\" is true, as \"7\" comes after \"1\"",
                    LocalFix.ReplaceLine(id, $"Compare it as a number: int {name} {op} {number}",
                        $"int turns the text typed into a whole number, and {number} without quotes is a number, so {op} compares the two as numbers.",
                        source.Path, later + 1, line[..comparison.Index] + $"int {name} {op} {number}" + line[(comparison.Index + comparison.Length)..]));
            }
        }
    }
}
