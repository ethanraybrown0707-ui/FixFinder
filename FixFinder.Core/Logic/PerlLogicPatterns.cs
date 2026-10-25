using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Logic;

/// <summary>
/// Mistakes Perl programs make that Perl runs without stopping - each run with Strawberry Perl 5.42, where without use
/// warnings Perl says nothing of any of them: a loop to <c>$i &lt;= scalar(@marks)</c> reads one position past the end;
/// text compared with == - "yes" == "no" is true, as both become 0; a line read from STDIN keeps its newline until chomp
/// takes it off, so it never eq the text typed; a decimal worked out by arithmetic, compared exactly;
/// <c>length(@marks)</c>, which counts the digits of the array's count - 2 for 12 items; and numbers put in order by
/// <c>sort @marks</c>, which compares as text - (10, 9, 100) came out 10 100 9, and perl -w said nothing.
/// </summary>
public static partial class PerlLogicPatterns
{
    private static readonly IReadOnlySet<string> PerlFiles = CodePattern.Files(".pl", ".pm");

    private static CodePattern Pattern(
        string id, Func<string, SourceFile, IReadOnlyList<string>, IEnumerable<LogicFinding>> find,
        Severity severity = Severity.Warning, Confidence confidence = Confidence.Likely) =>
        new(id, PerlFiles, Syntax.Perl, find, severity, confidence);

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        Pattern("logic-perl-loop-to-count", LoopToCount, Severity.Error),
        Pattern("logic-perl-text-compared-as-numbers", TextComparedAsNumbers, Severity.Error, Confidence.Certain),
        Pattern("logic-perl-line-not-chomped", LineNotChomped, Severity.Error),
        Pattern("logic-perl-float-equality", FloatEquality),
        Pattern("logic-perl-length-of-array", LengthOfArray, Severity.Error, Confidence.Certain),
        Pattern("logic-perl-method-in-string", MethodInString, Severity.Error, Confidence.Certain),
        Pattern("logic-perl-numbers-sorted-as-text", NumbersSortedAsText, Severity.Error, Confidence.Certain),
        Pattern("logic-perl-loop-to-last-index", LoopToLastIndex, Severity.Error),
    ];

    /// <summary><c>for (my $i = 0; $i &lt; $#marks; $i++)</c> - a loop that stops below the last position, $#marks.</summary>
    [GeneratedRegex(@"\bfor(?:each)?\s*\(\s*my\s+\$(?<variable>\w+)\s*=\s*0\s*;\s*\$\k<variable>\s*(?<comparison><)\s*\$#(?<array>\w+)\s*;")]
    private static partial Regex LoopBelowLastIndex();

    /// <summary>
    /// A loop to $i &lt; $#marks that reads $marks[$i]: $#marks is the last position, not the count, so stopping below it
    /// misses the last item - (70, 80, 90) printed 70 and 80, with nothing said by perl -w. The change stops after the last
    /// position, &lt;=.
    /// </summary>
    private static IEnumerable<LogicFinding> LoopToLastIndex(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (LoopBelowLastIndex().Match(masked[index]) is not { Success: true } loop || BraceBlocks.Body(masked, index) is not { } body) continue;

            var variable = loop.Groups["variable"].Value;
            var array = loop.Groups["array"].Value;
            var position = new Regex($@"\${Regex.Escape(array)}\s*\[\s*\${Regex.Escape(variable)}\s*\]");
            if (!Enumerable.Range(body.First, body.End - body.First).Any(line => position.IsMatch(masked[line]))) continue;

            var line = source.Lines[index];
            var comparison = loop.Groups["comparison"];

            yield return new LogicFinding(id, index + 1,
                $"$#{array} is the last position of @{array}, not its count, so a loop that stops below it never reaches the last item",
                LocalFix.ReplaceLine(id, $"Go up to the last position: ${variable} <= $#{array}",
                    $"<= takes in $#{array} itself, so the loop's last pass reads the last item of @{array}.",
                    source.Path, index + 1, line[..comparison.Index] + "<=" + line[(comparison.Index + comparison.Length)..]));
        }
    }

    /// <summary><c>my @marks = (10, 9, 100);</c> - an array given only numbers.</summary>
    [GeneratedRegex(@"(?:\bmy\s+)?@(?<array>\w+)\s*=\s*\((?<values>\s*-?\d+(?:\.\d+)?(?:\s*,\s*-?\d+(?:\.\d+)?)*\s*,?\s*)\)\s*;")]
    private static partial Regex GivenOnlyNumbers();

    /// <summary>@marks given a list or items by push or unshift - and whether what it is given holds text: a quote, or qw.</summary>
    [GeneratedRegex(@"@(?<array>\w+)\s*=(?![=~])(?<given>[^;]*)|\b(?:push|unshift)\s*\(?\s*@(?<array>\w+)\s*,(?<given>[^;]*)")]
    private static partial Regex GivenSomething();

    [GeneratedRegex(@"[""']|\bqw\b")]
    private static partial Regex HoldsText();

    /// <summary>
    /// What sort's text order makes of the numbers an array was given, beside their order by size - where each is a whole
    /// number written as Perl writes it back, which 1.50 or -0 is not - or null otherwise.
    /// </summary>
    private static (string Given, string AsText, string BySize)? Orders(string values)
    {
        var numbers = values.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (!numbers.All(number => Regex.IsMatch(number, @"^(?:0|-?[1-9]\d{0,17})$"))) return null;

        return (string.Join(", ", numbers), string.Join(" ", numbers.Order(StringComparer.Ordinal)), string.Join(" ", numbers.OrderBy(long.Parse)));
    }

    /// <summary><c>sort @marks</c> or <c>sort(@marks)</c> - sort given no block or sub to compare with.</summary>
    [GeneratedRegex(@"\bsort\s*(?<bracket>\()?\s*@(?<array>\w+)\b")]
    private static partial Regex SortWithNothingToCompare();

    /// <summary>
    /// sort @marks, where @marks was given only numbers: sort with nothing to compare by compares as text, character by
    /// character, so (10, 9, 100) sorted to 10 100 9. Left alone where @marks is given text anywhere. Where it is given its
    /// numbers once and nothing more, the message names the order they come out in. The change compares them as numbers, with
    /// &lt;=&gt;.
    /// </summary>
    private static IEnumerable<LogicFinding> NumbersSortedAsText(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var givenNumbers = masked.SelectMany(line => GivenOnlyNumbers().Matches(line)).ToLookup(given => given.Groups["array"].Value, StringComparer.Ordinal);
        if (givenNumbers.Count == 0) yield break;

        var givenAnything = masked.SelectMany(line => GivenSomething().Matches(line)).ToList();
        var givenText = givenAnything.Where(given => HoldsText().IsMatch(given.Groups["given"].Value)).Select(given => given.Groups["array"].Value).ToHashSet(StringComparer.Ordinal);
        // How many times each array is given something other than its own items in a new order, as @marks = sort @marks gives.
        var timesGiven = givenAnything
            .Where(given => !Regex.IsMatch(given.Groups["given"].Value, $@"^\s*(?:reverse\s+)?sort\b.*@{Regex.Escape(given.Groups["array"].Value)}\b"))
            .GroupBy(given => given.Groups["array"].Value, StringComparer.Ordinal)
            .ToDictionary(times => times.Key, times => times.Count(), StringComparer.Ordinal);

        for (var index = 0; index < masked.Count; index++)
        {
            if (SortWithNothingToCompare().Match(masked[index]) is not { Success: true } sort) continue;

            var array = sort.Groups["array"].Value;
            if (!givenNumbers.Contains(array) || givenText.Contains(array)) continue;

            var line = source.Lines[index];
            var listStart = sort.Groups["bracket"].Success ? sort.Groups["bracket"].Index : sort.Groups["array"].Index - 1;
            var beforeList = line[..listStart].EndsWith(' ') ? line[..listStart] : line[..listStart] + " ";
            var orders = timesGiven.GetValueOrDefault(array) == 1 ? Orders(givenNumbers[array].Single().Groups["values"].Value) : null;
            var asText = "sort with nothing to compare by puts things in order as text, character by character";

            yield return new LogicFinding(id, index + 1,
                orders switch
                {
                    { } order when order.AsText != order.BySize => $"{asText} - so {order.Given}, the numbers in @{array}, come out as {order.AsText}, not {order.BySize}",
                    { } order => $"{asText} - {order.Given}, the numbers in @{array}, happen to come out in order, but as text 100 comes before 9, so other numbers would not",
                    null => $"{asText} - and @{array} holds numbers, so 10, 9 and 100 would come out as 10 100 9",
                },
                LocalFix.ReplaceLine(id, "Compare them as numbers: sort { $a <=> $b }",
                    "<=> compares two numbers as numbers, and sort { $a <=> $b } puts them in order from smallest to largest.",
                    source.Path, index + 1, beforeList + "{ $a <=> $b } " + line[listStart..]));
        }
    }

    /// <summary><c>for (my $i = 0; $i &lt;= scalar(@marks); $i++)</c>, or <c>$i &lt;= @marks</c>: one step past the last position.</summary>
    [GeneratedRegex(@"\bfor(?:each)?\s*\(\s*my\s+\$(?<variable>\w+)\s*=\s*0\s*;\s*\$\k<variable>\s*(?<comparison><=)\s*(?:scalar\s*\(\s*@(?<array>\w+)\s*\)|@(?<array>\w+))\s*;")]
    private static partial Regex LoopToCountHeading();

    private static IEnumerable<LogicFinding> LoopToCount(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (LoopToCountHeading().Match(masked[index]) is not { Success: true } loop || BraceBlocks.Body(masked, index) is not { } body) continue;

            var variable = loop.Groups["variable"].Value;
            var array = loop.Groups["array"].Value;
            var position = new Regex($@"\${Regex.Escape(array)}\s*\[\s*\${Regex.Escape(variable)}\s*\]");
            if (!Enumerable.Range(body.First, body.End - body.First).Any(line => position.IsMatch(masked[line]))) continue;

            var line = source.Lines[index];
            var comparison = loop.Groups["comparison"];

            yield return new LogicFinding(id, index + 1,
                $"@{array}'s positions run from 0 to $#{array}, one less than its count, so on the loop's last pass `${array}[${variable}]` is past " +
                "the end - undef, which Perl uses as nothing, warning only with use warnings",
                LocalFix.ReplaceLine(id, "Stop the loop below the count: <",
                    $"@{array}'s positions run from 0 to $#{array}, so the loop has to stop while ${variable} is still below the count.",
                    source.Path, index + 1, line[..comparison.Index] + "<" + line[(comparison.Index + comparison.Length)..]));
        }
    }

    /// <summary>A numeric comparison with a word in quotes on one side: <c>$answer == "no"</c>.</summary>
    [GeneratedRegex(@"(?<left>\$\w+(?:\{[^}]*\}|\[[^\]]*\])?|""[^""]*""|'[^']*')\s*(?<operator>==|!=)\s*(?<right>\$\w+(?:\{[^}]*\}|\[[^\]]*\])?|""[^""]*""|'[^']*')")]
    private static partial Regex NumericComparison();

    private static IEnumerable<LogicFinding> TextComparedAsNumbers(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            // The quotes' contents are read from the line as written: masking blanks them.
            foreach (Match comparison in NumericComparison().Matches(source.Lines[index]))
            {
                if (CodeText.Mask(source.Lines[index], Syntax.Perl)[comparison.Groups["operator"].Index] is not ('=' or '!')) continue;

                var word = new[] { comparison.Groups["left"].Value, comparison.Groups["right"].Value }
                    .FirstOrDefault(side => (side.StartsWith('"') || side.StartsWith('\'')) && !Regex.IsMatch(side[1..^1], @"^\s*-?\d+(?:\.\d+)?\s*$"));
                if (word is null) continue;

                var symbol = comparison.Groups["operator"].Value;
                var textWord = symbol == "==" ? "eq" : "ne";
                var line = source.Lines[index];
                var place = comparison.Groups["operator"];

                yield return new LogicFinding(id, index + 1,
                    $"{symbol} compares numbers, and {word} is not one - Perl takes it as 0, so any two words are equal and the comparison " +
                    "gives the same answer whatever the text holds",
                    LocalFix.ReplaceLine(id, $"Compare text with {textWord}",
                        $"{textWord} compares text as text; {symbol} turned both sides into numbers first.",
                        source.Path, index + 1, line[..place.Index] + textWord + line[(place.Index + place.Length)..]));
            }
        }
    }

    /// <summary><c>my $typed = &lt;STDIN&gt;;</c> - a line read, newline and all.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)my\s+\$(?<name>\w+)\s*=\s*<STDIN>\s*;")]
    private static partial Regex LineRead();

    private static IEnumerable<LogicFinding> LineNotChomped(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (LineRead().Match(masked[index]) is not { Success: true } read) continue;

            var name = read.Groups["name"].Value;
            var chomped = new Regex($@"\bchomp\s*\(?\s*(?:my\s+)?\${Regex.Escape(name)}\b");
            var comparedAsText = new Regex($@"\${Regex.Escape(name)}\s*(?:eq|ne)\s*[""']|[""']\s*(?:eq|ne)\s*\${Regex.Escape(name)}\b");
            if (masked.Any(line => chomped.IsMatch(line))) continue;
            if (!Enumerable.Range(index + 1, masked.Count - index - 1).Any(line => comparedAsText.IsMatch(masked[line]))) continue;

            var line = source.Lines[index];

            yield return new LogicFinding(id, index + 1,
                $"<STDIN> gives the line typed with its newline still on the end, so `${name}` is \"5\\n\" where \"5\" was typed - and it is " +
                "never eq to text without the newline",
                LocalFix.ReplaceLine(id, $"Take the newline off as it is read: chomp(my ${name} = <STDIN>);",
                    "chomp takes the newline off the end of the line read, so what is kept is just what was typed.",
                    source.Path, index + 1, $"{read.Groups["indent"].Value}chomp(my ${name} = <STDIN>);" + line[(read.Index + read.Length)..]));
        }
    }

    [GeneratedRegex(@"^\s*(?:my\s+)?\$(?<name>\w+)\s*(?<assign>[-+*/]?=)(?!=)\s*(?<value>[^;]+);")]
    private static partial Regex Assignment();

    [GeneratedRegex(@"\$(?<name>\w+)\s*(?<op>==|!=)\s*(?<value>\d*\.\d*[1-9]\d*)(?![\w.])")]
    private static partial Regex ExactComparison();

    private static IEnumerable<LogicFinding> FloatEquality(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var worked = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in masked)
        {
            if (Assignment().Match(line) is not { Success: true } assignment) continue;

            var value = assignment.Groups["value"].Value;
            var byArithmetic = assignment.Groups["assign"].Value != "=" || Regex.IsMatch(Regex.Replace(value, @"\d*\.\d+|\d+\.\d*", "0"), @"[-+*/]");
            if (byArithmetic && Regex.IsMatch(value, @"\d*\.\d+|\d+\.\d*")) worked.Add(assignment.Groups["name"].Value);
        }

        if (worked.Count == 0) yield break;

        for (var index = 0; index < masked.Count; index++)
        {
            if (ExactComparison().Match(masked[index]) is not { Success: true } compared || !worked.Contains(compared.Groups["name"].Value)) continue;

            var name = compared.Groups["name"].Value;
            var value = compared.Groups["value"].Value;
            var close = compared.Groups["op"].Value == "==" ? $"abs(${name} - {value}) < 1e-9" : $"abs(${name} - {value}) >= 1e-9";
            var line = source.Lines[index];

            yield return new LogicFinding(id, index + 1,
                $"`${name}` is a decimal worked out by arithmetic, compared with {value} exactly - and arithmetic on decimals is rarely " +
                "exact: 0.1 + 0.2 is 0.30000000000000004, though print shows it as 0.3",
                LocalFix.ReplaceLine(id, $"Compare within a tiny tolerance: {close}",
                    $"{close} is true when ${name} is {value} to within a billionth, which arithmetic's tiny errors stay inside.",
                    source.Path, index + 1, line[..compared.Index] + close + line[(compared.Index + compared.Length)..]));
        }
    }

    /// <summary>length(@marks): the number of digits in the array's count, not the count.</summary>
    private static IEnumerable<LogicFinding> LengthOfArray(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (PerlArrays.LengthOfArray().Match(masked[index]) is not { Success: true } length) continue;

            var array = length.Groups["array"].Value;

            yield return new LogicFinding(id, index + 1,
                $"length counts the characters of text, so given @{array} it counts the characters of the array's count - " +
                $"length(@{array}) is how many digits the count has: 2 for an array of 12 items",
                PerlArrays.CountFix(id, source, index + 1, length));
        }
    }

    /// <summary><c>$student-&gt;name</c> - a method call - not ->{ or ->[, which Perl does put into text.</summary>
    [GeneratedRegex(@"(?<!\\)\$(?<object>[A-Za-z_]\w*)->(?<method>[A-Za-z_]\w*)(?:\(\))?(?![\w\[{(])")]
    private static partial Regex MethodCall();

    /// <summary>
    /// "Name: $student->name" in double quotes: Perl puts in the value of $student - an object, written as Student=HASH(0x...) -
    /// and writes ->name after it as it is; it calls no method inside text. The call is taken out of the text and joined to it
    /// with . on each side.
    /// </summary>
    private static IEnumerable<LogicFinding> MethodInString(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < source.Lines.Count; index++)
        {
            var line = source.Lines[index];

            for (var start = line.IndexOf('"'); start >= 0 && start < masked[index].Length; start = line.IndexOf('"', start + 1))
            {
                // A double quote in the code - the masked line keeps it - that opens text, and where that text ends.
                if (masked[index][start] != '"') continue;
                var end = start + 1;
                while (end < line.Length && line[end] != '"') end += line[end] == '\\' ? 2 : 1;
                if (end >= line.Length) break;

                var text = line[(start + 1)..end];
                if (MethodCall().Match(text) is not { Success: true } call)
                {
                    start = end;
                    continue;
                }

                var before = text[..call.Index];
                var after = text[(call.Index + call.Length)..];
                var methodCall = $"${call.Groups["object"].Value}->{call.Groups["method"].Value}";
                var joined = (before.Length > 0 ? $"\"{before}\" . " : "") + methodCall + (after.Length > 0 ? $" . \"{after}\"" : "");

                yield return new LogicFinding(id, index + 1,
                    $"Perl calls no method inside text: it puts in the value of ${call.Groups["object"].Value} - an object, which it writes as its " +
                    $"class and where it is kept, such as Student=HASH(0x...) - and writes ->{call.Groups["method"].Value} after it as it is",
                    LocalFix.ReplaceLine(id, $"Call {methodCall} outside the text, and join it with .",
                        "Outside the quotes the method is called, and . joins what it gives back to the text on each side.",
                        source.Path, index + 1, line[..start] + joined + line[(end + 1)..]));
                break;
            }
        }
    }
}
