using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WowCapture.Core.Annotations;
using WowCapture.Core.Geometry;
using WowCapture.Core.Layout;
using WowCapture.Editor.Rendering;

namespace WowCapture.App.Overlay;

/// <summary>
/// 单块显示器上的绘制面：底图 + 压暗 + 吸附高亮 + 选区 + 标注 + 提示，同时负责鼠标输入。
/// </summary>
/// <remarks>
/// <para>
/// 渲染顺序：底图 → 压暗 → 吸附高亮 → 选区挖亮/边框 → 标注 → 尺寸提示。
/// 标注在"挖亮"之后画，所以永远落在真实画面上而不是压暗层上。
/// </para>
/// <para>
/// 标注使用虚拟屏物理像素坐标，本元素负责推入一个变换矩阵把它映射到窗口 DIP：
/// 屏幕上除以 DPI 缩放，导出时不需要这个矩阵（因为导出是 1:1 物理像素）。
/// 这就是"同一份标注在预览与导出里像素一致"的原因。
/// </para>
/// </remarks>
internal sealed class OverlaySurface : FrameworkElement
{
    /// <summary>按住鼠标移动超过这个物理像素数，就从"吸附整窗"转成"自由框选"。</summary>
    private const int SnapDragThreshold = 4;

    private static readonly Brush HoverFill = CreateFrozenBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF));
    private static readonly Brush HintBackground = CreateFrozenBrush(Color.FromArgb(0xD0, 0x20, 0x20, 0x20));
    private static readonly Brush HoverTitleBackground = CreateFrozenBrush(Color.FromArgb(0xD8, 0x1F, 0x2A, 0x36));

    /// <summary>选中标注的虚线描边：只在预览里画，不进导出。</summary>
    private static readonly Pen SelectionOutlinePen = CreateFrozenDashPen(Color.FromRgb(0x1E, 0x90, 0xFF), 1.5);

    private readonly OverlaySession _session;
    private readonly int _monitorIndex;
    private readonly OverlayPlacement _placement;
    private readonly BitmapSource _image;

    private ToolbarLayout? _toolbar;
    private int _hoveredToolbarIndex = -1;

    private readonly Pen _borderPen;
    private readonly Pen _hoverPen;
    private readonly Pen _hintBorderPen;

    private bool _dragging;
    private bool _annotating;
    private bool _editing;
    private bool _pressedForSnap;
    private ScreenPoint _pressPoint;

    public OverlaySurface(OverlaySession session, OverlayPlacement placement, BitmapSource image, int monitorIndex)
    {
        _session = session;
        _placement = placement;
        _image = image;
        _monitorIndex = monitorIndex;

        // 1:1 映射的前提：不做双线性插值，否则高 DPI 下整块图会发糊。
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);

        _borderPen = CreateFrozenPen(new SolidColorBrush(Color.FromRgb(0x1E, 0x90, 0xFF)), 2.0);
        _hoverPen = CreateFrozenPen(new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7)), 1.5);
        _hintBorderPen = CreateFrozenPen(new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)), 1);

        Focusable = true;
        IsHitTestVisible = true;
        Cursor = System.Windows.Input.Cursors.Cross;
    }

    /// <summary>实际绘制次数。自检用它判断"窗口建了但根本没画"这类问题。</summary>
    public int RenderCount { get; private set; }

    /// <summary>所属遮罩窗口：开内联文字输入框需要它提供的 Canvas 与 DPI 换算。</summary>
    internal OverlayWindow? Host { get; set; }

    public void InvalidateSurface() => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        RenderCount++;

        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        // 1. 底图：整块屏的真实画面。
        dc.DrawImage(_image, bounds);

        // 2. 压暗：只影响选区之外，选区在第 4 步被擦亮。
        var dim = Math.Clamp(_session.DimOpacity, 0, 1);
        if (dim > 0)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)Math.Round(dim * 255), 0, 0, 0)), null, bounds);
        }

        // 3. 窗口吸附高亮：只在没有拖动、且不与已有选区重合时画。
        DrawHoverHighlight(dc, bounds);

        // 4. 选区（挖亮 + 边框）。
        var selectionRect = DrawSelection(dc, bounds);

        // 5. 标注：在挖亮之后画，落在真实画面上。
        if (selectionRect is { } clip)
        {
            DrawAnnotations(dc, clip);
        }

        // 6. 尺寸提示永远在最上层。
        if (_session.Selection is { IsEmpty: false } selected && selectionRect is { } hintClip)
        {
            DrawSizeHint(dc, selected, hintClip, hintClip, bounds);
        }

        // 7. 工具栏压在最上层（它不属于导出内容）。
        DrawToolbar(dc);
    }

    /// <summary>工具栏只由"选区右上角所在的那块屏"绘制，避免跨屏时出现两条。</summary>
    private void DrawToolbar(DrawingContext dc)
    {
        if (_session.Selection is not { IsEmpty: false } selection
            || _session.GetToolbarMonitorIndex() != _monitorIndex)
        {
            _toolbar = null;
            return;
        }

        var selectionDip = ToLocalDip(selection);
        _toolbar = OverlayToolbar.Build(
            selectionDip,
            ActualWidth,
            ActualHeight,
            _session.ActiveTool,
            _session.Style.Stroke);

        OverlayToolbar.Draw(dc, _toolbar, _session.ActiveTool, _session.Style.Stroke, _hoveredToolbarIndex);
    }

    /// <summary>自检用：最近一次渲染出的工具栏布局。</summary>
    internal ToolbarLayout? LastToolbar => _toolbar;

    private void DrawHoverHighlight(DrawingContext dc, Rect bounds)
    {
        if (_dragging || _annotating || _session.HoverCandidate is not { } hover)
        {
            return;
        }

        // 已经选中这个窗口时不要再叠一层高亮，否则边框看起来像双线。
        if (_session.Selection is { } selection && selection == hover.Bounds)
        {
            return;
        }

        var rect = ToLocalDip(hover.Bounds);
        var visible = Rect.Intersect(rect, bounds);
        if (visible.IsEmpty)
        {
            return;
        }

        dc.DrawRectangle(HoverFill, null, visible);
        DrawOutline(dc, _hoverPen, rect, bounds);
        DrawHoverTitle(dc, hover, rect, bounds);
    }

    private Rect? DrawSelection(DrawingContext dc, Rect bounds)
    {
        if (_session.Selection is not { IsEmpty: false } selected)
        {
            return null;
        }

        var selRect = ToLocalDip(selected);
        var visible = Rect.Intersect(selRect, bounds);
        if (visible.IsEmpty)
        {
            return null;
        }

        // 选区恢复原始亮度：用裁剪重画底图，而不是叠白色半透明（会偏色）。
        dc.PushClip(new RectangleGeometry(visible));
        dc.DrawImage(_image, bounds);
        dc.Pop();

        DrawOutline(dc, _borderPen, selRect, bounds);
        return visible;
    }

    private void DrawAnnotations(DrawingContext dc, Rect clip)
    {
        var annotations = _session.Annotations;
        var pending = _session.PendingAnnotation;
        if (annotations.Count == 0 && pending is null)
        {
            return;
        }

        var scale = _placement.Scale;

        // 标注坐标（虚拟屏物理像素）→ 本窗口 DIP：先平移再除以缩放。
        var matrix = new Matrix(
            1 / scale,
            0,
            0,
            1 / scale,
            -_placement.PhysicalBounds.X / scale,
            -_placement.PhysicalBounds.Y / scale);

        dc.PushClip(new RectangleGeometry(clip));
        dc.PushTransform(new MatrixTransform(matrix));

        // 马赛克要采样干净底图，不是采样已经画出来的结果。
        var mosaicSource = new MosaicSource(_session.FrameBitmap, _session.VirtualBounds);
        AnnotationRenderer.Draw(dc, annotations, mosaicSource);

        if (pending is not null)
        {
            AnnotationRenderer.Draw(dc, pending, mosaicSource);
        }

        // 选中标注的虚线框：画在标注层之上、只在预览可见，永远不进导出。
        if (_session.SelectedAnnotation is { } selected)
        {
            var bounds = selected.GetBounds();
            if (!bounds.IsEmpty)
            {
                var outline = new Rect(bounds.X - 3, bounds.Y - 3, bounds.Width + 6, bounds.Height + 6);
                dc.DrawRectangle(null, SelectionOutlinePen, outline);
            }
        }

        dc.Pop();
        dc.Pop();
    }

    /// <summary>
    /// 只画落在本屏范围内的那几条边。跨屏时另一块屏的窗口会画剩下的部分，
    /// 因此不会出现"半截线"或重复描边。
    /// </summary>
    private static void DrawOutline(DrawingContext dc, Pen pen, Rect rect, Rect bounds)
    {
        var half = pen.Thickness / 2;

        if (rect.Top >= bounds.Top - half)
        {
            dc.DrawLine(pen, new Point(Math.Max(rect.Left, bounds.Left), rect.Top), new Point(Math.Min(rect.Right, bounds.Right), rect.Top));
        }

        if (rect.Bottom <= bounds.Bottom + half)
        {
            dc.DrawLine(pen, new Point(Math.Max(rect.Left, bounds.Left), rect.Bottom), new Point(Math.Min(rect.Right, bounds.Right), rect.Bottom));
        }

        if (rect.Left >= bounds.Left - half)
        {
            dc.DrawLine(pen, new Point(rect.Left, Math.Max(rect.Top, bounds.Top)), new Point(rect.Left, Math.Min(rect.Bottom, bounds.Bottom)));
        }

        if (rect.Right <= bounds.Right + half)
        {
            dc.DrawLine(pen, new Point(rect.Right, Math.Max(rect.Top, bounds.Top)), new Point(rect.Right, Math.Min(rect.Bottom, bounds.Bottom)));
        }
    }

    private void DrawHoverTitle(DrawingContext dc, Platform.Windows.WindowHit hover, Rect rect, Rect bounds)
    {
        // 窗口太小时标题框会盖住整个窗口，不如不画。
        if (rect.Width < 80 || rect.Height < 40)
        {
            return;
        }

        var text = string.IsNullOrWhiteSpace(hover.Title)
            ? $"{hover.Bounds.Width} × {hover.Bounds.Height}"
            : hover.Title;

        var dpi = VisualTreeHelper.GetDpi(this);
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            13,
            Brushes.White,
            dpi.PixelsPerDip)
        {
            MaxTextWidth = Math.Max(40, Math.Min(rect.Width - 24, 600)),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };

        DrawBadge(dc, formatted, rect.Left + 6, rect.Top + 6, bounds, HoverTitleBackground);
    }

    private void DrawSizeHint(DrawingContext dc, ScreenRect selection, Rect selRect, Rect _, Rect bounds)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var formatted = new FormattedText(
            $"{selection.Width} × {selection.Height}  [{_session.Annotations.Count} 标注]",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            13,
            Brushes.White,
            dpi.PixelsPerDip);

        var boxWidth = formatted.Width + 12;
        var boxHeight = formatted.Height + 6;

        var x = selRect.Right - boxWidth - 2;
        var y = selRect.Bottom + 6;

        // 贴到屏幕底部就翻到选区内部，避免提示框被裁掉一半。
        if (y + boxHeight > bounds.Bottom - 2)
        {
            y = selRect.Bottom - boxHeight - 6;
        }

        if (y < bounds.Top + 2)
        {
            y = bounds.Top + 2;
        }

        if (x < bounds.Left + 2)
        {
            x = bounds.Left + 2;
        }

        DrawBadge(dc, formatted, x, y, bounds, HintBackground);
    }

    private void DrawBadge(DrawingContext dc, FormattedText text, double x, double y, Rect bounds, Brush background)
    {
        const double paddingX = 6;
        const double paddingY = 3;
        var boxWidth = text.Width + (paddingX * 2);
        var boxHeight = text.Height + (paddingY * 2);

        x = Math.Clamp(x, bounds.Left + 4, Math.Max(bounds.Left + 4, bounds.Right - boxWidth - 4));
        y = Math.Clamp(y, bounds.Top + 4, Math.Max(bounds.Top + 4, bounds.Bottom - boxHeight - 4));

        dc.DrawRoundedRectangle(background, _hintBorderPen, new Rect(x, y, boxWidth, boxHeight), 3, 3);
        dc.DrawText(text, new Point(x + paddingX, y + paddingY));
    }

    /// <summary>虚拟屏物理矩形 → 本窗口的 DIP 矩形（可能超出窗口，调用方负责裁剪）。</summary>
    private Rect ToLocalDip(ScreenRect physical) => new(
        (physical.X - _placement.PhysicalBounds.X) / _placement.Scale,
        (physical.Y - _placement.PhysicalBounds.Y) / _placement.Scale,
        physical.Width / _placement.Scale,
        physical.Height / _placement.Scale);

    private ScreenPoint ToPhysical(Point dip) => _placement.ToPhysical(dip.X, dip.Y);

    protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        Focus();

        // 有内联输入框在编辑：这次点击先提交文字，不继续画。
        if (Host is { IsEditingText: true } editing)
        {
            editing.CommitText();
            e.Handled = true;
            return;
        }

        // 工具栏优先：它画在选区之上，点击它不能触发标注或重新框选。
        if (_toolbar is { } toolbar)
        {
            var index = toolbar.IndexOf(e.GetPosition(this));
            if (index >= 0)
            {
                ExecuteToolbar(toolbar.Buttons[index]);
                e.Handled = true;
                return;
            }
        }

        if (e.ClickCount == 2)
        {
            _session.RequestConfirm();
            e.Handled = true;
            return;
        }

        var physical = ToPhysical(e.GetPosition(this));

        // 已框选且按在选区内：开始画标注（文字工具开输入框，序号工具直接落点）。
        if (_session.Selection is { IsEmpty: false } selection && selection.Contains(physical))
        {
            // 选择工具：优先进入"抓取已有标注"分支；抓不到就只是取消选中。
            if (_session.ActiveTool == AnnotationTool.Select)
            {
                if (_session.BeginAnnotationEdit(physical))
                {
                    _editing = true;
                    CaptureMouse();
                }

                e.Handled = true;
                return;
            }

            if (_session.ActiveTool == AnnotationTool.Text)
            {
                Host?.BeginTextEdit(physical);
                e.Handled = true;
                return;
            }

            if (_session.ActiveTool == AnnotationTool.Badge)
            {
                _session.AddBadge(physical);
                e.Handled = true;
                return;
            }

            _annotating = true;
            CaptureMouse();
            _session.BeginAnnotation(physical);
            e.Handled = true;
            return;
        }

        // 有吸附候选就整窗选中。先记下按点：如果用户接着拖动，再改成自由框选。
        if (_session.HoverCandidate is not null && _session.SelectHoveredWindow())
        {
            _pressedForSnap = true;
            _pressPoint = physical;
            e.Handled = true;
            return;
        }

        _dragging = true;
        CaptureMouse();
        _session.BeginSelection(physical);
        e.Handled = true;
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        var local = e.GetPosition(this);
        var physical = ToPhysical(local);

        // 悬停在工具栏上时只更新按钮高亮，不做标注/吸附。
        if (!_annotating && !_dragging && !_pressedForSnap && !_editing)
        {
            var hoverIndex = _toolbar?.IndexOf(local) ?? -1;
            if (hoverIndex != _hoveredToolbarIndex)
            {
                _hoveredToolbarIndex = hoverIndex;
                Cursor = hoverIndex >= 0 ? System.Windows.Input.Cursors.Arrow : System.Windows.Input.Cursors.Cross;
                InvalidateVisual();
            }

            // 选择工具下，悬停在可命中的标注上给"可拖动"光标，其他地方回到十字。
            if (hoverIndex < 0 && _session.ActiveTool == AnnotationTool.Select)
            {
                var wanted = _session.HitTestAnnotation(physical) >= 0
                    ? System.Windows.Input.Cursors.SizeAll
                    : System.Windows.Input.Cursors.Cross;

                if (Cursor != wanted)
                {
                    Cursor = wanted;
                }
            }
        }

        if (_editing)
        {
            _session.UpdateAnnotationEdit(physical);
            return;
        }

        if (_annotating)
        {
            _session.UpdateAnnotation(physical);
            return;
        }

        // 移动期间不做吸附查询，停住 80ms 后才查（见 OverlaySession）。
        _session.NotifyPointerMoved(physical);

        if (_pressedForSnap)
        {
            if (Math.Abs(physical.X - _pressPoint.X) > SnapDragThreshold
                || Math.Abs(physical.Y - _pressPoint.Y) > SnapDragThreshold)
            {
                _pressedForSnap = false;
                _dragging = true;
                CaptureMouse();
                _session.BeginSelection(_pressPoint);
                _session.UpdateSelection(physical);
            }

            return;
        }

        if (!_dragging)
        {
            return;
        }

        _session.UpdateSelection(physical);
    }

    protected override void OnMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        var physical = ToPhysical(e.GetPosition(this));

        if (_editing)
        {
            _editing = false;
            ReleaseMouseCapture();
            _session.EndAnnotationEdit(physical);
            e.Handled = true;
            return;
        }

        if (_annotating)
        {
            _annotating = false;
            ReleaseMouseCapture();
            _session.EndAnnotation(physical);
            e.Handled = true;
            return;
        }

        if (_pressedForSnap)
        {
            _pressedForSnap = false;
            _session.NotifyPointerMoved(physical);
            e.Handled = true;
            return;
        }

        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ReleaseMouseCapture();
        _session.EndSelection(physical);
        _session.NotifyPointerMoved(physical);
        e.Handled = true;
    }

    protected override void OnMouseRightButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        _session.Cancel();
        e.Handled = true;
    }

    private static Pen CreateFrozenPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }

    private static Pen CreateFrozenDashPen(Color color, double thickness)
    {
        var pen = new Pen(new SolidColorBrush(color), thickness) { DashStyle = DashStyles.Dash };
        pen.Freeze();
        return pen;
    }

    /// <summary>把工具栏按钮变成会话状态变化。</summary>
    private void ExecuteToolbar(ToolbarButton button)
    {
        switch (button.Action)
        {
            case ToolbarAction.Tool:
                _session.SelectTool(button.Tool);
                break;

            case ToolbarAction.Color:
                _session.SetStyle(_session.Style.WithStroke(button.Color));
                break;

            case ToolbarAction.Undo:
                _session.Undo();
                break;

            case ToolbarAction.Redo:
                _session.Redo();
                break;

            case ToolbarAction.Pin:
                _session.RequestPin();
                break;

            case ToolbarAction.Confirm:
                _session.RequestConfirm();
                break;

            case ToolbarAction.Cancel:
                _session.Cancel();
                break;
        }

        InvalidateVisual();
    }

    private static Brush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
