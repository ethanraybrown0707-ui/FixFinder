using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// Mistakes Go programs make that still build, and that go vet does not report - each run with Go 1.27, and neither go
/// build nor go vet said anything about it: a loop to <c>i &lt;= len(xs)</c> that reads <c>xs[i]</c> panics on its last
/// pass; <c>float64(total / count)</c> of two ints has lost its fraction before it becomes a float; a new value given to
/// a range loop's value, and not used after, changes only the loop's copy; strconv's Atoi and Parse functions give back 0
/// - or false - for text that is not a number, which the error thrown away with _ would have said; and a strings function
/// such as <c>strings.ToUpper(name)</c> on a line of its own leaves name as it was.
/// </summary>
public static partial class GoLogicPatterns
{
    private static readonly IReadOnlySet<string> GoFiles = CodePattern.Files(".go");

    private static CodePattern Pattern(
        string id, Func<string, SourceFile, IReadOnlyList<string>, IEnumerable<LogicFinding>> find,
        Severity severity = Severity.Warning, Confidence confidence = Confidence.Likely) =>
        new(id, GoFiles, Syntax.CLike, find, severity, confidence);

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        Pattern("logic-go-loop-to-length", LoopToLength, Severity.Error),
        Pattern("logic-go-integer-average", IntegerAverage, Severity.Error),
        Pattern("logic-go-range-value-changed", RangeValueChanged, Severity.Error),
        Pattern("logic-go-parse-error-ignored", ParseErrorIgnored, confidence: Confidence.Possible),
        Pattern("logic-go-result-discarded", ResultDiscarded),
    ];

    /// <summary><c>for i := 0; i &lt;= len(xs); i++</c>.</summary>
    [GeneratedRegex(@"\bfor\s+(?<variable>[A-Za-z_]\w*)\s*:=\s*0\s*;\s*\k<variable>\s*(?<comparison><=)\s*len\(\s*(?<collection>[A-Za-z_][\w.]*)\s*\)\s*;")]
    private static partial Regex LoopUpToLength();

    /// <summary>
    /// A loop that runs while <c>i &lt;= len(xs)</c> and uses i as a position in xs: its last pass asks for xs[len(xs)],
    /// one past the end, and the program panics with index out of range. The change is the one the crash makes too.
    /// </summary>
    private static IEnumerable<LogicFinding> LoopToLength(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (LoopUpToLength().Match(masked[index]) is not { Success: true } loop || BraceBlocks.Body(masked, index) is not { } body) continue;

            var variable = loop.Groups["variable"].Value;
            var collection = loop.Groups["collection"].Value;
            var position = new Regex($@"(?<![\w.]){Regex.Escape(collection)}\s*\[\s*{Regex.Escape(variable)}\s*\]");
            if (!Enumerable.Range(body.First, body.End - body.First).Any(line => position.IsMatch(masked[line]))) continue;

            var comparison = loop.Groups["comparison"];
            var line = source.Lines[index];
            var corrected = line[..comparison.Index] + "<" + line[(comparison.Index + comparison.Length)..];

            yield return new LogicFinding(id, index + 1,
                $"`{variable} <= len({collection})` lets {variable} reach len({collection}), so on the loop's last pass `{collection}[{variable}]` " +
                $"asks for a position one past the end of {collection}, and Go stops the program with index out of range",
                LocalFix.ReplaceLine(id, "Stop the loop before len: <",
                    $"The positions in {collection} run from 0 to len({collection}) - 1, so the loop has to stop while {variable} is still less than len({collection}).",
                    source.Path, index + 1, corrected));
        }
    }

    /// <summary><c>float64(total / count)</c>: a division made a float afterwards.</summary>
    [GeneratedRegex(@"\b(?<float>float64|float32)\(\s*(?<dividend>[A-Za-z_]\w*|len\([^()]*\))\s*/\s*(?<divisor>[A-Za-z_]\w*|len\([^()]*\))\s*\)")]
    private static partial Regex FloatOfDivision();

    /// <summary>
    /// A division of two whole numbers turned into a float: Go divides the ints first, dropping the fraction, and only then
    /// makes a float of what is left - so 3 / 2 becomes 1, not 1.5.
    /// </summary>
    private static IEnumerable<LogicFinding> IntegerAverage(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var code = string.Join("\n", masked);

        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match division in FloatOfDivision().Matches(masked[index]))
            {
                var dividend = division.Groups["dividend"].Value;
                var divisor = division.Groups["divisor"].Value;
                if (!IsWholeNumber(dividend, code) || !IsWholeNumber(divisor, code)) continue;

                var kind = division.Groups["float"].Value;
                var line = source.Lines[index];
                var corrected = line[..division.Index] + $"{kind}({dividend}) / {kind}({divisor})" + line[(division.Index + division.Length)..];

                yield return new LogicFinding(id, index + 1,
                    $"`{dividend} / {divisor}` divides two whole numbers, which in Go gives a whole number - the fraction is dropped " +
                    $"before {kind} is applied, so 3 / 2 comes out as 1, not 1.5",
                    LocalFix.ReplaceLine(id, $"Make each number a {kind} before dividing",
                        $"Dividing {kind}s keeps the fraction, so each number is made a {kind} first.",
                        source.Path, index + 1, corrected));
            }
        }
    }

    [GeneratedRegex(@"^-?\d+$|^len\(|^u?int(?:8|16|32|64)?\(")]
    private static partial Regex WholeNumberValue();

    [GeneratedRegex(@"^u?int(?:8|16|32|64)?$")]
    private static partial Regex WholeNumberType();

    /// <summary>
    /// Whether a value is surely a whole number: len(...) always is, and a name is when the file makes it at least once
    /// and every way it makes it gives it one - := or var = a number with no point, len(...) or an int(...) conversion,
    /// or a var or parameter of an int type.
    /// </summary>
    private static bool IsWholeNumber(string value, string code)
    {
        if (value.StartsWith("len(", StringComparison.Ordinal)) return true;

        var name = Regex.Escape(value);
        var givenValues = Regex.Matches(code, $@"(?:(?<![\w.]){name}\s*:=|\bvar\s+{name}\s*=)\s*(?<value>[^\n;]*)")
            .Select(made => made.Groups["value"].Value.Trim())
            .ToList();
        var givenTypes = Regex.Matches(code, $@"(?<![\w.]){name}(?:\s*,\s*[A-Za-z_]\w*)*\s+(?<type>[A-Za-z_]\w*)\s*(?:[,)=\n]|$)")
            .Select(typed => typed.Groups["type"].Value)
            .ToList();

        if (givenValues.Count + givenTypes.Count == 0) return false;

        return givenValues.All(given => WholeNumberValue().IsMatch(given)) && givenTypes.All(type => WholeNumberType().IsMatch(type));
    }

    /// <summary><c>for i, mark := range marks {</c> - or with _ for the position.</summary>
    [GeneratedRegex(@"\bfor\s+(?<position>[A-Za-z_]\w*)\s*,\s*(?<value>[A-Za-z_]\w*)\s*:=\s*range\s+(?<collection>[A-Za-z_][\w.]*)\s*\{\s*$")]
    private static partial Regex RangeLoop();

    /// <summary>The names a position is given when the loop has none, in the order they are tried.</summary>
    private static readonly string[] PositionNames = ["i", "index", "position"];

    /// <summary>
    /// A range loop that gives its value a new value and never uses it after: the value is the loop's copy of the item, so
    /// the collection keeps what it had and the line does nothing. A line inside a loop nested in the body is left alone,
    /// as the nested loop can read the value again on its next pass.
    /// </summary>
    private static IEnumerable<LogicFinding> RangeValueChanged(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var code = string.Join("\n", masked);

        for (var heading = 0; heading < masked.Count; heading++)
        {
            if (RangeLoop().Match(masked[heading]) is not { Success: true } loop || BraceBlocks.Body(masked, heading) is not { } body) continue;

            var value = loop.Groups["value"].Value;
            var collection = loop.Groups["collection"].Value;
            var mention = new Regex($@"(?<![\w.]){Regex.Escape(value)}\b");
            var assignment = new Regex($@"^\s*(?<target>{Regex.Escape(value)})\s*(?:(?:[-+*/%&|^]|<<|>>|&\^)?=(?!=)|\+\+|--)");

            var bodyLines = Enumerable.Range(body.First, body.End - body.First).ToList();
            if (bodyLines.Where(line => assignment.IsMatch(masked[line])).ToList() is not [var changed]) continue;
            if (bodyLines.Any(line => line > changed && mention.IsMatch(masked[line]))) continue;
            if (InsideNestedLoopOrClosure(masked, body.First, changed)) continue;

            yield return new LogicFinding(id, changed + 1,
                $"Giving `{value}` a new value changes only the loop's copy of the item - {collection} keeps what it had - and nothing " +
                $"after this line uses {value}, so the line changes nothing",
                IndexedFix(id, source, masked, code, loop, heading, changed, assignment));
        }
    }

    /// <summary>Whether a line is inside a loop, or a function literal, that starts in the body above it.</summary>
    private static bool InsideNestedLoopOrClosure(IReadOnlyList<string> masked, int first, int line)
    {
        var openedByLoop = new Stack<bool>();

        for (var index = first; index < line; index++)
        {
            var opensLoop = Regex.IsMatch(masked[index], @"\bfor\b|\bfunc\s*\(");

            foreach (var character in masked[index])
            {
                if (character == '{') openedByLoop.Push(opensLoop);
                else if (character == '}' && openedByLoop.Count > 0) openedByLoop.Pop();
            }
        }

        return openedByLoop.Contains(true);
    }

    /// <summary>
    /// The change that puts the new value in the collection - <c>marks[i] = mark + 5</c> - when the collection is a slice,
    /// an array or a map the file makes; a loop with _ for its position is given one. Null for anything else, such as a
    /// string, whose bytes cannot be changed.
    /// </summary>
    private static LocalFix? IndexedFix(string id, SourceFile source, IReadOnlyList<string> masked, string code, Match loop, int heading, int changed, Regex assignment)
    {
        var collection = loop.Groups["collection"].Value;
        var name = Regex.Escape(collection);
        var madeChangeable = Regex.IsMatch(code, $@"(?<![\w.]){name}\s*:=\s*(?:\[|map\[|make\()|\bvar\s+{name}\s+(?:\[|map\[)|(?<![\w.]){name}\s+(?:\[|map\[)");
        if (!madeChangeable) return null;

        var position = loop.Groups["position"].Value;
        var bodyText = string.Join("\n", Enumerable.Range(heading, changed - heading + 1).Select(line => masked[line]));
        var givenPosition = position == "_" ? PositionNames.FirstOrDefault(candidate => !Regex.IsMatch(bodyText, $@"(?<![\w.]){candidate}\b")) : position;
        if (givenPosition is null) return null;

        var target = assignment.Match(masked[changed]).Groups["target"];
        var changedLine = source.Lines[changed];
        var storedInCollection = changedLine[..target.Index] + $"{collection}[{givenPosition}]" + changedLine[(target.Index + target.Length)..];
        var explanation = $"{collection}[{givenPosition}] is the item itself, so giving it the new value changes {collection}.";

        if (position != "_") return LocalFix.ReplaceLine(id, $"Change {collection}[{givenPosition}]", explanation, source.Path, changed + 1, storedInCollection);

        var positionGroup = loop.Groups["position"];
        var headingLine = source.Lines[heading];
        var namedHeading = headingLine[..positionGroup.Index] + givenPosition + headingLine[(positionGroup.Index + positionGroup.Length)..];

        return new LocalFix
        {
            RuleId = id,
            Title = $"Change {collection}[{givenPosition}]",
            Explanation = $"The loop is given a name for the position, {givenPosition}, and {explanation}",
            File = source.Path,
            StartLine = heading + 1,
            RemoveCount = changed - heading + 1,
            NewLines = [namedHeading, .. Enumerable.Range(heading + 1, changed - heading - 1).Select(line => source.Lines[line]), storedInCollection],
        };
    }

    /// <summary><c>age, _ := strconv.Atoi(text)</c>.</summary>
    [GeneratedRegex(@"(?<![\w.])(?<value>[A-Za-z_]\w*)\s*,\s*_\s*:?=\s*strconv\.(?<function>Atoi|ParseInt|ParseUint|ParseFloat|ParseBool)\s*\(")]
    private static partial Regex ParseWithErrorThrownAway();

    /// <summary>
    /// The error strconv gives for text that is not a number, thrown away with _: the value is then 0 - false, for
    /// ParseBool - and nothing says the text was not one. There is no one change for this: what to do instead is the
    /// program's to say.
    /// </summary>
    private static IEnumerable<LogicFinding> ParseErrorIgnored(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match parse in ParseWithErrorThrownAway().Matches(masked[index]))
            {
                var function = parse.Groups["function"].Value;
                var value = parse.Groups["value"].Value;
                var (what, nothing) = function == "ParseBool" ? ("true or false", "false") : ("a number", "0");

                yield return new LogicFinding(id, index + 1,
                    $"strconv.{function} gives back an error for text that is not {what}, and `_` throws it away - so for such text {value} is " +
                    $"{nothing}, and nothing says the text was wrong",
                    null);
            }
        }
    }

    /// <summary><c>strings.ToUpper(name)</c> as a statement of its own.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)(?<call>strings\.(?<function>ToUpper|ToLower|Title|TrimSpace|Trim|TrimLeft|TrimRight|TrimPrefix|TrimSuffix|Replace|ReplaceAll|Repeat)\(\s*(?<text>[A-Za-z_]\w*)\s*(?:,[^;]*)?\))\s*;?\s*$")]
    private static partial Regex StringsCallOnItsOwn();

    /// <summary>
    /// A strings function that gives back a changed copy, called on a line of its own: Go's strings never change, so the
    /// text it was given is just as it was. The copy is kept in the variable when the text is one the file makes.
    /// </summary>
    private static IEnumerable<LogicFinding> ResultDiscarded(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var code = string.Join("\n", masked);

        for (var index = 0; index < masked.Count; index++)
        {
            if (StringsCallOnItsOwn().Match(masked[index]) is not { Success: true } discarded) continue;

            var function = discarded.Groups["function"].Value;
            var text = discarded.Groups["text"].Value;
            var name = Regex.Escape(text);
            var isVariable = Regex.IsMatch(code, $@"(?<![\w.]){name}\s*:=|\bvar\s+{name}\b|(?<![\w.]){name}(?:\s*,\s*[A-Za-z_]\w*)*\s+string\b");

            var call = discarded.Groups["call"];
            var line = source.Lines[index];
            var fix = isVariable
                ? LocalFix.ReplaceLine(id, $"Keep the copy in {text}",
                    $"strings.{function} gives back the changed text; giving it to {text} keeps it.",
                    source.Path, index + 1, discarded.Groups["indent"].Value + $"{text} = " + line.Substring(call.Index, call.Length))
                : null;

            yield return new LogicFinding(id, index + 1,
                $"strings.{function} gives back a new, changed string and leaves {text} as it was - Go's strings never change - so on a " +
                "line of its own it changes nothing",
                fix);
        }
    }
}
