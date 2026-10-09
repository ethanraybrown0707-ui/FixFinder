using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Logic;

/// <summary>
/// Mistakes OCaml programs make that still compile: a for loop to a length, which OCaml counts up to and including; ==
/// and != - which ask whether two values are the very same one in memory - used for text, lists and floats, where = and
/// &lt;&gt; were meant; and a whole-number division turned into a float after its fraction was already dropped.
/// </summary>
public static partial class OCamlLogicPatterns
{
    private static readonly IReadOnlySet<string> OCamlFiles = CodePattern.Files(".ml");

    private static CodePattern Pattern(
        string id, Func<string, SourceFile, IReadOnlyList<string>, IEnumerable<LogicFinding>> find,
        Severity severity = Severity.Warning, Confidence confidence = Confidence.Likely) =>
        new(id, OCamlFiles, Syntax.OCaml, find, severity, confidence);

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        Pattern("logic-ocaml-for-to-length", ForToLength, Severity.Error),
        Pattern("logic-ocaml-physical-equality", PhysicalEquality, Severity.Error),
        Pattern("logic-ocaml-integer-average", IntegerAverage, Severity.Error),
    ];

    /// <summary>
    /// <c>for i = 0 to Array.length a do</c> with <c>a.(i)</c> - or a.[i], Array.get a i, List.nth a i - inside: the last pass
    /// asks for position Array.length a, one past the end.
    /// </summary>
    private static IEnumerable<LogicFinding> ForToLength(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (OCamlLoops.ForToLength().Match(masked[index]) is not { Success: true } loop) continue;

            var variable = Regex.Escape(loop.Groups["variable"].Value);
            var collection = Regex.Escape(loop.Groups["collection"].Value);
            var position = new Regex($@"(?<![\w.']){collection}\s*\.\s*[(\[]\s*{variable}\s*[)\]]|\b(?:Array|String|Bytes)\.get\s+{collection}\s+{variable}\b|\bList\.nth\s+{collection}\s+{variable}\b");

            if (!Body(masked, index).Any(line => position.IsMatch(line))) continue;

            yield return new LogicFinding(id, index + 1,
                $"`for {loop.Groups["variable"].Value} = 0 to {loop.Groups["length"].Value}` counts up to and including {loop.Groups["length"].Value}, so its last pass asks " +
                $"{loop.Groups["collection"].Value} for a position one past its end",
                OCamlLoops.UpperBoundFix(id, source, index + 1, loop));
        }
    }

    /// <summary>The loop's body: from its do to the done that closes it.</summary>
    private static IEnumerable<string> Body(IReadOnlyList<string> masked, int loopLine)
    {
        var depth = 0;

        for (var index = loopLine; index < masked.Count; index++)
        {
            var line = index == loopLine ? masked[index][(masked[index].IndexOf(" do", StringComparison.Ordinal) + 3)..] : masked[index];
            yield return line;

            depth += Regex.Matches(line, @"\bdo\b").Count - Regex.Matches(line, @"\bdone\b").Count;
            if (depth < 0 || (index > loopLine && depth <= 0 && Regex.IsMatch(line, @"\bdone\b"))) yield break;
        }
    }

    /// <summary>
    /// == or != with text, a list that is not empty, or a decimal on one side - written in the code, so certainly one of them.
    /// Those are made in memory each time, so two equal ones need not be the same one: = and &lt;&gt; compare what they hold.
    /// </summary>
    [GeneratedRegex(@"(?<left>""\s*""|\[[^\]]*[^\]\s][^\]]*\]|\b\d+\.\d*)\s*(?<operator>==|!=)(?![=])|(?<![=<>!:])(?<operator>==|!=)\s*(?<right>""\s*""|\[[^\]]*[^\]\s][^\]]*\]|\d+\.\d*)")]
    private static partial Regex ComparedInMemory();

    private static IEnumerable<LogicFinding> PhysicalEquality(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (ComparedInMemory().Match(masked[index]) is not { Success: true } comparison) continue;

            var symbol = comparison.Groups["operator"];
            var meant = symbol.Value == "==" ? "=" : "<>";
            var line = source.Lines[index];
            var corrected = line[..symbol.Index] + meant + line[(symbol.Index + symbol.Length)..];

            yield return new LogicFinding(id, index + 1,
                $"`{symbol.Value}` asks whether two values are the very same one in memory, not whether they are {(meant == "=" ? "equal" : "different")} - and text, " +
                "lists and decimals are made anew each time, so two equal ones need not be the same one",
                LocalFix.ReplaceLine(id, $"Compare what they hold with {meant}",
                    $"{meant} compares the values themselves, which is what {symbol.Value} is almost always meant to do here.",
                    source.Path, index + 1, corrected));
        }
    }

    /// <summary><c>float_of_int (total / count)</c>: the division is between ints, so its fraction is gone before it becomes a float.</summary>
    [GeneratedRegex(@"\b(?<convert>float_of_int|float)\s*\(\s*(?<dividend>[^()/]+?|[^()/]*\([^()]*\)[^()/]*?)\s*/(?!\.)\s*(?<divisor>[^()/]+?|[^()/]*\([^()]*\)[^()/]*?)\s*\)")]
    private static partial Regex ConvertedAfterDividing();

    private static IEnumerable<LogicFinding> IntegerAverage(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        static string AsFloat(string value) => Regex.IsMatch(value, @"^[\w.']+$") ? $"float_of_int {value}" : $"float_of_int ({value})";

        for (var index = 0; index < masked.Count; index++)
        {
            if (ConvertedAfterDividing().Match(masked[index]) is not { Success: true } converted) continue;

            var line = source.Lines[index];
            var dividend = line.Substring(converted.Groups["dividend"].Index, converted.Groups["dividend"].Length).Trim();
            var divisor = line.Substring(converted.Groups["divisor"].Index, converted.Groups["divisor"].Length).Trim();
            var exact = $"({AsFloat(dividend)} /. {AsFloat(divisor)})";
            var corrected = line[..converted.Index] + exact + line[(converted.Index + converted.Length)..];

            yield return new LogicFinding(id, index + 1,
                $"`{dividend} / {divisor}` divides one int by another, which gives an int: its fraction is dropped before {converted.Groups["convert"].Value} " +
                "makes it a float - 7 / 2 is 3, so this gives 3. rather than 3.5",
                LocalFix.ReplaceLine(id, $"Divide as floats: {exact}",
                    "Turning each number into a float before dividing, and dividing with /., keeps the fraction.",
                    source.Path, index + 1, corrected));
        }
    }
}
