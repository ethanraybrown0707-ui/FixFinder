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
    /// <param name="FirstPrinted">Where among the lines read doctest's report of it begins - of a run's, among those it printed to its standard output.</param>
    /// <param name="LastPrinted">Where it ends.</param>
    public sealed record Failure(
        string File, int Line, string Name, string Example, string Expected, string Got, string? Exception, int FirstPrinted, int LastPrinted);

    /// <summary>The first line of a failure's report: File "marks.py", line 6, in __main__.grade.</summary>
    [GeneratedRegex(@"^File ""(?<file>.+)"", line (?<line>\d+), in (?<name>\S+)\s*$")]
    private static partial Regex WhereItIs();

    /// <summary>A line of an example, of what it shows or of what it gave: doctest indents each by four spaces.</summary>
    private const string Indent = "    ";

    /// <summary>
    /// Every failure a run printed, read from its standard output alone, which is where doctest prints its report. A program
    /// that crashes after its doctests prints its traceback to its standard error, and the two arrive down two pipes at once,
    /// so the lines of one are sometimes captured in among the lines of the other - and a traceback line inside a report
    /// would be read as part of an example, or keep the report from being read at all.
    /// </summary>
    public static IReadOnlyList<Failure> FailuresIn(IReadOnlyList<CapturedLine> captured) =>
        FailuresIn(OnStandardOutput(captured).Select(line => line.Text).ToList());

    /// <summary>Every failure in these lines, in the order they were printed; none when they are not doctest's report.</summary>
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
        var printed = OnStandardOutput(captured);
        var failures = FailuresIn(printed.Select(line => line.Text).ToList());
        if (failures.Count == 0) return captured;

        // Only the reports' own lines are left out: a line of the program's own traceback captured in among them is kept.
        var reports = new HashSet<CapturedLine>(
            failures.SelectMany(failure => printed.GetRange(failure.FirstPrinted, failure.LastPrinted - failure.FirstPrinted + 1)),
            ReferenceEqualityComparer.Instance);

        return captured.Where(line => !reports.Contains(line)).ToList();
    }

    private static List<CapturedLine> OnStandardOutput(IReadOnlyList<CapturedLine> captured) =>
        captured.Where(line => line.Stream == StreamKind.StdOut).ToList();
}
