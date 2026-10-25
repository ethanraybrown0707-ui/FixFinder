using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A date written out with the wrong letters, each run with the language's own toolchain for 9:30 on 5 February 2024 - and,
/// for Java's year, 30 December 2024 - with nothing said by any of them:
/// strftime's %M is the minutes, not the month, so "%Y-%M-%d" gave 2024-30-05 in Python, Ruby, Perl and Elixir; in Java's
/// patterns YYYY is the year of the week - 2025 on 30 December 2024 - DD the day of the year - 2024-02-36 - and mm the
/// minutes - 2024-30-05; in .NET's - C#, F# and PowerShell - mm is the minutes, and YYYY and DD are not letters it knows, so
/// they are printed as they are - YYYY-02-DD; Go writes a date from the way it writes 2 January 2006 at 15:04:05 -
/// "2006-01-02" - so "YYYY-MM-DD" printed YYYY-MM-DD; and in PHP's date, m is the month and i the minutes, so "H:m" gave
/// 09:02. The change writes the letters each means.
/// </summary>
public static partial class DateFormatPatterns
{
    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        Date("logic-python-date-format-minutes", CodePattern.Files(".py", ".pyw"), Syntax.Python, StrftimeCall(), "\"'", StrftimeProblem),
        Date("logic-ruby-date-format-minutes", CodePattern.Files(".rb"), Syntax.Python, StrftimeCall(), "\"'", StrftimeProblem),
        Date("logic-perl-date-format-minutes", CodePattern.Files(".pl", ".pm"), Syntax.Perl, StrftimeCall(), "\"'", StrftimeProblem),
        Date("logic-elixir-date-format-minutes", CodePattern.Files(".exs", ".ex"), Syntax.Python, StrftimeCall(), "\"", StrftimeProblem),
        Date("logic-java-date-pattern-letters", CodePattern.Files(".java"), Syntax.CLike, JavaPatternCall(), "\"", JavaPatternProblem),
        Date("logic-csharp-date-format-letters", CodePattern.Files(".cs"), Syntax.CLike, DotNetFormatCall(), "\"", DotNetFormatProblem, composite: true),
        Date("logic-fsharp-date-format-letters", CodePattern.Files(".fsx"), Syntax.FSharp, DotNetFormatCall(), "\"", DotNetFormatProblem, composite: true),
        Date("logic-powershell-date-format-letters", CodePattern.Files(".ps1"), Syntax.Python, PowerShellFormatCall(), "\"'", DotNetFormatProblem, composite: true),
        Date("logic-go-date-layout-letters", CodePattern.Files(".go"), Syntax.CLike, GoLayoutCall(), "\"", GoLayoutProblem),
        Date("logic-php-date-minutes-as-month", CodePattern.Files(".php"), Syntax.CLike, PhpDateCall(), "\"'", PhpDateProblem),
    ];

    /// <summary>What is wrong with a format, and the format with it put right.</summary>
    private sealed record Problem(string Message, string Changed, string Title);

    private static CodePattern Date(string id, IReadOnlySet<string> extensions, Syntax syntax, Regex call, string quotes, Func<string, Problem?> problem, bool composite = false) =>
        new(id, extensions, syntax, (patternId, source, masked) => WrongLetters(patternId, source, masked, call, quotes, problem, composite), Severity.Error, Confidence.Likely);

    [GeneratedRegex(@"\b(?:strftime|strptime)\s*\(")]
    private static partial Regex StrftimeCall();

    [GeneratedRegex(@"\bofPattern\s*\(|\bSimpleDateFormat\s*\(|\.applyPattern\s*\(")]
    private static partial Regex JavaPatternCall();

    [GeneratedRegex(@"\.ToString\s*\(|\bTryParseExact\s*\(|\bParseExact\s*\(")]
    private static partial Regex DotNetFormatCall();

    [GeneratedRegex(@"\.ToString\s*\(|\bTryParseExact\s*\(|\bParseExact\s*\(|-Format\s", RegexOptions.IgnoreCase)]
    private static partial Regex PowerShellFormatCall();

    [GeneratedRegex(@"\.Format\s*\(|\btime\.Parse(?:InLocation)?\s*\(|\.AppendFormat\s*\(")]
    private static partial Regex GoLayoutCall();

    [GeneratedRegex(@"(?<![\w$>])(?:gm)?date\s*\(|->format\s*\(|\bdate_format\s*\(|::createFromFormat\s*\(")]
    private static partial Regex PhpDateCall();

    /// <summary>A part of composite formatting - {0:dd/mm/yyyy}, or {when:dd/mm/yyyy} in interpolated text.</summary>
    [GeneratedRegex(@"\{[^{}:""]+:(?<format>[^{}""]+)\}")]
    private static partial Regex CompositePart();

    /// <summary>strftime: %M with the year or the day, and neither the hour nor the month.</summary>
    private static Problem? StrftimeProblem(string format)
    {
        if (!format.Contains("%M", StringComparison.Ordinal) || !Regex.IsMatch(format, "%[YydebBj]") || Regex.IsMatch(format, "%[HIklmRTr]")) return null;

        return new Problem(
            "%M is the minutes of the hour, not the month - the month is %m - so the minutes are written where the month should be: 9:30 on " +
            "5 February 2024 gives 30 for the month",
            format.Replace("%M", "%m", StringComparison.Ordinal), "Write the month with %m");
    }

    /// <summary>Java's pattern letters outside its 'quoted text'.</summary>
    private static Problem? JavaPatternProblem(string pattern)
    {
        var letters = Regex.Replace(pattern, "'[^']*'", match => new string(' ', match.Length));
        var reasons = new List<string>();
        var changed = letters.ToCharArray();

        if (letters.Contains('Y') && !letters.Contains('w'))
        {
            reasons.Add("Y is the year of the week, not of the date - 30 December 2024 is in the first week of 2025, so its YYYY is 2025");
            Replace(changed, 'Y', 'y');
        }

        if (letters.Contains('D') && letters.Contains('M'))
        {
            reasons.Add("D is the day of the year - 5 February is day 36 - where d is the day of the month");
            Replace(changed, 'D', 'd');
        }

        if (letters.Contains('m') && !letters.Contains('M') && !Regex.IsMatch(letters, "[HhkK]") && Regex.IsMatch(letters, "[yYdD]"))
        {
            reasons.Add("m is the minutes, not the month - M is the month - so 9:30 on 5 February gives 30 for the month");
            Replace(changed, 'm', 'M');
        }

        return reasons.Count == 0 ? null : new Problem(string.Join("; ", reasons), Restore(pattern, changed), "Use the letters for the date");
    }

    /// <summary>.NET's custom format letters: mm for the month, and YYYY or DD, which it prints as they are.</summary>
    private static Problem? DotNetFormatProblem(string format)
    {
        // One letter on its own is a standard format - "D" is the long date - not a custom one.
        if (format.Length < 2 || !Regex.IsMatch(format, "yy|YY|dd|DD|MM")) return null;

        var letters = Regex.Replace(format, @"'[^']*'|""[^""]*""|\\.", match => new string(' ', match.Length));
        var reasons = new List<string>();
        var changed = letters.ToCharArray();

        if (Regex.IsMatch(letters, "[YD]"))
        {
            reasons.Add("Y and D are not letters a date's format knows - the year is yyyy and the day dd - so they are printed as they are: YYYY-02-DD");
            Replace(changed, 'Y', 'y');
            Replace(changed, 'D', 'd');
        }

        if (letters.Contains('m') && !letters.Contains('M') && !Regex.IsMatch(letters, "[Hh]") && Regex.IsMatch(letters, "[yYdD]"))
        {
            reasons.Add("mm is the minutes, not the month - MM is the month - so 9:30 on 5 February 2024 gives 2024-30-05");
            Replace(changed, 'm', 'M');
        }

        return reasons.Count == 0 ? null : new Problem(string.Join("; ", reasons), Restore(format, changed), "Use the letters for the date");
    }

    /// <summary>Go's layout letters - YYYY, MM, DD - in a layout with none of the numbers of 2 January 2006 at 15:04:05 in it.</summary>
    private static Problem? GoLayoutProblem(string layout)
    {
        if (!Regex.IsMatch(layout, "YYYY|yyyy|DD|dd") || Regex.IsMatch(layout, @"\d|Jan|Mon|MST")) return null;

        var changed = Regex.Replace(layout, "YYYY|yyyy|YY|yy|MM|DD|dd|HH|hh|mm|SS|ss", token => token.Value switch
        {
            "YYYY" or "yyyy" => "2006",
            "YY" or "yy" => "06",
            // MM straight after the hour - HH:MM - is the minutes.
            "MM" when token.Index >= 3 && Regex.IsMatch(layout[(token.Index - 3)..token.Index], "^[Hh]{2}:$") => "04",
            "MM" => "01",
            "DD" or "dd" => "02",
            "HH" => "15",
            "hh" => "03",
            // mm is the minutes beside an MM for the month, or straight after the hour; otherwise - dd/mm/yyyy - the month.
            "mm" when layout.Contains("MM", StringComparison.Ordinal) ||
                      (token.Index >= 3 && Regex.IsMatch(layout[(token.Index - 3)..token.Index], "^[Hh]{2}:$")) => "04",
            "mm" => "01",
            _ => "05",
        });

        return new Problem(
            $"Go writes a date the way its layout writes 2 January 2006 at 15:04:05 - \"2006-01-02\" - and letters are not part of that, so " +
            $"\"{layout}\" is printed as it is: {layout}",
            changed, "Write the layout with 2006, 01 and 02");
    }

    /// <summary>PHP's date: m straight after the hour and a colon, with no i for the minutes anywhere.</summary>
    private static Problem? PhpDateProblem(string format)
    {
        if (!Regex.IsMatch(format, "[HhGg]:m") || Regex.IsMatch(format, @"(?<!\\)i")) return null;

        return new Problem(
            "m is the month in date's letters - the minutes are i - so the hour is followed by the month: 9:30 on 5 February gives 09:02",
            Regex.Replace(format, "(?<=[HhGg]:)m", "i"), "Write the minutes with i");
    }

    private static void Replace(char[] letters, char from, char to)
    {
        for (var index = 0; index < letters.Length; index++)
            if (letters[index] == from) letters[index] = to;
    }

    /// <summary>The changed letters, with the quoted text that was blanked out to read them put back as it was.</summary>
    private static string Restore(string original, char[] changed) =>
        new(original.Select((character, index) => changed[index] == ' ' && character != ' ' ? character : changed[index]).ToArray());

    /// <summary>Each piece of quoted text on a masked line - its start and end in the line, inside the quotes.</summary>
    private static IEnumerable<(int Start, int End)> QuotedText(string masked, string quotes)
    {
        for (var index = 0; index < masked.Length; index++)
        {
            if (quotes.IndexOf(masked[index]) < 0) continue;

            var close = masked.IndexOf(masked[index], index + 1);
            if (close < 0) yield break;

            yield return (index + 1, close);
            index = close;
        }
    }

    private static IEnumerable<LogicFinding> WrongLetters(string id, SourceFile source, IReadOnlyList<string> masked, Regex call, string quotes,
        Func<string, Problem?> problem, bool composite)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            var line = source.Lines[index];
            var called = call.IsMatch(masked[index]);

            foreach (var (start, end) in QuotedText(masked[index], quotes))
            {
                var text = line[start..end];

                // A whole format given to a call that takes one, or a format inside composite formatting, anywhere.
                var formats = new List<(int Start, string Format)>();
                if (called && call.Match(masked[index]).Index < start) formats.Add((start, text));
                if (composite)
                    formats.AddRange(CompositePart().Matches(text).Select(part => (start + part.Groups["format"].Index, part.Groups["format"].Value)));

                foreach (var (at, format) in formats)
                {
                    if (problem(format) is not { } wrong) continue;

                    yield return new LogicFinding(id, index + 1, wrong.Message,
                        LocalFix.ReplaceLine(id, $"{wrong.Title}: \"{wrong.Changed}\"",
                            $"\"{wrong.Changed}\" asks for the date's own parts, so the date is written as it was meant.",
                            source.Path, index + 1, line[..at] + wrong.Changed + line[(at + format.Length)..]));
                }
            }
        }
    }
}
