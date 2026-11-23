namespace WowCapture.Core.Input;

/// <summary>
/// 一个全局热键组合。虚拟键码沿用 Win32 约定（<c>A</c>=0x41、<c>F1</c>=0x70），
/// 这样 Core 不需要知道 WPF 或 WinForms 的 Keys 枚举，平台层也不必做映射表。
/// </summary>
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, int VirtualKey)
{
    /// <summary>默认 <c>Ctrl+Alt+A</c>：比 PrintScreen 更不容易被系统或其它软件占用。</summary>
    public static HotkeyGesture Default { get; } =
        new(HotkeyModifiers.Control | HotkeyModifiers.Alt, VirtualKeyCodes.A);

    /// <summary>至少要有修饰键，且虚拟键码必须在 1..255 之间，否则 <c>RegisterHotKey</c> 必然失败。</summary>
    public bool IsValid => VirtualKey is > 0 and <= 0xFF && Modifiers != HotkeyModifiers.None;

    public override string ToString() => HotkeyText.Format(this);
}

/// <summary>用到的虚拟键码（只列会出现在默认配置与文档里的那些）。</summary>
public static class VirtualKeyCodes
{
    public const int A = 0x41;
    public const int Z = 0x5A;
    public const int F1 = 0x70;
}
