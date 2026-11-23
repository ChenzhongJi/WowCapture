using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WowCapture.App.Output;
using WowCapture.App.Services;
using WowCapture.Platform;
using WowCapture.Platform.Interop;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace WowCapture.App.Pin;

/// <summary>
/// 贴图窗口（Pin）：把截图变成屏幕上的一块浮窗，可以拖动、缩放、调透明度，对照着抄东西用。
/// </summary>
/// <remarks>
/// <para>
/// 尺寸计算与遮罩窗口同一套规则：图片是物理像素，窗口是 DIP，<c>像素 / 缩放 = DIP</c>。
/// 因此 100% 缩放时图片严格 1:1 显示；放大时用最近邻保证像素清晰，缩小时用高质量插值避免锯齿。
/// </para>
/// <para>
/// 窗口无边框、置顶、不进 Alt+Tab，所以边框是"画上去"的（Grid 里叠一个 Rectangle），
/// 而不是用 Border 的 BorderThickness——后者会把内容区挤小 2 DIP，破坏 1:1。
/// </para>
/// </remarks>
internal sealed class PinWindow : Window
{
    private const double MinScale = 0.1;
    private const double MaxScale = 8.0;
    private const double MinOpacity = 0.15;
    private const double OpacityStep = 0.08;

    private readonly BitmapSource _image;
    private double _dpiScale;
    private readonly Image _imageView;
    private readonly FileLog _log;

    private double _scale = 1.0;

    public PinWindow(BitmapSource image, double dpiScale, double leftDip, double topDip, FileLog log)
    {
        _image = image;
        _dpiScale = dpiScale <= 0 ? 1.0 : dpiScale;
        _log = log;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;

        // 从创建时就是分层窗口：点击穿透依赖 WS_EX_LAYERED，
        // 而给已经创建的 WPF 窗口运行期补这个样式会导致整个窗口不再绘制。
        AllowsTransparency = true;
        Background = Brushes.Transparent;

        // 必须激活：Windows 把 WM_MOUSEWHEEL 发给"有焦点的窗口"，
        // 不激活的话用户滚轮完全没反应（自检里真实注入滚轮时发现的）。
        ShowActivated = true;

        SnapsToDevicePixels = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = leftDip;
        Top = topDip;

        _imageView = new Image
        {
            Source = image,
            Stretch = Stretch.Fill,
        };

        var frame = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = 1,
            IsHitTestVisible = false,
        };

        var root = new Grid();
        root.Children.Add(_imageView);
        root.Children.Add(frame);
        Content = root;

        ApplyScale(1.0, new Point(0, 0), force: true);

        SourceInitialized += OnSourceInitialized;
        DpiChanged += OnDpiChanged;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseWheel += OnMouseWheel;
        MouseRightButtonUp += OnMouseRightButtonUp;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>当前缩放比例（1.0 = 原始像素）。</summary>
    internal double Scale => _scale;

    /// <summary>是否处于点击穿透状态。</summary>
    public bool IsClickThrough { get; private set; }

    /// <summary>收到鼠标左键按下的次数（自检用来判断点击有没有被吃掉）。</summary>
    internal int MouseDownCount { get; private set; }

    /// <summary>点击穿透状态变化。托盘菜单要靠它刷新"取消贴图穿透"的可用状态。</summary>
    public event EventHandler? ClickThroughChanged;

    /// <summary>
    /// 开关点击穿透：开启后鼠标事件直接落到下面的窗口，贴图变成纯粹的文字对照板。
    /// </summary>
    /// <remarks>
    /// 穿透之后本窗口收不到鼠标事件，也就没法再用同样的手势关掉——
    /// 必须由外部（托盘菜单）恢复，这是这个功能天生的取舍。
    /// </remarks>
    public void SetClickThrough(bool enabled)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || IsClickThrough == enabled)
        {
            return;
        }

        var style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GwlExStyle);
        var updated = enabled
            ? style | NativeMethods.WsExTransparent
            : style & ~NativeMethods.WsExTransparent;

        NativeMethods.SetWindowLong(hwnd, NativeMethods.GwlExStyle, updated);

        // 注意：不要在这里加 WS_EX_LAYERED 或调用 SetLayeredWindowAttributes。
        // 窗口从创建起就是分层窗口（AllowsTransparency），WPF 用逐像素 alpha 绘制；
        // 运行期改成"整窗 alpha"会让 WPF 的内容完全不显示。
        // 改完扩展样式要通知窗口管理器重新计算命中测试，否则鼠标仍然会被吃掉。
        NativeMethods.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpFrameChanged);

        IsClickThrough = enabled;
        _log.Info(enabled
            ? "贴图已开启点击穿透（可在托盘菜单「取消贴图穿透」恢复）"
            : "贴图已恢复可点击");

        ClickThroughChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>自检用：直接设定缩放。</summary>
    internal void SetScaleForTest(double scale) => ApplyScale(scale, new Point(0, 0));

    /// <summary>自检用：直接设定透明度。</summary>
    internal void SetOpacityForTest(double opacity) => SetOpacity(opacity);

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;

        // 不进 Alt+Tab；置顶由 Topmost + 窗口样式共同保证。
        var exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GwlExStyle);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GwlExStyle, exStyle | NativeMethods.WsExToolWindow);
    }

    /// <summary>
    /// 拖到缩放不同的显示器后，WPF 会自动按新 DPI 重算窗口的 DIP 尺寸（物理尺寸不变），
    /// 但"1:1 像素"要求宽高 == 像素尺寸 / 新缩放 × 当前倍率——必须跟着重算，
    /// 否则贴图在新屏上不再 1:1，对照抄写会错位（贴图恰恰在混合 DPI 多屏下最常用）。
    /// </summary>
    private void OnDpiChanged(object? sender, DpiChangedEventArgs e)
    {
        _dpiScale = e.NewDpi.DpiScaleX <= 0 ? 1.0 : e.NewDpi.DpiScaleX;
        Width = (_image.PixelWidth / _dpiScale) * _scale;
        Height = (_image.PixelHeight / _dpiScale) * _scale;
        _log.Info($"贴图跨屏：新缩放 {e.NewDpi.DpiScaleX:P0}，已重算尺寸保持 1:1");
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        MouseDownCount++;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            SetClickThrough(true);
            e.Handled = true;
            return;
        }

        if (e.ClickCount == 2)
        {
            // 双击复位：回到 100% 与不透明。
            ApplyScale(1.0, new Point(Width / 2, Height / 2));
            SetOpacity(1.0);
            e.Handled = true;
            return;
        }

        // 点击即提到最前（多张贴图之间）。
        Activate();

        // 手动算 Left/Top 会和高 DPI 取整打架，拖起来一顿一顿的；
        // DragMove 交给系统模态移动循环，跟手且平滑。
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // 鼠标已松开（比如快速点击）时 DragMove 会抛异常，忽略即可。
        }

        e.Handled = true;
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var anchor = e.GetPosition(this);
        var modifiers = Keyboard.Modifiers;

        // 透明度用 Alt 而不是 Ctrl：精准触控板的双指捏合会合成 Ctrl+滚轮，
        // 用 Ctrl 会把"缩放"变成"调透明度"，用户会觉得滚轮行为完全不对。
        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            SetOpacity(Opacity + (e.Delta > 0 ? OpacityStep : -OpacityStep));
        }
        else
        {
            // 以光标为锚点缩放：光标下的内容保持不动，比"从左上角放大"顺手得多。
            // 滚轮事件每次滚动一格触发一次，连续滚动可达几十次，因此不逐格写日志。
            ApplyScale(_scale * (e.Delta > 0 ? 1.1 : 1 / 1.1), anchor);
        }

        e.Handled = true;
    }

    private void ApplyScale(double newScale, Point anchorDip, bool force = false)
    {
        var target = Math.Clamp(newScale, MinScale, MaxScale);
        if (!force && Math.Abs(target - _scale) < 1e-6)
        {
            return;
        }

        var ratio = target / _scale;

        // 让锚点下的像素留在原处：窗口左上角按比例反向外移。
        Left -= anchorDip.X * (ratio - 1);
        Top -= anchorDip.Y * (ratio - 1);

        _scale = target;
        Width = (_image.PixelWidth / _dpiScale) * target;
        Height = (_image.PixelHeight / _dpiScale) * target;

        // 放大用最近邻保留像素块，缩小用高质量避免摩尔纹。
        RenderOptions.SetBitmapScalingMode(
            _imageView,
            target >= 1 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
    }

    private void SetOpacity(double opacity) => Opacity = Math.Clamp(opacity, MinOpacity, 1.0);

    private void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var copy = new MenuItem { Header = "复制到剪贴板" };
        copy.Click += (_, _) =>
        {
            if (SelectionOutput.CopyToClipboard(_image, _log))
            {
                _log.Info($"贴图已复制：{_image.PixelWidth}×{_image.PixelHeight}");
            }
        };

        var save = new MenuItem { Header = "另存为…" };
        save.Click += (_, _) => SaveAs();

        var close = new MenuItem { Header = "关闭" };
        close.Click += (_, _) => Close();

        var menu = new ContextMenu();
        menu.Items.Add(copy);
        menu.Items.Add(save);
        menu.Items.Add(new Separator());
        menu.Items.Add(close);

        menu.PlacementTarget = this;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void SaveAs()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "另存为",
            Filter = "PNG 图片|*.png|JPEG 图片|*.jpg",
            FileName = $"pin-{DateTime.Now:yyyyMMdd-HHmmss}",
            DefaultExt = ".png",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            BitmapEncoder encoder = Path.GetExtension(dialog.FileName).ToLowerInvariant() == ".jpg"
                ? new JpegBitmapEncoder { QualityLevel = 92 }
                : new PngBitmapEncoder();

            encoder.Frames.Add(BitmapFrame.Create(_image));

            using var stream = File.Create(dialog.FileName);
            encoder.Save(stream);
            _log.Info($"贴图已另存：{dialog.FileName}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _log.Error("贴图另存失败", ex);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}
