namespace WowCapture.Core.Input;

/// <summary>热键修饰键。位值与 Win32 的 <c>MOD_*</c> 一致，但刻意不复用其类型。</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8,
}
