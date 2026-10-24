using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Logic;

/// <summary>
/// Mistakes Elixir programs make that run on and give the wrong answer - each run with Elixir 1.20.4, and Elixir said
/// nothing of the first five: a range to <c>0..length(marks)</c>, which takes in its end, so <c>Enum.at(marks, i)</c> is nil
/// on the last pass; a line read with IO.gets compared with text, though it still has its newline; <c>Map.put</c> or
/// <c>String.upcase</c> on a line of its own, whose changed copy is lost; a decimal worked out by arithmetic, compared
/// exactly; and <c>if mark = 40 do</c>, which gives mark the value 40 and is always true. The sixth - a variable given a
/// new value inside fn, for or if, which leaves the one outside as it was - Elixir warns of only as a variable that is
/// unused. And text compared with a number, which Elixir also says nothing of: "7" == 7 was false, and "7" &gt; 10 true.
/// </summary>
public static partial class ElixirLogicPatterns
{
    private static readonly IReadOnlySet<string> ElixirFiles = CodePattern.Files(".exs", ".ex");

    private static CodePattern Pattern(
        string id, Func<string, SourceFile, IReadOnlyList<string>, IEnumerable<LogicFinding>> find,
        Severity severity = Severity.Warning, Confidence confidence = Confidence.Likely) =>
        new(id, ElixirFiles, Syntax.Python, find, severity, confidence);

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        Pattern("logic-elixir-range-to-length", RangeToLength, Severity.Error),
        Pattern("logic-elixir-input-not-trimmed", InputNotTrimmed, Severity.Error),
        Pattern("logic-elixir-result-discarded", ResultDiscarded),
        Pattern("logic-elixir-float-equality", FloatEquality),
        Pattern("logic-elixir-assignment-in-condition", AssignmentInCondition, Severity.Error, Confidence.Certain),
        Pattern("logic-elixir-new-variable-in-block", NewVariableInBlock, Severity.Error),
        Pattern("logic-elixir-text-compared-with-number", TextComparedWithNumber, Severity.Error, Confidence.Certain),
    ];

    /// <summary><c>for i &lt;- 0..length(marks) do</c>, or <c>Enum.each(0..length(marks), fn i -&gt;</c>: a range to the length itself.</summary>
    [GeneratedRegex(@"(?:\bfor\s+(?<variable>[a-z_]\w*)\s*<-\s*|\bEnum\.each\(\s*)(?<range>0\s*\.\.\s*length\(\s*(?<list>[a-z_]\w*)\s*\))(?!\s*-|\s*//)(?:\s*,\s*fn\s+(?<fnVariable>[a-z_]\w*)\s*->|\s+do\b|\s*,)")]
    private static partial Regex RangeToLengthHeading();

    /// <summary>
    /// A range to the length whose number is used as a position with Enum.at: Elixir's ranges take in their end, and positions
    /// start at 0, so on the last pass the position is one past the end, and Enum.at gives nil. The position is looked for in the
    /// lines as written - "#{Enum.at(marks, i)}" is inside a string.
    /// </summary>
    private static IEnumerable<LogicFinding> RangeToLength(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (RangeToLengthHeading().Match(masked[index]) is not { Success: true } heading) continue;

            var variable = heading.Groups["variable"].Success ? heading.Groups["variable"].Value : heading.Groups["fnVariable"].Value;
            var list = heading.Groups["list"].Value;
            var position = new Regex($@"\bEnum\.at\(\s*{Regex.Escape(list)}\s*,\s*{Regex.Escape(variable)}\s*\)");
            if (variable.Length == 0 || !Enumerable.Range(index, BlockEnd(masked, index) - index).Any(line => position.IsMatch(source.Lines[line]))) continue;

            var range = heading.Groups["range"];
            var line = source.Lines[index];

            yield return new LogicFinding(id, index + 1,
                $"0..length({list}) takes in length({list}) itself - Elixir's ranges include their end - and positions start at 0, so on " +
                $"the last pass Enum.at({list}, {variable}) is one past the end, and gives nil with no error",
                LocalFix.ReplaceLine(id, $"Stop at the last position: 0..(length({list}) - 1)//1",
                    $"length({list}) - 1 is the last position. //1 makes the range count up, so for an empty list it has no positions at all.",
                    source.Path, index + 1, line[..range.Index] + $"0..(length({list}) - 1)//1" + line[(range.Index + range.Length)..]));
        }
    }

    /// <summary>The line after the last one indented under <paramref name="heading"/> - the heading's own line alone when nothing is.</summary>
    private static int BlockEnd(IReadOnlyList<string> masked, int heading)
    {
        var indent = CodeText.Indentation(masked[heading]).Length;
        var end = heading + 1;
        while (end < masked.Count && (masked[end].Trim().Length == 0 || CodeText.Indentation(masked[end]).Length > indent)) end++;
        return end;
    }

    /// <summary><c>answer = IO.gets("Continue? ")</c>: a line read as it was typed, newline and all.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)(?<name>[a-z_]\w*)\s*=\s*(?<read>IO\.gets\((?:[^()]|\([^()]*\))*\))\s*$")]
    private static partial Regex ReadLine();

    /// <summary>
    /// A line read with IO.gets and compared with text: IO.gets gives back the line with its newline still on the end, so it
    /// never equals the text typed. The change trims it as it is read.
    /// </summary>
    private static IEnumerable<LogicFinding> InputNotTrimmed(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (ReadLine().Match(masked[index]) is not { Success: true } read) continue;

            var name = read.Groups["name"].Value;
            var compared = new Regex($@"(?<![\w.]){Regex.Escape(name)}\s*(?:===?|!==?)\s*""|""\s*(?:===?|!==?)\s*{Regex.Escape(name)}\b");
            if (!masked.Skip(index + 1).Any(line => compared.IsMatch(line))) continue;

            var line = source.Lines[index];
            var readCall = line.Substring(read.Groups["read"].Index, read.Groups["read"].Length);

            yield return new LogicFinding(id, index + 1,
                $"IO.gets gives back the line typed with its newline still on the end - \"y\\n\", not \"y\" - so comparing {name} with text " +
                "is never true for what was typed",
                LocalFix.ReplaceLine(id, $"Trim the line as it is read: {name} = String.trim(IO.gets(...))",
                    "String.trim takes the newline - and any spaces - off both ends, so the text is what was typed.",
                    source.Path, index + 1, $"{read.Groups["indent"].Value}{name} = String.trim({readCall})"));
        }
    }

    /// <summary><c>Map.put(ages, "Bob", 20)</c> on a line of its own: a function that gives back a changed copy.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)(?<call>(?<module>Map|List|String|Enum|Keyword|MapSet)\.(?<function>put|put_new|delete|update|merge|drop|insert_at|replace_at|delete_at|update_at|upcase|downcase|capitalize|trim|reverse|sort|uniq|map|filter|reject|replace|concat)\(\s*(?<name>[a-z_]\w*)\b.*\))\s*$")]
    private static partial Regex CopyOnItsOwn();

    /// <summary>
    /// A changed copy made on a line of its own, and not the last line of a function - where it would be what the function gives
    /// back: Elixir's values never change, so the copy is lost and the value is as it was. The change keeps the copy in the name.
    /// </summary>
    private static IEnumerable<LogicFinding> ResultDiscarded(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (CopyOnItsOwn().Match(masked[index]) is not { Success: true } copy) continue;

            // The last line of a block is what the block gives back, and an end follows it.
            var next = masked.Skip(index + 1).FirstOrDefault(line => line.Trim().Length > 0);
            if (next is not null && Regex.IsMatch(next, @"^\s*end\b")) continue;
            if (next is null && copy.Groups["indent"].Value.Length > 0) continue;

            var name = copy.Groups["name"].Value;
            var function = $"{copy.Groups["module"].Value}.{copy.Groups["function"].Value}";
            var call = source.Lines[index].Substring(copy.Groups["call"].Index, copy.Groups["call"].Length);

            yield return new LogicFinding(id, index + 1,
                $"Elixir's values never change: {function} gives back a new, changed copy and leaves {name} as it was - so on a line of " +
                "its own the change is lost",
                LocalFix.ReplaceLine(id, $"Keep the copy in {name}: {name} = {function}(...)",
                    $"{function} gives back the changed copy; giving it to {name} keeps it.",
                    source.Path, index + 1, $"{copy.Groups["indent"].Value}{name} = {call}"));
        }
    }

    /// <summary>A name given a value by arithmetic with a decimal in it: total = 0.1 + 0.2, total = total + 0.1.</summary>
    [GeneratedRegex(@"^\s*(?<name>[a-z_]\w*)\s*=(?![=~>])\s*(?<value>.*\d+\.\d+.*)$")]
    private static partial Regex DecimalWorkedOut();

    [GeneratedRegex(@"(?<![\w.])(?<name>[a-z_]\w*)\s*(?<op>===?|!==?)\s*(?<value>\d+\.\d*[1-9]\d*)(?![\w.])")]
    private static partial Regex ComparedWithDecimal();

    /// <summary>
    /// A decimal worked out by arithmetic, compared exactly with a decimal: the arithmetic leaves a tiny error - 0.1 + 0.2 is
    /// 0.30000000000000004 - so the comparison is false when the two are equal for every practical purpose. The change
    /// compares the difference with a tiny tolerance, written 1.0e-9 as Elixir needs.
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
            var equal = compared.Groups["op"].Value.StartsWith('=');
            var close = equal ? $"abs({name} - {value}) < 1.0e-9" : $"abs({name} - {value}) >= 1.0e-9";
            var line = source.Lines[index];

            yield return new LogicFinding(id, index + 1,
                $"{name} is a decimal worked out by arithmetic, which leaves a tiny error - 0.1 + 0.2 is 0.30000000000000004 - so " +
                $"comparing it exactly with {value} is {(equal ? "false" : "true")} even when the two are equal for every practical purpose",
                LocalFix.ReplaceLine(id, $"Compare within a tiny tolerance: {close}",
                    "The difference between the two is compared with a tiny tolerance, which allows for the error the arithmetic left.",
                    source.Path, index + 1, line[..compared.Index] + close + line[(compared.Index + compared.Length)..]));
        }
    }

    /// <summary><c>if mark = 40 do</c>: = in a condition, given a plain value.</summary>
    [GeneratedRegex(@"\b(?<keyword>if|unless)\s+(?<name>[a-z_]\w*)\s*(?<match>=)(?![=~>])\s*(?<value>-?\d+(?:\.\d+)?|""[^""]*""|:\w+|true|false|nil)\s*(?:,\s*do:|\s+do\b)")]
    private static partial Regex MatchInCondition();

    /// <summary>
    /// = in an if's condition: in Elixir = matches, giving the name the value, and the condition is then that value - which
    /// counts as true unless it is false or nil - so the block runs whatever the name held, and the name has lost its value.
    /// </summary>
    private static IEnumerable<LogicFinding> AssignmentInCondition(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (MatchInCondition().Match(masked[index]) is not { Success: true } condition) continue;

            var name = condition.Groups["name"].Value;
            var value = source.Lines[index].Substring(condition.Groups["value"].Index, condition.Groups["value"].Length);
            var match = condition.Groups["match"];
            var line = source.Lines[index];

            yield return new LogicFinding(id, index + 1,
                $"= in Elixir matches: it gives {name} the value {value}, and the condition is then that value - so the {condition.Groups["keyword"].Value} " +
                $"does not ask whether {name} is {value}, and {name} has lost the value it had",
                LocalFix.ReplaceLine(id, $"Compare with ==: {name} == {value}",
                    $"== asks whether {name} is {value}, and leaves {name} as it was.",
                    source.Path, index + 1, line[..match.Index] + "==" + line[(match.Index + match.Length)..]));
        }
    }

    /// <summary>
    /// A variable given a value before a block - Enum.each's fn, a for, an if - given a new one inside it, and read after it:
    /// the new one lasts only until the block ends, so after it the variable is as it was. The change - the one the rule on
    /// Elixir's warning makes too - gives the block's own result to the variable.
    /// </summary>
    private static IEnumerable<LogicFinding> NewVariableInBlock(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 1; index < masked.Count; index++)
        {
            var onOneLine = ElixirBlocks.EachOnOneLine().Match(masked[index]);
            var name = onOneLine.Success ? onOneLine.Groups["name"].Value
                : ElixirBlocks.Assignment().Match(masked[index]) is { Success: true } assigned ? assigned.Groups["name"].Value
                : null;
            if (name is null) continue;

            // After the block: the next line, for Enum.each on one line; after the end, for a block over three lines. A read is
            // looked for in the lines as written, as "#{total}" reads it inside a string.
            var afterBlock = onOneLine.Success ? index + 1 : index + 2;
            var given = new Regex($@"^\s*{Regex.Escape(name)}\s*=(?![=~>])");
            var read = new Regex($@"(?<![\w.]){Regex.Escape(name)}\b(?!\s*=(?![=~>]))");
            if (!masked.Take(index).Any(line => given.IsMatch(line)) || !source.Lines.Skip(afterBlock).Any(line => read.IsMatch(line))) continue;

            if (ElixirBlocks.KeptFix(id, source, index + 1, name) is not { } fix) continue;

            yield return new LogicFinding(id, index + 1,
                $"Elixir's variables never change: = inside the block makes a new {name}, which lasts only until the block ends - so " +
                $"after it, {name} is as it was before the block",
                fix);
        }
    }

    /// <summary>
    /// A name given text: a quoted string, or a line IO.gets reads - trimmed with String.trim, or not.
    /// </summary>
    [GeneratedRegex(@"^\s*(?<name>[a-z_]\w*)\s*=\s*(?<value>""[^""]*""|(?<trimmed>String\.trim\(\s*)?IO\.gets\((?:[^()]|\([^()]*\))*\)\)?(?<piped>\s*\|>\s*String\.trim\(\s*\))?)\s*$")]
    private static partial Regex GivenText();

    [GeneratedRegex(@"(?<![\w.])(?<name>[a-z_]\w*)\s*(?<op>===|!==|==|!=|>=|<=|>|<)\s*(?<number>-?\d+)(?![\w.])")]
    private static partial Regex ComparedWithWholeNumber();

    /// <summary>
    /// Text compared with a whole number: Elixir never counts text equal to a number - "7" == 7 is false - and orders every
    /// piece of text after every number, so "7" &gt; 10 is true. Left alone wherever the name is given anything but text. The
    /// change turns the text into a number first with String.to_integer - after String.trim, for a line read with its
    /// newline still on.
    /// </summary>
    private static IEnumerable<LogicFinding> TextComparedWithNumber(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        // Read from the lines as written, as masking blanks the text inside quotes; a name given anything else is left alone.
        var text = new Dictionary<string, bool>(StringComparer.Ordinal);
        var untrimmed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in source.Lines)
        {
            if (Regex.Match(line, @"^\s*(?<name>[a-z_]\w*)\s*=(?![=~>])") is not { Success: true } given) continue;

            var name = given.Groups["name"].Value;
            var value = GivenText().Match(line);
            text[name] = value.Success && text.GetValueOrDefault(name, true);
            if (value.Success && value.Groups["value"].Value.StartsWith("IO.gets", StringComparison.Ordinal) && !value.Groups["piped"].Success) untrimmed.Add(name);
        }

        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match compared in ComparedWithWholeNumber().Matches(masked[index]))
            {
                var name = compared.Groups["name"].Value;
                if (!text.GetValueOrDefault(name)) continue;

                var op = compared.Groups["op"].Value;
                var number = compared.Groups["number"].Value;
                var line = source.Lines[index];
                var nameGroup = compared.Groups["name"];
                var read = untrimmed.Contains(name) ? $"String.to_integer(String.trim({name}))" : $"String.to_integer({name})";
                var ordered = op is ">" or "<" or ">=" or "<=";

                yield return new LogicFinding(id, index + 1,
                    $"`{name}` holds text, and Elixir never counts text equal to a number - \"7\" == 7 is false - " +
                    (ordered ? $"and it orders all text after all numbers, so \"7\" > 10 is true: `{name} {op} {number}` is the same every time"
                             : $"so `{name} {op} {number}` is {(op.StartsWith('!') ? "true" : "false")} every time"),
                    LocalFix.ReplaceLine(id, $"Turn the text into a number first: {read} {op} {number}",
                        $"String.to_integer turns text such as \"7\" into the number 7, so the comparison is of two numbers.",
                        source.Path, index + 1, line[..nameGroup.Index] + read + line[(nameGroup.Index + nameGroup.Length)..]));
            }
        }
    }
}
