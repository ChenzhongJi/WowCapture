using System.Windows;
using System.Windows.Media;
using WowCapture.Core.Annotations;
using WowCapture.Editor.Rendering;

namespace WowCapture.App.Overlay;

internal enum ToolbarAction
{
    Tool,
    Color,
    Undo,
    Redo,
    Pin,
    Confirm,
    Cancel,
}

/// <summary>工具栏上的一个按钮。<see cref="Rect"/> 是所在遮罩窗口的本地 DIP 坐标。</summary>
internal sealed record ToolbarButton(Rect Rect, ToolbarAction Action, AnnotationTool Tool, ArgbColor Color);

internal sealed class ToolbarLayout
{
    public required Rect Bounds { get; init; }

    public required IReadOnlyList<ToolbarButton> Buttons { get; init; }

    public int IndexOf(Point localDip)
    {
        for (var i = 0; i < Buttons.Count; i++)
        {
            if (Buttons[i].Rect.Contains(localDip))
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>
/// 浮动标注工具栏：贴合选区上沿，放不下就翻到下方，横向夹在屏幕内。
/// </summary>
/// <remarks>
/// <para>
/// 为什么自绘而不是放一堆 WPF 按钮：遮罩窗口是不透明的全屏窗口，里面再套一层控件布局
/// 会引入焦点、命中测试、DPI 取整三套坐标系统；自绘只需要一套（窗口本地 DIP），
/// 而且图标用矢量画，不依赖 "Segoe UI Symbol" 这类字体在不同机器上的字形差异。
/// </para>
/// <para>
/// <b>所有按钮必须是同一尺寸</b>（30×30）：图标画在居中的 16×16 方框里，色点是居中的 18px 圆。
/// 尺寸不统一会让工具栏看起来"歪歪扭扭"，也会让点击热区大小不一致。
/// </para>
/// </remarks>
internal static class OverlayToolbar
{
    private const double ButtonSize = 30;
    private const double IconSize = 16;
    private const double SwatchDiameter = 18;
    private const double Gap = 3;
    private const double GroupGap = 10;
    private const double Padding = 5;
    private const double Margin = 8;
    private const double Radius = 7;
    private const double AccentInset = 1;

    /// <summary>箭头头部统一尺寸：所有带箭头的图标（箭头工具、撤销、重做）共用。</summary>
    private const double HeadLength = 6.2;

    private const double HeadHalfWidth = 2.9;

    private static readonly (AnnotationTool Tool, string Name)[] Tools =
    [
        (AnnotationTool.Arrow, "箭头"),
        (AnnotationTool.Rectangle, "矩形"),
        (AnnotationTool.Ellipse, "椭圆"),
        (AnnotationTool.Line, "直线"),
        (AnnotationTool.Pen, "画笔"),
        (AnnotationTool.Highlighter, "荧光笔"),
        (AnnotationTool.Mosaic, "马赛克"),
        (AnnotationTool.Text, "文字"),
        (AnnotationTool.Badge, "序号"),
    ];

    private static readonly ArgbColor[] Palette =
    [
        ArgbColor.Red,
        ArgbColor.Yellow,
        ArgbColor.Blue,
        ArgbColor.White,
    ];

    private static readonly ToolbarAction[] Actions =
    [
        ToolbarAction.Undo,
        ToolbarAction.Redo,
        ToolbarAction.Pin,
        ToolbarAction.Confirm,
        ToolbarAction.Cancel,
    ];

    private static readonly Brush BackgroundBrush = FrozenBrush(Color.FromArgb(0xF2, 0x1E, 0x1E, 0x1E));
    private static readonly Brush AccentBrush = FrozenBrush(Color.FromRgb(0x2F, 0x6F, 0xEB));
    private static readonly Brush HoverBrush = FrozenBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
    private static readonly Brush IconBrush = FrozenBrush(Color.FromRgb(0xE6, 0xE6, 0xE6));
    private static readonly Brush ActiveIconBrush = FrozenBrush(Colors.White);
    private static readonly Pen BorderPen = FrozenPen(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF), 1);
    private static readonly Pen IconPen = FrozenPen(Color.FromRgb(0xE6, 0xE6, 0xE6), 2, round: true);
    private static readonly Pen ActiveIconPen = FrozenPen(Colors.White, 2, round: true);
    private static readonly Pen SwatchRingPen = FrozenPen(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF), 1);
    private static readonly Pen SwatchActiveRingPen = FrozenPen(Colors.White, 2, round: true);

    /// <summary>计算工具栏布局。所有坐标都是该遮罩窗口的本地 DIP。</summary>
    public static ToolbarLayout Build(
        Rect selectionDip,
        double surfaceWidth,
        double surfaceHeight,
        AnnotationTool activeTool,
        ArgbColor activeColor)
    {
        var buttons = new List<ToolbarButton>(Tools.Length + Palette.Length + Actions.Length);
        var x = Padding;
        const double rowTop = Padding;

        void AddGroup<T>(IReadOnlyList<T> items, Func<T, ToolbarButton> factory)
        {
            foreach (var item in items)
            {
                buttons.Add(factory(item));
                x += ButtonSize + Gap;
            }

            // 组间距 = GroupGap，已在上一个元素后加过 Gap，这里只补差额。
            x += GroupGap - Gap;
        }

        AddGroup(Tools, t => new ToolbarButton(new Rect(x, rowTop, ButtonSize, ButtonSize), ToolbarAction.Tool, t.Tool, default));
        AddGroup(Palette, c => new ToolbarButton(new Rect(x, rowTop, ButtonSize, ButtonSize), ToolbarAction.Color, default, c));
        AddGroup(Actions, a => new ToolbarButton(new Rect(x, rowTop, ButtonSize, ButtonSize), a, default, default));

        var width = x - Gap - (GroupGap - Gap) + Padding;
        var height = (Padding * 2) + ButtonSize;

        // 优先贴在选区上沿外面；上面放不下就翻到下面；再不行就贴住上边。
        var left = selectionDip.Right - width;
        left = Math.Clamp(left, Margin, Math.Max(Margin, surfaceWidth - width - Margin));

        var top = selectionDip.Top - Margin - height;
        if (top < Margin)
        {
            top = selectionDip.Bottom + Margin;
        }

        if (top + height > surfaceHeight - Margin)
        {
            top = Math.Max(Margin, selectionDip.Top - Margin - height);
        }

        top = Math.Clamp(top, Margin, Math.Max(Margin, surfaceHeight - height - Margin));

        var bounds = new Rect(left, top, width, height);
        var offset = new Vector(left, top);

        return new ToolbarLayout
        {
            Bounds = bounds,
            Buttons = buttons.Select(b => b with { Rect = Rect.Offset(b.Rect, offset) }).ToArray(),
        };
    }

    public static void Draw(
        DrawingContext dc,
        ToolbarLayout layout,
        AnnotationTool activeTool,
        ArgbColor activeColor,
        int hoveredIndex)
    {
        dc.DrawRoundedRectangle(BackgroundBrush, BorderPen, layout.Bounds, Radius, Radius);

        for (var i = 0; i < layout.Buttons.Count; i++)
        {
            var button = layout.Buttons[i];
            var isActive = IsActive(button, activeTool, activeColor);

            if (isActive || i == hoveredIndex)
            {
                dc.DrawRoundedRectangle(isActive ? AccentBrush : HoverBrush, null, Expand(button.Rect, AccentInset), 5, 5);
            }

            DrawGlyph(dc, button, isActive, activeColor);
        }
    }

    private static bool IsActive(ToolbarButton button, AnnotationTool activeTool, ArgbColor activeColor) => button.Action switch
    {
        ToolbarAction.Tool => button.Tool == activeTool,
        ToolbarAction.Color => button.Color == activeColor,
        _ => false,
    };

    private static void DrawGlyph(DrawingContext dc, ToolbarButton button, bool isActive, ArgbColor activeColor)
    {
        var pen = isActive ? ActiveIconPen : IconPen;
        var brush = isActive ? ActiveIconBrush : IconBrush;
        var box = IconBox(button.Rect);

        switch (button.Action)
        {
            case ToolbarAction.Color:
                DrawSwatch(dc, button.Rect, button.Color, isActive);
                break;

            case ToolbarAction.Undo:
                DrawHistoryIcon(dc, box, pen, brush, mirrored: false);
                break;

            case ToolbarAction.Redo:
                DrawHistoryIcon(dc, box, pen, brush, mirrored: true);
                break;

            case ToolbarAction.Pin:
                DrawPinIcon(dc, box, pen, brush);
                break;

            case ToolbarAction.Confirm:
                DrawCheckIcon(dc, box, pen);
                break;

            case ToolbarAction.Cancel:
                DrawCrossIcon(dc, box, pen);
                break;

            default:
                DrawToolIcon(dc, box, button.Tool, pen, brush, activeColor);
                break;
        }
    }

    private static void DrawSwatch(DrawingContext dc, Rect button, ArgbColor color, bool isActive)
    {
        var center = Center(button);
        var radius = SwatchDiameter / 2;
        var fill = FrozenBrush(Color.FromArgb(255, color.R, color.G, color.B));

        dc.DrawEllipse(fill, SwatchRingPen, center, radius, radius);

        if (isActive)
        {
            dc.DrawEllipse(null, SwatchActiveRingPen, center, radius + 2.5, radius + 2.5);
        }
    }

    /// <summary>
    /// 图标统一规格：内容画在 14×14 的内容框里（16 图标框内缩 1），
    /// 箭头头部统一 6.2×2.9。对角线图标（箭头/直线）要略超出内容框，
    /// 否则视觉上会比方形图标小一圈。
    /// </summary>
    private static void DrawToolIcon(DrawingContext dc, Rect box, AnnotationTool tool, Pen pen, Brush brush, ArgbColor activeColor)
    {
        var content = Inset(box, 1);

        switch (tool)
        {
            case AnnotationTool.Arrow:
                var shaftStart = new Point(box.Left + 1.5, box.Bottom - 1.5);
                var shaftTip = new Point(box.Right - 1.5, box.Top + 1.5);
                var direction = shaftTip - shaftStart;
                direction.Normalize();
                dc.DrawLine(pen, shaftStart, shaftTip - (direction * HeadLength));
                DrawArrowHead(dc, shaftStart, shaftTip, brush);
                break;

            case AnnotationTool.Rectangle:
                dc.DrawRoundedRectangle(null, pen, content, 2, 2);
                break;

            case AnnotationTool.Ellipse:
                dc.DrawEllipse(null, pen, Center(box), content.Width / 2, content.Height / 2);
                break;

            case AnnotationTool.Line:
                // 对角线按内容框画，视觉长度与方形图标接近。
                dc.DrawLine(pen, new Point(content.Left, content.Bottom), new Point(content.Right, content.Top));
                break;

            case AnnotationTool.Pen:
                // S 形曲线：一条斜线配个三角会看起来和"箭头"工具一样，曲线才像笔迹。
                var curve = new StreamGeometry();
                using (var context = curve.Open())
                {
                    context.BeginFigure(new Point(box.Left + 1, box.Bottom - 3), isFilled: false, isClosed: false);
                    context.BezierTo(
                        new Point(box.Left + 4, box.Top + 1),
                        new Point(box.Right - 4, box.Bottom - 1),
                        new Point(box.Right - 1, box.Top + 3),
                        isStroked: true,
                        isSmoothJoin: true);
                }

                curve.Freeze();
                dc.DrawGeometry(null, pen, curve);
                break;

            case AnnotationTool.Highlighter:
                // 图标用当前选中的颜色：否则用户改完颜色也看不出荧光笔会跟着变。
                var highlighterColor = Color.FromArgb(255, activeColor.R, activeColor.G, activeColor.B);
                var highlightPen = FrozenPen(highlighterColor, 5, round: true);
                dc.DrawLine(highlightPen, new Point(box.Left + 3, box.Bottom - 3), new Point(box.Right - 2, box.Top + 3));
                break;

            case AnnotationTool.Mosaic:
                // 3×3 棋盘格，比"一个方块"更容易识别成"打码"。
                var cell = content.Width / 3;
                for (var row = 0; row < 3; row++)
                {
                    for (var column = 0; column < 3; column++)
                    {
                        if ((row + column) % 2 != 0)
                        {
                            continue;
                        }

                        dc.DrawRectangle(brush, null, new Rect(content.Left + (column * cell), content.Top + (row * cell), cell, cell));
                    }
                }

                break;

            case AnnotationTool.Text:
                // 大写 T：横杠 + 中竖。
                dc.DrawLine(pen, new Point(box.Left + 1, box.Top + 2), new Point(box.Right - 1, box.Top + 2));
                dc.DrawLine(pen, new Point(Center(box).X, box.Top + 2), new Point(Center(box).X, box.Bottom - 2));
                break;

            case AnnotationTool.Badge:
                var badgeCenter = Center(box);
                dc.DrawEllipse(null, pen, badgeCenter, (box.Width / 2) - 1, (box.Height / 2) - 1);
                var number = TextMetrics.CreateFormattedText("1", 9, brush);
                dc.DrawText(number, new Point(badgeCenter.X - (number.Width / 2), badgeCenter.Y - (number.Height / 2)));
                break;
        }
    }

    private static void DrawArrowHead(DrawingContext dc, Point start, Point end, Brush brush)
    {
        var direction = end - start;
        if (direction.Length < 1)
        {
            return;
        }

        direction.Normalize();
        var normal = new Vector(-direction.Y, direction.X) * HeadHalfWidth;
        var basePoint = end - (direction * HeadLength);

        dc.DrawGeometry(brush, null, Triangle(end, basePoint + normal, basePoint - normal));
    }

    /// <summary>
    /// 撤销/重做：一条从右下弯向左上的曲线 + 左端实心箭头（重做水平镜像）。
    /// </summary>
    /// <remarks>
    /// 之前的"半圆弧 + 竖直尾巴 + 小三角"三段拼在一起，在 16×16 里像"↰"这种直角钩，
    /// 和其他描边图标的语言不一致。现在改成单一曲线 + 大一号的箭头，轮廓就是一个标准"↶"。
    /// </remarks>
    private static void DrawHistoryIcon(DrawingContext dc, Rect box, Pen pen, Brush brush, bool mirrored)
    {
        var center = Center(box);

        if (mirrored)
        {
            // 直接水平镜像坐标系，比再写一套镜像几何更不容易出错。
            dc.PushTransform(new ScaleTransform(-1, 1, center.X, center.Y));
        }

        var headBaseX = box.Left + 1 + HeadLength;
        var midY = box.Top + 6.5;

        var curve = new StreamGeometry();
        using (var context = curve.Open())
        {
            context.BeginFigure(new Point(box.Right - 2.5, box.Bottom - 2.5), isFilled: false, isClosed: false);
            context.BezierTo(
                new Point(box.Right - 2.5, box.Top + 4),
                new Point(headBaseX + 2.5, midY),
                new Point(headBaseX, midY),
                isStroked: true,
                isSmoothJoin: true);
        }

        curve.Freeze();
        dc.DrawGeometry(null, pen, curve);

        // 箭头与"箭头工具"同一套尺寸，贴在曲线末端指向左。
        dc.DrawGeometry(brush, null, Triangle(
            new Point(headBaseX, midY - HeadHalfWidth),
            new Point(headBaseX, midY + HeadHalfWidth),
            new Point(box.Left + 1, midY)));

        if (mirrored)
        {
            dc.Pop();
        }
    }

    /// <summary>图钉：圆头 + 挡板 + 针杆。</summary>
    private static void DrawPinIcon(DrawingContext dc, Rect box, Pen pen, Brush brush)
    {
        var centerX = Center(box).X;
        var headY = box.Top + 3.5;

        dc.DrawEllipse(brush, null, new Point(centerX, headY), 2.8, 2.8);
        dc.DrawLine(pen, new Point(centerX - 4, headY + 3.5), new Point(centerX + 4, headY + 3.5));
        dc.DrawLine(pen, new Point(centerX, headY + 3.5), new Point(centerX, box.Bottom - 1.5));
    }

    private static void DrawCheckIcon(DrawingContext dc, Rect box, Pen pen)    {
        var center = Center(box);
        dc.DrawLine(pen, new Point(center.X - 6, center.Y), new Point(center.X - 2, center.Y + 4));
        dc.DrawLine(pen, new Point(center.X - 2, center.Y + 4), new Point(center.X + 6, center.Y - 5));
    }

    private static void DrawCrossIcon(DrawingContext dc, Rect box, Pen pen)
    {
        var center = Center(box);
        dc.DrawLine(pen, new Point(center.X - 5, center.Y - 5), new Point(center.X + 5, center.Y + 5));
        dc.DrawLine(pen, new Point(center.X + 5, center.Y - 5), new Point(center.X - 5, center.Y + 5));
    }

    private static StreamGeometry Triangle(Point a, Point b, Point c)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(a, isFilled: true, isClosed: true);
            context.LineTo(b, isStroked: false, isSmoothJoin: false);
            context.LineTo(c, isStroked: false, isSmoothJoin: false);
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>按钮内居中的图标方框。</summary>
    private static Rect IconBox(Rect button) => new(
        button.Left + ((button.Width - IconSize) / 2),
        button.Top + ((button.Height - IconSize) / 2),
        IconSize,
        IconSize);

    /// <summary>四边同时内缩 amount。</summary>
    private static Rect Inset(Rect rect, double amount) => new(
        rect.Left + amount,
        rect.Top + amount,
        Math.Max(0, rect.Width - (amount * 2)),
        Math.Max(0, rect.Height - (amount * 2)));

    /// <summary>四边同时外扩 amount（高亮底比按钮大一圈）。</summary>
    private static Rect Expand(Rect rect, double amount) => Inset(rect, -amount);

    private static Point Center(Rect rect) => new(rect.Left + (rect.Width / 2), rect.Top + (rect.Height / 2));

    private static Brush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Color color, double thickness, bool round = false)
    {
        var pen = new Pen(FrozenBrush(color), thickness);

        if (round)
        {
            pen.StartLineCap = PenLineCap.Round;
            pen.EndLineCap = PenLineCap.Round;
            pen.LineJoin = PenLineJoin.Round;
        }

        pen.Freeze();
        return pen;
    }
}
