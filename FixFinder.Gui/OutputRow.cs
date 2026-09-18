namespace FixFinder.Gui;

/// <summary>One line of the target's captured output, shaped for the "Captured output" ListBox.</summary>
public sealed class OutputRow
{
    public required string DisplayLine { get; init; }

    public required bool IsError { get; init; }
}
