using System.Runtime.InteropServices;
using System.Windows.Interop;
using WowCapture.Core.Input;
using WowCapture.Platform.Interop;

namespace WowCapture.App.Services;

/// <summary>
/// 基于 <c>RegisterHotKey</c> 的全局热键服务。
/// </summary>
/// <remarks>
/// <para>
/// 热键必须挂在某个窗口上，而托盘工具平时没有窗口，所以这里建一个 1×1 的隐藏
/// <see cref="HwndSource"/> 专门收 <c>WM_HOTKEY</c>。消息由 WPF 的消息循环派发，
/// 因此 <see cref="Pressed"/> 总是在 UI 线程触发，订阅方不需要再切线程。
/// </para>
/// <para>
/// 刻意不用低级键盘钩子：杀软会报警，而且"前台是提权窗口时收不到热键"是系统的安全边界，
/// 挂钩子也绕不过去，只会把问题变得更不可预测。
/// </para>
/// </remarks>
internal sealed class HotkeyService : IHotkeyService
{
    /// <summary>只注册一个热键，id 取 "WW" 免得和别人的 1 撞。</summary>
    private const int HotkeyId = 0x5757;

    private readonly FileLog _log;
    private readonly HwndSource _source;
    private bool _registered;
    private bool _disposed;

    public HotkeyService(FileLog log)
    {
        _log = log;

        var parameters = new HwndSourceParameters("WowCapture.HotkeyWindow")
        {
            WindowStyle = 0,
            ExtendedWindowStyle = NativeMethods.WsExToolWindow,
            Width = 1,
            Height = 1,
            PositionX = 0,
            PositionY = 0,
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public event EventHandler? Pressed;

    public HotkeyGesture? Current { get; private set; }

    public HotkeyRegisterResult Register(HotkeyGesture gesture, out int win32Error)
    {
        win32Error = 0;
        Unregister();

        if (!gesture.IsValid)
        {
            _log.Warn($"热键非法（必须有修饰键且在 1..255 之间）：{gesture}");
            return HotkeyRegisterResult.Invalid;
        }

        var modifiers = ToWin32(gesture.Modifiers) | NativeMethods.ModNoRepeat;

        if (!NativeMethods.RegisterHotKey(_source.Handle, HotkeyId, modifiers, (uint)gesture.VirtualKey))
        {
            win32Error = Marshal.GetLastWin32Error();
            _log.Warn($"注册热键 {gesture} 失败，Win32 错误码 {win32Error}");

            return win32Error == NativeMethods.ErrorHotkeyAlreadyRegistered
                ? HotkeyRegisterResult.AlreadyUsed
                : HotkeyRegisterResult.Failed;
        }

        _registered = true;
        Current = gesture;
        _log.Info($"热键已注册：{gesture}");
        return HotkeyRegisterResult.Success;
    }

    public void Unregister()
    {
        if (!_registered)
        {
            return;
        }

        NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
        Current = null;
    }

    /// <summary>
    /// 诊断用：向自己的消息窗投递一条 <c>WM_HOTKEY</c>，验证回调链路是否接对。
    /// 故意不用 <c>SendInput</c> 注入真实按键——那会把组合键打到当前前台应用里去。
    /// </summary>
    internal bool SimulatePress()
    {
        if (!_registered || _disposed)
        {
            return false;
        }

        return NativeMethods.PostMessage(_source.Handle, NativeMethods.WmHotkey, new IntPtr(HotkeyId), IntPtr.Zero);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }

    private static uint ToWin32(HotkeyModifiers modifiers)
    {
        var value = 0u;

        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            value |= NativeMethods.ModAlt;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Control))
        {
            value |= NativeMethods.ModControl;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            value |= NativeMethods.ModShift;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Win))
        {
            value |= NativeMethods.ModWin;
        }

        return value;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Unregister();

        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
