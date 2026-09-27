namespace FixFinder.Core.Checking;

/// <summary>How far a proposed fix was actually taken, in the order the stages happen.</summary>
public enum VerificationStage
{
    /// <summary>A copy with the change was given to the compiler.</summary>
    Compiled,

    /// <summary>A copy with the change was run, and the failure it was meant to fix did not happen again.</summary>
    Ran,

    /// <summary>What that run printed was what the person said the program should print.</summary>
    MatchedExpectedOutput,
}

/// <summary>What happened at one stage. Not tested is the absence of a stage rather than a result of one.</summary>
public enum StageResult
{
    Passed,

    Failed,

    /// <summary>Not attempted, and nothing follows from that: there was no compiler, or no expected output was given.</summary>
    Skipped,

    /// <summary>Attempted, and the answer cannot be trusted either way - a timeout, or a program that needs a person.</summary>
    Inconclusive,
}

/// <param name="Detail">What was done, in words a reader can check the claim against.</param>
public sealed record VerificationStep(VerificationStage Stage, StageResult Result, string Detail);

/// <summary>
/// What FixFinder actually established about a fix, stage by stage.
/// </summary>
/// <remarks>
/// The reason this is a list of stages rather than a flag is that the interesting answers are the partial ones. A fix
/// that compiles has been shown to be valid code and nothing more - not that it works, and not that it fixes anything.
/// Saying "verified" on the strength of that would be the most misleading thing FixFinder could do with a fix, so
/// <see cref="IsVerified"/> will not say it until the program has been run and the failure has stopped happening.
/// </remarks>
public sealed record Verification
{
    public static readonly Verification NotTested = new();

    public IReadOnlyList<VerificationStep> Steps { get; init; } = [];

    public bool WasTested => Steps.Count > 0;

    public StageResult? ResultOf(VerificationStage stage) =>
        Steps.FirstOrDefault(step => step.Stage == stage)?.Result;

    /// <summary>
    /// Whether the fix was shown to work. Running it and not seeing the failure again is the least that will do; where
    /// the person said what the program should print, that has to match as well.
    /// </summary>
    public bool IsVerified =>
        ResultOf(VerificationStage.Ran) == StageResult.Passed &&
        ResultOf(VerificationStage.MatchedExpectedOutput) is null or StageResult.Passed or StageResult.Skipped &&
        !Steps.Any(step => step.Result is StageResult.Failed);

    /// <summary>Whether anything actually went wrong, as opposed to not having been looked at.</summary>
    public bool SomethingFailed => Steps.Any(step => step.Result == StageResult.Failed);

    public string Summary => !WasTested
        ? "Not tested"
        : IsVerified
            ? "Fix verified"
            : SomethingFailed
                ? "Not verified"
                : "Partly checked";

    public Verification With(VerificationStage stage, StageResult result, string detail) =>
        this with { Steps = [.. Steps.Where(step => step.Stage != stage), new VerificationStep(stage, result, detail)] };
}
