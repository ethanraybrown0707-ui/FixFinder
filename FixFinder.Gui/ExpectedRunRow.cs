namespace FixFinder.Gui;

/// <summary>One more run to check a program's logic with: what to type in, and what it should print.</summary>
public sealed class ExpectedRunRow
{
    public string Input { get; set; } = "";

    public string Expected { get; set; } = "";
}
