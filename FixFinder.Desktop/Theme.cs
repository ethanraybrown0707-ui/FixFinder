using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using FixFinder.Core.Engine;

namespace FixFinder.Desktop;

/// <summary>
/// Swaps the windows' colours without restarting the program.
/// </summary>
/// <remarks>
/// Every colour lives in one of two dictionaries with the same keys in both, and changing theme replaces the one that
/// is merged in. That only reaches the window because the XAML asks for brushes with DynamicResource: a StaticResource
/// is looked up once when the control is built and would keep the colour it was born with. The dictionaries live in this
/// library, so FixFinder and FixFinder Learn are painted from the same ones.
/// </remarks>
public static class Theme
{
    private const string Personalise = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>Whichever dictionary is merged in now, so the next change knows what to take out.</summary>
    private static ResourceDictionary? _applied;

    public static void Apply(AppearanceChoice choice)
    {
        var dark = choice switch
        {
            AppearanceChoice.Dark => true,
            AppearanceChoice.Light => false,
            _ => WindowsPrefersDark(),
        };

        var wanted = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/FixFinder.Desktop;component/Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Absolute),
        };

        var merged = Application.Current.Resources.MergedDictionaries;

        // Added before the old one goes, so nothing is ever asked for a brush that neither dictionary is providing.
        merged.Add(wanted);
        if (_applied is not null) merged.Remove(_applied);

        _applied = wanted;
        _dark = dark;

        foreach (Window window in Application.Current.Windows) PaintTitleBar(window);
    }

    /// <summary>Whether the theme applied now is the dark one.</summary>
    private static bool _dark;

    /// <summary>
    /// Has a window's title bar painted in the theme's colours - Windows paints it white otherwise, whatever the window
    /// below it is - from before it is first shown, and again whenever the theme changes.
    /// </summary>
    public static void FollowInTitleBar(Window window) => window.SourceInitialized += (_, _) => PaintTitleBar(window);

    private const int UseDarkMode = 20;
    private const int BorderColour = 34;
    private const int CaptionColour = 35;
    private const int CaptionTextColour = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    /// <summary>
    /// The title bar in the colour of the header band under it - the cards' - so the two run together, its title in the
    /// text's, and the edge in the cards' edge's: on Windows 11, which takes a colour for each. Windows 10 takes only dark
    /// or light, and ignores the rest.
    /// </summary>
    private static void PaintTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        var dark = _dark ? 1 : 0;
        DwmSetWindowAttribute(handle, UseDarkMode, ref dark, sizeof(int));

        foreach (var (attribute, brush) in new[] { (CaptionColour, "CardBrush"), (CaptionTextColour, "TextBrush"), (BorderColour, "CardBorderBrush") })
        {
            if (Application.Current.TryFindResource(brush) is not SolidColorBrush { Color: var colour }) continue;

            // Windows takes a colour as red, green and blue in its lowest three bytes, red lowest.
            var asWindowsTakesIt = colour.R | colour.G << 8 | colour.B << 16;
            DwmSetWindowAttribute(handle, attribute, ref asWindowsTakesIt, sizeof(int));
        }
    }

    /// <summary>
    /// Whether Windows itself is set to dark for applications. A missing or unreadable value means light, which is
    /// what Windows does with it too.
    /// </summary>
    private static bool WindowsPrefersDark()
    {
        try
        {
            return Registry.GetValue(Personalise, "AppsUseLightTheme", 1) is int light && light == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
