using WowCapture.Core.Geometry;

namespace WowCapture.Core.Annotations;

/// <summary>
/// 一次标注。全部是不可变记录：撤销只是换一个列表快照，不复制位图（架构 D5）。
/// 所有坐标都是虚拟屏物理像素，与抓屏、选区共用同一套坐标系。
/// </summary>
public abstract record Annotation
{
    /// <summary>包围盒（物理像素），用于命中测试与失效区域。</summary>
    public abstract ScreenRect GetBounds();

    /// <summary>
    /// 整体平移（物理像素），返回新标注。不可变模型下"移动"就是换一个新记录，
    /// 撤销栈里存着的旧快照天然不受影响（架构 D5）。
    /// </summary>
    public abstract Annotation Translate(int dx, int dy);
}

/// <summary>矩形框。</summary>
public sealed record RectangleAnnotation(ScreenRect Bounds, AnnotationStyle Style) : Annotation
{
    /// <summary>
    /// 描边以矩形边缘为中心线，一半画在 <see cref="Bounds"/> 之外，失效区域必须把它算进来。
    /// </summary>
    public override ScreenRect GetBounds() => StrokeGeometry.Inflate(Bounds, Style.StrokeWidth);

    public override Annotation Translate(int dx, int dy) => this with { Bounds = Bounds.Offset(dx, dy) };
}

/// <summary>椭圆框。</summary>
public sealed record EllipseAnnotation(ScreenRect Bounds, AnnotationStyle Style) : Annotation
{
    /// <summary>同 <see cref="RectangleAnnotation"/>：外接矩形之外还有半条描边。</summary>
    public override ScreenRect GetBounds() => StrokeGeometry.Inflate(Bounds, Style.StrokeWidth);

    public override Annotation Translate(int dx, int dy) => this with { Bounds = Bounds.Offset(dx, dy) };
}

/// <summary>直线。保留起止点方向，因为箭头需要它。</summary>
public sealed record LineAnnotation(ScreenPoint Start, ScreenPoint End, AnnotationStyle Style) : Annotation
{
    public override ScreenRect GetBounds() => StrokeGeometry.Inflate(ScreenRect.FromPoints(Start, End), Style.StrokeWidth);

    public override Annotation Translate(int dx, int dy) => this with
    {
        Start = new ScreenPoint(Start.X + dx, Start.Y + dy),
        End = new ScreenPoint(End.X + dx, End.Y + dy),
    };
}

/// <summary>箭头：从 Start 指向 End，头部是实心三角。</summary>
public sealed record ArrowAnnotation(ScreenPoint Start, ScreenPoint End, AnnotationStyle Style) : Annotation
{
    /// <summary>
    /// 头部两翼垂直于线段向外伸出 <c>headLength / 2</c>（公式与
    /// <c>AnnotationRenderer.DrawArrow</c> 完全一致），粗描边 + 短线时可达描边半宽的 6 倍以上，
    /// 因此失效区域按翼展膨胀，而不是普通描边。
    /// </summary>
    public override ScreenRect GetBounds()
    {
        var dx = (double)(End.X - Start.X);
        var dy = (double)(End.Y - Start.Y);
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        var headLength = Math.Min(Math.Max(Style.StrokeWidth * 4, 12), length * 0.9);
        var pad = (int)Math.Ceiling(Style.StrokeWidth / 2) + (int)Math.Ceiling(headLength * 0.5) + 1;
        return ScreenRect.FromPoints(Start, End).Inflate(pad, pad);
    }

    public override Annotation Translate(int dx, int dy) => this with
    {
        Start = new ScreenPoint(Start.X + dx, Start.Y + dy),
        End = new ScreenPoint(End.X + dx, End.Y + dy),
    };
}

/// <summary>自由绘制（画笔 / 荧光笔）。点集是物理像素采样点。</summary>
public sealed record StrokeAnnotation(IReadOnlyList<ScreenPoint> Points, AnnotationStyle Style) : Annotation
{
    public override ScreenRect GetBounds()
    {
        if (Points.Count == 0)
        {
            return ScreenRect.Empty;
        }

        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;

        foreach (var point in Points)
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        return StrokeGeometry.Inflate(ScreenRect.FromLtrb(minX, minY, maxX, maxY), Style.StrokeWidth);
    }

    public override Annotation Translate(int dx, int dy) => this with
    {
        Points = [.. Points.Select(p => new ScreenPoint(p.X + dx, p.Y + dy))],
    };
}

/// <summary>
/// 文字标注。
/// </summary>
/// <param name="Position">文字左上角（物理像素）。</param>
/// <param name="Text">内容。</param>
/// <param name="FontSize">字号（物理像素，不是点数也不是 DIP）。</param>
/// <param name="Color">颜色。</param>
/// <param name="Bounds">创建时测得的包围盒，供命中测试与失效区域使用。</param>
public sealed record TextAnnotation(
    ScreenPoint Position,
    string Text,
    double FontSize,
    ArgbColor Color,
    ScreenRect Bounds) : Annotation
{
    public override ScreenRect GetBounds() => Bounds;

    public override Annotation Translate(int dx, int dy) => this with
    {
        Position = new ScreenPoint(Position.X + dx, Position.Y + dy),
        Bounds = Bounds.Offset(dx, dy),
    };
}

/// <summary>
/// 序号标记：一个带数字的实心圆，做步骤说明时一眼就能对上文字。
/// </summary>
/// <param name="Center">圆心（物理像素）。</param>
/// <param name="Number">序号，从 1 开始。</param>
/// <param name="Color">圆底色。</param>
/// <param name="Radius">半径（物理像素）。</param>
public sealed record BadgeAnnotation(ScreenPoint Center, int Number, ArgbColor Color, int Radius) : Annotation
{
    /// <summary>
    /// 与 <c>AnnotationRenderer.DrawBadge</c> 用同一公式：绘制半径下限 8，白色外描边再向外伸半宽。
    /// 两位以内序号不会超出该圆；三位以上数字（字号 <c>radius * 1.25</c>）可能略越界，
    /// 文字测量在 Editor 层，这里按圆处理，属已接受的极端场景。
    /// </summary>
    public override ScreenRect GetBounds()
    {
        var radius = Math.Max(8, Radius);
        var outlineWidth = Math.Max(2, radius * 0.16);
        var pad = (int)Math.Ceiling(outlineWidth / 2) + 1;
        return new(
            Center.X - radius - pad,
            Center.Y - radius - pad,
            (radius + pad) * 2,
            (radius + pad) * 2);
    }

    public override Annotation Translate(int dx, int dy) => this with
    {
        Center = new ScreenPoint(Center.X + dx, Center.Y + dy),
    };
}

/// <summary>
/// 马赛克（隐私打码）。
/// </summary>
/// <remarks>
/// <b>非破坏</b>：只记录区域与块大小，渲染时才从干净底图采样像素化。
/// 因此它可以随时撤销、可以改块大小，导出图也不会有"马赛克叠马赛克"的画质损失。
/// </remarks>
/// <param name="Bounds">要打码的区域（物理像素）。</param>
/// <param name="BlockSize">每个色块的边长（物理像素），越大越糊。</param>
public sealed record MosaicAnnotation(ScreenRect Bounds, int BlockSize) : Annotation
{
    public override ScreenRect GetBounds() => Bounds;

    public override Annotation Translate(int dx, int dy) => this with { Bounds = Bounds.Offset(dx, dy) };
}

internal static class StrokeGeometry
{
    /// <summary>描边以路径为中线向两侧扩展，包围盒要跟着膨胀半个带宽。</summary>
    public static ScreenRect Inflate(ScreenRect bounds, double strokeWidth)
    {
        var pad = (int)Math.Ceiling(strokeWidth / 2) + 1;
        return bounds.Inflate(pad, pad);
    }
}

/// <summary>可用的标注工具。</summary>
public enum AnnotationTool
{
    /// <summary>选择/移动/删除已有标注（不新增）。</summary>
    Select,

    Arrow,
    Rectangle,
    Ellipse,
    Line,
    Pen,
    Highlighter,
    Mosaic,
    Text,
    Badge,
}

/// <summary>截图会话的结果：选区 + 该选区上的标注。</summary>
public sealed record CaptureOutcome(ScreenRect Selection, IReadOnlyList<Annotation> Annotations)
{
    public static CaptureOutcome For(ScreenRect selection) => new(selection, []);
}

/// <summary>确认之后要做什么。</summary>
public enum CaptureAction
{
    /// <summary>复制到剪贴板（并按设置决定是否保存）。</summary>
    Copy,

    /// <summary>贴到屏幕上（Pin）。</summary>
    Pin,
}
