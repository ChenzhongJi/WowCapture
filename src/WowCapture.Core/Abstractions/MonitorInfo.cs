using WowCapture.Core.Geometry;

namespace WowCapture.Core.Abstractions;

/// <summary>一块显示器。Bounds / WorkArea 全部是物理像素的虚拟屏坐标。</summary>
/// <param name="Index">枚举顺序，仅用于标识与日志。</param>
/// <param name="Bounds">显示器完整边界。</param>
/// <param name="WorkArea">排除任务栏后的可用区域。</param>
/// <param name="Dpi">该显示器的有效 DPI（96 = 100%，144 = 150%）。</param>
/// <param name="IsPrimary">是否主显示器。</param>
public readonly record struct MonitorInfo(
    int Index,
    ScreenRect Bounds,
    ScreenRect WorkArea,
    uint Dpi,
    bool IsPrimary)
{
    /// <summary>缩放比例，例如 1.5 表示 150%。</summary>
    public double Scale => Dpi / 96.0;
}
