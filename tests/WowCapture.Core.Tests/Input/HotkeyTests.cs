using WowCapture.Core.Input;
using Xunit;

namespace WowCapture.Core.Tests.Input;

public class HotkeyGestureTests
{
    [Fact]
    public void Default_是合法的CtrlAltA()
    {
        var gesture = HotkeyGesture.Default;

        Assert.True(gesture.IsValid);
        Assert.Equal("Ctrl+Alt+A", gesture.ToString());
    }

    [Fact]
    public void IsValid_缺少修饰键或键码越界时为假()
    {
        Assert.False(new HotkeyGesture(HotkeyModifiers.None, VirtualKeyCodes.A).IsValid);
        Assert.False(new HotkeyGesture(HotkeyModifiers.Control, 0).IsValid);
        Assert.False(new HotkeyGesture(HotkeyModifiers.Control, 0x100).IsValid);
        Assert.True(new HotkeyGesture(HotkeyModifiers.Control, 0xFF).IsValid);
    }
}

public class HotkeyTextTests
{
    [Fact]
    public void Format_修饰键顺序固定()
    {
        var gesture = new HotkeyGesture(
            HotkeyModifiers.Shift | HotkeyModifiers.Win | HotkeyModifiers.Alt | HotkeyModifiers.Control,
            VirtualKeyCodes.Z);

        Assert.Equal("Ctrl+Alt+Shift+Win+Z", HotkeyText.Format(gesture));
    }

    [Theory]
    [InlineData(0x30, "0")]
    [InlineData(0x39, "9")]
    [InlineData(0x41, "A")]
    [InlineData(0x5A, "Z")]
    [InlineData(0x08, "Backspace")]
    [InlineData(0x0D, "Enter")]
    [InlineData(0x20, "Space")]
    [InlineData(0x2D, "Insert")]
    [InlineData(0x70, "F1")]
    [InlineData(0x7B, "F12")]
    [InlineData(0x7F, "F16")]
    [InlineData(0x87, "F24")]
    [InlineData(0x6A, "Num*")]
    [InlineData(0x2C, "PrintScreen")]
    [InlineData(0xC1, "VK_C1")]
    public void KeyName_覆盖常用键与兜底(int virtualKey, string expected)
    {
        Assert.Equal(expected, HotkeyText.KeyName(virtualKey));
    }
}
