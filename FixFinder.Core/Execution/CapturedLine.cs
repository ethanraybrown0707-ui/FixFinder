namespace FixFinder.Core.Execution;

/// <summary>Which of the target's two output streams a line arrived on.</summary>
public enum StreamKind
{
    StdOut,
    StdErr,
}

/// <summary>
/// One line of output captured from the target process.
/// </summary>
/// <param name="Sequence">
/// Monotonic counter across BOTH streams, assigned as each line arrives. stdout and stderr are
/// read by two independent callbacks, so this is the only record of their relative order - and
/// it is approximate by nature, because the two pipes are buffered separately by the OS. Good
/// enough to keep a stack trace contiguous, which is all the parsers need; not good enough to
/// prove that a given stdout line was printed before a given stderr line.
/// </param>
/// <param name="Stream">The stream the line arrived on.</param>
/// <param name="Text">The line, without its trailing newline.</param>
/// <param name="Elapsed">Time since the process was started.</param>
public sealed record CapturedLine(int Sequence, StreamKind Stream, string Text, TimeSpan Elapsed)
{
    public bool IsError => Stream == StreamKind.StdErr;

    /// <summary>Prefixed form used by the GUI's output pane and the run log.</summary>
    public string DisplayLine => Stream == StreamKind.StdErr ? $"[err] {Text}" : $"      {Text}";
}
