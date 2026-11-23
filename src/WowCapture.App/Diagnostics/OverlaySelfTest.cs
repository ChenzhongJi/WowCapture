using System.IO;
using WowCapture.App.Imaging;
using WowCapture.App.Output;
using WowCapture.App.Overlay;
using WowCapture.App.Services;
using WowCapture.Capture;
using WowCapture.Core.Geometry;
using WowCapture.Core.Imaging;
using WowCapture.Platform.Interop;
using WowCapture.Platform.Screen;

namespace WowCapture.App.Diagnostics;

/// <summary>
/// <c>--overlay-test</c>：无人值守地走一遍"冻结帧 → 遮罩 → 选区 → 裁剪 → 剪贴板"，
/// 并把用户实际看到的遮罩截下来存成 PNG。
/// </summary>
/// <remarks>
/// 存在的理由：遮罩这条链路的错误几乎都是"看起来对、其实差几像素"或"画出来了但没显示"，
/// 单测覆盖不到，人工点一遍又不可重复。这个模式把它变成可复现、可存档的产物。
/// </remarks>
internal static class OverlaySelfTest
{
    private static readonly TimeSpan HardTimeout = TimeSpan.FromSeconds(25);

    public static async Task<int> RunAsync(FileLog log)
    {
        var core = RunCoreAsync(log);
        var finished = await Task.WhenAny(core, Task.Delay(HardTimeout));

        if (finished != core)
        {
            log.Warn($"遮罩自检超过 {HardTimeout.TotalSeconds:F0}s 未完成，按失败处理");
            return 5;
        }

        return await core;
    }

    private static async Task<int> RunCoreAsync(FileLog log)
    {
        var capturer = new GdiScreenCapturer();
        var frame = capturer.CaptureVirtualScreen();
        var bounds = frame.Bounds;
        var bitmap = FrameBitmap.ToBitmapSource(frame);
        var monitors = new Win32MonitorService().GetMonitors();

        log.Info($"遮罩自检：虚拟屏 {bounds}，显示器 {monitors.Count} 块");

        using var session = OverlaySession.Create(bitmap, bounds, monitors, dimOpacity: 0.45);
        session.Show();

        var selection = new ScreenRect(
            bounds.X + (bounds.Width / 6),
            bounds.Y + (bounds.Height / 5),
            bounds.Width / 2,
            bounds.Height / 3);

        session.SetSelectionForTest(selection);

        // 等 DWM 把遮罩与选区合成出来再截屏，否则截到的是上一帧。
        await Task.Delay(200);
        LogWindowState(session, log);

        await Task.Delay(400);

        var overlayShot = capturer.CaptureVirtualScreen();
        var shotPath = Path.Combine(AppPaths.DataRoot, "overlay-shot.png");
        FrameBitmap.SavePng(overlayShot, shotPath);
        log.Info($"遮罩截图：{shotPath}");

        var cropped = SelectionOutput.Crop(bitmap, bounds, selection);
        var resultPath = Path.Combine(AppPaths.DataRoot, "overlay-result.png");
        FrameBitmap.SavePng(cropped, resultPath);
        log.Info($"裁剪结果：{resultPath}（{cropped.PixelWidth}×{cropped.PixelHeight}）");

        var copied = SelectionOutput.CopyToClipboard(cropped, log);
        log.Info($"剪贴板写入：{(copied ? "成功" : "失败")}");

        // 断言而不是靠肉眼：在"含遮罩"的截图里找边框色、并比较选区内外的亮度。
        // 这两条能同时抓住"窗口没显示"和"显示了但没画"两类问题。
        var (borderOk, dimOk) = VerifyOverlayRendered(overlayShot, frame, selection);
        log.Info($"遮罩渲染校验：边框={Result(borderOk)}，压暗={Result(dimOk)}（选区外平均亮度应为原来的 ~55%）");

        var ok = copied
                 && cropped.PixelWidth == selection.Width
                 && cropped.PixelHeight == selection.Height
                 && borderOk
                 && dimOk;

        log.Info(ok ? "遮罩自检：通过" : "遮罩自检：失败");
        return ok ? 0 : 3;
    }

    private static string Result(bool ok) => ok ? "通过" : "失败";

    /// <summary>
    /// 校验遮罩真的出现在屏幕上：
    /// 1) 选区四角必须是边框色（说明窗口与选区坐标一致）；
    /// 2) 选区外的平均亮度必须明显低于冻结帧（说明压暗层确实画了）。
    /// </summary>
    private static (bool Border, bool Dim) VerifyOverlayRendered(CapturedFrame shot, CapturedFrame frame, ScreenRect selection)
    {
        var borderOk = true;

        foreach (var (x, y) in new[]
                 {
                     (selection.Left, selection.Top),
                     (selection.Right - 1, selection.Top),
                     (selection.Left, selection.Bottom - 1),
                     (selection.Right - 1, selection.Bottom - 1),
                 })
        {
            var pixel = shot.PixelAt(x, y);

            // 边框色 #1E90FF = (B=255, G=144, R=30)，BGRA 顺序。
            if (pixel.IsEmpty
                || Math.Abs(pixel[0] - 255) > 70
                || Math.Abs(pixel[1] - 144) > 70
                || Math.Abs(pixel[2] - 30) > 70)
            {
                borderOk = false;
                break;
            }
        }

        var dimSum = 0.0;
        var originalSum = 0.0;
        var samples = 0;

        for (var y = frame.Bounds.Y + 20; y < frame.Bounds.Bottom; y += 60)
        {
            for (var x = frame.Bounds.X + 20; x < frame.Bounds.Right; x += 60)
            {
                if (selection.Contains(new ScreenPoint(x, y)))
                {
                    continue;
                }

                var darkened = shot.PixelAt(x, y);
                var original = frame.PixelAt(x, y);
                if (darkened.IsEmpty || original.IsEmpty)
                {
                    continue;
                }

                dimSum += Luma(darkened);
                originalSum += Luma(original);
                samples++;
            }
        }

        // 45% 的压暗 → 亮度约为原来 55%；留足余量，只要求明显变暗。
        var dimOk = samples > 50 && originalSum > 0 && dimSum / originalSum < 0.8;

        return (borderOk, dimOk);
    }

    private static double Luma(ReadOnlySpan<byte> bgra) =>
        (bgra[2] * 0.299) + (bgra[1] * 0.587) + (bgra[0] * 0.114);

    /// <summary>把每个遮罩窗口的真实状态打出来：自检的价值就在于区分"没建窗口"和"建了但没画"。</summary>
    private static void LogWindowState(OverlaySession session, FileLog log)
    {
        foreach (var window in session.Windows)
        {
            var hwnd = window.Handle;
            var rectText = "n/a";
            var style = 0;
            var exStyle = 0;

            if (hwnd != IntPtr.Zero)
            {
                style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GwlStyle);
                exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GwlExStyle);

                if (NativeMethods.GetWindowRect(hwnd, out var rect))
                {
                    rectText = $"{rect.Left},{rect.Top} {rect.Width}x{rect.Height}";
                }
            }

            log.Info(
                $"窗口 hwnd=0x{hwnd:X} visible={window.IsVisible} 屏幕={rectText} " +
                $"DIP={window.ActualWidth:F0}x{window.ActualHeight:F0} drawn={window.RenderCount} " +
                $"WS_VISIBLE={(style & NativeMethods.WsVisible) != 0} EX=0x{exStyle:X}");
        }
    }
}
