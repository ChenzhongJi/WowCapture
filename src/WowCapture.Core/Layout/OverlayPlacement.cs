using WowCapture.Core.Abstractions;
using WowCapture.Core.Geometry;

namespace WowCapture.Core.Layout;

/// <summary>
/// 一块显示器上遮罩窗口的摆放：物理像素矩形 + 与之对应的 WPF 逻辑（DIP）尺寸。
/// </summary>
/// <remarks>
/// <para>
/// 这是架构 D3 的<b>唯一换算点</b>。规则只有一句：
/// 窗口用 <c>SetWindowPos</c> 摆到 <see cref="PhysicalBounds"/>，
/// 同时把 WPF 的 <c>Width/Height</c> 设成 <see cref="WidthDip"/>/<see cref="HeightDip"/>，
/// 于是 WPF 按 <see cref="Scale"/> 渲染出来的设备像素正好是物理像素，1:1 不重采样。
/// </para>
/// <para>
/// 除这里以外，全链路只允许出现物理像素坐标。任何"随手乘一下 dpiScale"都会在混合缩放的多屏上
/// 产出偏移若干像素的经典 bug，且只在别人的机器上复现。
/// </para>
/// </remarks>
public readonly record struct OverlayPlacement(
    ScreenRect PhysicalBounds,
    double WidthDip,
    double HeightDip,
    double Scale)
{
    public static OverlayPlacement For(MonitorInfo monitor)
    {
        var scale = monitor.Scale <= 0 ? 1.0 : monitor.Scale;
        return new OverlayPlacement(
            monitor.Bounds,
            monitor.Bounds.Width / scale,
            monitor.Bounds.Height / scale,
            scale);
    }

    /// <summary>窗口内 DIP 坐标 → 虚拟屏物理坐标。</summary>
    public ScreenPoint ToPhysical(double dipX, double dipY) => new(
        PhysicalBounds.X + (int)Math.Round(dipX * Scale, MidpointRounding.AwayFromZero),
        PhysicalBounds.Y + (int)Math.Round(dipY * Scale, MidpointRounding.AwayFromZero));

    /// <summary>虚拟屏物理坐标 → 窗口内 DIP 坐标。</summary>
    public (double X, double Y) ToDip(ScreenPoint physical) => (
        (physical.X - PhysicalBounds.X) / Scale,
        (physical.Y - PhysicalBounds.Y) / Scale);

    /// <summary>
    /// 自检：在窗口内取若干 DIP 点，物理↔DIP 往返一次，要求完全回到原点。
    /// 浮点往返若出现误差，说明缩放比例不是 DPI/96 的精确值，必须在建模阶段修掉。
    /// </summary>
    public int VerifyRoundTrip(IReadOnlyList<(double X, double Y)> dipPoints)
    {
        var failures = 0;

        foreach (var (dipX, dipY) in dipPoints)
        {
            var physical = ToPhysical(dipX, dipY);
            var (backX, backY) = ToDip(physical);

            if (Math.Abs(backX - dipX) > 0.0001 || Math.Abs(backY - dipY) > 0.0001)
            {
                failures++;
            }
        }

        return failures;
    }
}
