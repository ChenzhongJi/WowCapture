using WowCapture.Core.Geometry;
using WowCapture.Core.Imaging;
using Xunit;

namespace WowCapture.Core.Tests.Imaging;

public class CapturedFrameTests
{
    private static readonly ScreenRect Bounds = new(10, 20, 100, 50);

    [Fact]
    public void 构造_缓冲长度必须与矩形匹配()
    {
        Assert.Throws<ArgumentException>(
            () => new CapturedFrame(new byte[100], Bounds));
        Assert.Throws<ArgumentNullException>(
            () => new CapturedFrame(null!, Bounds));

        var frame = new CapturedFrame(new byte[100 * 50 * 4], Bounds);

        Assert.Equal(100, frame.Width);
        Assert.Equal(50, frame.Height);
        Assert.Equal(400, frame.Stride);
    }

    [Fact]
    public void PixelAt_框内返回四字节_越界返回空()
    {
        var frame = new CapturedFrame(new byte[100 * 50 * 4], Bounds);

        Assert.Equal(4, frame.PixelAt(10, 20).Length);
        Assert.Equal(4, frame.PixelAt(109, 69).Length);

        // 右/下边界是开区间。
        Assert.True(frame.PixelAt(110, 20).IsEmpty);
        Assert.True(frame.PixelAt(10, 70).IsEmpty);
        Assert.True(frame.PixelAt(9, 20).IsEmpty);
    }

    [Fact]
    public void PixelAt_偏移按虚拟屏绝对坐标计算()
    {
        var pixels = new byte[100 * 50 * 4];
        pixels[0] = 0xAB;
        pixels[3] = 0xCD;
        var frame = new CapturedFrame(pixels, Bounds);

        var span = frame.PixelAt(10, 20);

        Assert.Equal(0xAB, span[0]);
        Assert.Equal(0xCD, span[3]);
    }
}
