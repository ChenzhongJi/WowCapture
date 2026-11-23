using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WowCapture.Core.Annotations;
using WowCapture.Core.Geometry;

namespace WowCapture.Editor.Rendering;

/// <summary>马赛克需要采样底图，因此渲染器要知道"干净底图是什么"。</summary>
/// <param name="Image">冻结帧（或导出时的裁剪底图）。</param>
/// <param name="Bounds">该图覆盖的虚拟屏区域（物理像素）。</param>
public sealed record MosaicSource(BitmapSource Image, ScreenRect Bounds);

/// <summary>
/// 把标注画进任意 <see cref="DrawingContext"/>。
/// </summary>
/// <remarks>
/// <para>
/// 渲染器本身<b>不认识屏幕也不认识导出</b>：调用方负责推入变换矩阵，
/// 把"虚拟屏物理像素"映射到目标空间（屏幕上除以 DPI 缩放，导出时平移 -选区原点）。
/// 因为画笔宽度、字号都用物理像素书写，同一份标注在两种目标下的像素尺寸完全一致。
/// </para>
/// <para>
/// 用保留模式（DrawingContext）而不是位图：撤销、改色、重排都能立刻重画，
/// 也不会出现"马赛克叠马赛克"的画质损失。
/// </para>
/// </remarks>
public static class AnnotationRenderer
{
    public static void Draw(DrawingContext dc, IEnumerable<Annotation> annotations, MosaicSource? mosaicSource = null)
    {
        ArgumentNullException.ThrowIfNull(dc);

        foreach (var annotation in annotations)
        {
            Draw(dc, annotation, mosaicSource);
        }
    }

    public static void Draw(DrawingContext dc, Annotation annotation, MosaicSource? mosaicSource = null)
    {
        switch (annotation)
        {
            case RectangleAnnotation rectangle:
                dc.DrawRectangle(
                    ToBrush(rectangle.Style.Fill),
                    ToPen(rectangle.Style),
                    ToRect(rectangle.Bounds));
                break;

            case EllipseAnnotation ellipse:
                var rect = ToRect(ellipse.Bounds);
                dc.DrawEllipse(
                    ToBrush(ellipse.Style.Fill),
                    ToPen(ellipse.Style),
                    new Point(rect.Left + (rect.Width / 2), rect.Top + (rect.Height / 2)),
                    rect.Width / 2,
                    rect.Height / 2);
                break;

            case LineAnnotation line:
                dc.DrawLine(ToPen(line.Style), ToPoint(line.Start), ToPoint(line.End));
                break;

            case ArrowAnnotation arrow:
                DrawArrow(dc, arrow);
                break;

            case StrokeAnnotation stroke:
                DrawStroke(dc, stroke);
                break;

            case MosaicAnnotation mosaic:
                DrawMosaic(dc, mosaic, mosaicSource);
                break;

            case TextAnnotation text:
                DrawText(dc, text);
                break;

            case BadgeAnnotation badge:
                DrawBadge(dc, badge);
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// 序号标记：实心圆 + 白色描边 + 白色数字。
    /// 白色描边保证它在深色和浅色背景上都读得出来。
    /// </summary>
    private static void DrawBadge(DrawingContext dc, BadgeAnnotation badge)
    {
        var center = ToPoint(badge.Center);
        var radius = Math.Max(8, badge.Radius);

        var outline = new Pen(Brushes.White, Math.Max(2, radius * 0.16));
        outline.Freeze();

        dc.DrawEllipse(ToBrush(badge.Color), outline, center, radius, radius);

        var number = TextMetrics.CreateFormattedText(
            badge.Number.ToString(CultureInfo.InvariantCulture),
            radius * 1.25,
            Brushes.White);

        // 数字在圆里居中（FormattedText 的 Height 含行距，视觉上略偏上，补偿一半行距差）。
        dc.DrawText(number, new Point(center.X - (number.Width / 2), center.Y - (number.Height / 2)));
    }

    /// <summary>
    /// 文字用矢量绘制，任何缩放下都清晰；字号单位与 <see cref="TextMetrics"/> 完全一致。
    /// </summary>
    private static void DrawText(DrawingContext dc, TextAnnotation annotation)
    {
        if (string.IsNullOrEmpty(annotation.Text))
        {
            return;
        }

        var brush = ToBrush(annotation.Color);
        var formatted = TextMetrics.CreateFormattedText(annotation.Text, annotation.FontSize, brush);
        dc.DrawText(formatted, ToPoint(annotation.Position));
    }

    /// <summary>
    /// 非破坏马赛克：把底图对应区域按块取平均色，然后逐块画实心矩形。
    /// 采样源永远是干净底图，所以撤销与重复导出都不会累积画质损失。
    /// </summary>
    /// <remarks>
    /// 不用 <c>TransformedBitmap</c> + 最近邻缩放：WPF 的图像插值由视觉对象的
    /// <c>BitmapScalingMode</c> 控制，而 <c>RenderTargetBitmap</c> 下并不可靠——
    /// 实测会退化成双线性，看起来是"打了一层模糊"而不是"像素块"。
    /// 逐块画矩形完全不依赖插值，屏幕预览与导出结果逐像素一致。
    /// </remarks>
    private static void DrawMosaic(DrawingContext dc, MosaicAnnotation mosaic, MosaicSource? source)
    {
        if (source is null)
        {
            return;
        }

        var visible = mosaic.Bounds.Intersect(source.Bounds);
        if (visible.Width < 2 || visible.Height < 2)
        {
            return;
        }

        var block = Math.Clamp(mosaic.BlockSize, 2, 64);
        var stride = visible.Width * 4;
        var pixels = new byte[stride * visible.Height];

        source.Image.CopyPixels(
            new Int32Rect(
                visible.X - source.Bounds.X,
                visible.Y - source.Bounds.Y,
                visible.Width,
                visible.Height),
            pixels,
            stride,
            0);

        for (var offsetY = 0; offsetY < visible.Height; offsetY += block)
        {
            var height = Math.Min(block, visible.Height - offsetY);

            for (var offsetX = 0; offsetX < visible.Width; offsetX += block)
            {
                var width = Math.Min(block, visible.Width - offsetX);
                var inset = Math.Max(1, Math.Min(width, height) / 4);

                long sumB = 0;
                long sumG = 0;
                long sumR = 0;

                // 四个样点求平均：比单点采样更能毁掉文字轮廓，成本又远低于全块遍历。
                for (var sampleY = 0; sampleY < 2; sampleY++)
                {
                    var py = Math.Clamp(offsetY + (sampleY == 0 ? inset : height - inset), 0, visible.Height - 1);

                    for (var sampleX = 0; sampleX < 2; sampleX++)
                    {
                        var px = Math.Clamp(offsetX + (sampleX == 0 ? inset : width - inset), 0, visible.Width - 1);
                        var index = (py * stride) + (px * 4);

                        sumB += pixels[index];
                        sumG += pixels[index + 1];
                        sumR += pixels[index + 2];
                    }
                }

                var brush = GetMosaicBrush((byte)(sumR / 4), (byte)(sumG / 4), (byte)(sumB / 4));
                dc.DrawRectangle(brush, null, new Rect(visible.X + offsetX, visible.Y + offsetY, width, height));
            }
        }
    }

    /// <summary>块颜色高度重复（相邻块常常同色），缓存笔刷避免每次渲染造出成千上万个对象。</summary>
    private static readonly Dictionary<int, Brush> MosaicBrushes = new();

    private static Brush GetMosaicBrush(byte r, byte g, byte b)
    {
        var key = (r << 16) | (g << 8) | b;

        if (MosaicBrushes.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();

        if (MosaicBrushes.Count > 4096)
        {
            MosaicBrushes.Clear();
        }

        MosaicBrushes[key] = brush;
        return brush;
    }

    private static void DrawArrow(DrawingContext dc, ArrowAnnotation arrow)
    {
        var start = ToPoint(arrow.Start);
        var end = ToPoint(arrow.End);
        var vector = end - start;
        var length = vector.Length;

        if (length < 0.5)
        {
            return;
        }

        // 箭头头部长度随线宽走，但吃掉整条线时退化为纯三角。
        var headLength = Math.Min(Math.Max(arrow.Style.StrokeWidth * 4, 12), length * 0.9);
        var unit = vector / length;
        var basePoint = end - (unit * headLength);
        var pen = ToPen(arrow.Style);

        dc.DrawLine(pen, start, basePoint);

        var normal = new Vector(-unit.Y, unit.X) * (headLength * 0.5);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(end, isFilled: true, isClosed: true);
            context.LineTo(basePoint + normal, isStroked: false, isSmoothJoin: false);
            context.LineTo(basePoint - normal, isStroked: false, isSmoothJoin: false);
        }

        geometry.Freeze();
        dc.DrawGeometry(ToBrush(arrow.Style.Stroke), null, geometry);
    }

    private static void DrawStroke(DrawingContext dc, StrokeAnnotation stroke)
    {
        if (stroke.Points.Count == 0)
        {
            return;
        }

        if (stroke.Points.Count == 1)
        {
            var point = ToPoint(stroke.Points[0]);
            var radius = Math.Max(1, stroke.Style.StrokeWidth / 2);
            dc.DrawEllipse(ToBrush(stroke.Style.Stroke), null, point, radius, radius);
            return;
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var start = ToPoint(stroke.Points[0]);
            context.BeginFigure(start, isFilled: false, isClosed: false);

            var points = new List<Point>(stroke.Points.Count - 1);
            for (var i = 1; i < stroke.Points.Count; i++)
            {
                points.Add(ToPoint(stroke.Points[i]));
            }

            context.PolyLineTo(points, isStroked: true, isSmoothJoin: true);
        }

        geometry.Freeze();
        dc.DrawGeometry(null, ToPen(stroke.Style), geometry);
    }

    private static Pen ToPen(AnnotationStyle style)
    {
        var pen = new Pen(ToBrush(style.Stroke), Math.Max(0.5, style.StrokeWidth))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };

        pen.Freeze();
        return pen;
    }

    private static Brush? ToBrush(ArgbColor? color)
    {
        if (color is not { } value || value.A == 0)
        {
            return null;
        }

        var brush = new SolidColorBrush(Color.FromArgb(value.A, value.R, value.G, value.B));
        brush.Freeze();
        return brush;
    }

    private static Brush ToBrush(ArgbColor color) => ToBrush((ArgbColor?)color)!;

    private static Rect ToRect(ScreenRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private static Point ToPoint(ScreenPoint point) => new(point.X, point.Y);
}
