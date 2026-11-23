using WowCapture.Core.Geometry;
using Xunit;

namespace WowCapture.Core.Tests.Geometry;

public class ScreenRectTests
{
    [Fact]
    public void FromLtrb_反向对角点_自动归一()
    {
        var rect = ScreenRect.FromLtrb(10, 20, 5, 8);

        Assert.Equal(new ScreenRect(5, 8, 5, 12), rect);
    }

    [Fact]
    public void Contains_右下边界为开区间()
    {
        var rect = new ScreenRect(0, 0, 10, 10);

        Assert.True(rect.Contains(new ScreenPoint(0, 0)));
        Assert.True(rect.Contains(new ScreenPoint(9, 9)));
        Assert.False(rect.Contains(new ScreenPoint(10, 0)));
        Assert.False(rect.Contains(new ScreenPoint(0, 10)));
    }

    [Fact]
    public void Intersect_无交集返回空_绝不出现负宽高()
    {
        var a = new ScreenRect(0, 0, 10, 10);
        var b = new ScreenRect(20, 20, 5, 5);

        Assert.Equal(ScreenRect.Empty, a.Intersect(b));
        Assert.False(a.IntersectsWith(b));

        var c = new ScreenRect(5, 5, 10, 10);
        var overlap = a.Intersect(c);

        Assert.Equal(new ScreenRect(5, 5, 5, 5), overlap);
    }

    [Fact]
    public void Union_空矩形返回另一个()
    {
        var a = ScreenRect.Empty;
        var b = new ScreenRect(-5, -5, 10, 10);

        Assert.Equal(b, a.Union(b));
        Assert.Equal(b, b.Union(a));
    }

    [Fact]
    public void Union_正常并集覆盖两者()
    {
        var a = new ScreenRect(0, 0, 10, 10);
        var b = new ScreenRect(20, 5, 10, 10);

        var union = a.Union(b);

        Assert.Equal(0, union.Left);
        Assert.Equal(0, union.Top);
        Assert.Equal(30, union.Right);
        Assert.Equal(15, union.Bottom);
    }

    [Fact]
    public void Inflate_四边扩展()
    {
        var rect = new ScreenRect(0, 0, 10, 10).Inflate(2, 3);

        Assert.Equal(new ScreenRect(-2, -3, 14, 16), rect);
    }

    [Fact]
    public void Center_奇数宽高向下取整()
    {
        var center = new ScreenRect(0, 0, 11, 11).Center;

        Assert.Equal(new ScreenPoint(5, 5), center);
    }

    [Fact]
    public void Area_空矩形为零()
    {
        Assert.Equal(0L, ScreenRect.Empty.Area);
        Assert.Equal(100L, new ScreenRect(0, 0, 10, 10).Area);
    }
}
