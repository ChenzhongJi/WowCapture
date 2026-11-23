using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WowCapture.App.Imaging;
using WowCapture.App.Overlay;
using WowCapture.App.Output;
using WowCapture.App.Services;
using WowCapture.Capture;
using WowCapture.Core.Annotations;
using WowCapture.Core.Geometry;
using WowCapture.Core.Imaging;
using WowCapture.Platform.Screen;
using WowCapture.Platform.Windows;
using WowCapture.Editor.Rendering;

namespace WowCapture.App.Diagnostics;

/// <summary>
/// <c>--annotate-test</c>：验证标注的渲染与合成导出。
/// </summary>
/// <remarks>
/// 断言分三层，缺一不可：
/// 1) 撤销把标注列表恢复到上一步（状态层）；
/// 2) 屏幕截图里能采到标注色（预览层）；
/// 3) 导出图的尺寸等于选区、且同一位置同样有标注色（合成层）。
/// 只测第 1 层会漏掉"画了但没渲染"，只测第 3 层会漏掉"预览和导出不一致"。
/// </remarks>
internal static class AnnotationSelfTest
{
    private static readonly TimeSpan HardTimeout = TimeSpan.FromSeconds(25);

    public static async Task<int> RunAsync(FileLog log)
    {
        var core = RunCoreAsync(log);
        var finished = await Task.WhenAny(core, Task.Delay(HardTimeout));

        if (finished != core)
        {
            log.Warn($"标注自检超过 {HardTimeout.TotalSeconds:F0}s 未完成，按失败处理");
            return 10;
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

        using var session = OverlaySession.Create(bitmap, bounds, monitors, 0.45);
        session.Show();
        session.HoverTrackingEnabled = false;

        var selection = new ScreenRect(
            bounds.X + (bounds.Width / 5),
            bounds.Y + (bounds.Height / 5),
            bounds.Width / 2,
            bounds.Height / 2);
        session.SetSelectionForTest(selection);

        var rect = new ScreenRect(selection.X + 120, selection.Y + 100, 380, 240);
        session.SelectTool(AnnotationTool.Rectangle);
        session.AddAnnotationForTest(new RectangleAnnotation(rect, AnnotationStyle.Default));

        var arrowStart = new ScreenPoint(selection.X + 600, selection.Y + 600);
        var arrowEnd = new ScreenPoint(selection.X + 950, selection.Y + 250);
        session.SelectTool(AnnotationTool.Arrow);
        session.AddAnnotationForTest(new ArrowAnnotation(arrowStart, arrowEnd, AnnotationStyle.Default));

        // 画笔：一条水平直线，后面用在导出图里采样验证点集渲染。
        var strokeY = selection.Y + 700;
        var strokePoints = Enumerable.Range(0, 40)
            .Select(i => new ScreenPoint(selection.X + 150 + (i * 8), strokeY))
            .ToArray();
        session.SelectTool(AnnotationTool.Pen);
        session.AddAnnotationForTest(new StrokeAnnotation(strokePoints, AnnotationStyle.Default));

        // 马赛克：区域放在文字密集处，否则"像素化"和"没画"在纯色背景上无法区分。
        var mosaicRect = new ScreenRect(selection.X + 560, selection.Y + 60, 560, 200);
        session.SelectTool(AnnotationTool.Mosaic);
        session.AddAnnotationForTest(new MosaicAnnotation(mosaicRect, 12));

        // 文字：走真实提交路径（AddTextAnnotation 就是内联输入框提交时调的方法）。
        var textPosition = new ScreenPoint(selection.X + 120, selection.Y + 780);
        session.SelectTool(AnnotationTool.Text);
        session.AddTextAnnotation("Hello 世界", textPosition);
        var textBounds = TextMetrics.Measure("Hello 世界", session.TextFontSize, textPosition);

        // 序号：连点三次应得到 1、2、3；撤销后补点应补回同一个编号（不跳号）。
        session.SelectTool(AnnotationTool.Badge);
        var badge1 = new ScreenPoint(selection.X + 280, selection.Y + 500);
        session.AddBadge(badge1);
        session.AddBadge(new ScreenPoint(selection.X + 340, selection.Y + 500));
        session.AddBadge(new ScreenPoint(selection.X + 400, selection.Y + 500));

        var numbers = session.Annotations.OfType<BadgeAnnotation>().Select(b => b.Number).ToArray();
        var numberingOk = numbers.Length == 3 && numbers[0] == 1 && numbers[1] == 2 && numbers[2] == 3;

        session.Undo();
        session.AddBadge(new ScreenPoint(selection.X + 460, selection.Y + 500));
        var renumberOk = session.Annotations.OfType<BadgeAnnotation>().Last().Number == 3;

        // 荧光笔：必须用当前选中的颜色 + 半透明，而不是写死的黄色。
        session.SetStyle(session.Style.WithStroke(ArgbColor.Blue));
        session.SelectTool(AnnotationTool.Highlighter);
        session.BeginAnnotation(new ScreenPoint(selection.X + 600, selection.Y + 760));
        session.UpdateAnnotation(new ScreenPoint(selection.X + 760, selection.Y + 760));
        session.EndAnnotation(new ScreenPoint(selection.X + 760, selection.Y + 760));

        var highlighter = session.Annotations.OfType<StrokeAnnotation>().Last();
        var highlighterOk = highlighter.Style.Stroke.A == 0x66
                            && highlighter.Style.Stroke.R == ArgbColor.Blue.R
                            && highlighter.Style.Stroke.G == ArgbColor.Blue.G
                            && highlighter.Style.Stroke.B == ArgbColor.Blue.B;
        log.Info(
            $"荧光笔取色：{(highlighterOk ? "通过" : "失败")}"
            + $"（A={highlighter.Style.Stroke.A}，RGB={highlighter.Style.Stroke.R},{highlighter.Style.Stroke.G},{highlighter.Style.Stroke.B}，期望 A=102 RGB=10,132,255）");

        // 撤销：加一条线再撤掉，数量应回到之前。
        var beforeUndo = session.Annotations.Count;
        session.AddAnnotationForTest(new LineAnnotation(arrowStart, new ScreenPoint(arrowStart.X + 200, arrowStart.Y), AnnotationStyle.Default));
        session.Undo();
        var undoOk = session.Annotations.Count == beforeUndo;
        log.Info($"撤销：{(undoOk ? "通过" : $"失败（{session.Annotations.Count} 条，期望 {beforeUndo} 条）")}");

        await Task.Delay(400);

        // 屏幕预览断言：矩形左边框中部应该是红色。
        var shot = capturer.CaptureVirtualScreen();
        var shotPath = Path.Combine(AppPaths.DataRoot, "annotate-shot.png");
        FrameBitmap.SavePng(shot, shotPath);
        log.Info($"标注截图：{shotPath}");

        var screenPixelOk = IsAnnotationRed(shot.PixelAt(rect.Left, rect.Top + (rect.Height / 2)));
        log.Info($"屏幕渲染：{(screenPixelOk ? "通过" : "失败")}（矩形左边框应为 #FF3B30）");

        // 工具栏：所有按钮必须同尺寸（曾经图标画在 50×50 的框里、色点比别人小一圈，看起来歪歪扭扭）。
        var hostWindow = session.Windows.FirstOrDefault(w => w.MonitorIndex == session.GetToolbarMonitorIndex());
        var toolbar = hostWindow?.Surface.LastToolbar;
        var toolbarUniform = toolbar is not null && toolbar.Buttons.All(b =>
            Math.Abs(b.Rect.Width - toolbar.Buttons[0].Rect.Width) < 0.01
            && Math.Abs(b.Rect.Height - toolbar.Buttons[0].Rect.Height) < 0.01);
        var toolbarOk = toolbar is not null
                        && toolbar.Buttons.Count == 18
                        && toolbar.Bounds.Width > 200
                        && toolbarUniform
                        && toolbar.Buttons[0].Action == ToolbarAction.Tool
                        && toolbar.Buttons[0].Tool == AnnotationTool.Arrow
                        && toolbar.Buttons[^1].Action == ToolbarAction.Cancel;
        log.Info(
            $"工具栏布局：{(toolbarOk ? "通过" : "失败")}"
            + $"（{(toolbar is null ? "null" : $"{toolbar.Buttons.Count} 个按钮，{toolbar.Bounds.Width:F0}×{toolbar.Bounds.Height:F0} DIP，尺寸统一={toolbarUniform}")}）");

        var toolbarDarkOk = false;
        if (toolbar is not null && hostWindow is not null)
        {
            // 取第一个与第二个按钮之间的间隙：那里应该露出工具栏的深色背景，而不是下方画面。
            var gapDip = new Point(
                toolbar.Bounds.Left + 6 + 32 + 2,
                toolbar.Bounds.Top + (toolbar.Bounds.Height / 2));
            var gapPixel = hostWindow.ToPhysical(gapDip);
            var pixel = shot.PixelAt(gapPixel.X, gapPixel.Y);
            toolbarDarkOk = pixel.Length == 4 && pixel[0] < 100 && pixel[1] < 100 && pixel[2] < 100;
        }

        log.Info($"工具栏渲染：{(toolbarDarkOk ? "通过" : "失败")}（期望深色背景）");

        // 导出断言：尺寸 == 选区，同一边框位置同样是红色。
        var outcome = new CaptureOutcome(selection, session.Annotations);
        var exported = SelectionOutput.Export(bitmap, bounds, outcome);
        var exportPath = Path.Combine(AppPaths.DataRoot, "annotate-result.png");
        FrameBitmap.SavePng(exported, exportPath);
        log.Info($"导出结果：{exportPath}（{exported.PixelWidth}×{exported.PixelHeight}）");

        var sizeOk = exported.PixelWidth == selection.Width && exported.PixelHeight == selection.Height;
        log.Info($"导出尺寸：{(sizeOk ? "通过" : $"失败（{exported.PixelWidth}×{exported.PixelHeight}，期望 {selection.Width}×{selection.Height}）")}");

        var exportPixel = SamplePixel(exported, rect.Left - selection.X, rect.Top - selection.Y + (rect.Height / 2));
        var exportPixelOk = IsAnnotationRed(exportPixel);
        log.Info($"导出合成：{(exportPixelOk ? "通过" : "失败")}（R{exportPixel[2]} G{exportPixel[1]} B{exportPixel[0]}）");

        var strokePixel = SamplePixel(exported, 310, strokeY - selection.Y);
        var strokeOk = IsAnnotationRed(strokePixel);
        log.Info($"画笔渲染：{(strokeOk ? "通过" : "失败")}（R{strokePixel[2]} G{strokePixel[1]} B{strokePixel[0]}）");

        // 马赛克：用合成棋盘格底图做确定性断言。
        // 真实屏幕内容每次都在变，靠它判断"有没有覆盖"会变成随机失败——这是陷阱清单第 27 条。
        var syntheticBounds = new ScreenRect(0, 0, 600, 300);
        var syntheticFrame = CreateCheckerboard(600, 300, cell: 10);
        var syntheticMosaic = new ScreenRect(120, 60, 300, 180);
        var mosaicExport = SelectionOutput.Export(
            syntheticFrame,
            syntheticBounds,
            new CaptureOutcome(syntheticBounds, [new MosaicAnnotation(syntheticMosaic, 12)]));

        var mosaicSolidOk = IsSolidBlock(mosaicExport, syntheticMosaic.X + 2, syntheticMosaic.Y + 2, 8, tolerance: 2);
        var mosaicDiffers = CountDifferences(
            mosaicExport,
            syntheticFrame,
            new Int32Rect(syntheticMosaic.X, syntheticMosaic.Y, syntheticMosaic.Width, syntheticMosaic.Height),
            tolerance: 8) > 500;

        var mosaicOk = mosaicSolidOk && mosaicDiffers;
        log.Info($"马赛克：{(mosaicOk ? "通过" : "失败")}（块内一致={mosaicSolidOk}，与棋盘格底图不同={mosaicDiffers}）");

        // 文字：导出图在文字包围盒内必须出现足够多的标注色（红）像素。
        var textRedPixels = CountRedPixels(exported, new Int32Rect(
            textBounds.X - selection.X,
            textBounds.Y - selection.Y,
            textBounds.Width,
            textBounds.Height));
        var textOk = textBounds.Width > 20 && textBounds.Height > 10 && textRedPixels > 20;
        log.Info($"文字渲染：{(textOk ? "通过" : "失败")}（包围盒 {textBounds.Width}×{textBounds.Height}，红色像素 {textRedPixels}）");

        // 序号：编号连续 + 圆内有实心红色填充。
        const int badgeRadius = 18;
        var badgeRedPixels = CountRedPixels(exported, new Int32Rect(
            badge1.X - badgeRadius - selection.X,
            badge1.Y - badgeRadius - selection.Y,
            badgeRadius * 2,
            badgeRadius * 2));

        var badgeOk = numberingOk && renumberOk && badgeRedPixels > 200;
        log.Info(
            $"序号标注：{(badgeOk ? "通过" : "失败")}"
            + $"（连续编号={numberingOk}，撤销后补号={renumberOk}，圆内红色像素 {badgeRedPixels}）");

        var ok = undoOk && screenPixelOk && sizeOk && exportPixelOk && strokeOk && mosaicOk && textOk && badgeOk && highlighterOk && toolbarOk && toolbarDarkOk;
        log.Info(ok ? "标注自检：通过" : "标注自检：失败");
        return ok ? 0 : 10;
    }

    /// <summary>#FF3B30 → BGRA：B=0x30, G=0x3B, R=0xFF。留出抗锯齿与色彩管理的余量。</summary>
    private static bool IsAnnotationRed(ReadOnlySpan<byte> pixel) =>
        pixel.Length == 4 && pixel[2] > 150 && pixel[1] < 120 && pixel[0] < 120;

    private static byte[] SamplePixel(BitmapSource source, int x, int y)
    {
        var buffer = new byte[4];
        source.CopyPixels(new Int32Rect(x, y, 1, 1), buffer, 4, 0);
        return buffer;
    }

    /// <summary>导出图里取一个 size×size 的小块，要求内部颜色差异不超过容差（马赛克块的判据）。</summary>
    private static bool IsSolidBlock(BitmapSource source, int x, int y, int size, int tolerance)
    {
        var buffer = new byte[size * size * 4];
        source.CopyPixels(new Int32Rect(x, y, size, size), buffer, size * 4, 0);

        for (var i = 4; i < buffer.Length; i += 4)
        {
            if (Math.Abs(buffer[i] - buffer[0]) > tolerance
                || Math.Abs(buffer[i + 1] - buffer[1]) > tolerance
                || Math.Abs(buffer[i + 2] - buffer[2]) > tolerance)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>生成黑白棋盘格底图，用于与"马赛克是否真的破坏了细节"做确定性对比。</summary>
    private static BitmapSource CreateCheckerboard(int width, int height, int cell)
    {
        var pixels = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var light = (((x / cell) + (y / cell)) % 2) == 0;
                var value = light ? (byte)255 : (byte)40;
                var index = ((y * width) + x) * 4;

                pixels[index] = value;
                pixels[index + 1] = value;
                pixels[index + 2] = value;
                pixels[index + 3] = 255;
            }
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>两张 Bgr32 图在指定区域内的差异像素数。</summary>
    private static int CountDifferences(BitmapSource left, BitmapSource right, Int32Rect region, int tolerance)
    {
        if (region.Width <= 0 || region.Height <= 0)
        {
            return 0;
        }

        var byteCount = region.Width * region.Height * 4;
        var leftPixels = new byte[byteCount];
        var rightPixels = new byte[byteCount];

        left.CopyPixels(region, leftPixels, region.Width * 4, 0);
        right.CopyPixels(region, rightPixels, region.Width * 4, 0);

        var differences = 0;

        for (var i = 0; i < byteCount; i += 4)
        {
            if (Math.Abs(leftPixels[i] - rightPixels[i]) > tolerance
                || Math.Abs(leftPixels[i + 1] - rightPixels[i + 1]) > tolerance
                || Math.Abs(leftPixels[i + 2] - rightPixels[i + 2]) > tolerance)
            {
                differences++;
            }
        }

        return differences;
    }

    /// <summary>统计导出图某区域内的标红色像素数（文字抗锯齿后仍有实心笔画核心）。</summary>
    private static int CountRedPixels(BitmapSource source, Int32Rect region)
    {
        if (region.Width <= 0 || region.Height <= 0)
        {
            return 0;
        }

        var buffer = new byte[region.Width * region.Height * 4];
        source.CopyPixels(region, buffer, region.Width * 4, 0);

        var count = 0;
        for (var i = 0; i < buffer.Length; i += 4)
        {
            if (buffer[i + 2] > 150 && buffer[i + 1] < 120 && buffer[i] < 120)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>导出图在该区域是否与原始画面有足够多像素不同（证明马赛克真的盖住了内容）。</summary>
    private static bool HasDifference(BitmapSource exported, CapturedFrame frame, ScreenRect selection, ScreenRect rect)
    {
        var differences = 0;

        for (var y = rect.Y + 4; y < rect.Bottom - 4; y += 7)
        {
            for (var x = rect.X + 4; x < rect.Right - 4; x += 7)
            {
                var original = frame.PixelAt(x, y);
                if (original.IsEmpty)
                {
                    continue;
                }

                var exportedPixel = SamplePixel(exported, x - selection.X, y - selection.Y);
                if (Math.Abs(exportedPixel[0] - original[0]) > 8
                    || Math.Abs(exportedPixel[1] - original[1]) > 8
                    || Math.Abs(exportedPixel[2] - original[2]) > 8)
                {
                    differences++;
                }
            }
        }

        return differences > 10;
    }
}
