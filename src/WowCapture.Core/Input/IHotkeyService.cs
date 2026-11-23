namespace WowCapture.Core.Input;

/// <summary>热键注册结果。</summary>
public enum HotkeyRegisterResult
{
    /// <summary>注册成功。</summary>
    Success,

    /// <summary>被其它进程占用（Win32 错误 1409）。这是最常见的情况，必须提示用户改键。</summary>
    AlreadyUsed,

    /// <summary>组合本身非法（无修饰键、键码越界）。</summary>
    Invalid,

    /// <summary>其它 Win32 失败。</summary>
    Failed,
}

/// <summary>
/// 全局热键服务。实现必须在 UI 消息泵所在的线程创建，
/// 且注册失败时把 Win32 错误码原样交给调用方——静默失败会让工具"突然不好使"。
/// </summary>
public interface IHotkeyService : IDisposable
{
    event EventHandler? Pressed;

    HotkeyGesture? Current { get; }

    HotkeyRegisterResult Register(HotkeyGesture gesture, out int win32Error);

    void Unregister();
}
