using FixFinder.Core.Execution;
using FixFinder.Core.Fingerprinting;
using FixFinder.Core.Parsing;
using FixFinder.Core.Patching;

namespace FixFinder.Core.Verification;

/// <summary>What re-running the program after a patch showed.</summary>
public enum FixVerdict
{
    /// <summary>No error of any consequence. The patch appears to have worked.</summary>
    Fixed,

    /// <summary>The identical error came back. The patch did not address it.</summary>
    SameErrorPersists,

    /// <summary>The build command failed, so the change does not even compile.</summary>
    BuildFailed,

    /// <summary>
    /// A different error now. Often real progress rather than a failure.
    /// </summary>
    DifferentError,

    /// <summary>The run proved nothing either way.</summary>
    Inconclusive,
}

/// <param name="Verdict">What the re-run showed.</param>
/// <param name="BeforeHash">Fingerprint of the original error.</param>
/// <param name="AfterHash">Fingerprint of whatever came back, if anything.</param>
/// <param name="RolledBack">True when the patch was undone automatically.</param>
public sealed record VerificationResult(
    FixVerdict Verdict,
    string Explanation,
    string? BeforeHash = null,
    string? AfterHash = null,
    TargetRunResult? Build = null,
    TargetRunResult? Rerun = null,
    bool RolledBack = false,
    string? RollbackSummary = null)
{
    public bool Succeeded => Verdict == FixVerdict.Fixed;

    /// <summary>
    /// The run this verdict is about: the re-run, or the build when that is what failed.
    /// </summary>
    /// <remarks>
    /// A caller working through several errors needs whichever run produced the error now
    /// showing, and for a compiled language that is as often the build as the program.
    /// </remarks>
    public TargetRunResult? Latest => Rerun ?? Build;

    /// <summary>True when <see cref="Latest"/> is a build rather than a run of the program.</summary>
    public bool NextCameFromBuild => Rerun is null && Build is not null;

    /// <summary>One line for the window, saying both what happened and what was done about it.</summary>
    public string Headline => Verdict switch
    {
        FixVerdict.Fixed => "Fixed — the program ran without the error.",
        FixVerdict.SameErrorPersists => RolledBack
            ? "The same error came back. The patch was rolled back."
            : "The same error came back.",
        FixVerdict.BuildFailed => RolledBack
            ? "The build failed. The patch was rolled back."
            : "The build failed.",
        FixVerdict.DifferentError => "A different error now. The patch was kept — this may be progress.",
        _ => "Inconclusive — this run could not show whether the patch helped.",
    };
}

/// <summary>
/// Builds and re-runs the program after a patch, and decides what to do about the result.
/// </summary>
/// <remarks>
/// The comparison is between error <i>fingerprints</i>, not output text. A patch changes line
/// numbers, so any check that included them would report every applied patch as a different
/// error; the fingerprint has paths, addresses, timestamps and line numbers normalised out
/// precisely so that "the same bug" and "a different bug" can be told apart.
/// <para>
/// <b>An honest caveat, stated here because the UI repeats it.</b> Re-running only proves
/// anything when the crash reproduces from the same invocation with no interaction. A failure
/// that depends on input, timing, the network or a click cannot be verified this way, and
/// neither can a program that was still running happily when the timeout stopped it. Those cases
/// return <see cref="FixVerdict.Inconclusive"/> rather than a verdict that would read as a
/// guarantee.
/// </para>
/// </remarks>
public sealed class FixVerifier(ParserRegistry? parsers = null)
{
    private readonly ParserRegistry _parsers = parsers ?? new ParserRegistry();

    /// <summary>How long a build may take before it is treated as hung.</summary>
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Languages whose running binary is stale the instant the source changes.
    /// </summary>
    /// <remarks>
    /// For these a re-run without a build re-runs the <i>old</i> program, which would report the
    /// original error and roll back a patch that was perfectly good. The window demands a build
    /// command before Apply is enabled for them.
    /// </remarks>
    private static readonly string[] CompiledLanguages = ["csharp", "java", "go", "rust", "gcc", "msvc"];

    public static bool NeedsBuild(string? languageId) =>
        languageId is not null && CompiledLanguages.Contains(languageId, StringComparer.OrdinalIgnoreCase);

    /// <summary>Confidence below which a parsed error is not treated as a real failure.</summary>
    private const int MeaningfulConfidence = 50;

    public event Action<string>? Log;

    /// <summary>
    /// Builds if asked, re-runs the target, compares the result, and rolls back if it should.
    /// </summary>
    /// <param name="before">The fingerprint of the error the patch was meant to fix.</param>
    /// <param name="backupFolder">The backup taken before applying, or null if there is none.</param>
    /// <param name="autoRollback">
    /// Whether a failure should undo the patch. On by default; the window offers no way to turn
    /// it off for a failed build or a repeated error, only for the ambiguous cases.
    /// </param>
    /// <param name="originalWasBuildFailure">
    /// True when the error being fixed was itself a compiler diagnostic. It changes what a failing
    /// build after the patch means: for a program that would not compile in the first place, a
    /// different diagnostic is the ordinary next step, while for one that built fine before, the
    /// patch has plainly broken something.
    /// </param>
    public async Task<VerificationResult> VerifyAsync(
        TargetSpec spec,
        ErrorFingerprint? before,
        BackupStore backups,
        string? backupFolder,
        bool autoRollback = true,
        bool originalWasBuildFailure = false,
        CancellationToken cancellationToken = default)
    {
        if (before is null)
        {
            return new VerificationResult(FixVerdict.Inconclusive,
                "There was no error to compare against, so re-running cannot show whether anything changed.");
        }

        // ---------------------------------------------------------- build

        TargetRunResult? build = null;

        if (spec.BuildCommand is { Length: > 0 } command)
        {
            Log?.Invoke($"Building: {command}");

            build = await RunBuildAsync(spec, command, cancellationToken);

            if (build.Outcome is not RunOutcome.ExitedClean)
            {
                var afterBuild = build.Error is not null ? FingerprintBuilder.Build(build.Error) : null;

                var moved =
                    afterBuild is not null &&
                    !string.Equals(afterBuild.Hash, before.Hash, StringComparison.Ordinal);

                // A program that already would not compile is the one case where a failing build
                // afterwards can be progress: fixing the first diagnostic in a file reveals the
                // second, exactly the way fixing the first of two runtime bugs does. Where the
                // program built cleanly before, though, the same observation means the opposite -
                // the patch has broken it - so the distinction is the caller's to supply and is
                // never inferred from the build output.
                if (originalWasBuildFailure && moved)
                {
                    return new VerificationResult(FixVerdict.DifferentError,
                        "That diagnostic is gone and the build now stops on a different one: " +
                        $"{build.Error!.Summary}. The change has been kept - this is what fixing one " +
                        "of several compiler errors looks like.",
                        before.Hash, afterBuild!.Hash, build, null);
                }

                var rollback = Rollback(backups, backupFolder, autoRollback, out var summary);

                return new VerificationResult(FixVerdict.BuildFailed,
                    $"The build command exited {build.ExitCode?.ToString() ?? "abnormally"}. " +
                    (originalWasBuildFailure
                        ? "The same diagnostic came back, so the change did not address it."
                        : "A patch that does not compile is not a fix.") +
                    (rollback ? $" {summary}" : ""),
                    before.Hash, afterBuild?.Hash, build, null, rollback, summary);
            }

            Log?.Invoke("Build succeeded.");
        }
        else if (NeedsBuild(before.LanguageId))
        {
            // Refusing to guess. Re-running a stale binary would report the old error and roll
            // back a patch that may have been correct, which is the worst of both outcomes.
            return new VerificationResult(FixVerdict.Inconclusive,
                $"This is a compiled language ({before.LanguageId}) and no build command was given, so " +
                "re-running would run the binary from before the patch. The change has been left in place.",
                before.Hash);
        }

        // ---------------------------------------------------------- re-run

        Log?.Invoke("Re-running the target to see whether the error is gone.");

        var runner = new TargetRunner(_parsers);
        var rerun = await runner.RunAsync(spec, cancellationToken);

        var after = rerun.Error is not null ? FingerprintBuilder.Build(rerun.Error) : null;

        // ---------------------------------------------------------- verdict

        if (rerun.Outcome == RunOutcome.LaunchFailed)
        {
            return new VerificationResult(FixVerdict.Inconclusive,
                $"The program could not be started for the re-run: {rerun.LaunchError}. " +
                "Nothing was rolled back, because this says nothing about the patch.",
                before.Hash, null, build, rerun);
        }

        if (rerun.Outcome is RunOutcome.TimedOut or RunOutcome.Cancelled && after is null)
        {
            return new VerificationResult(FixVerdict.Inconclusive,
                rerun.Outcome == RunOutcome.TimedOut
                    ? "The program was still running when the timeout stopped it, and printed no error. " +
                      "For a server or a desktop app that is normal, and it does not show whether the patch helped."
                    : "The re-run was stopped before it finished, so it shows nothing either way.",
                before.Hash, null, build, rerun);
        }

        if (after is null || rerun.Error is { Confidence: < MeaningfulConfidence })
        {
            return new VerificationResult(FixVerdict.Fixed,
                rerun.Outcome == RunOutcome.ExitedClean
                    ? "The program ran to completion with no error in its output."
                    : $"The program exited {rerun.ExitCode}, but nothing in its output parsed as an error.",
                before.Hash, after?.Hash, build, rerun);
        }

        if (string.Equals(after.Hash, before.Hash, StringComparison.Ordinal))
        {
            var rolledBack = Rollback(backups, backupFolder, autoRollback, out var summary);

            return new VerificationResult(FixVerdict.SameErrorPersists,
                "The same error came back, identical once line numbers and paths are normalised out. " +
                "The patch did not address it." + (rolledBack ? $" {summary}" : ""),
                before.Hash, after.Hash, build, rerun, rolledBack, summary);
        }

        // Deliberately never rolled back automatically. Fixing the first of two bugs looks
        // exactly like this, and undoing real progress unasked would be the worse mistake.
        return new VerificationResult(FixVerdict.DifferentError,
            $"The original error is gone and a different one has appeared: {rerun.Error!.Summary}. " +
            "That is often progress rather than a failure, so the patch has been kept - roll it back " +
            "yourself if you disagree.",
            before.Hash, after.Hash, build, rerun);
    }

    private bool Rollback(BackupStore backups, string? backupFolder, bool autoRollback, out string summary)
    {
        if (!autoRollback || backupFolder is null)
        {
            summary = backupFolder is null
                ? "There was no backup to roll back to."
                : "Automatic rollback was switched off, so the change is still in place.";

            return false;
        }

        var restore = backups.Restore(backupFolder);
        summary = restore.Summary;

        Log?.Invoke($"Rolled back: {restore.Summary}");

        return restore.Ok;
    }

    /// <summary>Runs the build command as its own target, so it is captured and logged like any other.</summary>
    private async Task<TargetRunResult> RunBuildAsync(TargetSpec spec, string command, CancellationToken ct)
    {
        var (program, arguments) = SplitCommand(command);

        var buildSpec = new TargetSpec
        {
            ExecutablePath = program,
            Arguments = arguments,
            WorkingDirectory = spec.BuildWorkingDirectory ?? spec.WorkingDirectory,
            LaunchViaDotnet = false,
            ExtraEnvironment = spec.ExtraEnvironment,
            Timeout = BuildTimeout,
        };

        var runner = new TargetRunner(_parsers);
        runner.Log += message => Log?.Invoke($"build: {message}");

        return await runner.RunAsync(buildSpec, ct);
    }

    /// <summary>
    /// Splits a command line into its program and the rest.
    /// </summary>
    /// <remarks>
    /// Only the first token is separated out, and the remainder is passed through untouched -
    /// the process start info takes arguments as one string anyway, so re-quoting them here
    /// could only corrupt something that was already correct.
    /// </remarks>
    public static (string Program, string Arguments) SplitCommand(string command)
    {
        var text = command.Trim();
        if (text.Length == 0) return ("", "");

        if (text[0] == '"')
        {
            var close = text.IndexOf('"', 1);

            if (close > 0)
                return (text[1..close], text[(close + 1)..].TrimStart());
        }

        var space = text.IndexOf(' ');

        return space < 0 ? (text, "") : (text[..space], text[(space + 1)..].TrimStart());
    }
}
