using System.Drawing;
using VolumeMixer.Infrastructure;

namespace VolumeMixer.Tests.Infrastructure;

public class TrayIconFactoryTests
{
    [Fact]
    public void Create_returns_square_small_icon_for_current_dpi()
    {
        using var icon = TrayIconFactory.Create();

        Assert.NotNull(icon);
        Assert.Equal(icon.Width, icon.Height);
        Assert.InRange(icon.Width, 16, 64);
    }

    [Fact]
    public void Create_muted_draws_different_pixels()
    {
        using var normal = TrayIconFactory.Create(muted: false).ToBitmap();
        using var muted = TrayIconFactory.Create(muted: true).ToBitmap();
        var differs = false;
        for (var x = 0; x < normal.Width && !differs; x++)
            for (var y = 0; y < normal.Height && !differs; y++)
                differs = normal.GetPixel(x, y) != muted.GetPixel(x, y);
        Assert.True(differs);
    }

    [Fact]
    public void Create_multiple_calls_return_distinct_disposable_icons()
    {
        using var icon1 = TrayIconFactory.Create();
        using var icon2 = TrayIconFactory.Create();

        Assert.NotNull(icon1);
        Assert.NotNull(icon2);
        // Each call produces a distinct Icon instance with its own handle
        Assert.NotSame(icon1, icon2);
    }

    [Fact]
    public void Created_icon_can_be_used_as_handle()
    {
        using var icon = TrayIconFactory.Create();

        // The handle must be non-zero and usable
        Assert.NotEqual(IntPtr.Zero, icon.Handle);
    }
}
