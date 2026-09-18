namespace FixFinder.Core.Execution;

/// <summary>Which of the target's two output streams a line arrived on.</summary>
public enum StreamKind
{
    StdOut,
    StdErr,
}

/// <summary>One line of output captured from the target process.</summary>
public sealed record CapturedLine(int Sequence, StreamKind Stream, string Text, TimeSpan Elapsed)
{
    public bool IsError => Stream == StreamKind.StdErr;

    public string DisplayLine => Stream == StreamKind.StdErr ? $"[err] {Text}" : $"      {Text}";
}
