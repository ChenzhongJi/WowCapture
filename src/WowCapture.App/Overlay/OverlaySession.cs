using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WowCapture.App.Imaging;
using WowCapture.Core.Abstractions;
using WowCapture.Core.Annotations;
using WowCapture.Core.Geometry;
using WowCapture.Editor.Rendering;
using WowCapture.Platform.Interop;
using WowCapture.Platform.Screen;
using WowCapture.Platform.Windows;

namespace WowCapture.App.Overlay;

/// <summary>
/// 一次截图会话的协调者：遮罩、选区、窗口吸附、标注，结束时给出选区与标注快照。
/// </summary>
/// <remarks>
/// <para>
/// 选区与标注都只有一份，用虚拟屏物理坐标表达，每块屏的窗口只渲染自己与它的交集。
/// 这样"从主屏拖到左侧副屏"不需要任何特殊分支——负坐标天然成立。
/// </para>
/// <para>
/// 标注是不可变记录，撤销靠保存列表快照实现（架构 D5）：一次撤销的成本是几个对象引用，
/// 而不是一张 66MB 的位图。
/// </para>
/// </remarks>
internal sealed class OverlaySession : IDisposable
{
    private const int HoverDebounceMs = 80;

    /// <summary>自由绘制时，两点间距小于这个物理像素数就不记，避免点集爆炸。</summary>
    private const int StrokeMinStep = 2;

    /// <summary>马赛克默认块大小（物理像素）。</summary>
    private const int MosaicBlockSize = 12;

    /// <summary>文字标注默认字号（物理像素）。</summary>
    private const int TextFontSizePhysical = 28;

    /// <summary>序号标记半径（物理像素）。</summary>
    private const int BadgeRadius = 18;

    /// <summary>荧光笔的透明度：让底下的文字仍然可读。</summary>
    private const byte HighlighterAlpha = 0x66;

    private readonly List<OverlayWindow> _windows = [];
    private readonly TaskCompletionSource<CaptureOutcome?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly DispatcherTimer _hoverDebounce;
    private readonly Stack<IReadOnlyList<Annotation>> _undoStack = new();
    private readonly Stack<IReadOnlyList<Annotation>> _redoStack = new();

    private IReadOnlyList<Annotation> _annotations = [];
    private Annotation? _pending;
    private List<ScreenPoint>? _strokePoints;
    private ScreenPoint _annotationStart;

    // ---- 标注编辑（选择工具） ----
    private Annotation? _selected;
    private int _selectedIndex = -1;
    private Annotation? _editOriginal;
    private IReadOnlyList<Annotation>? _preEditAnnotations;
    private ScreenPoint _editGrab;
    private bool _editing;

    private ScreenPoint _anchor;
    private ScreenPoint _lastPointer;
    private bool _hasPointer;
    private bool _dragging;
    private bool _closed;
    private bool _disposed;
    private WindowHit? _hover;

    private OverlaySession(BitmapSource frame, ScreenRect virtualBounds, IReadOnlyList<MonitorInfo> monitors, double dimOpacity)
    {
        FrameBitmap = frame;
        VirtualBounds = virtualBounds;
        Monitors = monitors;
        DimOpacity = dimOpacity;
        Style = AnnotationStyle.Default;

        _hoverDebounce = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(HoverDebounceMs),
        };
        _hoverDebounce.Tick += (_, _) =>
        {
            _hoverDebounce.Stop();
            ResolveHover();
        };
    }

    /// <summary>整块虚拟屏的冻结图。裁剪选区时直接从这里取，保证与用户所见一致。</summary>
    public BitmapSource FrameBitmap { get; }

    public ScreenRect VirtualBounds { get; }

    public IReadOnlyList<MonitorInfo> Monitors { get; }

    public double DimOpacity { get; }

    public ScreenRect? Selection { get; private set; }

    public IReadOnlyList<Annotation> Annotations => _annotations;

    /// <summary>正在拖拽中、尚未提交的标注（用于预览）。</summary>
    public Annotation? PendingAnnotation => _pending;

    /// <summary>选择工具当前选中的标注（虚线描边的高亮对象）。</summary>
    public Annotation? SelectedAnnotation => _selected;

    public AnnotationTool ActiveTool { get; private set; } = AnnotationTool.Arrow;

    public AnnotationStyle Style { get; private set; }

    /// <summary>文字标注字号（物理像素）。</summary>
    public int TextFontSize => TextFontSizePhysical;

    /// <summary>当前鼠标悬停命中的窗口（吸附候选）。</summary>
    public WindowHit? HoverCandidate => _hover;

    /// <summary>吸附时要排除的进程。默认排除本进程，避免吸附到遮罩自己。</summary>
    internal int HoverExcludeProcessId { get; set; } = Environment.ProcessId;

    /// <summary>是否响应真实鼠标移动去重新解析吸附。自检会关掉它。</summary>
    internal bool HoverTrackingEnabled { get; set; } = true;

    /// <summary>完成时给出选区与标注；用户取消时为 <c>null</c>。</summary>
    public Task<CaptureOutcome?> Result => _completion.Task;

    /// <summary>用户确认时选择的后继动作（复制 / 贴图）。取消时无意义。</summary>
    public CaptureAction RequestedAction { get; private set; } = CaptureAction.Copy;

    /// <summary>当前所有遮罩窗口（自检用）。</summary>
    internal IReadOnlyList<OverlayWindow> Windows => _windows;

    public static OverlaySession Create(
        BitmapSource frame,
        ScreenRect virtualBounds,
        IReadOnlyList<MonitorInfo> monitors,
        double dimOpacity)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(monitors);

        return new OverlaySession(frame, virtualBounds, monitors, Math.Clamp(dimOpacity, 0, 1));
    }

    public void Show()
    {
        if (_windows.Count > 0)
        {
            throw new InvalidOperationException("该会话已经显示过。");
        }

        if (Monitors.Count == 0)
        {
            throw new InvalidOperationException("没有可用显示器。");
        }

        foreach (var monitor in Monitors)
        {
            var sourceRect = new Int32Rect(
                monitor.Bounds.X - VirtualBounds.X,
                monitor.Bounds.Y - VirtualBounds.Y,
                monitor.Bounds.Width,
                monitor.Bounds.Height);

            var monitorImage = new CroppedBitmap(FrameBitmap, sourceRect);
            monitorImage.Freeze();

            _windows.Add(new OverlayWindow(this, monitor, monitorImage));
        }

        foreach (var window in _windows)
        {
            window.ShowOverlay();
        }

        // 让 DWM 把这一帧合成出来再激活，避免"先亮后暗"的闪烁。
        NativeMethods.DwmFlush();

        // 再抬一次顶层：显示前后可能已有别的置顶窗口插入到顶层带，遮罩必须压在它们上面。
        foreach (var window in _windows)
        {
            window.BringToTop();
        }

        // 键盘焦点给鼠标所在的那块屏。
        var cursor = ScreenCursor.GetPosition();
        var focus = _windows.FirstOrDefault(w => w.PhysicalBounds.Contains(cursor)) ?? _windows[0];
        focus.ActivateOverlay();
    }

    public void SelectTool(AnnotationTool tool)
    {
        ActiveTool = tool;

        // 换工具时把画笔与编辑状态都收干净，避免上一笔的点集或选中的索引被下一个工具复用。
        _pending = null;
        _strokePoints = null;
        _selected = null;
        _editing = false;
        _editOriginal = null;
        _preEditAnnotations = null;
        InvalidateAll();
    }

    /// <summary>工具栏应该出现在哪块屏上（选区右上角所在的那块）。-1 表示还没有选区。</summary>
    public int GetToolbarMonitorIndex()
    {
        if (Selection is not { IsEmpty: false } selection || Monitors.Count == 0)
        {
            return -1;
        }

        var anchor = new ScreenPoint(selection.Right - 1, selection.Top);

        foreach (var monitor in Monitors)
        {
            if (monitor.Bounds.Contains(anchor))
            {
                return monitor.Index;
            }
        }

        return Monitors[0].Index;
    }

    public void SetStyle(AnnotationStyle style) => Style = style;

    /// <summary>鼠标移动时调用。移动期间不做吸附查询，停下 80ms 后再查。</summary>
    public void NotifyPointerMoved(ScreenPoint physical)
    {
        if (_closed)
        {
            return;
        }

        _lastPointer = physical;
        _hasPointer = true;

        if (!HoverTrackingEnabled)
        {
            return;
        }

        if (_dragging)
        {
            ClearHover();
            return;
        }

        // 重启动防抖：只有停住才会真正触发一次查询。
        _hoverDebounce.Stop();
        _hoverDebounce.Start();
    }

    public void BeginSelection(ScreenPoint physical)
    {
        _anchor = physical;
        _dragging = true;
        _hoverDebounce.Stop();
        ClearHover();
        ResetAnnotations();
        Selection = new ScreenRect(physical.X, physical.Y, 0, 0).Intersect(VirtualBounds);
        InvalidateAll();
    }

    public void UpdateSelection(ScreenPoint physical)
    {
        if (!_dragging)
        {
            return;
        }

        // 夹在虚拟屏内：允许用户把鼠标拖到屏幕边缘之外，但选区只算真实存在的像素，
        // 否则尺寸提示会显示一个根本截不到的数字。
        var rect = ScreenRect.FromPoints(_anchor, physical).Intersect(VirtualBounds);
        Selection = rect.IsEmpty ? null : rect;
        InvalidateAll();
    }

    public void EndSelection(ScreenPoint physical)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        var rect = ScreenRect.FromPoints(_anchor, physical).Intersect(VirtualBounds);

        // 1px 以内的"点击"不算选区，否则会剪出一张 1×1 的图让用户莫名其妙。
        Selection = rect.Width >= 2 && rect.Height >= 2 ? rect : null;
        InvalidateAll();
    }

    /// <summary>方向键微调：整体平移，并限制在虚拟屏范围内。</summary>
    public void NudgeSelection(int dx, int dy)
    {
        if (Selection is not { IsEmpty: false } current)
        {
            return;
        }

        var moved = current.Offset(dx, dy);

        // 不让选区被推出屏幕：先夹住，再保证尺寸不变。
        var x = Math.Clamp(moved.X, VirtualBounds.X, Math.Max(VirtualBounds.X, VirtualBounds.Right - moved.Width));
        var y = Math.Clamp(moved.Y, VirtualBounds.Y, Math.Max(VirtualBounds.Y, VirtualBounds.Bottom - moved.Height));

        Selection = new ScreenRect(x, y, moved.Width, moved.Height);
        InvalidateAll();
    }

    /// <summary>把当前悬停的窗口直接变成选区。返回是否有可吸附的窗口。</summary>
    public bool SelectHoveredWindow()
    {
        if (_hover is not { } hit)
        {
            return false;
        }

        _hoverDebounce.Stop();
        ResetAnnotations();
        Selection = hit.Bounds.Intersect(VirtualBounds);
        _hover = null;
        InvalidateAll();
        return !Selection.Value.IsEmpty;
    }

    // ---- 标注 ----

    /// <summary>选中区域内的按下：开始一条标注。</summary>
    public bool BeginAnnotation(ScreenPoint physical)
    {
        if (ActiveTool == AnnotationTool.Select)
        {
            // 选择工具由 BeginAnnotationEdit 负责；走到这里说明调用方路由错了。
            return false;
        }

        if (Selection is not { IsEmpty: false } selection || !selection.Contains(physical))
        {
            return false;
        }

        _annotationStart = physical;
        _strokePoints = ActiveTool is AnnotationTool.Pen or AnnotationTool.Highlighter ? [physical] : null;
        _pending = CreateAnnotation(_annotationStart, physical);
        InvalidateAll();
        return true;
    }

    public void UpdateAnnotation(ScreenPoint physical)
    {
        if (_pending is null)
        {
            return;
        }

        if (_strokePoints is not null)
        {
            var last = _strokePoints[^1];
            if (Math.Abs(physical.X - last.X) < StrokeMinStep && Math.Abs(physical.Y - last.Y) < StrokeMinStep)
            {
                return;
            }

            _strokePoints.Add(physical);
        }

        _pending = CreateAnnotation(_annotationStart, physical);
        InvalidateAll();
    }

    public void EndAnnotation(ScreenPoint physical)
    {
        if (_pending is null)
        {
            return;
        }

        var annotation = CreateAnnotation(_annotationStart, physical);
        _pending = null;
        _strokePoints = null;

        if (IsTooSmall(annotation))
        {
            InvalidateAll();
            return;
        }

        PushUndo();
        _annotations = [.. _annotations, annotation];
        InvalidateAll();
    }

    public void Undo()
    {
        if (_undoStack.Count == 0)
        {
            return;
        }

        _redoStack.Push(_annotations);
        _annotations = _undoStack.Pop();

        // 列表变了，选中的索引不再可信。
        _selected = null;
        InvalidateAll();
    }

    public void Redo()
    {
        if (_redoStack.Count == 0)
        {
            return;
        }

        _undoStack.Push(_annotations);
        _annotations = _redoStack.Pop();
        _selected = null;
        InvalidateAll();
    }

    // ---- 标注编辑（T2.1：画错不再只能撤销重画） ----

    /// <summary>选择工具下命中测试：返回最上层命中的标注索引；-1 表示没命中或不在选区内。</summary>
    public int HitTestAnnotation(ScreenPoint physical)
    {
        if (Selection is not { IsEmpty: false } selection || !selection.Contains(physical))
        {
            return -1;
        }

        for (var i = _annotations.Count - 1; i >= 0; i--)
        {
            if (_annotations[i].GetBounds().Contains(physical))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// 选择工具下按下：命中则"抓起"最上层的标注准备拖动（返回 true）；没命中则只清除选中（返回 false）。
    /// </summary>
    public bool BeginAnnotationEdit(ScreenPoint physical)
    {
        if (ActiveTool != AnnotationTool.Select)
        {
            return false;
        }

        var index = HitTestAnnotation(physical);
        if (index < 0)
        {
            _selected = null;
            _editing = false;
            InvalidateAll();
            return false;
        }

        _editing = true;
        _selectedIndex = index;
        _editOriginal = _annotations[index];
        _preEditAnnotations = _annotations;
        _editGrab = physical;
        _selected = _annotations[index];
        InvalidateAll();
        return true;
    }

    /// <summary>拖动中：把被抓起的标注平移到当前指针位置（逐次生成新记录做预览）。</summary>
    public void UpdateAnnotationEdit(ScreenPoint physical)
    {
        if (!_editing || _editOriginal is not { } original)
        {
            return;
        }

        var moved = original.Translate(physical.X - _editGrab.X, physical.Y - _editGrab.Y);
        _selected = moved;

        // 逐次重建列表：标注是引用快照，这条路径的分配量可以忽略。
        var updated = new Annotation[_annotations.Count];
        for (var i = 0; i < _annotations.Count; i++)
        {
            updated[i] = i == _selectedIndex ? moved : _annotations[i];
        }

        _annotations = updated;
        InvalidateAll();
    }

    /// <summary>松开：有位移才把拖动前的快照压入撤销栈（原地不动不算一次操作）。</summary>
    public void EndAnnotationEdit(ScreenPoint physical)
    {
        if (!_editing)
        {
            return;
        }

        _editing = false;
        UpdateAnnotationEdit(physical);

        if (physical.X != _editGrab.X || physical.Y != _editGrab.Y)
        {
            if (_preEditAnnotations is not null)
            {
                _undoStack.Push(_preEditAnnotations);
                _redoStack.Clear();
            }
        }
        else
        {
            // 没有位移：恢复原列表，不污染撤销栈。
            if (_preEditAnnotations is not null)
            {
                _annotations = _preEditAnnotations;
            }
        }

        _editOriginal = null;
        _preEditAnnotations = null;
        InvalidateAll();
    }

    /// <summary>Delete 键：删掉当前选中的标注，走撤销栈。</summary>
    public void DeleteSelectedAnnotation()
    {
        if (_selected is not { } target)
        {
            return;
        }

        PushUndo();
        _annotations = _annotations.Where(a => !ReferenceEquals(a, target)).ToArray();
        _selected = null;
        InvalidateAll();
    }

    /// <summary>方向键微调选中的标注。返回是否有标注在动（没有选中时调用方应转去微调选区）。</summary>
    public bool NudgeSelectedAnnotation(int dx, int dy)
    {
        if (_selected is not { } current)
        {
            return false;
        }

        var updated = _annotations.ToArray();
        var replaced = false;

        for (var i = 0; i < updated.Length; i++)
        {
            if (ReferenceEquals(updated[i], current))
            {
                PushUndo();
                updated[i] = current.Translate(dx, dy);
                _selected = updated[i];
                replaced = true;
                break;
            }
        }

        if (!replaced)
        {
            return false;
        }

        _annotations = updated;
        InvalidateAll();
        return true;
    }

    /// <summary>
    /// 在指定位置放一个序号标记，编号自动递增。
    /// </summary>
    /// <remarks>
    /// 编号取"现有最大编号 + 1"而不是维护一个计数器：这样撤销之后再点，会补回同样的编号，
    /// 不会出现 ①② 撤销 ② 再点变成 ③ 的跳号。
    /// </remarks>
    public void AddBadge(ScreenPoint center)
    {
        if (Selection is not { IsEmpty: false } selection || !selection.Contains(center))
        {
            return;
        }

        var number = _annotations
            .OfType<BadgeAnnotation>()
            .Select(b => b.Number)
            .DefaultIfEmpty(0)
            .Max() + 1;

        var annotation = new BadgeAnnotation(center, number, Style.Stroke, BadgeRadius);

        PushUndo();
        _annotations = [.. _annotations, annotation];
        InvalidateAll();
    }

    /// <summary>
    /// 内联输入框提交文字。位置是文字左上角的虚拟屏物理坐标。
    /// </summary>
    public void AddTextAnnotation(string text, ScreenPoint position)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        var bounds = TextMetrics.Measure(trimmed, TextFontSizePhysical, position);
        var annotation = new TextAnnotation(position, trimmed, TextFontSizePhysical, Style.Stroke, bounds);

        PushUndo();
        _annotations = [.. _annotations, annotation];
        InvalidateAll();
    }

    /// <summary>自检用：直接追加一条标注（走与真实提交相同的快照逻辑）。</summary>
    internal void AddAnnotationForTest(Annotation annotation)
    {
        PushUndo();
        _annotations = [.. _annotations, annotation];
        InvalidateAll();
    }

    private Annotation CreateAnnotation(ScreenPoint start, ScreenPoint end) => ActiveTool switch
    {
        AnnotationTool.Rectangle => new RectangleAnnotation(ScreenRect.FromPoints(start, end), Style),
        AnnotationTool.Ellipse => new EllipseAnnotation(ScreenRect.FromPoints(start, end), Style),
        AnnotationTool.Line => new LineAnnotation(start, end, Style),
        AnnotationTool.Pen => new StrokeAnnotation(_strokePoints?.ToArray() ?? [start, end], Style),
        AnnotationTool.Highlighter => new StrokeAnnotation(
            _strokePoints?.ToArray() ?? [start, end],
            // 荧光笔 = 当前颜色 + 半透明 + 加粗。硬编码黄色会让工具栏的选色对它失效。
            Style with
            {
                Stroke = Style.Stroke with { A = HighlighterAlpha },
                StrokeWidth = Math.Max(Style.StrokeWidth * 4, 18),
            }),
        AnnotationTool.Mosaic => new MosaicAnnotation(ScreenRect.FromPoints(start, end), MosaicBlockSize),
        _ => new ArrowAnnotation(start, end, Style),
    };

    private static bool IsTooSmall(Annotation annotation)
    {
        var bounds = annotation.GetBounds();
        return bounds.Width < 3 && bounds.Height < 3;
    }

    private void PushUndo()
    {
        _undoStack.Push(_annotations);

        // 新操作让"重做"失效，否则重做会跳到一个已经不存在的历史分支。
        _redoStack.Clear();
    }

    private void ResetAnnotations()
    {
        _annotations = [];
        _undoStack.Clear();
        _redoStack.Clear();
        _pending = null;
        _strokePoints = null;
        _selected = null;
        _editing = false;
        _editOriginal = null;
        _preEditAnnotations = null;
    }

    public void RequestConfirm()
    {
        RequestedAction = CaptureAction.Copy;

        if (Selection is { IsEmpty: false } selection)
        {
            Close(new CaptureOutcome(selection, [.. _annotations]));
        }
    }

    /// <summary>把当前选区贴到屏幕上。</summary>
    public void RequestPin()
    {
        RequestedAction = CaptureAction.Pin;

        if (Selection is { IsEmpty: false } selection)
        {
            Close(new CaptureOutcome(selection, [.. _annotations]));
        }
    }

    public void Cancel() => Close(null);

    /// <summary>自检用：直接摆一个选区，走与真实确认完全相同的渲染与裁剪路径。</summary>
    internal void SetSelectionForTest(ScreenRect selection)
    {
        Selection = selection;
        InvalidateAll();
    }

    /// <summary>自检用：绕过防抖，立刻按指定坐标做一次吸附查询。</summary>
    internal WindowHit? SetHoverForTest(ScreenPoint physical, int excludeProcessId)
    {
        // 探针窗口属于本进程，所以这里允许把排除规则改成 0。
        HoverExcludeProcessId = excludeProcessId;

        _lastPointer = physical;
        _hasPointer = true;

        var hit = TopLevelWindowFinder.FindAt(physical, excludeProcessId);
        SetHover(hit);
        return hit;
    }

    private void ResolveHover()
    {
        if (_closed || _dragging || !_hasPointer)
        {
            return;
        }

        // 按住 Ctrl 临时关闭吸附，回到自由框选。
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            ClearHover();
            return;
        }

        SetHover(TopLevelWindowFinder.FindAt(_lastPointer, HoverExcludeProcessId));
    }

    private void SetHover(WindowHit? hit)
    {
        if (Nullable.Equals(_hover, hit))
        {
            return;
        }

        _hover = hit;
        InvalidateAll();
    }

    private void ClearHover()
    {
        if (_hover is null)
        {
            return;
        }

        _hover = null;
        InvalidateAll();
    }

    private void Close(CaptureOutcome? result)
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _hoverDebounce.Stop();

        foreach (var window in _windows)
        {
            window.Close();
        }

        _windows.Clear();
        _completion.TrySetResult(result);
    }

    private void InvalidateAll()
    {
        foreach (var window in _windows)
        {
            window.InvalidateSurface();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _hoverDebounce.Stop();

        if (!_closed)
        {
            _closed = true;
            foreach (var window in _windows)
            {
                window.Close();
            }

            _windows.Clear();
            _completion.TrySetResult(null);
        }
    }
}
