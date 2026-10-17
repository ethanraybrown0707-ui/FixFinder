using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Logic;

/// <summary>
/// Mistakes Erlang programs make that erlc compiles and the program runs past - each run with Erlang/OTP 29, with erlc -Wall
/// saying nothing of any of them when the values come in from outside: a line read with io:get_line compared with a number,
/// which is always true, as text sorts after every number; the same line compared with text, though it still has its
/// newline; a decimal worked out by arithmetic, compared exactly; a list of numbers printed with ~p, which prints it as
/// text when every number is a printable character - [80, 70, 90] as "PFZ"; a number joined to text with ++ inside
/// io:format, which stops the program with bad argument; and lists:sort or another function that gives back a changed copy,
/// called on a line of its own, which leaves the value as it was.
/// </summary>
public static partial class ErlangLogicPatterns
{
    private static readonly IReadOnlySet<string> ErlangFiles = CodePattern.Files(".erl");

    private static CodePattern Pattern(
        string id, Func<string, SourceFile, IReadOnlyList<string>, IEnumerable<LogicFinding>> find,
        Severity severity = Severity.Warning, Confidence confidence = Confidence.Likely) =>
        new(id, ErlangFiles, Syntax.Erlang, find, severity, confidence);

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        Pattern("logic-erlang-text-compared-with-number", TextComparedWithNumber, Severity.Error, Confidence.Certain),
        Pattern("logic-erlang-input-not-trimmed", InputNotTrimmed, Severity.Error),
        Pattern("logic-erlang-float-equality", FloatEquality),
        Pattern("logic-erlang-list-printed-as-text", ListPrintedAsText),
        Pattern("logic-erlang-number-joined-to-text", NumberJoinedToText, Severity.Error, Confidence.Certain),
        Pattern("logic-erlang-result-discarded", ResultDiscarded, Severity.Error),
    ];

    /// <summary><c>Typed = io:get_line("Age? ")</c>: a line read as it was typed - text, newline and all.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)(?<name>[A-Z][\w@]*)\s*=\s*(?<read>io:get_line\((?:[^()]|\([^()]*\))*\))\s*(?<after>,?\s*)$")]
    private static partial Regex ReadLine();

    /// <summary>The lines after a line read with io:get_line, to the end of its function, compared as <paramref name="compared"/> says.</summary>
    private static IEnumerable<(Match Read, int Index)> ReadLinesCompared(IReadOnlyList<string> masked, Func<string, Regex> compared)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (ReadLine().Match(masked[index]) is not { Success: true } read) continue;

            var end = ErlangCode.FunctionEnd(masked, index);
            var comparison = compared(read.Groups["name"].Value);
            if (Enumerable.Range(index + 1, Math.Max(0, end - index)).Any(line => comparison.IsMatch(masked[line]))) yield return (read, index);
        }
    }

    /// <summary>
    /// A line read with io:get_line compared with a number: Erlang orders every number before every list - and text is a
    /// list - so "7\n" &gt; 17 is true. The change reads the line as a number as it is read, as a comparison in a guard cannot
    /// call a function.
    /// </summary>
    private static IEnumerable<LogicFinding> TextComparedWithNumber(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        foreach (var (read, index) in ReadLinesCompared(masked, name =>
                     new Regex($@"(?<![\w@]){Regex.Escape(name)}\s*(?:=<|>=|<|>|==|/=|=:=|=/=)\s*-?\d|-?\d+(?:\.\d+)?\s*(?:=<|>=|<|>|==|/=|=:=|=/=)\s*{Regex.Escape(name)}(?![\w@])")))
        {
            var name = read.Groups["name"].Value;
            var line = source.Lines[index];
            var readCall = line.Substring(read.Groups["read"].Index, read.Groups["read"].Length);

            yield return new LogicFinding(id, index + 1,
                $"{name} is the text typed - a list of characters - and Erlang puts every number before every list, so comparing {name} " +
                "with a number gives the same answer whatever was typed",
                LocalFix.ReplaceLine(id, $"Read it as a number: {name} = list_to_integer(string:trim(io:get_line(...)))",
                    "string:trim takes the newline off the line typed, and list_to_integer reads what is left as a whole number.",
                    source.Path, index + 1, $"{read.Groups["indent"].Value}{name} = list_to_integer(string:trim({readCall})){read.Groups["after"].Value.TrimEnd()}"));
        }
    }

    /// <summary>
    /// A line read with io:get_line compared with text: it still has its newline on the end - "y\n" - so it never equals "y".
    /// The change trims it as it is read.
    /// </summary>
    private static IEnumerable<LogicFinding> InputNotTrimmed(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        foreach (var (read, index) in ReadLinesCompared(masked, name =>
                     new Regex($@"(?<![\w@]){Regex.Escape(name)}\s*(?:==|/=|=:=|=/=)\s*""|""\s*(?:==|/=|=:=|=/=)\s*{Regex.Escape(name)}(?![\w@])")))
        {
            var name = read.Groups["name"].Value;
            var line = source.Lines[index];
            var readCall = line.Substring(read.Groups["read"].Index, read.Groups["read"].Length);

            yield return new LogicFinding(id, index + 1,
                $"io:get_line gives back the line typed with its newline still on the end - \"y\\n\", not \"y\" - so comparing {name} with " +
                "text is never true for what was typed",
                LocalFix.ReplaceLine(id, $"Trim the line as it is read: {name} = string:trim(io:get_line(...))",
                    "string:trim takes the newline - and any spaces - off both ends, so the text is what was typed.",
                    source.Path, index + 1, $"{read.Groups["indent"].Value}{name} = string:trim({readCall}){read.Groups["after"].Value.TrimEnd()}"));
        }
    }

    /// <summary>A name given a value by arithmetic with a decimal in it: Total = 0.1 + 0.2.</summary>
    [GeneratedRegex(@"^\s*(?<name>[A-Z][\w@]*)\s*=(?![=:<])\s*(?<value>[^,]*\d+\.\d+[^,]*),?\s*$")]
    private static partial Regex DecimalWorkedOut();

    [GeneratedRegex(@"(?<![\w@])(?<name>[A-Z][\w@]*)\s*(?<op>==|=:=|/=|=/=)\s*(?<value>\d+\.\d*[1-9]\d*)(?![\w.])")]
    private static partial Regex ComparedWithDecimal();

    /// <summary>
    /// A decimal worked out by arithmetic, compared exactly with a decimal: the arithmetic leaves a tiny error - 0.1 + 0.2 is
    /// 0.30000000000000004 - so the comparison fails when the two are equal for every practical purpose. The change compares
    /// the difference with a tiny tolerance, which abs lets a guard do too.
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
            var equal = compared.Groups["op"].Value is "==" or "=:=";
            var close = equal ? $"abs({name} - {value}) < 1.0e-9" : $"abs({name} - {value}) >= 1.0e-9";
            var line = source.Lines[index];

            yield return new LogicFinding(id, index + 1,
                $"{name} is a decimal worked out by arithmetic, which leaves a tiny error - 0.1 + 0.2 is 0.30000000000000004 - so comparing " +
                $"it exactly with {value} is {(equal ? "false" : "true")} even when the two are equal for every practical purpose",
                LocalFix.ReplaceLine(id, $"Compare within a tiny tolerance: {close}",
                    "The difference between the two is compared with a tiny tolerance, which allows for the error the arithmetic left.",
                    source.Path, index + 1, line[..compared.Index] + close + line[(compared.Index + compared.Length)..]));
        }
    }

    /// <summary><c>Marks = [80, 70, 90]</c>: a name given a list of whole numbers as it is written.</summary>
    [GeneratedRegex(@"^\s*(?<name>[A-Z][\w@]*)\s*=\s*\[(?<numbers>\s*\d+(?:\s*,\s*\d+)*\s*)\]\s*,?\s*$")]
    private static partial Regex NumberListGiven();

    /// <summary>io:format with a ~p and the names it prints: <c>io:format("~p~n", [Marks])</c>.</summary>
    [GeneratedRegex(@"\bio:(?:format|fwrite)\(\s*""(?<format>[^""]*)""\s*,\s*\[(?<names>[^\]]*)\]\s*\)")]
    private static partial Regex Printed();

    /// <summary>
    /// A list of whole numbers printed with ~p: ~p prints a list whose numbers are all printable characters as text, so
    /// [80, 70, 90] prints as "PFZ". ~w prints the numbers as they are.
    /// </summary>
    private static IEnumerable<LogicFinding> ListPrintedAsText(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var printable = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in masked)
        {
            if (NumberListGiven().Match(line) is not { Success: true } given) continue;

            var numbers = given.Groups["numbers"].Value.Split(',').Select(number => int.Parse(number.Trim())).ToList();
            if (numbers.All(number => number is >= 32 and <= 126)) printable[given.Groups["name"].Value] = new string(numbers.Select(number => (char)number).ToArray());
        }

        for (var index = 0; index < masked.Count; index++)
        {
            if (Printed().Match(source.Lines[index]) is not { Success: true } printed) continue;

            var format = printed.Groups["format"].Value;
            var names = printed.Groups["names"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var directives = Regex.Matches(format, "~[a-zA-Z]").Where(directive => directive.Value is not "~n").ToList();
            if (directives.Count != names.Length) continue;

            for (var position = 0; position < names.Length; position++)
            {
                if (directives[position].Value != "~p" || !printable.TryGetValue(names[position], out var asText)) continue;

                var line = source.Lines[index];
                var at = printed.Groups["format"].Index + directives[position].Index;

                yield return new LogicFinding(id, index + 1,
                    $"~p prints a list whose numbers are all printable characters as text - {names[position]} prints as \"{asText}\", " +
                    "not as its numbers",
                    LocalFix.ReplaceLine(id, $"Print the numbers with ~w",
                        $"~w prints {names[position]} as the list of numbers it is.",
                        source.Path, index + 1, line[..at] + "~w" + line[(at + 2)..]));
                break;
            }
        }
    }

    /// <summary><c>io:format("Total: " ++ Total)</c>: text joined to a name, and printed.</summary>
    [GeneratedRegex(@"\bio:format\(\s*""(?<text>[^""~]*)""\s*\+\+\s*(?<name>[A-Z][\w@]*)\s*\)")]
    private static partial Regex TextJoinedAndPrinted();

    /// <summary>A name given a number: a whole number, a sum, a length.</summary>
    [GeneratedRegex(@"^\s*(?<name>[A-Z][\w@]*)\s*=\s*(?:-?\d+(?:\.\d+)?|length\(.*\)|lists:sum\(.*\)|[\w@]+\s*[-+*]\s*[\w@]+)\s*,?\s*$")]
    private static partial Regex NumberGiven();

    /// <summary>
    /// A number joined to text with ++ and printed: ++ joins lists, and a number is not one, so io:format is given something
    /// that is not text and stops the program with bad argument. The change prints the number with ~p, after the text.
    /// </summary>
    private static IEnumerable<LogicFinding> NumberJoinedToText(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var numbers = masked.Select(line => NumberGiven().Match(line)).Where(given => given.Success).Select(given => given.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);

        for (var index = 0; index < masked.Count; index++)
        {
            if (TextJoinedAndPrinted().Match(source.Lines[index]) is not { Success: true } joined || !numbers.Contains(joined.Groups["name"].Value)) continue;

            var name = joined.Groups["name"].Value;
            var text = joined.Groups["text"].Value;
            var line = source.Lines[index];

            yield return new LogicFinding(id, index + 1,
                $"++ joins lists - and text is one - but {name} is a number, so io:format is given something that is not text, and stops " +
                "the program with bad argument",
                LocalFix.ReplaceLine(id, $"Print {name} with ~p: io:format(\"{text}~p\", [{name}])",
                    $"~p in the text is where io:format prints {name}, which it is given in the list after the text.",
                    source.Path, index + 1, line[..joined.Index] + $"io:format(\"{text}~p\", [{name}])" + line[(joined.Index + joined.Length)..]));
        }
    }

    /// <summary><c>lists:sort(Marks),</c> on a line of its own: a function that gives back a changed copy.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)(?<call>(?<function>lists:sort|lists:reverse|lists:usort|string:uppercase|string:lowercase|string:trim|maps:put|maps:remove)\((?:[^()]|\([^()]*\))*\b(?<name>[A-Z][\w@]*)\s*\))\s*,\s*$")]
    private static partial Regex CopyOnItsOwn();

    /// <summary>What the copy is called, by what made it: Sorted, Reversed, Upper, Lower, Trimmed - or New.</summary>
    private static string CopyNamePrefix(string function) => function switch
    {
        "lists:sort" or "lists:usort" => "Sorted",
        "lists:reverse" => "Reversed",
        "string:uppercase" => "Upper",
        "string:lowercase" => "Lower",
        "string:trim" => "Trimmed",
        _ => "New",
    };

    /// <summary>
    /// A changed copy made on a line of its own, its value lost: Erlang's values never change, so the name read after it is as it
    /// was. The change gives the copy a name of its own - Erlang will not give a name a second value - and reads that name to the
    /// end of the function.
    /// </summary>
    private static IEnumerable<LogicFinding> ResultDiscarded(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (CopyOnItsOwn().Match(masked[index]) is not { Success: true } copy) continue;

            var name = copy.Groups["name"].Value;
            var function = copy.Groups["function"].Value;
            var end = ErlangCode.FunctionEnd(masked, index);
            var read = new Regex($@"(?<![\w@:#?'$]){Regex.Escape(name)}(?![\w@(:])");
            if (!Enumerable.Range(index + 1, Math.Max(0, end - index)).Any(line => read.IsMatch(masked[line]))) continue;

            var newName = CopyNamePrefix(function) + name;
            var call = source.Lines[index].Substring(copy.Groups["call"].Index, copy.Groups["call"].Length);
            var after = ErlangCode.Renamed(source.Lines, index + 1, end, name, newName);

            yield return new LogicFinding(id, index + 1,
                $"Erlang's values never change: {function} gives back a new, changed copy and leaves {name} as it was - so on a line of its " +
                $"own the change is lost, and the lines after it read {name} unchanged",
                new LocalFix
                {
                    RuleId = id,
                    Title = $"Keep the copy as {newName}, and read it after",
                    Explanation = $"{function} gives back the changed copy. An Erlang name is given its value once, so the copy is given a " +
                                  $"name of its own, {newName}, which the lines after it read in place of {name}.",
                    File = source.Path,
                    StartLine = index + 1,
                    RemoveCount = end - index + 1,
                    NewLines = [$"{copy.Groups["indent"].Value}{newName} = {call},", .. after],
                });
        }
    }
}
