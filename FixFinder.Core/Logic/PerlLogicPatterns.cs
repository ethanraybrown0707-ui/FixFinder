using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// Mistakes Perl programs make that Perl runs without stopping - each run with Strawberry Perl 5.42, where without use
/// warnings Perl says nothing of any of them: a loop to <c>$i &lt;= scalar(@marks)</c> reads one position past the end;
/// text compared with == - "yes" == "no" is true, as both become 0; a line read from STDIN keeps its newline until chomp
/// takes it off, so it never eq the text typed; and a decimal worked out by arithmetic, compared exactly.
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
    ];

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
}
