using FixFinder.Core.Display;

namespace FixFinder.Tests.Display;

public class WindowFitTests
{
    /// <summary>
    /// A 2880 x 1800 laptop screen at 200% scaling, as Windows lays windows out on it: 1440 x 900, less a 48-high taskbar.
    /// It is the screen FixFinder's main window ran off the top and the bottom of, at the 880 it was designed to be.
    /// </summary>
    private static readonly UsableArea LaptopAtDoubleScaling = new(0, 0, 1440, 852);

    [Fact]
    public void A_window_taller_than_the_screen_is_shrunk_to_fit_and_centred()
    {
        var placement = WindowFit.Fit(LaptopAtDoubleScaling, wantedWidth: 1260, wantedHeight: 880, smallestWidth: 1000, smallestHeight: 640);

        Assert.True(placement.FitsInside(LaptopAtDoubleScaling));
        Assert.Equal(852 * WindowFit.MostOfTheScreen, placement.Height, precision: 6);
        Assert.Equal(1260, placement.Width);
        Assert.Equal((852 - placement.Height) / 2, placement.Top, precision: 6);
        Assert.Equal((1440 - 1260) / 2.0, placement.Left, precision: 6);
    }

    [Fact]
    public void A_window_the_screen_has_room_for_keeps_the_size_it_was_designed_at()
    {
        var desktop = new UsableArea(0, 0, 1920, 1032);

        var placement = WindowFit.Fit(desktop, 1260, 880, 1000, 640);

        Assert.Equal(1260, placement.Width);
        Assert.Equal(880, placement.Height);
        Assert.Equal(1000, placement.SmallestWidth);
        Assert.Equal(640, placement.SmallestHeight);
    }

    [Fact]
    public void The_smallest_size_shrinks_with_the_window_on_a_screen_smaller_than_it()
    {
        var small = new UsableArea(0, 0, 900, 560);

        var placement = WindowFit.Fit(small, 1260, 880, 1000, 640);

        Assert.True(placement.FitsInside(small));
        Assert.True(placement.SmallestWidth <= placement.Width);
        Assert.True(placement.SmallestHeight <= placement.Height);
    }

    [Fact]
    public void A_screen_to_the_left_of_the_main_one_is_measured_from_its_own_corner()
    {
        var leftScreen = new UsableArea(-1280, 0, 1280, 984);

        var placement = WindowFit.Fit(leftScreen, 700, 820, 400, 480);

        Assert.True(placement.FitsInside(leftScreen));
        Assert.True(placement.Left < 0);
    }

    [Fact]
    public void A_window_that_already_fits_is_left_where_it_is()
    {
        var placed = new WindowPlacement(100, 40, 800, 600, 400, 300);

        Assert.Equal(placed, WindowFit.KeepInside(LaptopAtDoubleScaling, placed));
    }

    [Fact]
    public void A_window_left_hanging_off_a_screen_that_got_smaller_is_moved_back_on_and_shrunk()
    {
        var placed = new WindowPlacement(500, 200, 1260, 880, 1000, 640);

        var kept = WindowFit.KeepInside(LaptopAtDoubleScaling, placed);

        Assert.True(kept.FitsInside(LaptopAtDoubleScaling));
        Assert.Equal(852, kept.Height);
        Assert.Equal(1440 - 1260, kept.Left);
    }
}
