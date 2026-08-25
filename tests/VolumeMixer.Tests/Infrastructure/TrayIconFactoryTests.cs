using System.Drawing;
using VolumeMixer.Infrastructure;

namespace VolumeMixer.Tests.Infrastructure;

public class TrayIconFactoryTests
{
    [Fact]
    public void Create_returns_16x16_icon()
    {
        using var icon = TrayIconFactory.Create();

        Assert.NotNull(icon);
        Assert.Equal(16, icon.Width);
        Assert.Equal(16, icon.Height);
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
