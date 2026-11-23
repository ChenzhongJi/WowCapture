using System.ComponentModel;
using System.Runtime.InteropServices;
using WowCapture.Core.Abstractions;
using WowCapture.Core.Geometry;
using WowCapture.Platform.Interop;

namespace WowCapture.Platform.Screen;

/// <summary>
/// Win32 显示器拓扑查询。
/// </summary>
/// <remarks>
/// 进程已声明 PerMonitorV2，因此 <c>MONITORINFO</c> 给出的矩形是物理像素，
/// 与抓屏、遮罩窗口使用的坐标系完全一致，不需要任何换算。
/// </remarks>
public sealed class Win32MonitorService : IMonitorService
{
    /// <summary>每块屏的 DPI 查询结果（诊断用）。</summary>
    public IReadOnlyList<MonitorDpiProbe> LastProbes { get; private set; } = [];

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>(4);
        var probes = new List<MonitorDpiProbe>(4);
        var index = 0;

        MonitorEnumProc callback = (IntPtr hMonitor, IntPtr _, ref NativeRect _, IntPtr _) =>
        {
            var info = new MonitorInfoEx { CbSize = Marshal.SizeOf<MonitorInfoEx>() };
            if (!NativeMethods.GetMonitorInfo(hMonitor, ref info))
            {
                return true;
            }

            var dpi = QueryEffectiveDpi(hMonitor);
            var shellScale = QueryShellScale(hMonitor);

            monitors.Add(new MonitorInfo(
                index++,
                info.RcMonitor.ToScreenRect(),
                info.RcWork.ToScreenRect(),
                dpi,
                (info.DwFlags & NativeMethods.MonitorInfoPrimary) != 0));

            probes.Add(new MonitorDpiProbe(info.SzDevice, dpi, shellScale));
            return true;
        };

        // 回调是同步的：EnumDisplayMonitors 返回时列表已经填好。
        if (!NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero) && monitors.Count == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "EnumDisplayMonitors 失败。");
        }

        GC.KeepAlive(callback);
        LastProbes = probes;
        return monitors;
    }

    public ScreenRect GetVirtualScreenBounds() => VirtualScreen.GetBounds();

    private static uint QueryEffectiveDpi(IntPtr hMonitor)
    {
        try
        {
            if (NativeMethods.GetDpiForMonitor(hMonitor, NativeMethods.MdtEffectiveDpi, out var dpiX, out _) == 0 && dpiX > 0)
            {
                return dpiX;
            }
        }
        catch (DllNotFoundException)
        {
            // Win8.1 之前没有 shcore，退到 96 DPI（100%）。
        }
        catch (EntryPointNotFoundException)
        {
        }

        return 96;
    }

    private static int QueryShellScale(IntPtr hMonitor)
    {
        try
        {
            if (NativeMethods.GetScaleFactorForMonitor(hMonitor, out var scale) == 0)
            {
                return scale;
            }
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }

        return 0;
    }
}

/// <summary>单块屏的 DPI 探测结果：<paramref name="Dpi"/> 来自 shcore 的 DPI 查询，<paramref name="ShellScale"/> 是系统缩放百分比。</summary>
public readonly record struct MonitorDpiProbe(string DeviceName, uint Dpi, int ShellScale);
