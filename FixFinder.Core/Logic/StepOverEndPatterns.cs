using System.Numerics;
using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A loop that runs while its counter != its end, and steps right over the end - each run with its language's own toolchain,
/// which said nothing. Counting from 0 by 3, the counter went 3, 6, 9, 12, 15 in Python and 0, 3, 6, 9, 12, 15 in Node, never
/// 10, and neither loop ended. An int counter in Java, C and C# ran on past 10 too, wrapping round past the largest int, and
/// landed on 10 only after 2,863,311,534 passes. With count &lt; 10 each stopped after 4.
/// </summary>
public static partial class StepOverEndPatterns
{
    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        new CodePattern("logic-python-loop-steps-over-end", CodePattern.Files(".py", ".pyw"), Syntax.Python, PythonWhileStepsOver, Severity.Error, Confidence.Certain),
        new CodePattern("logic-loop-steps-over-end", CodePattern.Files(".c", ".cpp", ".cc", ".cxx", ".c++", ".java", ".cs", ".js", ".mjs", ".cjs"), Syntax.CLike,
            ForStepsOver, Severity.Error, Confidence.Certain),
    ];

    /// <summary><c>for (int count = 0; count != 10; count += 3)</c> - a start, an end tested with !=, and a step, all whole numbers.</summary>
    [GeneratedRegex(@"\bfor\s*\(\s*(?:(?<type>int|long|short|let|var|auto)\s+)?(?<name>[A-Za-z_$][\w$]*)\s*=\s*(?<start>-?\d+)\s*;\s*\k<name>\s*(?<test>!==?)\s*(?<end>-?\d+)\s*;\s*(?:\k<name>\s*(?<op>[+-])=\s*(?<step>\d+)|\k<name>\s*(?<op>\+\+|--)|(?<op>\+\+|--)\s*\k<name>)\s*\)")]
    private static partial Regex ForLoop();

    /// <summary><c>while count != 10:</c>.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)while\s+(?<name>[A-Za-z_]\w*)\s*(?<test>!=)\s*(?<end>-?\d+)\s*:\s*$")]
    private static partial Regex PythonWhile();

    /// <summary>
    /// Whether counting from <paramref name="start"/> by <paramref name="step"/> - up, or down when it is negative - ever lands
    /// exactly on <paramref name="end"/>.
    /// </summary>
    private static bool LandsOn(long start, long step, long end) =>
        step != 0 && (end - start) % step == 0 && (end - start) / step >= 0;

    /// <summary>
    /// Whether the counter goes away from the end - up from 10 towards 0 - rather than over it. Then &lt; or &gt; would stop the
    /// loop before it started, which is not what was meant either, so no change is offered.
    /// </summary>
    private static bool GoesAway(long start, long step, long end) => (end - start) / (double)step < 0;

    /// <summary>The counter's first values, up to the first one past the end: "3, 6, 9, 12".</summary>
    private static string ValuesGoing(long start, long step, long end, bool includeStart)
    {
        var values = new List<long>();
        var value = includeStart ? start : start + step;
        while (values.Count < 6)
        {
            values.Add(value);
            if (step > 0 ? value > end : value < end) break;
            value += step;
        }

        return string.Join(", ", values);
    }

    /// <summary>
    /// How many passes an int counter takes to land on <paramref name="end"/>, wrapping round past the largest int - the
    /// smallest number of steps whose total, counted round 2^32, is the distance to the end - or null when it never lands on it:
    /// counting by 3 from 0 it reaches 10 after 2,863,311,534 passes, as Java, C and C# all printed; counting by 2 from 1, never.
    /// </summary>
    private static BigInteger? PassesInAnInt(long start, long step, long end)
    {
        var round = BigInteger.Pow(2, 32);
        var distance = (((BigInteger)end - start) % round + round) % round;
        var stride = ((BigInteger)step % round + round) % round;
        var shared = BigInteger.GreatestCommonDivisor(stride, round);
        if (distance % shared != 0) return null;

        var reducedRound = round / shared;
        return distance / shared * InverseOf(stride / shared, reducedRound) % reducedRound;
    }

    /// <summary>The number that, multiplied by <paramref name="value"/>, leaves 1 when divided by <paramref name="modulus"/>.</summary>
    private static BigInteger InverseOf(BigInteger value, BigInteger modulus)
    {
        var (previousRemainder, remainder) = (modulus, value % modulus);
        var (previousFactor, factor) = (BigInteger.Zero, BigInteger.One);
        while (remainder != 0)
        {
            var quotient = previousRemainder / remainder;
            (previousRemainder, remainder) = (remainder, previousRemainder - quotient * remainder);
            (previousFactor, factor) = (factor, previousFactor - quotient * factor);
        }

        return (previousFactor % modulus + modulus) % modulus;
    }

    /// <summary>What happens to the loop once the counter has stepped over its end, by how its counter is kept.</summary>
    private enum Counter
    {
        /// <summary>Python's whole numbers and JavaScript's numbers, which never wrap round: the loop never ends.</summary>
        NeverWraps,

        /// <summary>An int in C, C++, Java or C#: it wraps round past its largest value, and may land on the end after.</summary>
        Int,

        /// <summary>Another whole-number type - long, short - which wraps round at a size of its own.</summary>
        OtherWholeNumber,
    }

    private static string Message(string name, long start, long step, long end, bool includeStart, Counter counter)
    {
        var goes = $"{name} starts at {start} and goes {(step > 0 ? "up" : "down")} by {Math.Abs(step)}";
        if (GoesAway(start, step, end) && counter == Counter.NeverWraps)
            return $"{goes}, away from {end} - so it is never {end}, and {name} != {end} stays true: the loop never ends";

        var steppedOver = GoesAway(start, step, end)
            ? $"{goes}, away from {end}, so {name} != {end} stays true"
            : $"{goes} - {ValuesGoing(start, step, end, includeStart)} - stepping over {end} without ever being it, so {name} != {end} stays true";

        return counter switch
        {
            Counter.NeverWraps => $"{steppedOver}: the loop never ends",
            Counter.Int => PassesInAnInt(start, step, end) is { } passes
                ? FormattableString.Invariant($"{steppedOver}, and the loop runs on past {end}: an int wraps round past its largest value, and {name} lands on {end} only after {passes:N0} passes")
                : $"{steppedOver}, and the loop runs on past {end}: an int wraps round past its largest value, but {name} never lands on {end}, so the loop never ends",
            _ => $"{steppedOver}, and the loop runs on far past {end}, until {name} wraps round past the largest value its type holds",
        };
    }

    private static IEnumerable<LogicFinding> ForStepsOver(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var script = Path.GetExtension(source.Path).ToLowerInvariant() is ".js" or ".mjs" or ".cjs" || TypeScriptCheck.IsTypeScript(source.Path);

        for (var index = 0; index < masked.Count; index++)
        {
            if (ForLoop().Match(masked[index]) is not { Success: true } loop) continue;

            var start = long.Parse(loop.Groups["start"].Value);
            var end = long.Parse(loop.Groups["end"].Value);
            var op = loop.Groups["op"].Value;
            var step = (op.StartsWith('-') ? -1 : 1) * (loop.Groups["step"].Success ? long.Parse(loop.Groups["step"].Value) : 1);
            if (LandsOn(start, step, end)) continue;

            var counter = script ? Counter.NeverWraps : loop.Groups["type"].Value is "int" or "var" or "auto" ? Counter.Int : Counter.OtherWholeNumber;
            var line = source.Lines[index];
            var test = loop.Groups["test"];
            var stops = step > 0 ? "<" : ">";
            var name = loop.Groups["name"].Value;

            yield return new LogicFinding(id, index + 1, Message(name, start, step, end, includeStart: true, counter),
                GoesAway(start, step, end)
                    ? null
                    : LocalFix.ReplaceLine(id, $"Stop once it reaches the end: {name} {stops} {end}",
                        $"{name} {stops} {end} is false as soon as {name} gets to {end} or past it, so the loop ends whether or not it lands on {end}.",
                        source.Path, index + 1, line[..test.Index] + stops + line[(test.Index + test.Length)..]));
        }
    }

    /// <summary>
    /// Python's while count != 10: with count given a whole number before it, and stepped by a whole number once in the loop -
    /// count += 3 - and nowhere else. Python's whole numbers never wrap round, so the loop never ends.
    /// </summary>
    private static IEnumerable<LogicFinding> PythonWhileStepsOver(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (PythonWhile().Match(masked[index]) is not { Success: true } loop) continue;

            var name = loop.Groups["name"].Value;
            var given = Enumerable.Range(0, index).Reverse()
                .Select(earlier => Regex.Match(masked[earlier], $@"^\s*{name}\s*=\s*(?<start>-?\d+)\s*$"))
                .FirstOrDefault(found => found.Success);
            if (given is null) continue;

            // The loop's body: the lines after it indented more deeply.
            var depth = CodeText.Indentation(source.Lines[index]).Length;
            var body = Enumerable.Range(index + 1, masked.Count - index - 1)
                .TakeWhile(later => masked[later].Trim().Length == 0 || CodeText.Indentation(source.Lines[later]).Length > depth)
                .Select(later => masked[later])
                .ToList();

            var changes = body.Where(code => Regex.IsMatch(code, $@"^\s*{name}\s*(?:[-+*/%]?=)(?!=)")).ToList();
            if (changes.Count != 1 || Regex.Match(changes[0], $@"^\s*{name}\s*(?<op>[+-])=\s*(?<step>\d+)\s*$") is not { Success: true } stepped) continue;

            var start = long.Parse(given.Groups["start"].Value);
            var end = long.Parse(loop.Groups["end"].Value);
            var step = (stepped.Groups["op"].Value == "-" ? -1 : 1) * long.Parse(stepped.Groups["step"].Value);
            if (LandsOn(start, step, end)) continue;

            var line = source.Lines[index];
            var test = loop.Groups["test"];
            var stops = step > 0 ? "<" : ">";

            yield return new LogicFinding(id, index + 1, Message(name, start, step, end, includeStart: false, Counter.NeverWraps),
                GoesAway(start, step, end)
                    ? null
                    : LocalFix.ReplaceLine(id, $"Stop once it reaches the end: while {name} {stops} {end}:",
                        $"{name} {stops} {end} is False as soon as {name} gets to {end} or past it, so the loop ends whether or not it lands on {end}.",
                        source.Path, index + 1, line[..test.Index] + stops + line[(test.Index + test.Length)..]));
        }
    }
}
