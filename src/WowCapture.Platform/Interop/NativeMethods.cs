using System.Runtime.InteropServices;

namespace WowCapture.Platform.Interop;

/// <summary>
/// 抓屏所需的 Win32 声明。只包含当前有调用点的函数——没有调用点的声明会烂成死代码。
/// </summary>
internal static partial class NativeMethods
{
    // ---- GetSystemMetrics ----
    public const int SmXVirtualScreen = 76;
    public const int SmYVirtualScreen = 77;
    public const int SmCxVirtualScreen = 78;
    public const int SmCyVirtualScreen = 79;

    // ---- BitBlt 光栅操作码 ----
    /// <summary>直接拷贝源像素。</summary>
    public const uint SrcCopy = 0x00CC0020;

    /// <summary>
    /// 同时包含分层窗口（菜单、提示框、部分自绘窗口）。
    /// 代价是被包含的分层窗口可能出现短暂闪烁，因此只在截图瞬间使用。
    /// </summary>
    public const uint CaptureBlt = 0x40000000;

    // ---- CreateDIBSection ----
    public const uint DibRgbColors = 0;
    public const uint BiRgb = 0;

    /// <summary>ATTACH_PARENT_PROCESS：把控制台附着到父进程（命令行模式输出用）。</summary>
    public const uint AttachParentProcess = 0xFFFFFFFF;

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial IntPtr GetDC(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    public static partial int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetrics(int nIndex);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    public static partial IntPtr CreateCompatibleDC(IntPtr hdc);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    public static partial IntPtr CreateDIBSection(
        IntPtr hdc,
        ref BitmapInfo pbmi,
        uint usage,
        out IntPtr ppvBits,
        IntPtr hSection,
        uint offset);

    [LibraryImport("gdi32.dll")]
    public static partial IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(IntPtr hObject);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteDC(IntPtr hdc);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BitBlt(
        IntPtr hdc,
        int x,
        int y,
        int cx,
        int cy,
        IntPtr hdcSrc,
        int x1,
        int y1,
        uint rop);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AttachConsole(uint dwProcessId);

    // ---- RegisterHotKey ----
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    /// <summary>按住不放时不重复触发。</summary>
    public const uint ModNoRepeat = 0x4000;

    public const int WmHotkey = 0x0312;

    /// <summary>ERROR_HOTKEY_ALREADY_REGISTERED：组合被其它进程占用。</summary>
    public const int ErrorHotkeyAlreadyRegistered = 1409;

    public const int WsExToolWindow = 0x00000080;

    /// <summary>点击穿透：鼠标消息直接落到下面的窗口。</summary>
    public const int WsExTransparent = 0x00000020;

    /// <summary>分层窗口。与 <see cref="WsExTransparent"/> 搭配时穿透行为更稳定。</summary>
    public const int WsExLayered = 0x00080000;

    /// <summary>SetLayeredWindowAttributes 的 LWA_ALPHA：整窗统一不透明度。</summary>
    public const uint LwaAlpha = 0x00000002;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetLayeredWindowAttributes(IntPtr hWnd, uint colorKey, byte alpha, uint flags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(IntPtr hWnd, int id);

    // PostMessage 在 user32 里只有 A/W 两个导出，没有无后缀版本；
    // LibraryImport 不会像 DllImport 那样按 CharSet 自动补 W，必须显式写 EntryPoint。
    [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    // ---- 显示器拓扑 ----
    /// <summary>MONITORINFOF_PRIMARY。</summary>
    public const uint MonitorInfoPrimary = 0x1;

    /// <summary>MDT_EFFECTIVE_DPI：决定缩放比例的 DPI。</summary>
    public const int MdtEffectiveDpi = 0;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfoEx lpmi);

    /// <summary>
    /// 取某块显示器的有效 DPI。
    /// 注意：微软文档提示 PMv2 进程不宜用本函数推导窗口尺寸，但读"这块屏是多少 DPI"仍然可用；
    /// 诊断里会再用 <see cref="GetScaleFactorForMonitor"/> 交叉验证一次。
    /// </summary>
    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    /// <summary>SHELLSCALINGAPI 的缩放百分比（100/125/150/175/200/…），返回 HRESULT。</summary>
    [DllImport("shcore.dll")]
    public static extern int GetScaleFactorForMonitor(IntPtr hMonitor, out int pScale);
    // ---- 窗口摆放（遮罩必须用物理像素定位）----
    public const int GwlExStyle = -20;

    public const int HwndTopmost = -1;

    public const uint SwpNoActivate = 0x0010;

    public const uint SwpNoMove = 0x0002;

    public const uint SwpNoSize = 0x0001;

    public const uint SwpNoZOrder = 0x0004;

    /// <summary>改了扩展样式后必须带上它，窗口管理器才会重新计算命中测试。</summary>
    public const uint SwpFrameChanged = 0x0020;

    [LibraryImport("user32.dll")]
    public static partial IntPtr WindowFromPoint(NativePoint point);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    /// <summary>GWL_STYLE。</summary>
    public const int GwlStyle = -16;

    /// <summary>WS_VISIBLE。</summary>
    public const int WsVisible = 0x10000000;

    // 扩展样式只有 32 位，用 W 版最稳：GetWindowLongPtrW 在 32 位进程里并不导出。
    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    public static partial int GetWindowLong(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    public static partial int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out NativePoint lpPoint);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(IntPtr hWnd);

    /// <summary>按类名找窗口。用来判断当前会话有没有任务栏（Shell_TrayWnd），托盘图标依赖它。</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    // ---- 自检用的真实输入注入（模拟滚轮），用来验证交互路径而不是只验证状态 ----
    public const uint MouseEventWheel = 0x0800;

    public const uint MouseEventLeftDown = 0x0002;

    public const uint MouseEventLeftUp = 0x0004;

    public const byte VirtualKeyControl = 0x11;

    public const byte VirtualKeyMenu = 0x12;

    public const uint KeyEventKeyUp = 0x0002;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll", EntryPoint = "mouse_event")]
    public static partial void MouseEvent(uint flags, int dx, int dy, int data, IntPtr extraInfo);

    [LibraryImport("user32.dll", EntryPoint = "keybd_event")]
    public static partial void KeybdEvent(byte virtualKey, byte scanCode, uint flags, IntPtr extraInfo);

    /// <summary>等 DWM 把当前这帧合成出来，用于消除遮罩首帧的闪烁。</summary>
    [LibraryImport("dwmapi.dll")]
    public static partial int DwmFlush();

    [LibraryImport("kernel32.dll")]
    public static partial IntPtr GetCurrentProcess();

    /// <summary>uiFlags：0 = GDI 对象，1 = USER 对象。</summary>
    [LibraryImport("user32.dll")]
    public static partial int GetGuiResources(IntPtr hProcess, uint uiFlags);

    // ---- 顶层窗口枚举（窗口吸附）----
    /// <summary>DWMWA_EXTENDED_FRAME_BOUNDS：不含投影阴影的真实可见边界。</summary>
    public const int DwmwaExtendedFrameBounds = 9;

    /// <summary>DWMWA_CLOAKED：非 0 表示窗口被 DWM 隐藏（虚拟桌面残留等）。</summary>
    public const int DwmwaCloaked = 14;

    public const int MaxWindowTextChars = 512;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetWindowText(IntPtr hWnd, [Out] char[] lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetClassName(IntPtr hWnd, [Out] char[] lpClassName, int nMaxCount);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [LibraryImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    public static partial int DwmGetWindowAttributeRect(IntPtr hwnd, int attribute, out NativeRect value, int size);

    [LibraryImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    public static partial int DwmGetWindowAttributeInt(IntPtr hwnd, int attribute, out int value, int size);
}

/// <summary><c>EnumWindows</c> 的回调。</summary>
internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

/// <summary><c>EnumDisplayMonitors</c> 的回调。</summary>
internal delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref NativeRect lprcMonitor, IntPtr dwData);

/// <summary>Win32 <c>MONITORINFOEXW</c>（104 字节，含 32 字符设备名）。</summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct MonitorInfoEx
{
    public int CbSize;

    public NativeRect RcMonitor;

    public NativeRect RcWork;

    public uint DwFlags;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string SzDevice;
}

/// <summary>Win32 <c>BITMAPINFOHEADER</c>。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct BitmapInfoHeader
{
    public uint Size;
    public int Width;

    /// <summary>负值表示自上而下的 DIB——正是我们要的行序，省掉一次整图翻转。</summary>
    public int Height;

    public ushort Planes;
    public ushort BitCount;
    public uint Compression;
    public uint SizeImage;
    public int XPelsPerMeter;
    public int YPelsPerMeter;
    public uint ClrUsed;
    public uint ClrImportant;
}

/// <summary>Win32 <c>BITMAPINFO</c>。32bpp BI_RGB 不需要调色板，只需头部。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct BitmapInfo
{
    public BitmapInfoHeader Header;
}
