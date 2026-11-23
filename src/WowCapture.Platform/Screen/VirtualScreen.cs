using WowCapture.Core.Geometry;
using WowCapture.Platform.Interop;

namespace WowCapture.Platform.Screen;

/// <summary>虚拟屏幕（所有显示器拼成的矩形）查询。</summary>
public static class VirtualScreen
{
    /// <summary>
    /// 当前虚拟屏边界。进程已声明 PerMonitorV2，因此这里拿到的是物理像素，
    /// 副屏位于主屏左侧或上方时坐标可能为负。
    /// </summary>
    public static ScreenRect GetBounds() =>
        new(
            NativeMethods.GetSystemMetrics(NativeMethods.SmXVirtualScreen),
            NativeMethods.GetSystemMetrics(NativeMethods.SmYVirtualScreen),
            NativeMethods.GetSystemMetrics(NativeMethods.SmCxVirtualScreen),
            NativeMethods.GetSystemMetrics(NativeMethods.SmCyVirtualScreen));
}
