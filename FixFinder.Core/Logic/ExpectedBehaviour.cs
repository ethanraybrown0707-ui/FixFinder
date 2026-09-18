using FixFinder.Core.Execution;

namespace FixFinder.Core.Logic;

/// <summary>One run the person described: what to type into the program, and what it should print back.</summary>
/// <param name="Input">What is typed in, or null for nothing.</param>
/// <param name="ExpectedOutput">What the program should print, line by line.</param>
public sealed record ExpectedRun(string? Input, string ExpectedOutput);

/// <summary>How a program is meant to behave: the arguments it is run with, and one or more runs with their expected output.</summary>
/// <remarks>
/// A logic error is a program that runs to the end and does the wrong thing, and "wrong" needs an answer to compare with.
/// Nothing here guesses what a program was meant to do - the person says, and every judgement is made against that.
/// </remarks>
public sealed record ExpectedBehaviour(IReadOnlyList<ExpectedRun> Runs, string? Arguments = null)
{
    public bool IsEmpty => Runs.Count == 0;

    /// <summary>The runs that say anything: an empty expected output is not a claim about the program.</summary>
    public static ExpectedBehaviour From(IEnumerable<ExpectedRun> runs, string? arguments) =>
        new(runs.Where(r => r.ExpectedOutput.Trim().Length > 0).ToList(), string.IsNullOrWhiteSpace(arguments) ? null : arguments);
}

/// <summary>Where a program's output first differs from what was expected.</summary>
/// <param name="Line">The 1-based output line where they part.</param>
/// <param name="Expected">That line as expected, or null when the output was meant to have ended.</param>
/// <param name="Actual">That line as printed, or null when the output stopped early.</param>
public sealed record OutputMismatch(int Line, string? Expected, string? Actual, int ExpectedLines, int ActualLines)
{
    public string Describe() => (Expected, Actual) switch
    {
        (null, { } actual) => $"it printed an extra line {Line}, \"{Shorten(actual)}\", after everything expected",
        ({ } expected, null) => $"it stopped after {ActualLines} line{(ActualLines == 1 ? "" : "s")}, where line {Line} should have been \"{Shorten(expected)}\"",
        _ => $"line {Line} was \"{Shorten(Actual!)}\" where \"{Shorten(Expected!)}\" was expected",
    };

    private static string Shorten(string text) => text.Length <= 80 ? text : text[..77] + "...";
}

/// <summary>Compares what a program printed with what it should have printed.</summary>
/// <remarks>
/// Line by line, after the differences nobody means: line endings, spaces at the end of a line, and blank lines at the end of
/// the output. Nothing else is forgiven - <c>5.0</c> is not <c>5</c> - because a comparison that guesses is a comparison
/// that can call a wrong program right.
/// </remarks>
public static class OutputComparison
{
    public static IReadOnlyList<string> Normalise(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(l => l.TrimEnd()).ToList();
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    /// <summary>What a run printed to standard output, normalised the same way.</summary>
    public static IReadOnlyList<string> Printed(TargetRunResult run) =>
        Normalise(string.Join("\n", run.Lines.Where(l => l.Stream == StreamKind.StdOut).Select(l => l.Text)));

    /// <summary>The first difference, or null when the output is what was expected.</summary>
    public static OutputMismatch? Compare(IReadOnlyList<string> actual, string expected)
    {
        var wanted = Normalise(expected);
        var count = Math.Max(actual.Count, wanted.Count);

        for (var i = 0; i < count; i++)
        {
            var a = i < actual.Count ? actual[i] : null;
            var e = i < wanted.Count ? wanted[i] : null;

            if (!string.Equals(a, e, StringComparison.Ordinal)) return new OutputMismatch(i + 1, e, a, wanted.Count, actual.Count);
        }

        return null;
    }
}
