using WowCapture.Core.Annotations;
using WowCapture.Core.Geometry;
using Xunit;

namespace WowCapture.Core.Tests.Annotations;

/// <summary>
/// <see cref="Annotation.GetBounds"/> 的契约是"命中测试与失效区域"：
/// 渲染器画到包围盒之外的每一笔（描边半宽、箭头翼、Badge 外描边）都必须被算进来，
/// 否则保留模式渲染在撤销/移动后会留下"鬼影"。这里逐类型锁死该契约。
/// </summary>
public class AnnotationBoundsTests
{
    private static AnnotationStyle Style(double strokeWidth) =>
        new(ArgbColor.Red, strokeWidth, null);

    [Fact]
    public void 矩形描边一半画在Bounds外_失效区域必须膨胀()
    {
        var rect = new RectangleAnnotation(new ScreenRect(100, 100, 200, 100), Style(12));

        var bounds = rect.GetBounds();

        // pad = ceil(12/2)+1 = 7
        Assert.Equal(new ScreenRect(93, 93, 214, 114), bounds);
        Assert.True(bounds.Contains(rect.Bounds));
    }

    [Fact]
    public void 椭圆失效区域与矩形同规则()
    {
        var ellipse = new EllipseAnnotation(new ScreenRect(0, 0, 50, 50), Style(4));

        var bounds = ellipse.GetBounds();

        // pad = ceil(4/2)+1 = 3
        Assert.Equal(new ScreenRect(-3, -3, 56, 56), bounds);
    }

    [Fact]
    public void 箭头头部两翼落在失效区域内()
    {
        // 默认描边 4：headLength = max(4*4,12) = 16，翼展 8；失效 pad = 2+8+1 = 11。
        var arrow = new ArrowAnnotation(new ScreenPoint(0, 0), new ScreenPoint(100, 0), Style(4));

        var bounds = arrow.GetBounds();

        // 翼尖在 (84, ±8)，必须被覆盖。
        Assert.True(bounds.Contains(new ScreenPoint(84, 8)));
        Assert.True(bounds.Contains(new ScreenPoint(84, -8)));
    }

    [Fact]
    public void 箭头_粗描边短线的翼展同样被覆盖()
    {
        // 描边 12、线长 20：headLength 退化为 20*0.9=18，翼展 9；pad = 6+9+1 = 16。
        var arrow = new ArrowAnnotation(new ScreenPoint(0, 0), new ScreenPoint(20, 0), Style(12));

        var bounds = arrow.GetBounds();

        // 翼尖在 (2, ±9)。
        Assert.True(bounds.Contains(new ScreenPoint(2, 9)));
        Assert.True(bounds.Contains(new ScreenPoint(2, -9)));
    }

    [Fact]
    public void Badge_半径下限与外描边都算进失效区域()
    {
        // Radius 4：绘制半径 max(8,4)=8，外描边 max(2, 8*0.16)=2，pad = 1+1 = 2。
        var badge = new BadgeAnnotation(new ScreenPoint(50, 50), Number: 1, ArgbColor.Red, Radius: 4);

        var bounds = badge.GetBounds();

        Assert.Equal(new ScreenRect(40, 40, 20, 20), bounds);

        // 圆边 + 半宽描边处（半径 8 + 1 像素）必须被覆盖。
        Assert.True(bounds.Contains(new ScreenPoint(42, 50)));
        Assert.True(bounds.Contains(new ScreenPoint(58, 50)));
    }

    [Fact]
    public void Badge_大半径同样成立()
    {
        var badge = new BadgeAnnotation(new ScreenPoint(0, 0), Number: 9, ArgbColor.Blue, Radius: 20);

        var bounds = badge.GetBounds();

        // 绘制半径 20，外描边 max(2,3.2)=3.2，pad = ceil(1.6)+1 = 3。
        Assert.Equal(new ScreenRect(-23, -23, 46, 46), bounds);
    }

    [Fact]
    public void 直线失效区域含描边半宽()
    {
        var line = new LineAnnotation(new ScreenPoint(0, 0), new ScreenPoint(10, 10), Style(4));

        // pad = 3。
        Assert.Equal(new ScreenRect(-3, -3, 16, 16), line.GetBounds());
    }

    [Fact]
    public void 画笔包围盒含全部采样点与描边()
    {
        var stroke = new StrokeAnnotation(
            new[]
            {
                new ScreenPoint(0, 0),
                new ScreenPoint(50, 30),
                new ScreenPoint(10, 60),
            },
            Style(4));

        var bounds = stroke.GetBounds();

        Assert.Equal(new ScreenRect(-3, -3, 56, 66), bounds);
    }
}
