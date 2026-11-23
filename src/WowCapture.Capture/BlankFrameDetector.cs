using WowCapture.Core.Imaging;

namespace WowCapture.Capture;

/// <summary>稀疏采样结果。</summary>
/// <param name="SampledCount">采样点数量。</param>
/// <param name="NonBlackCount">其中非纯黑（R|G|B 不全为 0）的采样点数量。</param>
/// <param name="IsUniform">所有采样点颜色是否完全一致。</param>
public readonly record struct BlankScan(int SampledCount, int NonBlackCount, bool IsUniform)
{
    /// <summary>没有任何非黑像素时视为空白。</summary>
    public bool IsLikelyBlank => NonBlackCount == 0;
}

/// <summary>
/// 空白帧检测：DRM 保护内容、独占全屏游戏、部分 UWP 窗口用 GDI 抓会得到纯黑图。
/// 与其把一张全黑图扣进剪贴板让用户以为是自己操作错了，不如识别出来换一条捕获路径。
/// </summary>
public static class BlankFrameDetector
{
    /// <summary>最多横向 64 个、纵向 36 个采样点——足够判断，又不至于拖慢抓屏。</summary>
    private const int MaxColumns = 64;
    private const int MaxRows = 36;

    public static BlankScan Scan(CapturedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.Bounds.IsEmpty)
        {
            return new BlankScan(0, 0, true);
        }

        var stepX = Math.Max(1, frame.Width / MaxColumns);
        var stepY = Math.Max(1, frame.Height / MaxRows);

        var sampled = 0;
        var nonBlack = 0;
        var uniform = true;
        uint first = 0;

        for (var y = 0; y < frame.Height; y += stepY)
        {
            for (var x = 0; x < frame.Width; x += stepX)
            {
                var offset = (y * frame.Stride) + (x * 4);
                var pixels = frame.Pixels;

                var b = pixels[offset];
                var g = pixels[offset + 1];
                var r = pixels[offset + 2];
                var packed = (uint)((r << 16) | (g << 8) | b);

                if (r != 0 || g != 0 || b != 0)
                {
                    nonBlack++;
                }

                if (sampled == 0)
                {
                    first = packed;
                }
                else if (uniform && packed != first)
                {
                    uniform = false;
                }

                sampled++;
            }
        }

        return new BlankScan(sampled, nonBlack, uniform);
    }
}
