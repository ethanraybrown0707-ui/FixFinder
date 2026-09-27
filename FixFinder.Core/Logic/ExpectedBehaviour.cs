using FixFinder.Core.Execution;

namespace FixFinder.Core.Logic;

/// <summary>One run the person described: what to type into the program, and what it should print back.</summary>
public sealed record ExpectedRun(string? Input, string ExpectedOutput);

/// <summary>How a program is meant to behave: the arguments it is run with, and one or more runs with their expected output.</summary>
public sealed record ExpectedBehaviour(IReadOnlyList<ExpectedRun> Runs, string? Arguments = null)
{
    public bool IsEmpty => Runs.Count == 0;

    public static ExpectedBehaviour From(IEnumerable<ExpectedRun> runs, string? arguments) =>
        new(runs.Where(r => r.ExpectedOutput.Trim().Length > 0).ToList(), string.IsNullOrWhiteSpace(arguments) ? null : arguments);
}

/// <summary>How a run ended: by finishing, or before it could - which decides what can truly be said about its output.</summary>
public enum RunEnding
{
    Finished,
    StoppedWithAnError,
    StillRunningWhenTimeRanOut,
    NotStarted,
}

/// <summary>
/// Where a program's output first differs from what was expected, compared as far as the run got. A run that stopped
/// with an error or ran out of time is compared on what it printed before then, and the description says how it ended
/// - never that it ran to the end, and never quoting FixFinder's own account of the run as something the program printed.
/// </summary>
public sealed record OutputMismatch(int Line, string? Expected, string? Actual, int ExpectedLines, int ActualLines)
{
    public RunEnding Ending { get; init; } = RunEnding.Finished;

    public string Describe()
    {
        var lines = $"{ActualLines} line{(ActualLines == 1 ? "" : "s")}";

        return (Ending, Expected, Actual) switch
        {
            (RunEnding.NotStarted, _, _) => "it could not be started",

            (RunEnding.Finished, null, { } actual) => $"it printed an extra line {Line}, \"{Shorten(actual)}\", after everything expected",
            (RunEnding.Finished, { } expected, null) => $"it stopped after {lines}, where line {Line} should have been \"{Shorten(expected)}\"",
            (RunEnding.Finished, _, _) => $"line {Line} was \"{Shorten(Actual!)}\" where \"{Shorten(Expected!)}\" was expected",

            (RunEnding.StoppedWithAnError, { } expected, null) =>
                $"it stopped with an error after printing {lines}, where line {Line} should have been \"{Shorten(expected)}\"",
            (RunEnding.StillRunningWhenTimeRanOut, { } expected, null) =>
                $"it had printed {lines} when the time ran out, where line {Line} should have been \"{Shorten(expected)}\"",

            (_, null, null) => $"it printed everything expected, but {HowItEnded}",
            (_, null, { } extra) => $"it printed an extra line {Line}, \"{Shorten(extra)}\", after everything expected, and then {HowItEnded}",
            _ => $"line {Line} was \"{Shorten(Actual!)}\" where \"{Shorten(Expected!)}\" was expected, and then {HowItEnded}",
        };
    }

    private string HowItEnded => Ending == RunEnding.StoppedWithAnError
        ? "it stopped with an error"
        : "it was still running when the time ran out";

    private static string Shorten(string text) => text.Length <= 80 ? text : text[..77] + "...";
}

/// <summary>Compares what a program printed with what it should have printed.</summary>
public static class OutputComparison
{
    public static IReadOnlyList<string> Normalise(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(l => l.TrimEnd()).ToList();
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    public static IReadOnlyList<string> Printed(TargetRunResult run) =>
        Normalise(string.Join("\n", run.Lines.Where(l => l.Stream == StreamKind.StdOut).Select(l => l.Text)));

    /// <summary>
    /// The first line where what the run printed differs from what was expected - or, for a run that did not finish,
    /// how it ended even when everything it printed was right, since a run that stopped has not printed what it should.
    /// </summary>
    public static OutputMismatch? Compare(IReadOnlyList<string> actual, string expected, RunEnding ending = RunEnding.Finished)
    {
        var wanted = Normalise(expected);
        var count = Math.Max(actual.Count, wanted.Count);

        for (var i = 0; i < count; i++)
        {
            var a = i < actual.Count ? actual[i] : null;
            var e = i < wanted.Count ? wanted[i] : null;

            if (!string.Equals(a, e, StringComparison.Ordinal)) return new OutputMismatch(i + 1, e, a, wanted.Count, actual.Count) { Ending = ending };
        }

        return ending == RunEnding.Finished ? null : new OutputMismatch(count + 1, null, null, wanted.Count, actual.Count) { Ending = ending };
    }

    /// <summary>How a run's outcome ended it, for comparing what it printed.</summary>
    public static RunEnding EndingOf(RunOutcome outcome) => outcome switch
    {
        RunOutcome.ExitedClean or RunOutcome.ExitedNonZero => RunEnding.Finished,
        RunOutcome.Crashed => RunEnding.StoppedWithAnError,
        RunOutcome.TimedOut => RunEnding.StillRunningWhenTimeRanOut,
        _ => RunEnding.NotStarted,
    };
}
