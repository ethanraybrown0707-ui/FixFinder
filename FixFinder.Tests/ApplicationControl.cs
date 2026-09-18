using FixFinder.Core.Engine;
using FixFinder.Core.Execution;

namespace FixFinder.Tests;

/// <summary>Whether Windows refused to start a program a live case needed - which says nothing about FixFinder.</summary>
/// <remarks>
/// A machine enforcing Application Control - Smart App Control, or a WDAC policy - can refuse a program built a moment ago,
/// and does so unevenly: the same case passes on one run and is refused on the next, most often when many are built at once.
/// A refused launch leaves nothing to judge a rule by, so a case that hits one returns without judging, the same way a case
/// returns when its toolchain is missing - and a return looks like a pass. The refusal is named in the outcome, in its words,
/// so it is never mistaken for anything else: a program that ran and failed is still a failure.
/// </remarks>
internal static class ApplicationControl
{
    public static bool Refused(SessionOutcome outcome) =>
        Mentions(outcome.Detail) ||
        outcome.Warnings.Any(Mentions) ||
        (outcome.Run is { } run && Refused(run));

    public static bool Refused(TargetRunResult run) =>
        Mentions(run.LaunchError) || run.Lines.Any(line => Mentions(line.Text));

    private static bool Mentions(string? text) =>
        text?.Contains("Application Control", StringComparison.OrdinalIgnoreCase) == true;
}
