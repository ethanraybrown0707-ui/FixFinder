using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// Java's and C#'s own library used in a way that runs, says nothing, and does something else - each run with javac
/// -Xlint:all or the C# compiler, which said nothing of any:
/// a Thread's run() called where start() was meant ran its work on the thread that called it - "worker on main";
/// new BigDecimal(0.1) holds 0.1000000000000000055511151231257827021181583404541015625, the double 0.1 is, where
/// new BigDecimal("0.1") holds 0.1; Arrays.asList of an int[] is a list of one item, the array, so its size was 1 and
/// contains(80) false; Math.abs of a hashCode can be negative - the hashCode of "polygenelubricants" is the smallest int,
/// whose absolute value is itself - so Math.abs(...) % 10 was -8; a C# lambda made in a for loop shares the loop's one
/// counter, so three of them printed 3 3 3; and an async method called without await gives back its Task, which printed
/// as the Task's type name where 3 was meant.
/// </summary>
public static partial class ManagedLibraryPatterns
{
    private static readonly IReadOnlySet<string> JavaFiles = CodePattern.Files(".java");
    private static readonly IReadOnlySet<string> CSharpFiles = CodePattern.Files(".cs");

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        new CodePattern("logic-java-thread-run", JavaFiles, Syntax.CLike, JavaThreadRun),
        new CodePattern("logic-java-bigdecimal-from-double", JavaFiles, Syntax.CLike, JavaBigDecimalFromDouble, Severity.Warning, Confidence.Certain),
        new CodePattern("logic-java-aslist-of-primitive-array", JavaFiles, Syntax.CLike, JavaAsListOfPrimitives, Severity.Error, Confidence.Certain),
        new CodePattern("logic-java-abs-of-hash", JavaFiles, Syntax.CLike, JavaAbsOfHash),
        new CodePattern("logic-csharp-for-closure", CSharpFiles, Syntax.CLike, CSharpForClosure, Severity.Error),
        new CodePattern("logic-csharp-task-printed", CSharpFiles, Syntax.CLike, CSharpTaskPrinted, Severity.Error),
    ];

    /// <summary><c>worker.run();</c>, or <c>new Thread(...).run();</c> on one line.</summary>
    [GeneratedRegex(@"(?:(?<![\w.])(?<name>[A-Za-z_]\w*)|(?<made>\bnew\s+Thread\s*\(.*\)))\s*\.\s*run\s*\(\s*\)\s*;")]
    private static partial Regex RunCalled();

    private static IEnumerable<LogicFinding> JavaThreadRun(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var code = string.Join("\n", masked);
        for (var index = 0; index < masked.Count; index++)
        {
            if (RunCalled().Match(masked[index]) is not { Success: true } called) continue;

            var name = called.Groups["name"].Value;
            if (!called.Groups["made"].Success &&
                !Regex.IsMatch(code, $@"\b(?:Thread|var)\s+{Regex.Escape(name)}\s*=\s*new\s+Thread\s*\(")) continue;

            var line = source.Lines[index];
            var run = line.LastIndexOf("run", called.Index + called.Length, StringComparison.Ordinal);
            var thread = called.Groups["made"].Success ? "the new thread" : name;

            yield return new LogicFinding(id, index + 1,
                $"run() runs {thread}'s work here, on the thread that calls it - nothing runs alongside it, and the program waits for it to " +
                "finish - where start() starts it on a thread of its own",
                LocalFix.ReplaceLine(id, $"Start the thread: {(called.Groups["made"].Success ? "new Thread(...)" : name)}.start()",
                    "start() runs the work on the new thread, alongside the code that started it.",
                    source.Path, index + 1, line[..run] + "start" + line[(run + 3)..]));
        }
    }

    /// <summary><c>new BigDecimal(0.1)</c> - a decimal written as a double - or <c>new BigDecimal(price)</c>, price a double.</summary>
    [GeneratedRegex(@"\bnew\s+(?<type>(?:java\.math\.)?BigDecimal)\s*\(\s*(?<value>-?\d+\.\d+|[A-Za-z_]\w*)\s*\)")]
    private static partial Regex BigDecimalOfDouble();

    private static IEnumerable<LogicFinding> JavaBigDecimalFromDouble(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var code = string.Join("\n", masked);
        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match made in BigDecimalOfDouble().Matches(masked[index]))
            {
                var value = made.Groups["value"].Value;
                var literal = char.IsDigit(value[0]) || value[0] == '-';
                if (!literal && !Regex.IsMatch(code, $@"\b(?:double|float)\s+(?:[A-Za-z_]\w*\s*(?:=[^,;]*)?,\s*)*{Regex.Escape(value)}\b")) continue;

                // Only a decimal a double cannot hold exactly; 0.5 and 2.25 are held as they are written.
                if (literal && IsExactInBinary(value)) continue;

                var line = source.Lines[index];
                var type = made.Groups["type"].Value;
                var changed = literal ? $"new {type}(\"{value}\")" : $"{type}.valueOf({value})";

                yield return new LogicFinding(id, index + 1,
                    $"{(literal ? value : $"the double {value}")} is a double before BigDecimal sees it, and a double cannot hold most decimals exactly - " +
                    "new BigDecimal(0.1) is 0.1000000000000000055511151231257827021181583404541015625 - so the BigDecimal starts with that error " +
                    "in it",
                    LocalFix.ReplaceLine(id, $"Make it from text: {changed}",
                        literal ? "Made from the text \"" + value + "\", the BigDecimal holds exactly that decimal."
                            : "BigDecimal.valueOf goes through the double's shortest text - 0.1 for 0.1 - so it holds the decimal that was written.",
                        source.Path, index + 1, line[..made.Index] + changed + line[(made.Index + made.Length)..]));
            }
        }
    }

    /// <summary>
    /// Whether a decimal is held exactly by a double - its fraction a sum of halves, quarters, eighths and so on. Written with n
    /// digits after the point it is its digits over 10^n, which is a number of halves only when its digits divide by 5^n: 0.25 is
    /// 25 over 100, and 25 divides by 25; 0.1 is 1 over 10, and 1 does not divide by 5.
    /// </summary>
    internal static bool IsExactInBinary(string value)
    {
        var digits = value.TrimStart('-');
        var point = digits.IndexOf('.');
        var fractionDigits = point < 0 ? 0 : digits.Length - point - 1;
        return System.Numerics.BigInteger.Parse(digits.Replace(".", "", StringComparison.Ordinal)) % System.Numerics.BigInteger.Pow(5, fractionDigits) == 0;
    }

    /// <summary><c>Arrays.asList(marks)</c>.</summary>
    [GeneratedRegex(@"\bArrays\s*\.\s*asList\s*\(\s*(?<name>[A-Za-z_]\w*)\s*\)")]
    private static partial Regex AsListOfName();

    private static IEnumerable<LogicFinding> JavaAsListOfPrimitives(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var code = string.Join("\n", masked);
        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match asList in AsListOfName().Matches(masked[index]))
            {
                var name = asList.Groups["name"].Value;
                var declared = Regex.Match(code, $@"\b(?<type>int|long|double|float|short|byte|char|boolean)\s*(?:\[\s*\]\s*{Regex.Escape(name)}\b|{Regex.Escape(name)}\s*\[\s*\])");
                if (!declared.Success) continue;

                var type = declared.Groups["type"].Value;
                var line = source.Lines[index];
                var streamed = type is "int" or "long" or "double";
                var changed = $"Arrays.stream({name}).boxed().collect(java.util.stream.Collectors.toList())";

                yield return new LogicFinding(id, index + 1,
                    $"Arrays.asList makes a list of objects, and an {type}[] is one object, so Arrays.asList({name}) is a list of one item - the array - " +
                    "not of its numbers: its size is 1, and contains finds none of them",
                    streamed
                        ? LocalFix.ReplaceLine(id, $"Make a list of its numbers: {changed}",
                            "Arrays.stream goes over the numbers themselves, boxed() makes each an object, and collect puts them in a list.",
                            source.Path, index + 1, line[..asList.Index] + changed + line[(asList.Index + asList.Length)..])
                        : null);
            }
        }
    }

    /// <summary><c>Math.abs(key.hashCode()) % size</c>, or <c>Math.abs(random.nextInt()) % size</c>.</summary>
    [GeneratedRegex(@"\bMath\s*\.\s*abs\s*\(\s*(?<value>(?:""[^""]*""|[\w.]*(?:\([^()]*\))?)\s*\.\s*(?:hashCode|nextInt)\s*\(\s*\))\s*\)\s*%\s*(?<size>[\w.]+(?:\(\))?)")]
    private static partial Regex AbsThenRemainder();

    private static IEnumerable<LogicFinding> JavaAbsOfHash(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match remainder in AbsThenRemainder().Matches(masked[index]))
            {
                var line = source.Lines[index];
                var value = line.Substring(remainder.Groups["value"].Index, remainder.Groups["value"].Length);
                var size = remainder.Groups["size"].Value;
                var changed = $"Math.floorMod({value}, {size})";

                yield return new LogicFinding(id, index + 1,
                    "Math.abs of the smallest int is that same negative number, as its positive is too big for an int - and a hashCode or " +
                    "nextInt() can be the smallest int, as \"polygenelubricants\".hashCode() is - so the remainder after it can be negative: -8 " +
                    "for % 10, which used as a position stops the program",
                    LocalFix.ReplaceLine(id, $"Take the remainder that is never negative: {changed}",
                        $"Math.floorMod gives a remainder from 0 up to {size} - 1 for every int, the smallest too.",
                        source.Path, index + 1, line[..remainder.Index] + changed + line[(remainder.Index + remainder.Length)..]));
            }
        }
    }

    /// <summary><c>for (int i = 0; i &lt; 3; i++)</c> - the counter a C# for loop shares across every pass.</summary>
    [GeneratedRegex(@"^\s*for\s*\(\s*(?:int|var|long)\s+(?<counter>[A-Za-z_]\w*)\s*=")]
    private static partial Regex CSharpForHeader();

    /// <summary>A lambda kept for later - added to a list, handed to Task.Run or a Thread, or given to a name or an event.</summary>
    [GeneratedRegex(@"(?:\.Add\s*\(|\+=|Task\s*\.\s*Run\s*\(|new\s+Thread\s*\(|=)\s*(?:\(\s*\)|\(?[A-Za-z_]\w*\)?)\s*=>")]
    private static partial Regex LambdaKept();

    private static IEnumerable<LogicFinding> CSharpForClosure(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (CSharpForHeader().Match(masked[index]) is not { Success: true } loop || BraceBlocks.Body(masked, index) is not { } body) continue;

            var counter = loop.Groups["counter"].Value;
            var used = new Regex($@"(?<![\w.]){Regex.Escape(counter)}\b");
            for (var inside = body.First; inside < body.End; inside++)
            {
                if (LambdaKept().Match(masked[inside]) is not { Success: true } lambda) continue;

                var afterArrow = masked[inside].IndexOf("=>", lambda.Index, StringComparison.Ordinal) + 2;
                if (!used.IsMatch(masked[inside][afterArrow..])) continue;

                var line = source.Lines[inside];
                var copy = "current" + char.ToUpperInvariant(counter[0]) + counter[1..];
                var indent = CodeText.Indentation(line);
                var changedLine = line[..afterArrow] + used.Replace(line[afterArrow..], copy);

                yield return new LogicFinding(id, inside + 1,
                    $"A for loop has one {counter} for every pass, and the lambda keeps that one {counter}, not its value when the lambda was made - " +
                    $"so when the lambda runs, after the loop, {counter} is the value that ended the loop, the same for every lambda: three printed 3 3 3",
                    new LocalFix
                    {
                        RuleId = id, Title = $"Give each pass its own copy: var {copy} = {counter};",
                        Explanation = $"A variable declared inside the loop is a new one each pass, so each lambda keeps the {counter} of its own pass.",
                        File = source.Path, StartLine = inside + 1, RemoveCount = 1, NewLines = [$"{indent}var {copy} = {counter};", changedLine],
                    });
                break;
            }
        }
    }

    /// <summary><c>static async Task&lt;int&gt; CountAsync(</c> - an async method that gives back a value.</summary>
    [GeneratedRegex(@"\basync\s+(?:Task|ValueTask)\s*<[^>]+>\s+(?<name>[A-Za-z_]\w*)\s*\(")]
    private static partial Regex AsyncMethodWithValue();

    private static IEnumerable<LogicFinding> CSharpTaskPrinted(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var code = string.Join("\n", masked);
        var asyncNames = AsyncMethodWithValue().Matches(code).Select(method => method.Groups["name"].Value).ToHashSet();
        if (asyncNames.Count == 0) yield break;

        // await can be written only in top-level statements, or in an async method; a Main that is not async cannot.
        var awaitAllowed = !Regex.IsMatch(code, @"\bstatic\s+(?:void|int)\s+Main\s*\(");

        for (var index = 0; index < masked.Count; index++)
        {
            // var count = CountAsync(); - kept without await, and printed later.
            var kept = Regex.Match(masked[index], @"^(?<indent>\s*)var\s+(?<name>[A-Za-z_]\w*)\s*=\s*(?<call>(?<method>[A-Za-z_]\w*)\s*\([^()]*\))\s*;\s*$");
            if (!kept.Success || !asyncNames.Contains(kept.Groups["method"].Value)) continue;

            var name = kept.Groups["name"].Value;
            var printed = Enumerable.Range(index + 1, masked.Count - index - 1).FirstOrDefault(line =>
                Regex.IsMatch(masked[line], $@"\bConsole\s*\.\s*Write(?:Line)?\s*\(\s*{Regex.Escape(name)}\s*\)") ||
                Regex.IsMatch(source.Lines[line], $@"\$""[^""]*\{{\s*{Regex.Escape(name)}\s*[}}:]"), -1);
            if (printed < 0) continue;

            var line = source.Lines[index];
            var call = kept.Groups["call"];
            var method = kept.Groups["method"].Value;

            yield return new LogicFinding(id, index + 1,
                $"{method} is async, and called without await it gives back the Task that will finish with the value - not the value - so " +
                $"printing {name} on line {printed + 1} prints the Task's type, not what {method} works out",
                awaitAllowed
                    ? LocalFix.ReplaceLine(id, $"Wait for its value: var {name} = await {line.Substring(call.Index, call.Length)};",
                        $"await waits for {method} to finish and gives its value, so {name} is that value.",
                        source.Path, index + 1, line[..call.Index] + "await " + line[call.Index..])
                    : null);
        }
    }
}
