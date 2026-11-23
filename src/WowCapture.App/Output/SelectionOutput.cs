using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WowCapture.App.Services;
using WowCapture.Core.Annotations;
using WowCapture.Core.Geometry;
using WowCapture.Editor.Rendering;

namespace WowCapture.App.Output;

/// <summary>选区裁剪与剪贴板输出（T1.7 的最小可用部分，多格式矩阵测试在 T1.7 完成）。</summary>
internal static class SelectionOutput
{
    /// <summary>从整块虚拟屏冻结图里裁出选区。坐标一律是虚拟屏物理像素。</summary>
    public static BitmapSource Crop(BitmapSource frame, ScreenRect virtualBounds, ScreenRect selection)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var clipped = selection.Intersect(virtualBounds);
        if (clipped.IsEmpty)
        {
            throw new ArgumentException($"选区 {selection} 完全落在截图范围 {virtualBounds} 之外。", nameof(selection));
        }

        var cropped = new CroppedBitmap(
            frame,
            new Int32Rect(
                clipped.X - virtualBounds.X,
                clipped.Y - virtualBounds.Y,
                clipped.Width,
                clipped.Height));

        // CroppedBitmap 只是源位图的视图：Freeze 之后仍强引用整幅冻结帧。
        // 贴图窗口会把它当 Image.Source 挂一整天，等于把整块虚拟屏（单 4K ≈ 33MB）常驻内存。
        // 这里物化成独立副本，切断对源帧的引用。
        var materialized = new WriteableBitmap(cropped);
        materialized.Freeze();
        return materialized;
    }

    /// <summary>
    /// 导出：裁剪底图并把标注合成上去。
    /// </summary>
    /// <remarks>
    /// 渲染目标是 96 DPI、尺寸等于选区的<b>物理像素</b>，标注坐标也是物理像素，
    /// 因此这里不需要任何缩放——预览与导出的线条粗细、箭头大小完全一致。
    /// 没有标注时直接返回裁剪结果，省掉一次离屏渲染。
    /// </remarks>
    public static BitmapSource Export(BitmapSource frame, ScreenRect virtualBounds, CaptureOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(outcome);

        var selection = outcome.Selection;
        var cropped = Crop(frame, virtualBounds, selection);

        if (outcome.Annotations.Count == 0)
        {
            return cropped;
        }

        var visual = new DrawingVisual();

        // 马赛克靠"缩小再最近邻放大"实现，导出侧必须显式声明插值方式。
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);

        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(cropped, new Rect(0, 0, selection.Width, selection.Height));

            // 标注空间是虚拟屏原点 → 平移到选区左下角从 0 开始。
            dc.PushTransform(new TranslateTransform(-selection.X, -selection.Y));
            AnnotationRenderer.Draw(dc, outcome.Annotations, new MosaicSource(frame, virtualBounds));
            dc.Pop();
        }

        var target = new RenderTargetBitmap(
            selection.Width,
            selection.Height,
            96,
            96,
            PixelFormats.Pbgra32);

        target.Render(visual);
        target.Freeze();
        return target;
    }

    /// <summary>
    /// 写入剪贴板：同时提供 <c>CF_DIB</c>（经由 BitmapSource 自动转换）与 <c>PNG</c> 流。
    /// 只写其中一种会出现"微信粘贴发黑"或"Office 粘贴丢透明"的经典不兼容。
    /// 同步版：仅供诊断工具与贴图右键菜单（低频小图）使用；主流程请用
    /// <see cref="CopyToClipboardAsync"/>，避免 PNG 编码与 Sleep 重试卡住 UI 线程。
    /// </summary>
    public static bool CopyToClipboard(BitmapSource bitmap, FileLog? log = null)
    {
        // 剪贴板被其它进程占用是常态（0x800401D0），必须重试而不是直接失败。
        const int maxAttempts = 5;
        const int retryDelayMs = 40;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(BuildDataObject(bitmap), copy: true);
                return true;
            }
            catch (COMException ex) when (attempt < maxAttempts)
            {
                log?.Warn($"剪贴板被占用（第 {attempt} 次），{retryDelayMs}ms 后重试：0x{ex.HResult:X8}");
                Thread.Sleep(retryDelayMs);
            }
            catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
            {
                log?.Error("写入剪贴板失败", ex);
                return false;
            }
        }

        log?.Error("写入剪贴板失败：多次重试后仍被占用");
        return false;
    }

    /// <summary>
    /// <see cref="CopyToClipboard"/> 的异步版，截图主流程用它：
    /// PNG 编码（4K 选区可达数百毫秒）放到线程池，占用重试改用 <c>Task.Delay</c>，
    /// 全程不阻塞 UI 线程。OLE 要求 <c>SetDataObject</c> 必须在 STA/UI 线程执行，因此保留在调用线程。
    /// </summary>
    public static async Task<bool> CopyToClipboardAsync(BitmapSource bitmap, FileLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        // 编码是纯 CPU 操作，位图已冻结，可安全跨线程。
        var data = await Task.Run(() => BuildDataObject(bitmap));

        const int maxAttempts = 5;
        const int retryDelayMs = 15;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, copy: true);
                return true;
            }
            catch (COMException ex) when (attempt < maxAttempts)
            {
                log?.Warn($"剪贴板被占用（第 {attempt} 次），{retryDelayMs}ms 后重试：0x{ex.HResult:X8}");
                await Task.Delay(retryDelayMs);
            }
            catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
            {
                log?.Error("写入剪贴板失败", ex);
                return false;
            }
        }

        log?.Error("写入剪贴板失败：多次重试后仍被占用");
        return false;
    }

    private static DataObject BuildDataObject(BitmapSource bitmap)
    {
        // 不要 using 这个流：DataObject 可能在 OleFlush 之后才按需读取它。
        var png = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(png);
        png.Position = 0;

        var data = new DataObject();
        data.SetImage(bitmap);
        data.SetData("PNG", png, autoConvert: false);
        return data;
    }
}
