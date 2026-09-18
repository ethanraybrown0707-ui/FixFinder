using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace FixFinder.Gui;

/// <summary>Shows text in a TextBlock with anything between backticks in the code font, the way the findings write names from the code.</summary>
public static class InlineCode
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(InlineCode), new PropertyMetadata("", OnTextChanged));

    public static string GetText(DependencyObject element) => (string)element.GetValue(TextProperty);

    public static void SetText(DependencyObject element, string value) => element.SetValue(TextProperty, value);

    private static void OnTextChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not TextBlock block) return;

        block.Inlines.Clear();

        var parts = ((string?)e.NewValue ?? "").Split('`');
        var codeFont = (FontFamily)Application.Current.FindResource("CodeFont");
        var codeText = (Brush)Application.Current.FindResource("TextBrush");

        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) continue;

            block.Inlines.Add(i % 2 == 1 && parts.Length % 2 == 1
                ? new Run(parts[i]) { FontFamily = codeFont, Foreground = codeText, FontSize = block.FontSize - 0.5 }
                : new Run(parts[i]));
        }
    }
}
