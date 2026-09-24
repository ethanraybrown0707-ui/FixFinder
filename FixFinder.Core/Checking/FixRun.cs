using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.Checking;

/// <summary>
/// Runs a copy of the program with a proposed fix in it, to find out whether the failure it was meant to fix happens
/// again - and, where the person said what the program should print, whether it prints it.
/// </summary>
/// <remarks>
/// The copy is the whole point. Finding out whether a fix works means running the program, and running the person's
/// own file would mean writing the fix into it first, which FixFinder does not do. Everything here happens in a folder
/// under the temporary directory, which is deleted afterwards whatever the outcome.
/// </remarks>
public static class FixRun
{
    private static string Root => Path.Combine(Path.GetTempPath(), "FixFinder-run");

    /// <summary>
    /// Tries the fix for real and records what that showed, leaving <paramref name="sofar"/> as it was if it could not
    /// be tried at all.
    /// </summary>
    public static async Task<Verification> CheckAsync(
        Verification sofar,
        SourceFile source,
        LocalFix fix,
        ParsedError? original,
        ExpectedBehaviour? expected,
        CancellationToken cancellationToken)
    {
        if (fix.ApplyTo(source) is not { } changed) return sofar;

        var folder = Path.Combine(Root, Guid.NewGuid().ToString("N")[..12]);

        try
        {
            Directory.CreateDirectory(folder);

            // The rest of the program comes too: a fix to one file of a program that will not run without the others
            // cannot be tried on its own.
            foreach (var beside in ProgramFiles.Of(source.Path))
            {
                if (string.Equals(beside, source.Path, StringComparison.OrdinalIgnoreCase)) continue;

                try { File.Copy(beside, Path.Combine(folder, Path.GetFileName(beside)), overwrite: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }

            var copy = Path.Combine(folder, Path.GetFileName(source.Path));
            await File.WriteAllBytesAsync(copy, source.Render(changed), cancellationToken);

            var plan = TargetFactory.FromFile(copy);
            if (!plan.Ok || plan.Spec is null)
            {
                return sofar.With(VerificationStage.Ran, StageResult.Skipped, "There was nothing on this machine to run it with.");
            }

            return await JudgeAsync(sofar, plan, original, expected, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return sofar.With(VerificationStage.Ran, StageResult.Skipped, "A copy to try the change on could not be made.");
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static async Task<Verification> JudgeAsync(
        Verification sofar, LaunchPlan plan, ParsedError? original, ExpectedBehaviour? expected, CancellationToken cancellationToken)
    {
        var runner = new TargetRunner(new ParserRegistry());

        // One run per thing the person said the program should print, so every one of them is checked - and a single
        // plain run when they said nothing, because the failure is worth testing either way.
        var wanted = expected is { IsEmpty: false } ? expected.Runs : [new ExpectedRun(null, "")];
        var checkingOutput = expected is { IsEmpty: false };

        var mismatched = (string?)null;

        foreach (var want in wanted)
        {
            var spec = want.Input is { } typed ? plan.Spec!.WithInput(typed) : plan.Spec!;
            var run = await runner.RunAsync(spec, cancellationToken);

            if (run.Outcome == RunOutcome.TimedOut)
            {
                return sofar.With(VerificationStage.Ran, StageResult.Inconclusive,
                    $"The copy was still going after {spec.Timeout.TotalSeconds:0} seconds, so nothing follows either way.");
            }

            if (run.Error is { } still)
            {
                return sofar.With(VerificationStage.Ran, StageResult.Failed, Same(still, original)
                    ? $"The copy still stopped with {Name(still)}."
                    : $"The copy stopped with a different error: {Name(still)}.");
            }

            if (checkingOutput && mismatched is null && OutputComparison.Compare(OutputComparison.Printed(run), want.ExpectedOutput) is { } off)
            {
                mismatched = off.Describe();
            }
        }

        var ran = sofar.With(VerificationStage.Ran, StageResult.Passed,
            original is null
                ? "The copy ran to the end without failing."
                : $"The copy ran and did not stop with {Name(original)} the way it did before.");

        if (!checkingOutput)
        {
            return ran.With(VerificationStage.MatchedExpectedOutput, StageResult.Skipped, "You did not say what it should print.");
        }

        return mismatched is null
            ? ran.With(VerificationStage.MatchedExpectedOutput, StageResult.Passed, "It printed what you said it should.")
            : ran.With(VerificationStage.MatchedExpectedOutput, StageResult.Failed, mismatched);
    }

    /// <summary>Whether this is the failure the fix was meant to stop, judged by what the runtime called it.</summary>
    private static bool Same(ParsedError now, ParsedError? before) =>
        before is not null &&
        string.Equals(now.ShortExceptionType ?? now.ErrorCode, before.ShortExceptionType ?? before.ErrorCode, StringComparison.Ordinal);

    private static string Name(ParsedError error) =>
        error.ShortExceptionType ?? error.ErrorCode ?? error.Message ?? "the same error";
}
