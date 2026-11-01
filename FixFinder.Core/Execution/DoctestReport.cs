using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution;

/// <summary>
/// What doctest prints of the examples that failed when a program runs its own doctests with doctest.testmod() and carries
/// on: for each, where it is, the example, and what it shows against what it gave - or the exception it raised.
/// </summary>
public static partial class DoctestReport
{
    /// <summary>One example that did not give what it shows.</summary>
    /// <param name="Name">What its docstring belongs to, as the code names it: grade, or Stack.push.</param>
    /// <param name="Exception">For an example that raised, the last line of what was raised: ZeroDivisionError: division by zero.</param>
    /// <param name="FirstPrinted">Where among the lines printed doctest's report of it begins.</param>
    /// <param name="LastPrinted">Where it ends.</param>
    public sealed record Failure(
        string File, int Line, string Name, string Example, string Expected, string Got, string? Exception, int FirstPrinted, int LastPrinted);

    /// <summary>The first line of a failure's report: File "marks.py", line 6, in __main__.grade.</summary>
    [GeneratedRegex(@"^File ""(?<file>.+)"", line (?<line>\d+), in (?<name>\S+)\s*$")]
    private static partial Regex WhereItIs();

    /// <summary>A line of an example, of what it shows or of what it gave: doctest indents each by four spaces.</summary>
    private const string Indent = "    ";

    /// <summary>Every failure a run printed, in the order it printed them; none when what it printed is not doctest's report.</summary>
    public static IReadOnlyList<Failure> FailuresIn(IReadOnlyList<string> lines)
    {
        var failures = new List<Failure>();

        for (var index = 0; index < lines.Count; index++)
        {
            if (WhereItIs().Match(lines[index]) is not { Success: true } where || index + 1 >= lines.Count || lines[index + 1] != "Failed example:") continue;

            var cursor = index + 2;
            string Section()
            {
                var section = new List<string>();
                while (cursor < lines.Count && lines[cursor].StartsWith(Indent, StringComparison.Ordinal)) section.Add(lines[cursor++][Indent.Length..]);
                return string.Join("\n", section).Trim();
            }

            var example = Section();
            if (cursor >= lines.Count) continue;

            var expected = "nothing";
            var got = "nothing";
            string? exception = null;

            switch (lines[cursor++])
            {
                case "Exception raised:":
                    exception = Section().Split('\n').Select(line => line.Trim()).LastOrDefault(line => line.Length > 0);
                    break;

                case var heading when heading is "Expected:" or "Expected nothing":
                    if (heading == "Expected:") expected = Section();
                    if (cursor < lines.Count && lines[cursor++] == "Got:") got = Section();
                    break;

                default:
                    continue;
            }

            // A program's own docstrings are __main__'s: the name is the code's without it, as the file's own module's is.
            var name = where.Groups["name"].Value;
            var module = Path.GetFileNameWithoutExtension(where.Groups["file"].Value);
            foreach (var owner in new[] { "__main__.", module + "." })
                if (name.StartsWith(owner, StringComparison.Ordinal)) name = name[owner.Length..];

            failures.Add(new Failure(where.Groups["file"].Value, int.Parse(where.Groups["line"].Value), name, example, expected, got, exception, index, cursor - 1));
            index = cursor - 1;
        }

        return failures;
    }

    /// <summary>
    /// What a run printed, without doctest's reports of the examples that failed: the exception an example raised is
    /// printed in its report with its traceback, and that is doctest saying what an example did, not the program stopping.
    /// </summary>
    public static IReadOnlyList<CapturedLine> WithoutReports(IReadOnlyList<CapturedLine> captured)
    {
        var failures = FailuresIn(captured.Select(line => line.Text).ToList());
        if (failures.Count == 0) return captured;

        return captured.Where((_, index) => !failures.Any(failure => index >= failure.FirstPrinted && index <= failure.LastPrinted)).ToList();
    }
}
