namespace FixFinder.Core.Checking;

/// <summary>
/// Why FixFinder is as sure as it says it is, for one finding.
/// </summary>
/// <remarks>
/// The confidence on a finding is a word - certain, likely, possible - and a word is a claim. What makes it worth
/// anything is what stands behind it, and that differs completely from one finding to the next: a compiler refusing to
/// build the file is not the same kind of certainty as a pattern that is usually a mistake, though both could be
/// written "Certain" and left there.
/// <para>
/// Everything said here is read off what actually happened. Where nothing established a finding beyond the rule that
/// noticed it, that is what it says, rather than dressing a pattern match up as proof.
/// </para>
/// </remarks>
public static class Evidence
{
    /// <summary>What stands behind this finding, in one sentence, strongest evidence first.</summary>
    public static string For(Finding finding)
    {
        var parts = new List<string>();

        // Strongest first: something outside FixFinder said so, or the program itself did.
        if (finding.Confirmation is { Length: > 0 })
        {
            parts.Add("the program was run with values that should break this line, and it broke");
        }
        else if (finding.Error is { } error && finding.Kind == FindingKind.Syntax)
        {
            parts.Add(error.ErrorCode is { Length: > 0 } code
                ? $"the compiler refused the file, with {code}"
                : "the compiler refused the file");
        }
        else if (finding.Error is not null)
        {
            parts.Add("the program was run and stopped here");
        }
        else if (finding.RuleId == "wrong-output")
        {
            parts.Add("the program ran to the end and printed something other than what you said it should");
        }
        else if (finding.RuleId == "stopped-before-expected-output")
        {
            parts.Add("the program was run with what you gave it, and stopped before it printed what you said it should");
        }
        else if (finding.FoundBy is { Length: > 0 } technique)
        {
            parts.Add($"following the values through the code - {technique} - showed it");
        }
        else
        {
            parts.Add("the code is written in a way that is usually a mistake");
        }

        if (finding.Witness is { Length: > 0 } witness) parts.Add($"it fails when {witness}");

        if (finding.Slice is { Count: > 1 } slice) parts.Add($"{slice.Count} lines decide the value it goes wrong on");

        if (finding.Verified.IsVerified) parts.Add("and the fix was run and the failure did not come back");
        else if (finding.Verified.ResultOf(VerificationStage.Compiled) == StageResult.Passed) parts.Add("and a copy with the fix compiles");

        return Sentence(string.Join("; ", parts));
    }

    /// <summary>
    /// What the confidence word means for this finding rather than in general, for the tooltip beside it.
    /// </summary>
    public static string Behind(Finding finding) => $"{finding.Confidence}: {For(finding)}";

    /// <summary>
    /// Where the confidence comes from, in a few words to go beside it - read off what happened, as <see cref="For"/> is,
    /// strongest first: a run that broke as the analysis said it would; the program crashing, or printing the wrong thing,
    /// when it ran; the compiler refusing it; an input that fails, worked out from the code - which can still depend on what
    /// is typed in, so it is not called proof; the values followed through the code; or the way the code is written alone.
    /// </summary>
    public static string Source(Finding finding) => finding switch
    {
        { Confirmation.Length: > 0 } => "run reproduced it",
        { Error: not null, Kind: FindingKind.Syntax } => "compiler said so",
        { Error: not null } or { RuleId: "wrong-output" or "stopped-before-expected-output" } => "seen when it ran",
        { Witness.Length: > 0 } => "failing input worked out",
        { FoundBy.Length: > 0 } => "values followed through the code",
        _ => "pattern only",
    };

    /// <summary>The confidence word with where it comes from: "Certain (seen when it ran)".</summary>
    public static string Labelled(Finding finding) => $"{finding.Confidence} ({Source(finding)})";

    private static string Sentence(string text) =>
        text.Length == 0 ? "" : char.ToUpperInvariant(text[0]) + text[1..] + (text.EndsWith('.') ? "" : ".");
}
