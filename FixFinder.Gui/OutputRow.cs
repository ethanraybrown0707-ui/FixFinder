namespace FixFinder.Gui;

/// <summary>
/// One line of the target's captured output, shaped for the "Captured output" ListBox.
/// </summary>
/// <remarks>
/// This is a view type on purpose. The engine's own <c>CapturedLine</c> carries a sequence
/// number, a stream kind and an elapsed time; the window only ever needs a string to draw and
/// a bool to colour by, and keeping those separate stops XAML binding concerns leaking back
/// into FixFinder.Core - which cannot reference WPF at all.
/// </remarks>
public sealed class OutputRow
{
    public required string DisplayLine { get; init; }

    /// <summary>True for stderr, which the DataTrigger in MainWindow.xaml draws in red.</summary>
    public required bool IsError { get; init; }
}
