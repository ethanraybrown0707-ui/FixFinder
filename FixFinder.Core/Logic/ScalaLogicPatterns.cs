using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Logic;

/// <summary>
/// Mistakes Scala programs make that still compile. Each is a behaviour seen in Scala 3.8.4 and Scala 2.13.18 alike, and
/// neither compiler warns about any of them: two arrays with the same items are not ==, a whole-number average stored in
/// a Double has already lost its fraction, and xs.sorted on a line of its own leaves xs as it was.
/// </summary>
public static partial class ScalaLogicPatterns
{
    private static readonly IReadOnlySet<string> ScalaFiles = CodePattern.Files(".scala", ".sc");

    private static CodePattern Pattern(
        string id, Func<string, SourceFile, IReadOnlyList<string>, IEnumerable<LogicFinding>> find,
        Severity severity = Severity.Warning, Confidence confidence = Confidence.Likely, FindingKind kind = FindingKind.Logic) =>
        new(id, ScalaFiles, Syntax.Scala, find, severity, confidence, kind);

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        Pattern("logic-scala-range-to-length", RangeToLength, Severity.Error),
        Pattern("logic-scala-array-equals", ArrayEquals, Severity.Error),
        Pattern("logic-scala-integer-average", IntegerAverage, Severity.Error),
        Pattern("logic-scala-result-discarded", ResultDiscarded),
        Pattern("logic-scala-option-get", OptionGet, confidence: Confidence.Possible),
    ];

    /// <summary><c>for (i &lt;- 0 to xs.length)</c>, in either of Scala's ways of writing a for.</summary>
    [GeneratedRegex(@"\bfor\s*[({]?\s*(?<variable>[A-Za-z_]\w*)\s*<-\s*0\s+to\s+(?<collection>[A-Za-z_]\w*)\.(?<measure>length|size)\b(?!\s*-\s*1)")]
    private static partial Regex ForOverRangeToLength();

    /// <summary>
    /// A loop over <c>0 to xs.length</c> that uses its number as a position in xs: the range counts up to and including the
    /// length, so its last pass asks for a position one past the end - and the program stops with an index out of bounds.
    /// </summary>
    private static IEnumerable<LogicFinding> RangeToLength(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (ForOverRangeToLength().Match(masked[index]) is not { Success: true } loop) continue;
            if (ScalaLoops.RangeToLength().Match(masked[index]) is not { Success: true } range) continue;

            var variable = loop.Groups["variable"].Value;
            var collection = loop.Groups["collection"].Value;
            var measure = loop.Groups["measure"].Value;
            if (!LoopIndexes(masked, index, loop.Index + loop.Length, collection, variable)) continue;

            yield return new LogicFinding(id, index + 1,
                $"`{variable} <- 0 to {collection}.{measure}` counts up to and including {collection}.{measure}, so on its last pass " +
                $"`{collection}({variable})` asks for a position one past the end of {collection}",
                ScalaLoops.UntilFix(id, source, index + 1, range));
        }
    }

    /// <summary>Whether the loop's body - the rest of its line, or the lines indented under it - uses <c>xs(i)</c>.</summary>
    private static bool LoopIndexes(IReadOnlyList<string> masked, int loopLine, int afterHeading, string collection, string variable)
    {
        var position = new Regex($@"(?<![\w$.]){Regex.Escape(collection)}\s*\(\s*{Regex.Escape(variable)}\s*\)");
        if (position.IsMatch(masked[loopLine][afterHeading..])) return true;

        var indent = CodeText.Indentation(masked[loopLine]).Length;

        for (var next = loopLine + 1; next < masked.Count; next++)
        {
            if (masked[next].Trim().Length == 0) continue;
            if (CodeText.Indentation(masked[next]).Length <= indent) return false;
            if (position.IsMatch(masked[next])) return true;
        }

        return false;
    }

    [GeneratedRegex(@"\b(?:val|var)\s+(?<name>[A-Za-z_]\w*)\s*(?::\s*Array\s*\[[^\]]*\]\s*)?=\s*(?:Array\s*(?:\.\s*\w+\s*)?(?:\[[^\]]*\]\s*)?\(|new\s+Array\s*\[|[\w.]+\.toArray\b)")]
    private static partial Regex ArrayMade();

    [GeneratedRegex(@"(?<name>[A-Za-z_]\w*)\s*:\s*Array\s*\[")]
    private static partial Regex ArrayTyped();

    [GeneratedRegex(@"(?<![\w$.])(?<left>[A-Za-z_]\w*)\s*(?<comparison>==|!=)\s*(?<right>[A-Za-z_]\w*)(?![\w$.(\[])")]
    private static partial Regex Comparison();

    /// <summary>
    /// Two arrays compared with == or !=: an Array is compared as the very same array, not by its items - unlike a List - so
    /// two arrays made apart are never ==, whatever they hold. sameElements compares the items.
    /// </summary>
    private static IEnumerable<LogicFinding> ArrayEquals(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var arrays = masked
            .SelectMany(line => ArrayMade().Matches(line).Concat(ArrayTyped().Matches(line)))
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        if (arrays.Count < 2) yield break;

        for (var index = 0; index < masked.Count; index++)
        {
            if (Comparison().Matches(masked[index]).FirstOrDefault(match =>
                    arrays.Contains(match.Groups["left"].Value) && arrays.Contains(match.Groups["right"].Value) &&
                    match.Groups["left"].Value != match.Groups["right"].Value) is not { } comparison)
                continue;

            var left = comparison.Groups["left"].Value;
            var right = comparison.Groups["right"].Value;
            var equal = comparison.Groups["comparison"].Value == "==";
            var replacement = equal ? $"{left}.sameElements({right})" : $"!{left}.sameElements({right})";

            var line = source.Lines[index];
            var corrected = line[..comparison.Index] + replacement + line[(comparison.Index + comparison.Length)..];

            yield return new LogicFinding(id, index + 1,
                $"`{left} {comparison.Groups["comparison"].Value} {right}` asks whether {left} and {right} are the very same array, not whether they " +
                "hold the same items - two arrays made apart are never ==, whatever they hold",
                LocalFix.ReplaceLine(id, $"Compare their items: {replacement}",
                    "== on two arrays asks whether they are one and the same array. sameElements asks whether they hold the same items, in the same order.",
                    source.Path, index + 1, corrected));
        }
    }

    [GeneratedRegex(@"\b(?:val|var)\s+(?<name>[A-Za-z_]\w*)\s*(?::\s*(?:List|Array|Seq|Vector|IndexedSeq)\s*\[\s*(?:Int|Long)\s*\]\s*)?=\s*(?:List|Array|Seq|Vector|IndexedSeq)\s*\(\s*-?\d+(?:\s*,\s*-?\d+)*\s*\)")]
    private static partial Regex WholeNumbersMade();

    [GeneratedRegex(@"(?<name>[A-Za-z_]\w*)\s*:\s*(?:List|Array|Seq|Vector|IndexedSeq)\s*\[\s*(?:Int|Long)\s*\]")]
    private static partial Regex WholeNumbersTyped();

    [GeneratedRegex(@"\b(?:val|var)\s+(?<name>[A-Za-z_]\w*)\s*(?::\s*(?:Int|Long)\s*)?=\s*-?\d+\s*$")]
    private static partial Regex WholeNumberMade();

    [GeneratedRegex(@"(?<name>[A-Za-z_]\w*)\s*:\s*(?:Int|Long)\b(?!\s*\])")]
    private static partial Regex WholeNumberTyped();

    [GeneratedRegex(@"(?<name>[A-Za-z_]\w*)\s*:\s*(?:Double|Float|BigDecimal)\b")]
    private static partial Regex DecimalTyped();

    /// <summary>A val, var or method declared as a Double or a Float, and the value it is given on the same line.</summary>
    [GeneratedRegex(@"(?:\b(?:val|var)\s+[A-Za-z_]\w*|\bdef\s+[A-Za-z_]\w*\s*(?:\([^()]*\))*)\s*:\s*(?<type>Double|Float)\s*=\s*(?<value>\S.*?)\s*$")]
    private static partial Regex DecimalGiven();

    [GeneratedRegex(@"^(?<numbers>[A-Za-z_]\w*)\.sum\s*/\s*\k<numbers>\.(?:length|size)$")]
    private static partial Regex SumOverCount();

    [GeneratedRegex(@"^(?<dividend>[A-Za-z_]\w*)\s*/\s*(?<divisor>[A-Za-z_]\w*)$")]
    private static partial Regex OneOverAnother();

    /// <summary>
    /// A Double given the quotient of two whole numbers - <c>val average: Double = scores.sum / scores.length</c> for a list
    /// of Ints. Dividing whole numbers gives a whole number, so the fraction is gone before the answer becomes a Double.
    /// </summary>
    private static IEnumerable<LogicFinding> IntegerAverage(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var decimals = masked.SelectMany(line => DecimalTyped().Matches(line)).Select(match => match.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);

        var wholeNumberLists = masked.SelectMany(line => WholeNumbersMade().Matches(line).Concat(WholeNumbersTyped().Matches(line)))
            .Select(match => match.Groups["name"].Value).Where(name => !decimals.Contains(name)).ToHashSet(StringComparer.Ordinal);

        var wholeNumbers = masked.SelectMany(line => WholeNumberMade().Matches(line).Concat(WholeNumberTyped().Matches(line)))
            .Select(match => match.Groups["name"].Value).Where(name => !decimals.Contains(name)).ToHashSet(StringComparer.Ordinal);

        for (var index = 0; index < masked.Count; index++)
        {
            if (DecimalGiven().Match(masked[index]) is not { Success: true } given) continue;

            var value = given.Groups["value"];
            string? exact = null;

            if (SumOverCount().Match(value.Value) is { Success: true } average && wholeNumberLists.Contains(average.Groups["numbers"].Value))
            {
                var numbers = average.Groups["numbers"].Value;
                exact = $"{numbers}.sum.toDouble / {value.Value[(value.Value.IndexOf('/') + 1)..].Trim()}";
            }
            else if (OneOverAnother().Match(value.Value) is { Success: true } quotient &&
                     wholeNumbers.Contains(quotient.Groups["dividend"].Value) && wholeNumbers.Contains(quotient.Groups["divisor"].Value))
            {
                exact = $"{quotient.Groups["dividend"].Value}.toDouble / {quotient.Groups["divisor"].Value}";
            }

            if (exact is null) continue;

            var line = source.Lines[index];
            var corrected = line[..value.Index] + exact + line[(value.Index + value.Length)..];
            var type = given.Groups["type"].Value;

            yield return new LogicFinding(id, index + 1,
                $"`{value.Value}` divides one whole number by another, which gives a whole number: the part after the point is dropped before " +
                $"the answer becomes a {type} - 3 / 2 is 1, so it would be 1.0 rather than 1.5",
                LocalFix.ReplaceLine(id, $"Divide as decimals: {exact}",
                    "Making the first number a Double before dividing makes the division a decimal one, so the fraction is kept.",
                    source.Path, index + 1, corrected));
        }
    }

    /// <summary>
    /// A line that only calls a method giving back a changed copy - <c>marks.sorted</c>, <c>name.trim</c>,
    /// <c>marks :+ 90</c> - with the copy kept nowhere. Methods that take a function, such as map, are left out: the
    /// function may do something of its own, such as print.
    /// </summary>
    [GeneratedRegex(@"^(?<indent>\s*)(?<name>[A-Za-z_]\w*)(?<call>\s*\.\s*(?<method>sorted|reverse|distinct|toUpperCase|toLowerCase|trim|sortBy|sortWith|updated|appended|prepended|take|drop|takeRight|dropRight)\b(?:\s*\((?:[^()]|\([^()]*\))*\))?|\s*(?<method>:\+|\+\+)\s*\S.*)\s*$")]
    private static partial Regex CopyMadeAndDropped();

    /// <summary>A line that goes on onto the next: ending with =, an operator, an opening bracket or a comma.</summary>
    [GeneratedRegex(@"(?:=|=>|->|<-|[-+*/%(,.]|&&|\|\|)\s*$")]
    private static partial Regex GoesOn();

    private static IEnumerable<LogicFinding> ResultDiscarded(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var variables = masked.SelectMany(line => Regex.Matches(line, @"\bvar\s+(?<name>[A-Za-z_]\w*)")).Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        for (var index = 0; index < masked.Count; index++)
        {
            if (CopyMadeAndDropped().Match(masked[index]) is not { Success: true } statement) continue;

            // The last line of a block is the block's value, which is kept; only a line with another statement after it is lost.
            var next = NextCode(masked, index);
            if (next < 0 || CodeText.Indentation(masked[next]).Length != statement.Groups["indent"].Length) continue;
            if (PreviousCode(masked, index) is var previous and >= 0 && GoesOn().IsMatch(masked[previous])) continue;

            var name = statement.Groups["name"].Value;
            var method = statement.Groups["method"].Value;
            var line = source.Lines[index];
            var kept = $"{statement.Groups["indent"].Value}{name} = {line[statement.Groups["name"].Index..].TrimEnd()}";

            yield return new LogicFinding(id, index + 1,
                $"`{line.Trim()}` gives back a changed copy of {name} and leaves {name} as it was - nothing keeps the copy, so the line changes nothing",
                variables.Contains(name)
                    ? LocalFix.ReplaceLine(id, $"Keep the copy: {kept.Trim()}",
                        $"{method} does not change {name}; it gives back a new {(method is "toUpperCase" or "toLowerCase" or "trim" ? "piece of text" : "collection")}. " +
                        $"{name} is a var, so it can be given the copy.",
                        source.Path, index + 1, kept)
                    : null);
        }
    }

    [GeneratedRegex(@"\.(?<call>get|find|headOption|lastOption|collectFirst|maxOption|minOption|lift)\s*(?:\((?:[^()]|\((?:[^()]|\([^()]*\))*\))*\))?\s*\.get\b(?![\w$(])")]
    private static partial Regex OptionTakenWithGet();

    [GeneratedRegex(@"\.(?:contains|isDefined|nonEmpty|isEmpty|exists|forall)\b")]
    private static partial Regex CheckedFirst();

    /// <summary>
    /// A value taken out of an Option with .get, where nothing above checks there is one: a Map's get(key).get, or
    /// find(...).get. When there is none, the program stops with NoSuchElementException: None.get.
    /// </summary>
    private static IEnumerable<LogicFinding> OptionGet(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (OptionTakenWithGet().Match(masked[index]) is not { Success: true } taken) continue;
            if (Enumerable.Range(Math.Max(0, index - 2), Math.Min(index, 2) + 1).Any(near => CheckedFirst().IsMatch(masked[near]))) continue;

            var call = taken.Groups["call"].Value;
            var written = source.Lines[index].Substring(taken.Index, taken.Length).Trim();
            var what = call == "get"
                ? "takes the value for a key out of what get gave back without checking the key is there - for a key that is not"
                : $"takes the value out of the Option {call} gave back without checking there is one - when there is none";

            yield return new LogicFinding(id, index + 1, $"`{written}` {what}, the program stops with NoSuchElementException: None.get", null);
        }
    }

    private static int NextCode(IReadOnlyList<string> masked, int index)
    {
        for (var next = index + 1; next < masked.Count; next++)
            if (masked[next].Trim().Length > 0) return next;

        return -1;
    }

    private static int PreviousCode(IReadOnlyList<string> masked, int index)
    {
        for (var previous = index - 1; previous >= 0; previous--)
            if (masked[previous].Trim().Length > 0) return previous;

        return -1;
    }
}
