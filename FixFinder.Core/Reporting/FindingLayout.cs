using FixFinder.Core.Checking;

namespace FixFinder.Core.Reporting;

/// <summary>One line of the list under "Why FixFinder thinks this": what was seen, marked as it turned out.</summary>
public sealed record Reason(StageResult Mark, string Text);

/// <summary>How sure FixFinder is that a fix is the change to make, and what that rests on.</summary>
public sealed record FixCertainty(Confidence Level, string Because)
{
    public string Labelled => $"{Level}. {char.ToUpperInvariant(Because[0])}{Because[1..]}.";
}

/// <summary>
/// A finding in the order it is read, the same in the window, in a saved report and on the command line: what happened, why
/// it happened in this program, the most likely fix and the exact code to change, what FixFinder saw that makes it say so,
/// and only then what this kind of mistake is in general.
/// </summary>
/// <remarks>
/// This program first, never the language first: someone whose program stopped wants to know which of their lines did it
/// and what to change there, and an explanation of what TypeError means in Python is help with that, not the answer to it.
/// </remarks>
public static class FindingLayout
{
    /// <summary>Where it went wrong and where it showed, in one line under the title - or null when they are not known apart.</summary>
    public static string? Summary(Finding finding) => finding.RootCause?.Summary;

    /// <summary>
    /// Why it happened in this program: what the check that found it said of this code, or what the compiler or the run said
    /// of this line - and the explanation itself only for a finding that has nothing more particular to say.
    /// </summary>
    public static string Cause(Finding finding)
    {
        if (finding.Found is { Length: > 0 } found) return found;
        if (finding.Error is { } error && Said(error) is { } said)
        {
            return finding.Kind == FindingKind.Syntax
                ? $"The compiler refused the code {Where(finding)}: {said}"
                : $"The program stopped {Where(finding)} with {said}";
        }

        return finding.Explanation;
    }

    /// <summary>
    /// What this kind of mistake is, in general - the explanation without the part <see cref="Cause"/> already said, or
    /// nothing when the explanation is all about this program.
    /// </summary>
    public static string LearnMore(Finding finding)
    {
        var cause = Cause(finding);
        if (ReferenceEquals(cause, finding.Explanation) || cause == finding.Explanation) return "";

        return finding.Explanation.StartsWith(cause, StringComparison.Ordinal) ? finding.Explanation[cause.Length..].Trim() : finding.Explanation;
    }

    /// <summary>
    /// What to do. Where a run traced the value it stopped on and the only change worked out makes the line skip the value,
    /// what to do is what the trace says - the change only stops the failure.
    /// </summary>
    public static string MostLikelyFix(Finding finding) =>
        finding.RootCause is { } cause && (finding.Fix is null || finding.Fix.OnlyStopsTheFailure) ? cause.WhatToDo : finding.SuggestedFix;

    /// <summary>
    /// How sure FixFinder is of the fix - which is not how sure it is of the finding: a crash is certain, and the change that
    /// stops it can still be a guess. Read off how far the change was taken: printing what it should beats running without
    /// the error, which beats building, which beats not being tried. Null when there is no fix.
    /// </summary>
    public static FixCertainty? FixConfidence(Finding finding)
    {
        if (finding.Fix is not { } fix) return null;

        var verified = finding.Verified;
        var certainty = verified.ResultOf(VerificationStage.MatchedExpectedOutput) switch
        {
            StageResult.Passed => new FixCertainty(Confidence.Certain, "a copy with the change printed what you said it should"),
            StageResult.Failed => new FixCertainty(Confidence.Possible, "a copy with the change ran, but did not print what you said it should"),
            _ => verified.ResultOf(VerificationStage.Ran) switch
            {
                StageResult.Passed => new FixCertainty(Confidence.Likely, finding.Error is not null
                    ? "a copy with the change ran without the error"
                    : "a copy with the change ran to the end"),
                StageResult.Failed => new FixCertainty(Confidence.Possible, "a copy with the change was run, and it still failed"),
                _ => verified.ResultOf(VerificationStage.Compiled) switch
                {
                    StageResult.Passed => new FixCertainty(Confidence.Possible, "a copy with the change builds, but was not run"),
                    StageResult.Failed => new FixCertainty(Confidence.Possible, "a copy with the change did not build"),
                    _ => new FixCertainty(Confidence.Possible, "the change was not tried"),
                },
            },
        };

        // A change that only stops the failure is never more than possible, however well it ran: what should happen instead
        // is the program writer's to decide.
        return fix.OnlyStopsTheFailure && certainty.Level != Confidence.Possible
            ? new FixCertainty(Confidence.Possible, $"{certainty.Because}, but the change only stops the failure: what should happen instead is yours to decide")
            : certainty;
    }

    /// <summary>
    /// What FixFinder saw that makes it say so: what the run that traced the value recorded, or what stands behind the finding;
    /// and how each stage of trying the fix went.
    /// </summary>
    public static IReadOnlyList<Reason> WhyItThinksThis(Finding finding)
    {
        var reasons = finding.RootCause is { Evidence.Count: > 0 } cause
            ? cause.Evidence.Select(seen => new Reason(StageResult.Passed, seen)).ToList()
            : [new Reason(StageResult.Passed, Evidence.For(finding))];

        if (finding.Fix is not null && finding.Verified.WasTested)
        {
            reasons.AddRange(finding.Verified.Steps
                .Where(step => step.Result != StageResult.Skipped)
                .OrderBy(step => step.Stage)
                .Select(step => new Reason(step.Result, step.Detail)));
        }

        return reasons;
    }

    private static string Where(Finding finding) => finding.Line is null && finding.InNotebook is null ? "" : FindingText.Where(finding);

    /// <summary>The error as the language said it - its type and message on one line - or null when it said nothing readable.</summary>
    private static string? Said(Parsing.ParsedError error)
    {
        var message = (error.Message ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
        var type = error.ShortExceptionType ?? error.ErrorCode;
        if (message.Length == 0) return type is { Length: > 0 } ? $"{type}." : null;

        if (message.Length > 300) message = message[..300].TrimEnd() + "...";
        var said = type is { Length: > 0 } && !message.StartsWith(type, StringComparison.Ordinal) ? $"{type}: {message}" : message;
        return said.EndsWith('.') ? said : said + ".";
    }
}
