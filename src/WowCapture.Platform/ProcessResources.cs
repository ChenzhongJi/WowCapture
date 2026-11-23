using System.Diagnostics;
using WowCapture.Platform.Interop;

namespace WowCapture.Platform;

/// <summary>
/// GDI / USER 对象计数。这两个数字是判断截图工具是否泄漏句柄的最快手段：
/// 每创建一次 DC、DIB、窗口，计数就应该在销毁后回到原值。
/// </summary>
public static class ProcessResources
{
    private const uint GdiObjects = 0;
    private const uint UserObjects = 1;

    public static int GdiObjectCount => NativeMethods.GetGuiResources(NativeMethods.GetCurrentProcess(), GdiObjects);

    public static int UserObjectCount => NativeMethods.GetGuiResources(NativeMethods.GetCurrentProcess(), UserObjects);

    /// <summary>当前进程私有内存（字节）。</summary>
    public static long PrivateMemoryBytes => Process.GetCurrentProcess().PrivateMemorySize64;
}
