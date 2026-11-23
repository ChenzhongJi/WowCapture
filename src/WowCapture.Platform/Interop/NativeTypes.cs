using System.Runtime.InteropServices;
using WowCapture.Core.Geometry;

namespace WowCapture.Platform.Interop;

/// <summary>Win32 <c>RECT</c>。Right/Bottom 为开区间，与 <see cref="ScreenRect"/> 语义一致。</summary>
[StructLayout(LayoutKind.Sequential)]
public struct NativeRect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public readonly int Width => Right - Left;

    public readonly int Height => Bottom - Top;

    public readonly ScreenRect ToScreenRect() => ScreenRect.FromLtrb(Left, Top, Right, Bottom);

    public static NativeRect From(ScreenRect rect) => new()
    {
        Left = rect.Left,
        Top = rect.Top,
        Right = rect.Right,
        Bottom = rect.Bottom,
    };
}

/// <summary>Win32 <c>POINT</c>，单位为物理像素。</summary>
[StructLayout(LayoutKind.Sequential)]
public struct NativePoint
{
    public int X;
    public int Y;

    public readonly ScreenPoint ToScreenPoint() => new(X, Y);
}

// M1（T1.2/T1.3）在此补充 EnumDisplayMonitors / GetMonitorInfo / GetDpiForMonitor /
// BitBlt / CreateDIBSection 等声明。现在刻意留空：没有调用点的 P/Invoke 声明只会变成死代码。
