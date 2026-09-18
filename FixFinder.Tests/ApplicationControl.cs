using FixFinder.Core.Engine;
using FixFinder.Core.Execution;

namespace FixFinder.Tests;

/// <summary>Whether Windows refused to start a program a live case needed - which says nothing about FixFinder.</summary>
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
