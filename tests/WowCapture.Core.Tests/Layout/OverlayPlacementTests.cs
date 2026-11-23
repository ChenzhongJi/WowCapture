using WowCapture.Core.Abstractions;
using WowCapture.Core.Geometry;
using WowCapture.Core.Layout;
using Xunit;

namespace WowCapture.Core.Tests.Layout;

public class OverlayPlacementTests
{
    private static MonitorInfo Monitor(int x, int y, int width, int height, uint Dpi) =>
        new(0, new ScreenRect(x, y, width, height), new ScreenRect(x, y, width, height), Dpi, IsPrimary: false);

    [Fact]
    public void For_Dip尺寸等于物理尺寸除以缩放()
    {
        var placement = OverlayPlacement.For(Monitor(0, 0, 2560, 1440, Dpi: 120));

        Assert.Equal(1.25, placement.Scale, 5);
        Assert.Equal(2048, placement.WidthDip, 5);
        Assert.Equal(1152, placement.HeightDip, 5);
    }

    [Fact]
    public void For_非正DPI按100处理()
    {
        var placement = OverlayPlacement.For(Monitor(0, 0, 1920, 1080, Dpi: 0));

        Assert.Equal(1.0, placement.Scale, 5);
        Assert.Equal(1920, placement.WidthDip, 5);
    }

    [Fact]
    public void ToPhysical_负坐标显示器同样成立()
    {
        var placement = OverlayPlacement.For(Monitor(-1920, 0, 1920, 1080, Dpi: 96));

        var physical = placement.ToPhysical(5, 10);

        Assert.Equal(new ScreenPoint(-1915, 10), physical);
        Assert.Equal(5.0, placement.ToDip(physical).X, 5);
        Assert.Equal(10.0, placement.ToDip(physical).Y, 5);
    }

    [Theory]
    [InlineData(96u)]    // 100%
    [InlineData(120u)]   // 125%
    [InlineData(144u)]   // 150%
    [InlineData(168u)]   // 175%
    [InlineData(192u)]  // 200%
    public void VerifyRoundTrip_整数对齐点常见DPI全零失败(uint dpi)
    {
        var monitor = Monitor(-1920, -1080, 3840, 2160, dpi);
        var placement = OverlayPlacement.For(monitor);

        // 语义是"物理↔DIP 完全回到原点"（容差 1e-4），因此采样点必须选
        // dip × scale 为整数的格点：4 的倍数对 125%/150%/175%/200% 全部成立。
        var points = new (double X, double Y)[]
        {
            (0, 0),
            (4, 8),
            (1600, 900),
        };

        Assert.Equal(0, placement.VerifyRoundTrip(points));
    }
}
