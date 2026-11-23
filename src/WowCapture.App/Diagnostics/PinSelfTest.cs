using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WowCapture.App.Pin;
using WowCapture.App.Services;
using WowCapture.Capture;
using WowCapture.Core.Geometry;
using WowCapture.Platform;
using WowCapture.Platform.Interop;
using WowCapture.Platform.Screen;

namespace WowCapture.App.Diagnostics;

/// <summary>
/// <c>--pin-test</c>：验证贴图窗口的创建、尺寸、置顶样式、真实渲染、缩放、透明度、
/// 点击穿透状态机与资源回收。
/// </summary>
/// <remarks>
/// <para>
/// 分两类断言：
/// <list type="bullet">
/// <item>确定性断言（必须通过）：窗口尺寸/样式、屏幕采样、缩放/透明度状态、穿透标志位、
/// 状态通知、句柄回收、反复贴图不累积。</item>
/// <item>真实输入注入（可跳过）：滚轮缩放、Alt+滚轮、点击穿透的最终行为。
/// 注入要求我们的窗口在前台且光标停在贴图上——应用在后台时 <c>SetForegroundWindow</c> 会被
/// 系统拒绝，光标也可能被真人移动，所以这类检查只在条件满足时执行，否则打印"跳过"。</item>
/// </list>
/// 这样自检不会因为"测试时有人在动鼠标"而假红，同时保留真机验证能力。
/// </para>
/// </remarks>
internal static class PinSelfTest
{
    private static readonly TimeSpan HardTimeout = TimeSpan.FromSeconds(25);

    public static async Task<int> RunAsync(FileLog log)
    {
        var core = RunCoreAsync(log);
        var finished = await Task.WhenAny(core, Task.Delay(HardTimeout));

        if (finished != core)
        {
            log.Warn($"贴图自检超过 {HardTimeout.TotalSeconds:F0}s 未完成，按失败处理");
            return 12;
        }

        return await core;
    }

    private static async Task<int> RunCoreAsync(FileLog log)
    {
        var monitor = new Win32MonitorService().GetMonitors().First();
        var scale = monitor.Scale > 0 ? monitor.Scale : 1.0;

        const int imageWidth = 120;
        const int imageHeight = 80;

        var image = CreateSolidBitmap(imageWidth, imageHeight, Colors.Red);
        var position = new ScreenRect(monitor.Bounds.X + 200, monitor.Bounds.Y + 200, imageWidth, imageHeight);
        var sampleX = position.X + 10;
        var sampleY = position.Y + 10;
        var center = new ScreenPoint(position.X + (imageWidth / 2), position.Y + (imageHeight / 2));

        ForceCollect();
        var baseGdi = ProcessResources.GdiObjectCount;
        var baseUser = ProcessResources.UserObjectCount;

        using var manager = new PinManager(log);

        var clickThroughNotifications = 0;
        manager.ClickThroughChanged += (_, _) => clickThroughNotifications++;

        var window = manager.Pin(image, position, scale);
        await Task.Delay(400);

        var hwnd = new WindowInteropHelper(window).Handle;

        // 1) 尺寸：物理像素 / 缩放 = DIP。
        var expectedWidth = imageWidth / scale;
        var expectedHeight = imageHeight / scale;
        var measuredWidth = window.Width;
        var measuredHeight = window.Height;
        var sizeOk = Math.Abs(measuredWidth - expectedWidth) < 1.5
                     && Math.Abs(measuredHeight - expectedHeight) < 1.5;

        // 2) 样式：置顶 + 不进 Alt+Tab。
        var exStyle = hwnd == IntPtr.Zero ? 0 : NativeMethods.GetWindowLong(hwnd, NativeMethods.GwlExStyle);
        var hasTopmost = (exStyle & 0x00000008) != 0;      // WS_EX_TOPMOST
        var hasToolWindow = (exStyle & NativeMethods.WsExToolWindow) != 0;
        var styleOk = hwnd != IntPtr.Zero && window.IsVisible && window.Topmost && hasTopmost && hasToolWindow;

        // 3) 真的画在屏幕上：采样贴合点应为纯红。
        var shot = new GdiScreenCapturer().CaptureVirtualScreen();
        var visibleOk = IsRed(shot.PixelAt(sampleX, sampleY));

        // 4) 缩放：2 倍后尺寸翻倍，且仍画在原位置。
        window.SetScaleForTest(2.0);
        await Task.Delay(250);
        var zoomedWidth = window.Width;
        var zoomSizeOk = Math.Abs(zoomedWidth - (expectedWidth * 2)) < 2;
        var zoomShot = new GdiScreenCapturer().CaptureVirtualScreen();
        var zoomVisibleOk = IsRed(zoomShot.PixelAt(sampleX, sampleY));

        // 5) 透明度。
        window.SetOpacityForTest(0.5);
        var measuredOpacity = window.Opacity;
        var opacityOk = Math.Abs(measuredOpacity - 0.5) < 0.01;

        // 6) 点击穿透的状态机与通知（这是托盘菜单"取消贴图穿透"可用状态的依据）。
        window.SetClickThrough(true);
        await Task.Delay(150);
        var transparentFlag =
            (NativeMethods.GetWindowLong(hwnd, NativeMethods.GwlExStyle) & NativeMethods.WsExTransparent) != 0;
        var managerSawClickThrough = manager.AnyClickThrough;
        var stillVisibleWhileTransparent = window.IsVisible && IsRed(
            new GdiScreenCapturer().CaptureVirtualScreen().PixelAt(sampleX, sampleY));

        window.SetClickThrough(false);
        await Task.Delay(150);
        var transparentCleared =
            (NativeMethods.GetWindowLong(hwnd, NativeMethods.GwlExStyle) & NativeMethods.WsExTransparent) == 0;

        var clickThroughOk = transparentFlag && managerSawClickThrough && transparentCleared && !manager.AnyClickThrough;
        var clickThroughStateOk = clickThroughNotifications == 2;

        // 7) 真实输入注入（仅在可控时执行，否则跳过并说明原因）。
        var injectedChecks = await RunInjectedInputChecksAsync(window, hwnd, center, sampleX, sampleY, log);

        // 8) 关闭后不残留窗口与句柄。窗口销毁是异步的，多等一会儿再统计，
        //    否则会把"还没回收"误判成"泄漏"。
        manager.CloseAll();
        await Task.Delay(700);

        var closedOk = manager.Count == 0 && !NativeMethods.IsWindow(hwnd);

        ForceCollect();
        await Task.Delay(300);
        ForceCollect();

        var gdiDelta = ProcessResources.GdiObjectCount - baseGdi;
        var userDelta = ProcessResources.UserObjectCount - baseUser;
        var resourcesOk = gdiDelta <= 30 && userDelta <= 25;

        // 9) 反复贴图/关闭不应累积句柄（陷阱清单第 22 条：迭代对比法）。
        for (var i = 0; i < 3; i++)
        {
            manager.Pin(image, position, scale);
            await Task.Delay(120);
            manager.CloseAll();
            await Task.Delay(120);
        }

        await Task.Delay(400);
        ForceCollect();

        var repeatGdiDelta = ProcessResources.GdiObjectCount - baseGdi;
        var repeatUserDelta = ProcessResources.UserObjectCount - baseUser;
        var noAccumulation = repeatGdiDelta - gdiDelta <= 5 && repeatUserDelta - userDelta <= 5;

        log.Info($"贴图尺寸：{(sizeOk ? "通过" : "失败")}（实测 {measuredWidth:F0}×{measuredHeight:F0} DIP，期望 {expectedWidth:F0}×{expectedHeight:F0}）");
        log.Info($"窗口样式：{(styleOk ? "通过" : "失败")}（EX=0x{exStyle:X}，TOPMOST={hasTopmost}，TOOLWINDOW={hasToolWindow}）");
        log.Info($"屏幕渲染：{(visibleOk ? "通过" : "失败")}（采样点应为纯红）");
        log.Info($"滚轮缩放：{(zoomSizeOk && zoomVisibleOk ? "通过" : "失败")}（2× 后宽度={zoomedWidth:F0} DIP，仍可见={zoomVisibleOk}）");
        log.Info($"透明度：{(opacityOk ? "通过" : "失败")}（{measuredOpacity:F2}）");
        log.Info(
            $"点击穿透状态：{(clickThroughOk ? "通过" : "失败")}"
            + $"（标志位设置={transparentFlag}，PinManager 感知={managerSawClickThrough}，"
            + $"穿透时仍可见={stillVisibleWhileTransparent}，恢复后清空={transparentCleared && !manager.AnyClickThrough}）");
        log.Info(
            $"穿透状态通知：{(clickThroughStateOk ? "通过" : "失败")}"
            + $"（收到 {clickThroughNotifications} 次变化通知，托盘菜单据此启用「取消贴图穿透」）");
        log.Info($"关闭清理：{(closedOk ? "通过" : "失败")}（剩余 {manager.Count} 张）");
        log.Info($"句柄回收：{(resourcesOk ? "通过" : "失败")}（GDI Δ{gdiDelta}，USER Δ{userDelta}）");
        log.Info($"反复贴图：{(noAccumulation ? "通过" : "失败")}（3 轮后 GDI Δ{repeatGdiDelta}、USER Δ{repeatUserDelta}）");

        if (injectedChecks is not null)
        {
            log.Info($"真实输入注入：{(injectedChecks.Value.Ok ? "通过" : "失败")}（{injectedChecks.Value.Detail}）");
        }

        var ok = sizeOk && styleOk && visibleOk && zoomSizeOk && zoomVisibleOk && opacityOk
                 && clickThroughOk && clickThroughStateOk
                 && closedOk && resourcesOk && noAccumulation
                 && (injectedChecks?.Ok ?? true);

        log.Info(ok ? "贴图自检：通过" : "贴图自检：失败");
        return ok ? 0 : 12;
    }

    /// <summary>
    /// 真实滚轮/点击注入。只在"贴图是前台窗口且光标确实停在贴图上"时执行，
    /// 否则返回 <c>null</c> 表示跳过（应用在后台时系统会拒绝前台切换）。
    /// </summary>
    private static async Task<(bool Ok, string Detail)?> RunInjectedInputChecksAsync(
        PinWindow window,
        IntPtr hwnd,
        ScreenPoint center,
        int sampleX,
        int sampleY,
        FileLog log)
    {
        NativeMethods.GetCursorPos(out var originalCursor);
        NativeMethods.SetCursorPos(center.X, center.Y);
        NativeMethods.SetForegroundWindow(hwnd);
        await Task.Delay(250);

        NativeMethods.GetCursorPos(out var cursorNow);
        var foreground = NativeMethods.GetForegroundWindow();
        var underCursor = NativeMethods.WindowFromPoint(new NativePoint { X = center.X, Y = center.Y });

        if (foreground != hwnd || underCursor != hwnd
            || cursorNow.X != center.X || cursorNow.Y != center.Y)
        {
            NativeMethods.SetCursorPos(originalCursor.X, originalCursor.Y);
            log.Info(
                $"真实输入注入：跳过（贴图不在前台或光标被移走：foreground=0x{foreground:X}，"
                + $"underCursor=0x{underCursor:X}，pin=0x{hwnd:X}，cursor=({cursorNow.X},{cursorNow.Y})）");
            return null;
        }

        // 滚轮 → 缩放，且不改透明度。
        window.SetScaleForTest(1.0);
        window.SetOpacityForTest(1.0);
        await Task.Delay(100);

        var scaleBefore = window.Scale;
        var opacityBefore = window.Opacity;
        NativeMethods.MouseEvent(NativeMethods.MouseEventWheel, 0, 0, 120, IntPtr.Zero);
        await Task.Delay(250);
        var wheelOk = window.Scale > scaleBefore + 0.05 && Math.Abs(window.Opacity - opacityBefore) < 0.001;

        // Alt+滚轮 → 透明度，且不改缩放。
        var scaleBeforeAlt = window.Scale;
        window.SetOpacityForTest(0.6);
        var opacityBeforeAlt = window.Opacity;
        NativeMethods.KeybdEvent(NativeMethods.VirtualKeyMenu, 0, 0, IntPtr.Zero);
        NativeMethods.MouseEvent(NativeMethods.MouseEventWheel, 0, 0, 120, IntPtr.Zero);
        NativeMethods.KeybdEvent(NativeMethods.VirtualKeyMenu, 0, NativeMethods.KeyEventKeyUp, IntPtr.Zero);
        await Task.Delay(250);
        var altWheelOk = window.Opacity > opacityBeforeAlt + 0.05
                         && Math.Abs(window.Scale - scaleBeforeAlt) < 0.001;

        // 点击：不穿透时贴图吃掉点击；穿透后点击落到下层（此处只验证贴图不再收到）。
        window.SetScaleForTest(1.0);
        window.SetOpacityForTest(1.0);
        await Task.Delay(100);

        var clicksBefore = window.MouseDownCount;
        NativeMethods.MouseEvent(NativeMethods.MouseEventLeftDown, 0, 0, 0, IntPtr.Zero);
        await Task.Delay(80);
        NativeMethods.MouseEvent(NativeMethods.MouseEventLeftUp, 0, 0, 0, IntPtr.Zero);
        await Task.Delay(250);
        var opaqueClickOk = window.MouseDownCount > clicksBefore;

        window.SetClickThrough(true);
        await Task.Delay(200);
        var clicksBeforeTransparent = window.MouseDownCount;
        NativeMethods.MouseEvent(NativeMethods.MouseEventLeftDown, 0, 0, 0, IntPtr.Zero);
        await Task.Delay(80);
        NativeMethods.MouseEvent(NativeMethods.MouseEventLeftUp, 0, 0, 0, IntPtr.Zero);
        await Task.Delay(250);
        var transparentClickOk = window.MouseDownCount == clicksBeforeTransparent;
        var stillVisible = IsRed(new GdiScreenCapturer().CaptureVirtualScreen().PixelAt(sampleX, sampleY));

        window.SetClickThrough(false);
        await Task.Delay(150);
        NativeMethods.SetCursorPos(originalCursor.X, originalCursor.Y);

        var ok = wheelOk && altWheelOk && opaqueClickOk && transparentClickOk && stillVisible;
        return (ok,
            $"滚轮缩放={wheelOk}，Alt+滚轮透明度={altWheelOk}，不穿透时吃掉点击={opaqueClickOk}，"
            + $"穿透后不吃点击={transparentClickOk}，穿透时仍可见={stillVisible}");
    }

    private static bool IsRed(ReadOnlySpan<byte> pixel) =>
        pixel.Length == 4 && pixel[2] > 200 && pixel[1] < 90 && pixel[0] < 90;

    private static BitmapSource CreateSolidBitmap(int width, int height, Color color)
    {
        var buffer = new byte[width * height * 4];

        for (var i = 0; i < buffer.Length; i += 4)
        {
            buffer[i] = color.B;
            buffer[i + 1] = color.G;
            buffer[i + 2] = color.R;
            buffer[i + 3] = 255;
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, buffer, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static void ForceCollect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
