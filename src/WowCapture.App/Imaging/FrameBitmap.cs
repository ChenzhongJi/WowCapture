using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WowCapture.Core.Imaging;

namespace WowCapture.App.Imaging;

/// <summary><see cref="CapturedFrame"/> 与 WPF 位图之间的唯一转换点。</summary>
internal static class FrameBitmap
{
    /// <summary>
    /// 转成 WPF 位图用于显示或编码。
    /// </summary>
    /// <remarks>
    /// 必须用 <see cref="PixelFormats.Bgr32"/> 而不是 <c>Bgra32</c>：
    /// GDI 写 32bpp DIB 时把每个像素的 alpha 字节填成 0，按 BGRA 解释会得到整张全透明的图
    /// （在深色背景上表现为纯黑，在浅色背景上表现为什么都看不见）。这是自研截图工具的经典坑。
    /// </remarks>
    public static BitmapSource ToBitmapSource(CapturedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var source = BitmapSource.Create(
            frame.Width,
            frame.Height,
            96,
            96,
            PixelFormats.Bgr32,
            palette: null,
            frame.Pixels,
            frame.Stride);

        source.Freeze();
        return source;
    }

    public static void SavePng(CapturedFrame frame, string path) => SavePng(ToBitmapSource(frame), path);

    public static void SavePng(BitmapSource bitmap, string path)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
