using FixFinder.Core.Parsing;

namespace FixFinder.Core.Execution;

/// <summary>How a run of the target ended.</summary>
public enum RunOutcome
{
    /// <summary>Exit code 0 and nothing that looks like an error. Nothing to search for.</summary>
    ExitedClean,

    /// <summary>Finished unhappily, but not in a way that looks like a crash.</summary>
    ExitedNonZero,

    /// <summary>Crashed: a parsed stack trace, or an exit code that means a fatal fault.</summary>
    Crashed,

    /// <summary>Still running when the configured timeout expired, and was killed.</summary>
    TimedOut,

    /// <summary>Stopped by the user.</summary>
    Cancelled,

    /// <summary>The process could not be started at all - missing file, blocked by policy.</summary>
    LaunchFailed,
}

/// <summary>The complete record of one run of the target.</summary>
public sealed class TargetRunResult
{
    public required RunOutcome Outcome { get; init; }

    /// <summary>Null when the process never started, or was killed before it could exit on its own.</summary>
    public int? ExitCode { get; init; }

    public required IReadOnlyList<CapturedLine> Lines { get; init; }

    public required TimeSpan Duration { get; init; }

    /// <summary>Why the launch failed, when <see cref="Outcome"/> is <see cref="RunOutcome.LaunchFailed"/>.</summary>
    public string? LaunchError { get; init; }

    /// <summary>
    /// False when the output pipes were still open five seconds after the process exited.
    /// </summary>
    /// <remarks>
    /// This means a grandchild process inherited the pipe handles and is still holding them, so
    /// the captured output may be incomplete. Worth surfacing rather than hiding: it is the
    /// difference between "the program printed nothing" and "we stopped listening too early".
    /// </remarks>
    public bool OutputStreamsClosedCleanly { get; init; } = true;

    /// <summary>Non-null when the decoded output looks like it was written in another codepage.</summary>
    public string? EncodingWarning { get; init; }

    /// <summary>The error found in the output, or null when nothing parsed as one.</summary>
    /// <remarks>
    /// Null is a real answer, not a gap. A program that ran fine, or one that failed in a way
    /// no parser recognises, both land here - and saying so plainly is better than inventing
    /// an error to search for.
    /// </remarks>
    public ParsedError? Error { get; init; }

    /// <summary>Plain-English summary for the window's outcome line and the run log.</summary>
    public required string Explanation { get; init; }

    public IEnumerable<CapturedLine> StdErrLines => Lines.Where(l => l.Stream == StreamKind.StdErr);
}
