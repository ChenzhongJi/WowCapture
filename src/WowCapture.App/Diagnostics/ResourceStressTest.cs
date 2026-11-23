using System.Diagnostics;
using WowCapture.App.Imaging;
using WowCapture.App.Overlay;
using WowCapture.App.Output;
using WowCapture.App.Services;
using WowCapture.Capture;
using WowCapture.Core.Geometry;
using WowCapture.Platform;
using WowCapture.Platform.Screen;

namespace WowCapture.App.Diagnostics;

/// <summary>
/// T1.9：连续抓屏压测。看两件事——托管内存能不能回到基线，以及 GDI/USER 句柄有没有泄漏。
/// </summary>
/// <remarks>
/// 截图工具最常见的慢性病不是崩，而是"开着一天之后内存 2GB、字都拖不动"：
/// 每次截图多留一个 DC 或 DIB。GDI 对象计数是唯一能在秒级暴露它的指标。
/// </remarks>
internal static class ResourceStressTest
{
    public static int Run(int iterations, FileLog log)
    {
        iterations = Math.Clamp(iterations, 5, 200);

        var capturer = new GdiScreenCapturer();
        var monitors = new Win32MonitorService().GetMonitors();

        // 预热：排除 JIT、首次大块分配、首个 DIB 创建的干扰。
        _ = capturer.CaptureVirtualScreen();

        ForceCollect();
        var baseGdi = ProcessResources.GdiObjectCount;
        var baseUser = ProcessResources.UserObjectCount;
        var baseManaged = GC.GetTotalMemory(true);
        var basePrivate = ProcessResources.PrivateMemoryBytes;

        log.Info($"压测起点：迭代 {iterations} 次，GDI={baseGdi}，USER={baseUser}，托管={Mb(baseManaged)}，私有={Mb(basePrivate)}");

        var total = Stopwatch.StartNew();

        for (var i = 0; i < iterations; i++)
        {
            var frame = capturer.CaptureVirtualScreen();
            var bitmap = FrameBitmap.ToBitmapSource(frame);

            var rect = new ScreenRect(
                frame.Bounds.X + 100,
                frame.Bounds.Y + 100,
                Math.Min(800, Math.Max(2, frame.Width - 200)),
                Math.Min(500, Math.Max(2, frame.Height - 200)));

            var cropped = SelectionOutput.Crop(bitmap, frame.Bounds, rect);

            // 会话对象也走一遍（不 Show，避免整屏闪烁），验证它与位图的释放关系。
            var session = OverlaySession.Create(bitmap, frame.Bounds, monitors, 0.45);
            session.BeginSelection(new ScreenPoint(rect.X, rect.Y));
            session.UpdateSelection(new ScreenPoint(rect.Right, rect.Bottom));
            session.EndSelection(new ScreenPoint(rect.Right, rect.Bottom));
            session.Dispose();

            _ = cropped.PixelWidth;
        }

        total.Stop();
        ForceCollect();

        // GDI 对象销毁是异步的，留一点时间再读计数。
        Thread.Sleep(200);

        var endGdi = ProcessResources.GdiObjectCount;
        var endUser = ProcessResources.UserObjectCount;
        var endManaged = GC.GetTotalMemory(true);
        var endPrivate = ProcessResources.PrivateMemoryBytes;

        log.Info($"压测耗时：{total.Elapsed.TotalMilliseconds:F0}ms，平均 {total.Elapsed.TotalMilliseconds / iterations:F1}ms/次");
        log.Info($"GDI 对象：{baseGdi} → {endGdi}（Δ {endGdi - baseGdi}）");
        log.Info($"USER 对象：{baseUser} → {endUser}（Δ {endUser - baseUser}）");
        log.Info($"托管内存：{Mb(baseManaged)} → {Mb(endManaged)}（Δ {Mb(endManaged - baseManaged)}）");
        log.Info($"私有内存：{Mb(basePrivate)} → {Mb(endPrivate)}（Δ {Mb(endPrivate - basePrivate)}）");

        // 阈值放松到"允许一帧残留"：LOH 上的位图可能要到下次回收才释放，
        // 真正的泄漏（每次 +20MB）会远远超过这个量级。
        var gdiOk = endGdi - baseGdi <= 5;
        var userOk = endUser - baseUser <= 5;
        var managedOk = endManaged - baseManaged <= 40 * 1024 * 1024;

        log.Info($"判定：GDI={Result(gdiOk)}，USER={Result(userOk)}，托管内存={Result(managedOk)}");

        var ok = gdiOk && userOk && managedOk;
        log.Info(ok ? "资源压测：通过" : "资源压测：失败（疑似泄漏）");
        return ok ? 0 : 7;
    }

    private static void ForceCollect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static string Result(bool ok) => ok ? "通过" : "失败";

    private static string Mb(long bytes) => $"{bytes / 1024.0 / 1024.0:F1}MB";
}
