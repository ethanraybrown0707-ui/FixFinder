using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using FixFinder.Core.Checking;

namespace FixFinder.Gui;

/// <summary>
/// Shows one line of a before-and-after with the part that actually changed picked out.
/// </summary>
/// <remarks>
/// Colouring a whole line says "something on this line is different" and leaves the reader to find it, which on a line
/// like a for header is one character in forty. The line is written in three pieces - what is the same before the
/// change, the change, and what is the same after it - and only the middle piece is marked.
/// </remarks>
public static class ChangedCode
{
    public static readonly DependencyProperty LineProperty = DependencyProperty.RegisterAttached(
        "Line", typeof(ChangeLine), typeof(ChangedCode), new PropertyMetadata(null, OnLineChanged));

    public static ChangeLine? GetLine(DependencyObject element) => (ChangeLine?)element.GetValue(LineProperty);

    public static void SetLine(DependencyObject element, ChangeLine? value) => element.SetValue(LineProperty, value);

    private static void OnLineChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not TextBlock block) return;

        block.Inlines.Clear();

        if (e.NewValue is not ChangeLine line) return;

        if (!line.HasHighlight)
        {
            block.Inlines.Add(new Run(line.Text));
            return;
        }

        var marked = line.Kind == ChangeKind.Removed ? "DiffRemovedMarkBrush" : "DiffAddedMarkBrush";

        block.Inlines.Add(new Run(line.Before));
        block.Inlines.Add(new Run(line.Changed)
        {
            Background = (Brush)Application.Current.FindResource(marked),
            FontWeight = FontWeights.SemiBold,
        });
        block.Inlines.Add(new Run(line.After));
    }
}
