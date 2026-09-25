using System.IO;
using System.Windows;
using Microsoft.Win32;
using FixFinder.Core.Engine;

namespace FixFinder.Gui;

/// <summary>
/// Swaps the window's colours without restarting it.
/// </summary>
/// <remarks>
/// Every colour lives in one of two dictionaries with the same keys in both, and changing theme replaces the one that
/// is merged in. That only reaches the window because the XAML asks for brushes with DynamicResource: a StaticResource
/// is looked up once when the control is built and would keep the colour it was born with.
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
            Source = new Uri($"pack://application:,,,/Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Absolute),
        };

        var merged = Application.Current.Resources.MergedDictionaries;

        // Added before the old one goes, so nothing is ever asked for a brush that neither dictionary is providing.
        merged.Add(wanted);
        if (_applied is not null) merged.Remove(_applied);

        _applied = wanted;
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
