using System.Text;
using WowCapture.Core.Geometry;
using WowCapture.Platform.Interop;

namespace WowCapture.Platform.Windows;

/// <summary>鼠标位置命中的顶层窗口。</summary>
/// <param name="Handle">窗口句柄。</param>
/// <param name="Bounds">不含投影阴影的可见边界（虚拟屏物理像素）。</param>
/// <param name="Title">窗口标题，可能为空。</param>
public readonly record struct WindowHit(IntPtr Handle, ScreenRect Bounds, string Title);

/// <summary>
/// 找出鼠标下方的顶层窗口，供"悬停吸附整窗"使用。
/// </summary>
/// <remarks>
/// <para>
/// 用 <c>EnumWindows</c> 按 Z 序（从上到下）扫描，而不是 <c>WindowFromPoint</c>：
/// 后者会把遮罩窗口自己返回出来，还得额外剔除。Z 序扫描天然拿到"用户眼里最上面那个"。
/// </para>
/// <para>
/// 边界取 <c>DWMWA_EXTENDED_FRAME_BOUNDS</c> 而不是 <c>GetWindowRect</c>：
/// 后者含窗口投影，截出来的图会多出一圈桌面背景。
/// </para>
/// </remarks>
public static class TopLevelWindowFinder
{
    /// <summary>桌面、任务栏、通知区域溢出窗等外壳窗口，不应该被吸附。</summary>
    private static readonly HashSet<string> ShellClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "NotifyIconOverflowWindow",
        "Windows.UI.Core.CoreWindow",
    };

    /// <summary>
    /// 命中测试。
    /// </summary>
    /// <param name="point">虚拟屏物理坐标。</param>
    /// <param name="excludeProcessId">要排除的进程（通常传本进程，避免吸附到自己的窗口）；0 表示不排除。</param>
    public static WindowHit? FindAt(ScreenPoint point, int excludeProcessId)
    {
        WindowHit? result = null;

        EnumWindowsProc callback = (hwnd, _) =>
        {
            try
            {
                if (!IsCandidate(hwnd, excludeProcessId))
                {
                    return true;
                }

                if (!TryGetBounds(hwnd, out var bounds) || !bounds.Contains(point))
                {
                    return true;
                }

                result = new WindowHit(hwnd, bounds, GetTitle(hwnd));

                // 按 Z 序从上到下，第一个包含该点的就是用户看到的那个窗口。
                return false;
            }
            catch (Exception)
            {
                // 回调里抛异常会穿透到原生代码，后果是进程直接崩；这里一律吞掉继续扫。
                return true;
            }
        };

        NativeMethods.EnumWindows(callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return result;
    }

    private static bool IsCandidate(IntPtr hwnd, int excludeProcessId)
    {
        if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd))
        {
            return false;
        }

        var exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GwlExStyle);

        // 工具窗口一律跳过：既排除了提示框之类的小窗口，也顺带排除了我们自己的遮罩窗口。
        if ((exStyle & NativeMethods.WsExToolWindow) != 0)
        {
            return false;
        }

        if (excludeProcessId != 0)
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
            if (processId == (uint)excludeProcessId)
            {
                return false;
            }
        }

        // 被 DWM 隐藏的窗口（切虚拟桌面后残留的那些）如果被吸附，看起来就是"选中了一个不存在的东西"。
        if (NativeMethods.DwmGetWindowAttributeInt(hwnd, NativeMethods.DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
        {
            return false;
        }

        return !ShellClasses.Contains(GetClassName(hwnd));
    }

    private static bool TryGetBounds(IntPtr hwnd, out ScreenRect bounds)
    {
        bounds = default;

        var hr = NativeMethods.DwmGetWindowAttributeRect(
            hwnd,
            NativeMethods.DwmwaExtendedFrameBounds,
            out var rect,
            System.Runtime.InteropServices.Marshal.SizeOf<NativeRect>());

        if (hr != 0 && !NativeMethods.GetWindowRect(hwnd, out rect))
        {
            return false;
        }

        bounds = rect.ToScreenRect();
        return bounds.Width > 0 && bounds.Height > 0;
    }

    private static string GetTitle(IntPtr hwnd)
    {
        var buffer = new char[NativeMethods.MaxWindowTextChars];
        var length = NativeMethods.GetWindowText(hwnd, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length).Trim() : string.Empty;
    }

    private static string GetClassName(IntPtr hwnd)
    {
        var buffer = new char[256];
        var length = NativeMethods.GetClassName(hwnd, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }
}
