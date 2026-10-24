using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// Mistakes Ruby programs make that run without a word - each run with Ruby 3.3, and ruby -w said nothing of any of them:
/// <c>(total / count).to_f</c> of two whole numbers has lost its fraction before it becomes a decimal - (3 / 2).to_f is
/// 1.0; a decimal worked out by arithmetic, compared exactly - 0.1 + 0.2 == 0.3 is false; a method that gives back a
/// changed copy - name.upcase, marks.sort - called on a line of its own, which leaves the value as it was; and a range with
/// two dots to an array's length, 0..marks.length, whose last number is one past the last position - marks[i] is nil.
/// </summary>
public static partial class RubyLogicPatterns
{
    private static readonly IReadOnlySet<string> RubyFiles = CodePattern.Files(".rb");

    private static CodePattern Pattern(
        string id, Func<string, SourceFile, IReadOnlyList<string>, IEnumerable<LogicFinding>> find,
        Severity severity = Severity.Warning, Confidence confidence = Confidence.Likely) =>
        new(id, RubyFiles, Syntax.Python, find, severity, confidence);

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        Pattern("logic-ruby-integer-average", IntegerAverage, Severity.Error),
        Pattern("logic-ruby-float-equality", FloatEquality),
        Pattern("logic-ruby-result-discarded", ResultDiscarded),
        Pattern("logic-ruby-range-to-length", RangeToLength, Severity.Error),
        Pattern("logic-ruby-elif", ElifForElsif, Severity.Error, Confidence.Certain),
        Pattern("logic-ruby-each-with-index-order", EachWithIndexOrder, Severity.Error),
    ];

    /// <summary><c>each_with_index do |index, mark|</c> or <c>{ |i, mark|</c> - the block's two names.</summary>
    [GeneratedRegex(@"\.each_with_index\s*(?:do\s*|\{\s*)\|\s*(?<names>(?<first>[a-z_]\w*)\s*,\s*(?<second>[a-z_]\w*))\s*\|")]
    private static partial Regex EachWithIndexBlock();

    /// <summary>A name for a position: i, j, k, n, index, idx, position, pos, number, num, count.</summary>
    [GeneratedRegex(@"^(?:i|j|k|n|ind(?:ex)?|idx|pos(?:ition)?|num(?:ber)?|count)$")]
    private static partial Regex PositionName();

    /// <summary>
    /// each_with_index gives each item first and its position second, so a block whose first name is a position's and second
    /// an item's has them the wrong way round - |index, mark| printed 70: 0 for marks [70, 80], with nothing said by ruby -w.
    /// The change swaps the names.
    /// </summary>
    private static IEnumerable<LogicFinding> EachWithIndexOrder(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (EachWithIndexBlock().Match(masked[index]) is not { Success: true } block) continue;

            var first = block.Groups["first"].Value;
            var second = block.Groups["second"].Value;
            if (!PositionName().IsMatch(first) || PositionName().IsMatch(second)) continue;

            var names = block.Groups["names"];
            var line = source.Lines[index];

            yield return new LogicFinding(id, index + 1,
                $"each_with_index gives each item first and its position second - so {first} is given the item, and {second} its position",
                LocalFix.ReplaceLine(id, $"Swap them: |{second}, {first}|",
                    $"With the item named first, {second} holds the item and {first} its position, as each_with_index gives them.",
                    source.Path, index + 1, line[..names.Index] + $"{second}, {first}" + line[(names.Index + names.Length)..]));
        }
    }

    /// <summary><c>(total / count).to_f</c>: a division made a decimal afterwards.</summary>
    [GeneratedRegex(@"\(\s*(?<dividend>[a-z_]\w*|\w+\.(?:length|size|count))\s*/\s*(?<divisor>[a-z_]\w*|\w+\.(?:length|size|count))\s*\)\.to_f\b")]
    private static partial Regex DecimalOfDivision();

    [GeneratedRegex(@"^-?\d[\d_]*$|\.(?:length|size|count|to_i)$|^\w+\.(?:sum|length|size|count)$")]
    private static partial Regex WholeNumberValue();

    private static IEnumerable<LogicFinding> IntegerAverage(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var code = string.Join("\n", masked);

        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match division in DecimalOfDivision().Matches(masked[index]))
            {
                var dividend = division.Groups["dividend"].Value;
                var divisor = division.Groups["divisor"].Value;
                if (!IsWholeNumber(dividend, code) || !IsWholeNumber(divisor, code)) continue;

                var line = source.Lines[index];
                var corrected = line[..division.Index] + $"{dividend}.to_f / {divisor}" + line[(division.Index + division.Length)..];

                yield return new LogicFinding(id, index + 1,
                    $"`{dividend} / {divisor}` divides two whole numbers, which in Ruby gives a whole number - the fraction is dropped " +
                    "before .to_f is applied, so (3 / 2).to_f is 1.0, not 1.5",
                    LocalFix.ReplaceLine(id, "Make the first number a decimal before dividing",
                        "A decimal divided by a whole number keeps the fraction, so the first number is made a decimal first.",
                        source.Path, index + 1, corrected));
            }
        }
    }

    /// <summary>
    /// Whether a value is surely a whole number: a count - .length, .size, .count - always is, and a name is when every line
    /// that gives it a value gives it a whole-number literal, a count, a .to_i or a .sum of something - and there is one.
    /// </summary>
    private static bool IsWholeNumber(string value, string code)
    {
        if (Regex.IsMatch(value, @"\.(?:length|size|count)$")) return true;

        var given = Regex.Matches(code, $@"^\s*{Regex.Escape(value)}\s*=(?!=)\s*(?<value>[^\n#]+)", RegexOptions.Multiline)
            .Select(made => made.Groups["value"].Value.Trim())
            .ToList();

        return given.Count > 0 && given.All(made => WholeNumberValue().IsMatch(made));
    }

    [GeneratedRegex(@"^\s*(?<name>[a-z_]\w*)\s*(?<assign>[-+*/]?=)(?!=)\s*(?<value>[^\n#]+)")]
    private static partial Regex Assignment();

    [GeneratedRegex(@"(?<![\w.])(?<name>[a-z_]\w*)\s*(?<op>==|!=)\s*(?<value>\d*\.\d*[1-9]\d*)(?![\w.])")]
    private static partial Regex ExactComparison();

    /// <summary>
    /// A decimal worked out by arithmetic with a decimal in it - or .to_f - compared exactly with a decimal: the arithmetic
    /// leaves a tiny error, so the comparison is false when the two are equal for every practical purpose.
    /// </summary>
    private static IEnumerable<LogicFinding> FloatEquality(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var worked = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in masked)
        {
            if (Assignment().Match(line) is not { Success: true } assignment) continue;

            var value = assignment.Groups["value"].Value;
            var byArithmetic = assignment.Groups["assign"].Value != "=" || Regex.IsMatch(Regex.Replace(value, @"\d*\.\d+|\d+\.\d*", "0"), @"[-+*/]");
            if (byArithmetic && Regex.IsMatch(value, @"\d*\.\d+|\d+\.\d*|\.to_f\b")) worked.Add(assignment.Groups["name"].Value);
        }

        if (worked.Count == 0) yield break;

        for (var index = 0; index < masked.Count; index++)
        {
            if (ExactComparison().Match(masked[index]) is not { Success: true } compared || !worked.Contains(compared.Groups["name"].Value)) continue;

            var name = compared.Groups["name"].Value;
            var value = compared.Groups["value"].Value;
            var close = compared.Groups["op"].Value == "==" ? $"({name} - {value}).abs < 1e-9" : $"({name} - {value}).abs >= 1e-9";
            var line = source.Lines[index];

            yield return new LogicFinding(id, index + 1,
                $"`{name}` is a decimal worked out by arithmetic, compared with {value} exactly - and arithmetic on decimals is rarely " +
                "exact: 0.1 + 0.2 is 0.30000000000000004",
                LocalFix.ReplaceLine(id, $"Compare within a tiny tolerance: {close}",
                    $"{close} is true when {name} is {value} to within a billionth, which arithmetic's tiny errors stay inside.",
                    source.Path, index + 1, line[..compared.Index] + close + line[(compared.Index + compared.Length)..]));
        }
    }

    /// <summary>The methods that give back a changed copy and leave the value as it was - each of which has a ! form that changes it.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)(?<name>[a-z_]\w*)\.(?<method>upcase|downcase|capitalize|swapcase|strip|lstrip|rstrip|chomp|chop|squeeze|reverse|sort|uniq|compact|flatten|shuffle)\s*$")]
    private static partial Regex CopyOnItsOwn();

    /// <summary>
    /// name.upcase on a line of its own: upcase gives back a new, changed copy and leaves name as it was, so the line changes
    /// nothing. The change keeps the copy in the name.
    /// </summary>
    private static IEnumerable<LogicFinding> ResultDiscarded(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (CopyOnItsOwn().Match(masked[index]) is not { Success: true } call) continue;

            var name = call.Groups["name"].Value;
            var method = call.Groups["method"].Value;

            yield return new LogicFinding(id, index + 1,
                $"{method} gives back a new, changed copy and leaves {name} as it was, so on a line of its own it changes nothing - " +
                $"{method}! changes {name} itself",
                LocalFix.ReplaceLine(id, $"Keep the copy in {name}",
                    $"{name}.{method} gives back the changed copy; giving it to {name} keeps it.",
                    source.Path, index + 1, $"{call.Groups["indent"].Value}{name} = {name}.{method}"));
        }
    }

    /// <summary>
    /// <c>(0..marks.length).each do |i|</c>, or <c>for i in 0..marks.length</c>: a range with two dots, which takes in its
    /// end - not 0..marks.length - 1, which stops at the last position.
    /// </summary>
    [GeneratedRegex(
        @"\(\s*0\s*(?<dots>\.\.)(?!\.)\s*(?<array>[a-z_]\w*)\.(?:length|size|count)\s*\)\.each\s*(?:do\b|\{)\s*\|\s*(?<variable>[a-z_]\w*)\s*\||" +
        @"\bfor\s+(?<variable>[a-z_]\w*)\s+in\s+0\s*(?<dots>\.\.)(?!\.)\s*(?<array>[a-z_]\w*)\.(?:length|size|count)\b(?!\s*-)")]
    private static partial Regex RangeToLengthHeading();

    /// <summary>
    /// A range to the length whose number is used as a position in the array: on the last pass the position is one past the
    /// end, which Ruby gives as nil. The position is looked for in the lines as written - "#{marks[i]}" is inside a string.
    /// </summary>
    private static IEnumerable<LogicFinding> RangeToLength(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (RangeToLengthHeading().Match(masked[index]) is not { Success: true } range) continue;

            // The block: the heading's own line, for { }, and the lines indented under it, for do ... end.
            var headingIndent = CodeText.Indentation(masked[index]).Length;
            var blockEnd = index + 1;
            while (blockEnd < masked.Count && (masked[blockEnd].Trim().Length == 0 || CodeText.Indentation(masked[blockEnd]).Length > headingIndent)) blockEnd++;

            var array = range.Groups["array"].Value;
            var variable = range.Groups["variable"].Value;
            var position = new Regex($@"(?<![\w.]){Regex.Escape(array)}\s*\[\s*{Regex.Escape(variable)}\s*\]");
            if (!Enumerable.Range(index, blockEnd - index).Any(line => position.IsMatch(source.Lines[line]))) continue;

            var line = source.Lines[index];
            var dots = range.Groups["dots"];

            yield return new LogicFinding(id, index + 1,
                $"0..{array}.length has two dots, so it takes in {array}.length itself - one past the last position, as positions start " +
                $"at 0 - and on the last pass {array}[{variable}] is nil, with no error",
                LocalFix.ReplaceLine(id, $"Stop before the length: 0...{array}.length",
                    $"A range with three dots stops before its end, so {variable} goes over {array}'s positions and no further.",
                    source.Path, index + 1, line[..dots.Index] + "..." + line[(dots.Index + dots.Length)..]));
        }
    }

    /// <summary><c>elif mark &gt; 50</c> at the start of a line.</summary>
    [GeneratedRegex(@"^(?<lead>\s*)elif\b")]
    private static partial Regex Elif();

    /// <summary>
    /// elif, as Python writes it, inside an if: Ruby has no elif, and reads the line as a call to a method called elif, in the if's
    /// own branch. When the if's condition is false nothing runs, with no error - Ruby 3.3 ran it and printed nothing - and when it
    /// is true, Ruby stops there with NoMethodError. The change is Ruby's elsif.
    /// </summary>
    private static IEnumerable<LogicFinding> ElifForElsif(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 1; index < masked.Count; index++)
        {
            if (Elif().Match(masked[index]) is not { Success: true } elif) continue;

            var indent = elif.Groups["lead"].Length;
            var inIf = Enumerable.Range(0, index).Reverse()
                .Where(above => masked[above].Trim().Length > 0)
                .Select(above => masked[above])
                .FirstOrDefault(above => CodeText.Indentation(above).Length <= indent) is { } heading &&
                Regex.IsMatch(heading, @"^\s*(?:if|unless|elsif)\b");
            if (!inIf) continue;

            var line = source.Lines[index];

            yield return new LogicFinding(id, index + 1,
                "Ruby has no elif, so it reads this line as a call to a method called elif, inside the if's own branch - when the condition " +
                "above is false, neither branch runs, with no error, and when it is true Ruby stops here",
                LocalFix.ReplaceLine(id, "Write elsif",
                    "Ruby writes \"else if\" as elsif, which starts the next branch of the if.",
                    source.Path, index + 1, elif.Groups["lead"].Value + "elsif" + line[(elif.Groups["lead"].Length + "elif".Length)..]));
        }
    }
}
