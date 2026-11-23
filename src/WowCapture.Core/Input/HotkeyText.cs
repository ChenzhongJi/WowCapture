namespace WowCapture.Core.Input;

/// <summary>把热键转成界面文案（<c>Ctrl+Alt+A</c>）。</summary>
public static class HotkeyText
{
    public static string Format(HotkeyGesture gesture)
    {
        var parts = new List<string>(4);

        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Win))
        {
            parts.Add("Win");
        }

        if (gesture.VirtualKey != 0)
        {
            parts.Add(KeyName(gesture.VirtualKey));
        }

        return string.Join("+", parts);
    }

    public static string KeyName(int virtualKey) => virtualKey switch
    {
        >= 0x30 and <= 0x39 => ((char)virtualKey).ToString(),        // 0-9
        >= 0x41 and <= 0x5A => ((char)virtualKey).ToString(),        // A-Z
        >= 0x70 and <= 0x87 => $"F{virtualKey - 0x6F}",              // F1-F24
        0x08 => "Backspace",
        0x09 => "Tab",
        0x0D => "Enter",
        0x1B => "Esc",
        0x20 => "Space",
        0x21 => "PageUp",
        0x22 => "PageDown",
        0x23 => "End",
        0x24 => "Home",
        0x25 => "Left",
        0x26 => "Up",
        0x27 => "Right",
        0x28 => "Down",
        0x2C => "PrintScreen",
        0x2D => "Insert",
        0x2E => "Delete",
        0x5B => "Win",
        0x6A => "Num*",
        0x6B => "Num+",
        0x6D => "Num-",
        0x6E => "Num.",
        0x6F => "Num/",
        _ => $"VK_{virtualKey:X2}",
    };
}
