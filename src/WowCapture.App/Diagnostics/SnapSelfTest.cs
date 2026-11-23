using System.IO;
using System.Windows;
using System.Windows.Interop;
using WowCapture.App.Imaging;
using WowCapture.App.Overlay;
using WowCapture.App.Services;
using WowCapture.Capture;
using WowCapture.Core.Geometry;
using WowCapture.Core.Imaging;
using WowCapture.Platform.Interop;
using WowCapture.Platform.Screen;
using WowCapture.Platform.Windows;

namespace WowCapture.App.Diagnostics;

/// <summary>
/// <c>--snap-test</c>：验证"悬停吸附整窗"。
/// </summary>
/// <remarks>
/// 造一个位置和标题都已知的探针窗口作为目标，于是这三点都能变成断言：
/// 查找器能否命中它、排除本进程后是否不再命中、高亮边框是否真的画在它的边界上。
/// 只看"感觉能吸附"是没法回归的。
/// </remarks>
internal static class SnapSelfTest
{
    private static readonly TimeSpan HardTimeout = TimeSpan.FromSeconds(25);

    public static async Task<int> RunAsync(FileLog log)
    {
        var core = RunCoreAsync(log);
        var finished = await Task.WhenAny(core, Task.Delay(HardTimeout));

        if (finished != core)
        {
            log.Warn($"吸附自检超过 {HardTimeout.TotalSeconds:F0}s 未完成，按失败处理");
            return 8;
        }

        return await core;
    }

    private static async Task<int> RunCoreAsync(FileLog log)
    {
        var capturer = new GdiScreenCapturer();
        var frame = capturer.CaptureVirtualScreen();
        var bitmap = FrameBitmap.ToBitmapSource(frame);
        var bounds = frame.Bounds;
        var monitors = new Win32MonitorService().GetMonitors();

        var probe = CreateProbeWindow();

        try
        {
            probe.Show();
            var probeHandle = new WindowInteropHelper(probe).Handle;

            var probeRect = new ScreenRect(bounds.X + 240, bounds.Y + 200, 640, 420);
            NativeMethods.SetWindowPos(
                probeHandle,
                new IntPtr(NativeMethods.HwndTopmost),
                probeRect.X,
                probeRect.Y,
                probeRect.Width,
                probeRect.Height,
                NativeMethods.SwpNoActivate);

            await Task.Delay(250);

            var center = new ScreenPoint(
                probeRect.X + (probeRect.Width / 2),
                probeRect.Y + (probeRect.Height / 2));

            // 1) 查找器能否命中探针。
            var found = TopLevelWindowFinder.FindAt(center, excludeProcessId: 0);
            var findOk = found is { } hit && hit.Handle == probeHandle;
            log.Info($"吸附查找：{(findOk ? "通过" : "失败")}（命中 \"{found?.Title ?? "无"}\"，期望 \"WowCapture SnapProbe\"）");

            if (!findOk)
            {
                log.Info("吸附自检：失败（找不到探针窗口，后续断言无意义）");
                return 8;
            }

            // 2) 排除本进程后不应再命中探针（生产环境就是这么调用的）。
            var excluded = TopLevelWindowFinder.FindAt(center, Environment.ProcessId);
            var excludeOk = excluded?.Handle != probeHandle;
            log.Info($"本进程排除：{(excludeOk ? "通过" : "失败")}（排除后命中 \"{excluded?.Title ?? "无"}\"）");

            var target = found!.Value.Bounds;
            log.Info($"命中边界：{target}（探针外框 {probeRect}；差值来自不可见边框）");

            // 3) 悬停高亮必须画在命中边界上。
            using var session = OverlaySession.Create(bitmap, bounds, monitors, 0.45);
            session.Show();

            // 关掉真实指针的重新解析，否则程序摆好的高亮会被真实光标位置覆盖。
            session.HoverTrackingEnabled = false;
            var hovered = session.SetHoverForTest(center, excludeProcessId: 0);
            log.Info($"高亮候选：{(hovered is { } h ? $"\"{h.Title}\" {h.Bounds}" : "无")}");

            await Task.Delay(450);

            // 先确认屏幕上最上层的是遮罩而不是探针，否则后面的像素断言没有意义。
            var topHandle = NativeMethods.WindowFromPoint(new NativePoint { X = center.X, Y = center.Y });
            var overlayHandle = session.Windows.Count > 0 ? session.Windows[0].Handle : IntPtr.Zero;
            log.Info($"屏幕最上层：0x{topHandle:X}（遮罩=0x{overlayHandle:X}，探针=0x{probeHandle:X}）");
            log.Info($"绘制次数：{session.Windows.Sum(w => w.RenderCount)}，高亮候选：{(session.HoverCandidate is { } still ? still.Title : "已被清空")}");

            var shot = capturer.CaptureVirtualScreen();
            var shotPath = Path.Combine(AppPaths.DataRoot, "snap-shot.png");
            FrameBitmap.SavePng(shot, shotPath);
            log.Info($"吸附截图：{shotPath}");

            foreach (var (label, x, y) in new[]
                     {
                         ("左上", target.Left, target.Top),
                         ("右上", target.Right - 1, target.Top),
                         ("左下", target.Left, target.Bottom - 1),
                         ("右下", target.Right - 1, target.Bottom - 1),
                     })
            {
                var pixel = shot.PixelAt(x, y);
                log.Info($"  角点{label} ({x},{y}) = {(pixel.IsEmpty ? "空" : $"R{pixel[2]} G{pixel[1]} B{pixel[0]}")}");
            }

            var borderOk = CheckHoverCorners(shot, target);
            log.Info($"吸附高亮渲染：{(borderOk ? "通过" : "失败")}（期望 #4FC3F7 = R79 G195 B247）");

            // 4) 点击整窗选择的状态契约（真实鼠标点击需人工验证，这里验结果）。
            var selected = session.SelectHoveredWindow();
            var selectOk = selected && session.Selection == target.Intersect(bounds);
            log.Info($"整窗选择：{(selectOk ? "通过" : $"失败（选区 {session.Selection?.ToString() ?? "null"}，期望 {target}）")}");

            var ok = excludeOk && borderOk && selectOk;
            log.Info(ok ? "吸附自检：通过" : "吸附自检：失败");
            return ok ? 0 : 8;
        }
        finally
        {
            probe.Close();
        }
    }

    private static Window CreateProbeWindow() => new()
    {
        Title = "WowCapture SnapProbe",
        Width = 400,
        Height = 260,
        WindowStyle = WindowStyle.SingleBorderWindow,
        ShowInTaskbar = false,
        Topmost = true,
        WindowStartupLocation = WindowStartupLocation.Manual,
    };

    /// <summary>四个角应落在吸附高亮色 #4FC3F7（BGRA：B=247, G=195, R=79）上。</summary>
    private static bool CheckHoverCorners(CapturedFrame shot, ScreenRect rect)
    {
        foreach (var (x, y) in new[]
                 {
                     (rect.Left, rect.Top),
                     (rect.Right - 1, rect.Top),
                     (rect.Left, rect.Bottom - 1),
                     (rect.Right - 1, rect.Bottom - 1),
                 })
        {
            var pixel = shot.PixelAt(x, y);

            if (pixel.IsEmpty
                || Math.Abs(pixel[0] - 247) > 70
                || Math.Abs(pixel[1] - 195) > 70
                || Math.Abs(pixel[2] - 79) > 70)
            {
                return false;
            }
        }

        return true;
    }
}
