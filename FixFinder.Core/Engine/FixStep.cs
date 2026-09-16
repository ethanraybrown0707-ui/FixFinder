using FixFinder.Core.Patching;
using FixFinder.Core.Verification;

namespace FixFinder.Core.Engine;

/// <summary>What one apply-and-check step did.</summary>
/// <param name="Apply">The write itself, or the reason there was not one.</param>
/// <param name="Verification">
/// What re-running showed, or null when there was nothing to re-run against.
/// </param>
public sealed record StepResult(ApplyResult Apply, VerificationResult? Verification)
{
    /// <summary>True when files were written and are still changed.</summary>
    public bool LeftChanged => Apply.Ok && Verification?.RolledBack != true;

    /// <summary>The verdict, or null when the change could not be verified at all.</summary>
    public FixVerdict? Verdict => Verification?.Verdict;

    public string Summary => Verification is null
        ? Apply.Summary
        : $"{Apply.Summary} {Verification.Headline}";
}

/// <summary>
/// Applies one candidate's patch and immediately checks whether it helped.
/// </summary>
/// <remarks>
/// Exists so that the two ways a patch can be applied - a person typing APPLY in the preview, and
/// the loop working through a run unattended - are the same code rather than two sequences that
/// can drift apart. Writing to someone's source tree is the last place worth having a second
/// implementation of anything.
/// <para>
/// It applies; it does not decide. Whether the patch <i>should</i> be applied was settled before
/// this is called, by the plan being appliable, the candidate clearing the score floor, and the
/// user saying so. What happens next is settled after it, by the caller reading the verdict.
/// </para>
/// </remarks>
/// <param name="language">The language the person said the program is in; the re-run is read with its parsers.</param>
public sealed class FixStep(BackupStore? backups = null, CodeLanguage? language = null)
{
    private readonly BackupStore _backups = backups ?? new BackupStore();

    public event Action<string>? Log;

    /// <summary>Where backups are written, so a caller can name the folder.</summary>
    public string BackupRoot => _backups.Root;

    /// <summary>
    /// Writes the outcome's patch, then builds and re-runs to see what it did.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the outcome has no appliable plan. That is a programming error rather than a
    /// user-facing state: every path to here has already checked <see cref="SessionOutcome.CanApply"/>,
    /// and failing loudly is better than a silent no-op that reads as "applied nothing".
    /// </exception>
    public async Task<StepResult> ApplyAsync(SessionOutcome outcome, CancellationToken cancellationToken = default)
    {
        if (outcome.Plan is not { CanApply: true } plan || outcome.SourceRoot is null || outcome.Best is null)
            throw new InvalidOperationException("This outcome has no patch that can be applied.");

        var applier = new PatchApplier();
        applier.Log += Relay;

        ApplyResult applied;

        try
        {
            applied = applier.Apply(
                plan, _backups, outcome.SourceRoot, dryRun: false,
                outcome.Best.Id, outcome.Best.Title, outcome.Best.Url);
        }
        finally
        {
            applier.Log -= Relay;
        }

        if (!applied.Ok) return new StepResult(applied, null);

        // Nothing to repeat means nothing to compare, and a verdict invented from no evidence
        // would be the one kind of answer this tool must not give.
        if (outcome.Spec is null) return new StepResult(applied, null);

        var verifier = new FixVerifier(language?.Parsers());
        verifier.Log += Relay;

        try
        {
            var verification = await verifier.VerifyAsync(
                outcome.Spec, outcome.Fingerprint, _backups, applied.BackupFolder,
                autoRollback: true, outcome.FailedToCompile, cancellationToken);

            return new StepResult(applied, verification);
        }
        finally
        {
            verifier.Log -= Relay;
        }
    }

    private void Relay(string message) => Log?.Invoke(message);
}
