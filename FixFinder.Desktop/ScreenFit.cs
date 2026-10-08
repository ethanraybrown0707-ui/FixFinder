using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using FixFinder.Core.Display;
using Microsoft.Win32;

namespace FixFinder.Desktop;

/// <summary>
/// Makes a window fit the screen it opens on - all of it, title bar to bottom edge - and keeps it on that screen when the
/// screen's size or scaling changes while it is open.
/// </summary>
/// <remarks>
/// A window's size in its XAML is the size it reads best at. Before it is shown, that size is checked against the part of
/// the screen windows may use - the screen the pointer is on, as that is where the person is working - and shrunk to fit
/// where it is bigger; once it is up, it is checked again against the screen it actually opened on, which can be another
/// one when there are several. Windows reports screens in pixels, and a window is measured in units of 1/96 inch, so each
/// screen's area is divided by that screen's own scaling: at 200%, 2880 pixels are 1440 units.
/// </remarks>
public static class ScreenFit
{
    /// <summary>Fits the window to the screen now, and again once it is shown and whenever the screens change.</summary>
    public static void Apply(Window window)
    {
        if (UsableAreaUnderThePointer() is { } area)
        {
            var placement = WindowFit.Fit(area, Wanted(window.Width, 800), Wanted(window.Height, 600), Wanted(window.MinWidth, 0), Wanted(window.MinHeight, 0));
            window.Width = placement.Width;
            window.Height = placement.Height;
            window.MinWidth = placement.SmallestWidth;
            window.MinHeight = placement.SmallestHeight;
        }

        void ScreensChanged(object? sender, EventArgs e) => window.Dispatcher.BeginInvoke(() => KeepOnItsScreen(window));

        window.Loaded += (_, _) => KeepOnItsScreen(window);
        SystemEvents.DisplaySettingsChanged += ScreensChanged;
        window.Closed += (_, _) => SystemEvents.DisplaySettingsChanged -= ScreensChanged;
    }

    /// <summary>A size the XAML gave, or the stand-in when it gave none - a window sized to its content has no width of its own.</summary>
    private static double Wanted(double size, double standIn) => double.IsNaN(size) || double.IsInfinity(size) ? standIn : size;

    /// <summary>
    /// Moves the window back onto the screen it is on, shrinking it if it has to, when any of it is off that screen. A
    /// window that is maximised or minimised is left to Windows, which already keeps it to the screen.
    /// </summary>
    private static void KeepOnItsScreen(Window window)
    {
        if (window.WindowState != WindowState.Normal || UsableAreaOf(window) is not { } area) return;

        var current = new WindowPlacement(window.Left, window.Top, window.ActualWidth, window.ActualHeight, window.MinWidth, window.MinHeight);
        var kept = WindowFit.KeepInside(area, current);
        if (kept == current) return;

        window.MinWidth = kept.SmallestWidth;
        window.MinHeight = kept.SmallestHeight;
        window.Width = kept.Width;
        window.Height = kept.Height;
        window.Left = kept.Left;
        window.Top = kept.Top;
    }

    /// <summary>The usable part of the screen the window is on, in the window's own units; null before it has a handle.</summary>
    private static UsableArea? UsableAreaOf(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return null;

        var monitor = MonitorFromWindow(handle, NearestMonitor);
        var scale = VisualTreeHelper.GetDpi(window);

        return WorkAreaOf(monitor) is { } pixels ? InUnits(pixels, scale.DpiScaleX, scale.DpiScaleY) : null;
    }

    /// <summary>
    /// The usable part of the screen the pointer is on, in window units - or, if Windows cannot say which screen that is
    /// or how it is scaled, the main screen's, which WPF always knows.
    /// </summary>
    private static UsableArea? UsableAreaUnderThePointer()
    {
        if (GetCursorPos(out var pointer))
        {
            var monitor = MonitorFromPoint(pointer, NearestMonitor);

            if (WorkAreaOf(monitor) is { } pixels && GetDpiForMonitor(monitor, EffectiveDpi, out var dpiAcross, out var dpiDown) == 0 && dpiAcross > 0 && dpiDown > 0)
                return InUnits(pixels, dpiAcross / 96.0, dpiDown / 96.0);
        }

        var main = SystemParameters.WorkArea;
        return main.Width > 0 && main.Height > 0 ? new UsableArea(main.Left, main.Top, main.Width, main.Height) : null;
    }

    private static UsableArea InUnits(Rect pixels, double scaleAcross, double scaleDown) =>
        new(pixels.Left / scaleAcross, pixels.Top / scaleDown, pixels.Width / scaleAcross, pixels.Height / scaleDown);

    /// <summary>A screen's work area - the screen less the taskbar and anything docked to its edges - in pixels.</summary>
    private static Rect? WorkAreaOf(IntPtr monitor)
    {
        if (monitor == IntPtr.Zero) return null;

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return null;

        var work = info.WorkArea;
        return new Rect(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top);
    }

    private const uint NearestMonitor = 2;
    private const int EffectiveDpi = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenPoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public ScreenRectangle Whole;
        public ScreenRectangle WorkArea;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out ScreenPoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(ScreenPoint point, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiAcross, out uint dpiDown);
}
