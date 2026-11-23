using System.Text.Json.Serialization;
using WowCapture.Core.Input;

namespace WowCapture.Core.Settings;

/// <summary>
/// 用户配置。刻意保持扁平、可手改：出问题时用户能直接打开 json 改，而不是重装。
/// </summary>
public sealed class AppSettings
{
    public HotkeySettings Hotkey { get; set; } = new();

    public OutputSettings Output { get; set; } = new();

    public CaptureSettings Capture { get; set; } = new();

    public StartupSettings Startup { get; set; } = new();

    /// <summary>首次运行引导是否已经看过了。</summary>
    public bool FirstRunCompleted { get; set; }

    public static AppSettings CreateDefault() => new();

    public HotkeyGesture GetHotkeyGesture() => new(Hotkey.Modifiers, Hotkey.VirtualKey);

    public sealed class HotkeySettings
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public HotkeyModifiers Modifiers { get; set; } = HotkeyModifiers.Control | HotkeyModifiers.Alt;

        /// <summary>Win32 虚拟键码；<c>0x41</c> 即字母 A。</summary>
        public int VirtualKey { get; set; } = VirtualKeyCodes.A;
    }

    public sealed class OutputSettings
    {
        /// <summary>为空表示使用默认目录（<c>图片\WowCapture</c>）。</summary>
        public string? Directory { get; set; }

        public string Format { get; set; } = "png";

        public bool CopyToClipboard { get; set; } = true;

        public bool AutoSave { get; set; }

        public string FileNameTemplate { get; set; } = "{yyyy}-{MM}-{dd}_{HHmmss}";
    }

    public sealed class CaptureSettings
    {
        /// <summary>鼠标悬停时吸附到窗口边界。</summary>
        public bool SmartSnap { get; set; } = true;

        public bool IncludeCursor { get; set; }

        /// <summary>遮罩压暗程度（0 = 不压暗，1 = 全黑）。</summary>
        public double DimOpacity { get; set; } = 0.45;

        /// <summary>确认截图后的默认动作（工具栏上的"贴图"按钮会覆盖它）。</summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public AfterCaptureAction AfterCapture { get; set; } = AfterCaptureAction.Copy;
    }

    public sealed class StartupSettings
    {
        /// <summary>开机自动启动（写在 HKCU 的 Run 项里，不需要管理员权限）。</summary>
        public bool RunAtLogin { get; set; }
    }
}

/// <summary>确认截图之后做什么。</summary>
public enum AfterCaptureAction
{
    /// <summary>复制到剪贴板（并按 <see cref="AppSettings.OutputSettings.AutoSave"/> 决定是否保存）。</summary>
    Copy,

    /// <summary>只保存到磁盘。</summary>
    Save,

    /// <summary>贴到屏幕上。</summary>
    Pin,

    /// <summary>什么都不做，只关闭遮罩。</summary>
    None,
}
