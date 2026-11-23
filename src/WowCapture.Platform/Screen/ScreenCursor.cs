using WowCapture.Core.Geometry;
using WowCapture.Platform.Interop;

namespace WowCapture.Platform.Screen;

/// <summary>鼠标位置（虚拟屏物理像素）。进程是 PerMonitorV2，因此不需要任何缩放换算。</summary>
public static class ScreenCursor
{
    public static ScreenPoint GetPosition() =>
        NativeMethods.GetCursorPos(out var point) ? point.ToScreenPoint() : new ScreenPoint(0, 0);
}
