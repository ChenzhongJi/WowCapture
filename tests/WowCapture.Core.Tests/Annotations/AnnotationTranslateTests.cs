using WowCapture.Core.Annotations;
using WowCapture.Core.Geometry;
using Xunit;

namespace WowCapture.Core.Tests.Annotations;

/// <summary>
/// <see cref="Annotation.Translate"/> 是标注拖动编辑的数学基础：
/// 平移后包围盒必须精确平移同样的距离，尺寸与样式保持不变。
/// </summary>
public class AnnotationTranslateTests
{
    private static AnnotationStyle Style(double strokeWidth) =>
        new(ArgbColor.Red, strokeWidth, null);

    private static void AssertBoundsMoved(Annotation original, Annotation moved, int dx, int dy)
    {
        var before = original.GetBounds();
        var after = moved.GetBounds();

        Assert.Equal(before.Width, after.Width);
        Assert.Equal(before.Height, after.Height);
        Assert.Equal(before.X + dx, after.X);
        Assert.Equal(before.Y + dy, after.Y);
    }

    [Fact]
    public void 矩形_包围盒精确平移()
    {
        var rect = new RectangleAnnotation(new ScreenRect(10, 20, 100, 50), Style(4));

        var moved = rect.Translate(15, -7);

        Assert.Equal(new ScreenRect(25, 13, 100, 50), ((RectangleAnnotation)moved).Bounds);
        AssertBoundsMoved(rect, moved, 15, -7);
    }

    [Fact]
    public void 椭圆_包围盒精确平移()
    {
        var ellipse = new EllipseAnnotation(new ScreenRect(0, 0, 80, 60), Style(12));

        var moved = ellipse.Translate(-5, 30);

        AssertBoundsMoved(ellipse, moved, -5, 30);
    }

    [Fact]
    public void 箭头_起止点与包围盒一起平移()
    {
        var arrow = new ArrowAnnotation(new ScreenPoint(10, 10), new ScreenPoint(200, 40), Style(4));

        var moved = (ArrowAnnotation)arrow.Translate(50, 25);

        Assert.Equal(new ScreenPoint(60, 35), moved.Start);
        Assert.Equal(new ScreenPoint(250, 65), moved.End);
        AssertBoundsMoved(arrow, moved, 50, 25);
    }

    [Fact]
    public void 画笔_全部采样点平移且数量不变()
    {
        var stroke = new StrokeAnnotation(
            new[] { new ScreenPoint(0, 0), new ScreenPoint(30, 12), new ScreenPoint(8, 44) },
            Style(4));

        var moved = (StrokeAnnotation)stroke.Translate(11, -3);

        Assert.Equal(3, moved.Points.Count);
        Assert.Equal(new ScreenPoint(11, -3), moved.Points[0]);
        Assert.Equal(new ScreenPoint(41, 9), moved.Points[1]);
        Assert.Equal(new ScreenPoint(19, 41), moved.Points[2]);
        AssertBoundsMoved(stroke, moved, 11, -3);
    }

    [Fact]
    public void 文字_位置与测量包围盒一起平移()
    {
        var text = new TextAnnotation(new ScreenPoint(100, 50), "你好", 28, ArgbColor.Blue, new ScreenRect(100, 50, 60, 34));

        var moved = (TextAnnotation)text.Translate(-40, 6);

        Assert.Equal(new ScreenPoint(60, 56), moved.Position);
        Assert.Equal(new ScreenRect(60, 56, 60, 34), moved.Bounds);
        Assert.Equal("你好", moved.Text);
    }

    [Fact]
    public void 序号_圆心平移()
    {
        var badge = new BadgeAnnotation(new ScreenPoint(50, 50), Number: 3, ArgbColor.Blue, Radius: 18);

        var moved = (BadgeAnnotation)badge.Translate(20, 20);

        Assert.Equal(new ScreenPoint(70, 70), moved.Center);
        Assert.Equal(3, moved.Number);
        AssertBoundsMoved(badge, moved, 20, 20);
    }

    [Fact]
    public void 马赛克_区域平移且块大小不变()
    {
        var mosaic = new MosaicAnnotation(new ScreenRect(10, 10, 200, 100), BlockSize: 12);

        var moved = (MosaicAnnotation)mosaic.Translate(5, 5);

        Assert.Equal(new ScreenRect(15, 15, 200, 100), moved.Bounds);
        Assert.Equal(12, moved.BlockSize);
        AssertBoundsMoved(mosaic, moved, 5, 5);
    }

    [Fact]
    public void 负方向平移同样成立()
    {
        var line = new LineAnnotation(new ScreenPoint(0, 0), new ScreenPoint(50, 50), Style(4));

        var moved = line.Translate(-100, -200);

        AssertBoundsMoved(line, moved, -100, -200);
    }
}
