using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using WowCapture.Core.Abstractions;
using WowCapture.Core.Geometry;
using WowCapture.Core.Imaging;
using WowCapture.Platform.Interop;
using WowCapture.Platform.Screen;

namespace WowCapture.Capture;

/// <summary>一次抓屏的耗时拆分，仅供基准与诊断使用。</summary>
public readonly record struct CaptureStats(
    ScreenRect Bounds,
    double BlitMs,
    double CopyMs,
    double TotalMs,
    long ByteCount);

/// <summary>
/// GDI 抓屏：<c>GetDC(NULL)</c> → <c>CreateDIBSection</c>（32bpp，top-down）→
/// <c>BitBlt(SRCCOPY | CAPTUREBLT)</c> → 拷进托管字节数组。
/// </summary>
/// <remarks>
/// <para>
/// 这是架构 D4 的第一级实现，覆盖绝大多数桌面场景且不需要 D3D 设备。
/// DRM 内容、独占全屏游戏会得到全黑画面，由 <see cref="BlankFrameDetector"/> 识别后交给回退链。
/// </para>
/// <para>
/// 全程在物理像素下工作：进程声明了 PerMonitorV2，<c>GetSystemMetrics</c> 给的虚拟屏边界就是物理像素，
/// 因此 <c>BitBlt</c> 的源坐标不需要任何缩放换算。
/// </para>
/// </remarks>
public sealed class GdiScreenCapturer : IScreenCapturer
{
    /// <summary>最近一次抓屏的耗时拆分；未调用过时为 <c>null</c>。</summary>
    public CaptureStats? LastStats { get; private set; }

    /// <summary>
    /// 是否带上 <c>CAPTUREBLT</c>：能抓到分层窗口（提示框、部分自绘 UI），
    /// 但会让 BitBlt 走完整的合成层回读，成本可能翻倍。实测数据见 docs/mvp-tasks.md 的 S1。
    /// </summary>
    public bool IncludeLayeredWindows { get; set; } = true;

    public CapturedFrame CaptureVirtualScreen()
    {
        var bounds = VirtualScreen.GetBounds();
        if (bounds.IsEmpty)
        {
            throw new InvalidOperationException("虚拟屏幕尺寸为 0：当前会话可能没有可用桌面。");
        }

        var byteCount = checked((long)bounds.Width * bounds.Height * 4);
        if (byteCount > Array.MaxLength)
        {
            throw new NotSupportedException($"虚拟屏 {bounds} 需要的缓冲区超过单数组上限，暂不支持。");
        }

        var total = Stopwatch.StartNew();
        var screenDc = NativeMethods.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetDC(NULL) 失败。");
        }

        IntPtr memoryDc = IntPtr.Zero;
        IntPtr dib = IntPtr.Zero;
        IntPtr previous = IntPtr.Zero;

        try
        {
            memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
            if (memoryDc == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateCompatibleDC 失败。");
            }

            var info = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = bounds.Width,

                    // 负高度 = 自上而下存储。默认的自下而上会逼我们做一次整图翻转。
                    Height = -bounds.Height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = NativeMethods.BiRgb,
                },
            };

            dib = NativeMethods.CreateDIBSection(
                screenDc,
                ref info,
                NativeMethods.DibRgbColors,
                out var bits,
                IntPtr.Zero,
                0);

            if (dib == IntPtr.Zero || bits == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateDIBSection 失败。");
            }

            previous = NativeMethods.SelectObject(memoryDc, dib);
            if (previous == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SelectObject 失败。");
            }

            var blit = Stopwatch.StartNew();
            var blitOk = NativeMethods.BitBlt(
                memoryDc,
                0,
                0,
                bounds.Width,
                bounds.Height,
                screenDc,
                bounds.X,
                bounds.Y,
                NativeMethods.SrcCopy | (IncludeLayeredWindows ? NativeMethods.CaptureBlt : 0u));
            blit.Stop();

            if (!blitOk)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "BitBlt 失败。");
            }

            // 不要用 new byte[]：66MB 的零初始化纯属浪费，整块马上就被下面覆盖。
            var pixels = GC.AllocateUninitializedArray<byte>((int)byteCount);

            var copy = Stopwatch.StartNew();
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            copy.Stop();

            total.Stop();
            LastStats = new CaptureStats(bounds, blit.Elapsed.TotalMilliseconds, copy.Elapsed.TotalMilliseconds, total.Elapsed.TotalMilliseconds, byteCount);

            return new CapturedFrame(pixels, bounds);
        }
        finally
        {
            // 必须在 DeleteObject 之前把原对象选回去，否则 DIB 仍被 DC 持有，删除会失败并泄漏 GDI 对象。
            if (memoryDc != IntPtr.Zero && previous != IntPtr.Zero)
            {
                NativeMethods.SelectObject(memoryDc, previous);
            }

            if (dib != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(dib);
            }

            if (memoryDc != IntPtr.Zero)
            {
                NativeMethods.DeleteDC(memoryDc);
            }

            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }
}
