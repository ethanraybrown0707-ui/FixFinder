using FixFinder.Core.Analysis.Dynamic;

namespace FixFinder.Gui;

/// <summary>One input tried, as the window shows it: what was typed, and what the program printed - and what stopped it.</summary>
public sealed class TriedInputRow(InputTried tried)
{
    public string TypedSaid => tried.TypedSaid;

    public string PrintedText =>
        string.Join(Environment.NewLine, tried.Printed.Concat(tried.StoppedWith is { } stopped ? [$"It stopped: {stopped}"] : []));
}
