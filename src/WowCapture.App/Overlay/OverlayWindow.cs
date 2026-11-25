using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WowCapture.Core.Abstractions;
using WowCapture.Core.Annotations;
using WowCapture.Core.Geometry;
using WowCapture.Core.Layout;
using WowCapture.Editor.Rendering;
using WowCapture.Platform;
using WowCapture.Platform.Interop;

namespace WowCapture.App.Overlay;

/// <summary>
/// 一块显示器上的遮罩窗口：不透明、无边框、置顶、不进 Alt+Tab。
/// </summary>
/// <remarks>
/// <para>
/// 它显示的是冻结帧里属于这块屏的那一部分，因此窗口可以完全不透明——
/// 这是 D1 的关键收益：不需要 <c>AllowsTransparency</c>，也就不会掉进软件渲染与合成闪烁。
/// </para>
/// <para>
/// 尺寸有两个来源必须同时满足：<c>SetWindowPos</c> 用物理像素定位，
/// WPF 的 <c>Width/Height</c> 用 DIP。两者的比例就是这块屏的缩放（见 D3 / OverlayPlacement）。
/// 只设其中一个，在高 DPI 下就会得到一块"差一点"的遮罩。
/// </para>
/// </remarks>
internal sealed class OverlayWindow : Window
{
    private readonly OverlaySession _session;
    private readonly OverlaySurface _surface;
    private readonly OverlayPlacement _placement;
    private readonly Canvas _overlayCanvas = new();

    private TextBox? _textBox;

    public OverlayWindow(OverlaySession session, MonitorInfo monitor, BitmapSource monitorImage)
    {
        _session = session;
        _placement = OverlayPlacement.For(monitor);
        PhysicalBounds = monitor.Bounds;
        MonitorIndex = monitor.Index;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 0;
        Top = 0;

        // 不透明背景：即使首帧还没画出来也只是一瞬间的黑色，而不是白闪。
        Background = Brushes.Black;

        Width = _placement.WidthDip;
        Height = _placement.HeightDip;

        _surface = new OverlaySurface(session, _placement, monitorImage, monitor.Index) { Host = this };

        // 遮罩面上盖一层 Canvas，专门放"内联文字输入框"这类临时控件。
        var root = new Grid();
        root.Children.Add(_surface);
        root.Children.Add(_overlayCanvas);
        Content = root;

        SourceInitialized += OnSourceInitialized;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>是否正在输入文字。用于让键盘事件优先给输入框，而不是触发工具快捷键。</summary>
    internal bool IsEditingText => _textBox is not null;

    /// <summary>在遮罩上开一个内联文字输入框。</summary>
    internal void BeginTextEdit(ScreenPoint physical)
    {
        // 已经有一个在编辑时再点一次 = 提交并结束。
        if (_textBox is not null)
        {
            CommitText();
            return;
        }

        var (dipX, dipY) = _placement.ToDip(physical);
        var scale = _placement.Scale;
        var color = _session.Style.Stroke;

        var textBox = new TextBox
        {
            MinWidth = 80,
            MaxWidth = Math.Max(120, ActualWidth - dipX - 16),
            FontFamily = new FontFamily(TextMetrics.FontFamilyName),
            // 输入框用 DIP，标注用物理像素：除以缩放即得同样的视觉大小。
            FontSize = Math.Max(10, _session.TextFontSize / scale),
            Foreground = new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B)),
            Background = new SolidColorBrush(Color.FromArgb(0xB4, 0x10, 0x10, 0x10)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x90, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(3, 1, 3, 1),
            AcceptsReturn = false,
            TextWrapping = TextWrapping.NoWrap,
            CaretBrush = Brushes.White,
        };

        textBox.PreviewKeyDown += OnTextBoxPreviewKeyDown;

        _overlayCanvas.Children.Add(textBox);
        Canvas.SetLeft(textBox, dipX);
        Canvas.SetTop(textBox, dipY);

        _textBox = textBox;
        textBox.Focus();
        Keyboard.Focus(textBox);
    }

    /// <summary>提交输入框里的文字。空文本视为取消。</summary>
    internal void CommitText()
    {
        if (_textBox is null)
        {
            return;
        }

        var text = _textBox.Text.Trim();
        var left = Canvas.GetLeft(_textBox);
        var top = Canvas.GetTop(_textBox);

        // 输入框有 1px 边框 + 3px 内边距，文字起点要把这部分补回来，否则导出后会偏移几个像素。
        const double inset = 1 + 3;
        var physical = _placement.ToPhysical(left + inset, top + inset);

        RemoveTextBox();

        if (text.Length > 0)
        {
            _session.AddTextAnnotation(text, physical);
        }
    }

    internal void CancelText() => RemoveTextBox();

    private void RemoveTextBox()
    {
        if (_textBox is null)
        {
            return;
        }

        _textBox.PreviewKeyDown -= OnTextBoxPreviewKeyDown;
        _overlayCanvas.Children.Remove(_textBox);
        _textBox = null;

        _surface.Focus();
    }

    private void OnTextBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            CommitText();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            CancelText();
            e.Handled = true;
        }
    }

    public ScreenRect PhysicalBounds { get; }

    public int MonitorIndex { get; }

    /// <summary>绘制面（自检用）。</summary>
    internal OverlaySurface Surface => _surface;

    /// <summary>本地 DIP 坐标 → 虚拟屏物理坐标（自检用）。</summary>
    internal ScreenPoint ToPhysical(Point dip) => _placement.ToPhysical(dip.X, dip.Y);

    /// <summary>窗口句柄（自检与诊断用）。</summary>
    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    /// <summary>实际绘制的次数（自检用）。</summary>
    public int RenderCount => _surface.RenderCount;

    public void InvalidateSurface() => _surface.InvalidateSurface();

    /// <summary>显示并把自己钉到目标物理矩形上。</summary>
    public void ShowOverlay()
    {
        Show();
        ApplyPhysicalPlacement();
    }

    public void ActivateOverlay()
    {
        var hwnd = new WindowInteropHelper(this).Handle;

        // 热键刚触发时本进程是前台，SetForegroundWindow 通常会被放行；
        // 拿不到焦点就收不到 Esc/方向键，这是必须尝试的一步。
        NativeMethods.SetForegroundWindow(hwnd);
        Activate();
        _surface.Focus();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;

        // WS_EX_TOOLWINDOW：不出现在 Alt+Tab 和任务视图里。
        var exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GwlExStyle);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GwlExStyle, exStyle | NativeMethods.WsExToolWindow);

        ApplyPhysicalPlacement();
    }

    /// <summary>
    /// 把窗口重新抬到顶层。别的置顶窗口（播放器、悬浮工具、我们的探针窗口）可能在遮罩显示之后
    /// 又占据了顶层带；不重新抬一次，遮罩就会被压在下面——表现为"截图时有些区域没被压暗"。
    /// </summary>
    public void BringToTop()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            hwnd,
            new IntPtr(NativeMethods.HwndTopmost),
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
    }

    /// <summary>
    /// 用物理像素精确摆放。之所以不直接用 WPF 的 Left/Top，是因为它们是 DIP，
    /// 跨 DPI 时会经过二次换算，正是偏移 bug 的来源。
    /// </summary>
    private void ApplyPhysicalPlacement()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            hwnd,
            new IntPtr(NativeMethods.HwndTopmost),
            PhysicalBounds.X,
            PhysicalBounds.Y,
            PhysicalBounds.Width,
            PhysicalBounds.Height,
            NativeMethods.SwpNoActivate);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // 正在输入文字：所有按键交给输入框（Enter 提交、Esc 取消由它自己处理），
        // 否则在这里会把字母当成工具快捷键吞掉。
        if (IsEditingText)
        {
            return;
        }

        // Ctrl+Z 撤销标注（放在最前面，避免 Z 被当成工具键）。
        if (e.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                _session.Redo();
            }
            else
            {
                _session.Undo();
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.Y && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            _session.Redo();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.None && TrySelectTool(e.Key))
        {
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.F3:
                _session.RequestPin();
                e.Handled = true;
                break;

            case Key.Delete:
                // 选择工具下删掉高亮的标注；其他工具按 Delete 不做事（误按不破坏画面）。
                _session.DeleteSelectedAnnotation();
                e.Handled = true;
                break;

            case Key.Escape:
                _session.Cancel();
                e.Handled = true;
                break;

            case Key.Enter:
                _session.RequestConfirm();
                e.Handled = true;
                break;

            case Key.Left:
            case Key.Right:
            case Key.Up:
            case Key.Down:
                var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
                var dx = e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0;
                var dy = e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0;

                // 有选中的标注就微调标注，否则微调选区。
                if (!_session.NudgeSelectedAnnotation(dx, dy))
                {
                    _session.NudgeSelection(dx, dy);
                }

                e.Handled = true;
                break;
        }
    }

    /// <summary>字母与数字都能切工具：字母好记，数字适合左手操作。</summary>
    private bool TrySelectTool(Key key)
    {
        var tool = key switch
        {
            Key.V or Key.D0 or Key.NumPad0 => AnnotationTool.Select,
            Key.A or Key.D1 or Key.NumPad1 => AnnotationTool.Arrow,
            Key.R or Key.D2 or Key.NumPad2 => AnnotationTool.Rectangle,
            Key.E or Key.D3 or Key.NumPad3 => AnnotationTool.Ellipse,
            Key.L or Key.D4 or Key.NumPad4 => AnnotationTool.Line,
            Key.P or Key.D5 or Key.NumPad5 => AnnotationTool.Pen,
            Key.H or Key.D6 or Key.NumPad6 => AnnotationTool.Highlighter,
            Key.M or Key.D7 or Key.NumPad7 => AnnotationTool.Mosaic,
            Key.T or Key.D8 or Key.NumPad8 => AnnotationTool.Text,
            Key.N or Key.D9 or Key.NumPad9 => AnnotationTool.Badge,
            _ => (AnnotationTool?)null,
        };

        if (tool is not { } selected)
        {
            return false;
        }

        _session.SelectTool(selected);
        return true;
    }
}
