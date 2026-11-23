using WowCapture.Core.Geometry;

namespace WowCapture.Core.Imaging;

/// <summary>
/// 冻结帧：按下截图热键那一刻，整个虚拟屏幕的像素快照。
/// 行优先、自上而下，stride 恒等于 <c>Width * 4</c>（32 位非预乘 BGRA8888）。
/// </summary>
/// <remarks>
/// <para>
/// 这是架构决策 D1 的核心载体：遮罩窗口显示的是这张图，而不是实时桌面。
/// 因此截图期间内存会多占用约 <c>宽 × 高 × 4</c> 字节（双 4K ≈ 66MB），
/// 会话结束必须立即释放，且同一时刻只允许存在一个会话。
/// </para>
/// <para>
/// 像素格式选 BGRA 而不是 BGR：WPF 的 <c>PixelFormats.Bgra32</c> 可直接零转换接管，
/// 带 alpha 也让未来支持圆角/阴影导出不需要换管线。
/// </para>
/// </remarks>
public sealed class CapturedFrame
{
    public CapturedFrame(byte[] pixels, ScreenRect bounds)
    {
        ArgumentNullException.ThrowIfNull(pixels);

        var expected = (long)bounds.Width * bounds.Height * 4;
        if (pixels.LongLength != expected)
        {
            throw new ArgumentException(
                $"像素缓冲区 {pixels.LongLength} 字节与矩形 {bounds} 不匹配（期望 {expected} 字节）。",
                nameof(pixels));
        }

        Pixels = pixels;
        Bounds = bounds;
    }

    /// <summary>本帧覆盖的虚拟屏区域。</summary>
    public ScreenRect Bounds { get; }

    public int Width => Bounds.Width;

    public int Height => Bounds.Height;

    public int Stride => Width * 4;

    /// <summary>
    /// BGRA 像素缓冲。<b>只读使用</b>：调用方不得修改内容，否则冻结帧与已绘制标注会不一致。
    /// </summary>
    public byte[] Pixels { get; }

    /// <summary>读取虚拟屏坐标处的单个像素；越界返回空 span。</summary>
    public ReadOnlySpan<byte> PixelAt(int virtualX, int virtualY)
    {
        if (!Bounds.Contains(new ScreenPoint(virtualX, virtualY)))
        {
            return ReadOnlySpan<byte>.Empty;
        }

        var offset = ((virtualY - Bounds.Y) * Stride) + ((virtualX - Bounds.X) * 4);
        return Pixels.AsSpan(offset, 4);
    }
}
