using FixFinder.Core.Parsing;

namespace FixFinder.Core.Execution;

/// <summary>How a run of the target ended.</summary>
public enum RunOutcome
{
    ExitedClean,

    ExitedNonZero,

    Crashed,

    TimedOut,

    Cancelled,

    LaunchFailed,
}

/// <summary>The complete record of one run of the target.</summary>
public sealed class TargetRunResult
{
    public required RunOutcome Outcome { get; init; }

    public int? ExitCode { get; init; }

    public required IReadOnlyList<CapturedLine> Lines { get; init; }

    public required TimeSpan Duration { get; init; }

    public string? LaunchError { get; init; }

    public bool OutputStreamsClosedCleanly { get; init; } = true;

    public string? EncodingWarning { get; init; }

    public ParsedError? Error { get; init; }

    public required string Explanation { get; init; }

    public IEnumerable<CapturedLine> StdErrLines => Lines.Where(l => l.Stream == StreamKind.StdErr);
}
