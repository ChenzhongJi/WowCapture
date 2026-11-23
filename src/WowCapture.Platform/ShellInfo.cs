using System.Diagnostics;
using WowCapture.Platform.Interop;

namespace WowCapture.Platform;

/// <summary>
/// 当前会话的"有没有桌面壳"信息。托盘图标依赖任务栏（Explorer），
/// 在服务会话、沙箱会话里它是不存在的，程序会正常运行但图标永远不出现。
/// </summary>
public static class ShellInfo
{
    /// <summary>当前会话是否存在任务栏（也就是有没有 Explorer 桌面）。</summary>
    public static bool IsTaskbarAvailable => NativeMethods.FindWindow("Shell_TrayWnd", null) != IntPtr.Zero;

    public static int SessionId
    {
        get
        {
            try
            {
                return Process.GetCurrentProcess().SessionId;
            }
            catch (Exception)
            {
                return -1;
            }
        }
    }
}
