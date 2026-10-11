using FixFinder.Core.Checking;

namespace FixFinder.Core.Teaching;

/// <summary>
/// More explanation for one finding, in the same plain words as the rest of it: the steps that put it right, what the
/// language said about it and how to read that, what the programming words in it mean, and a habit that stops it
/// happening again. The window, a saved report and the command line all show the same.
/// </summary>
public sealed record FurtherExplanation(
    IReadOnlyList<string> Steps,
    MessageRead? Message,
    IReadOnlyList<GlossaryEntry> Words,
    string NextTime)
{
    public static FurtherExplanation For(Finding finding) => new(
        StepByStep.For(finding),
        MessageReading.For(finding),
        Glossary.In(finding),
        Habits.For(ConceptMap.Of(finding)));
}
