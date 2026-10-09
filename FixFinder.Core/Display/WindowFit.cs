namespace FixFinder.Core.Display;

/// <summary>
/// The part of a screen windows may use - the whole screen less the taskbar - in the units a window is measured in.
/// </summary>
public readonly record struct UsableArea(double Left, double Top, double Width, double Height);

/// <summary>Where a window goes and how big it is: its size, its place, and the smallest it may be made.</summary>
public readonly record struct WindowPlacement(double Left, double Top, double Width, double Height, double SmallestWidth, double SmallestHeight)
{
    /// <summary>Whether the whole of the window is inside the area, so none of it is off the screen.</summary>
    public bool FitsInside(UsableArea area) =>
        Left >= area.Left && Top >= area.Top && Left + Width <= area.Left + area.Width && Top + Height <= area.Top + area.Height;
}

/// <summary>
/// Works out how big a window can be on the screen it opens on, so all of it - title bar to bottom edge - is on the screen.
/// </summary>
/// <remarks>
/// A window is designed at the size it reads best at, but a laptop screen run at a high scaling can be smaller than that
/// in the units Windows lays windows out in: a 2880 x 1800 screen at 200% is 1440 x 900 of them, and the taskbar takes
/// some of that. A window taller than what is left is centred over it and runs off the top and the bottom. So a window is
/// given the size it is designed at where the screen has room for it with a margin round it, and otherwise as much as the
/// screen has less that margin; the smallest size it may be dragged to shrinks with it, so the screen can always hold it.
/// </remarks>
public static class WindowFit
{
    /// <summary>
    /// The most of the usable area's width or height a window is given, so a little of the desktop shows round it and its
    /// edges can be found.
    /// </summary>
    public const double MostOfTheScreen = 0.94;

    /// <summary>
    /// The size and place for a window that wants a certain size, on a screen with this usable area: the size it wants
    /// where there is room, as much as there is where there is not, and centred in the area either way.
    /// </summary>
    public static WindowPlacement Fit(UsableArea area, double wantedWidth, double wantedHeight, double smallestWidth, double smallestHeight)
    {
        var width = Room(area.Width, wantedWidth);
        var height = Room(area.Height, wantedHeight);

        return new WindowPlacement(
            Left: area.Left + (area.Width - width) / 2,
            Top: area.Top + (area.Height - height) / 2,
            Width: width,
            Height: height,
            SmallestWidth: Math.Min(smallestWidth, width),
            SmallestHeight: Math.Min(smallestHeight, height));
    }

    /// <summary>
    /// A window already on screen, moved and if need be shrunk so all of it is inside the area - for a screen whose size
    /// or scaling changed under it. A window that already fits is left exactly where it is.
    /// </summary>
    public static WindowPlacement KeepInside(UsableArea area, WindowPlacement current)
    {
        if (current.FitsInside(area)) return current;

        var width = Math.Min(current.Width, area.Width);
        var height = Math.Min(current.Height, area.Height);

        return current with
        {
            Width = width,
            Height = height,
            Left = Math.Clamp(current.Left, area.Left, area.Left + area.Width - width),
            Top = Math.Clamp(current.Top, area.Top, area.Top + area.Height - height),
            SmallestWidth = Math.Min(current.SmallestWidth, width),
            SmallestHeight = Math.Min(current.SmallestHeight, height),
        };
    }

    /// <summary>How much of one side of the area a window may have: what it wants, up to the area less the margin round it.</summary>
    private static double Room(double available, double wanted) => Math.Min(wanted, available * MostOfTheScreen);
}
