using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WowCapture.App.Services;
using WowCapture.Core.Input;
using WowCapture.Core.Settings;
using WowCapture.Platform.Interop;
using WinForms = System.Windows.Forms;

namespace WowCapture.App.Windows;

/// <summary>
/// 设置窗口（最小版）：热键、截图后动作、输出目录、开机自启。
/// 保存即生效——热键当场重注册、托盘文案当场刷新，不需要重启程序。
/// </summary>
/// <remarks>
/// 高级项（命名模板、压暗程度、马赛克块大小…）仍走 settings.json 手改；
/// 这个窗口只覆盖"改起来最高频、改错代价最大"的四项。
/// </remarks>
internal sealed class SettingsWindow : Window
{
    private readonly SettingsStore _store;
    private readonly HotkeyService _hotkey;
    private readonly FileLog _log;
    private readonly Action _applied;

    private readonly TextBlock _hotkeyText;
    private readonly Button _recordButton;
    private readonly ComboBox _actionBox;
    private readonly TextBox _outputBox;
    private readonly CheckBox _autoStartBox;
    private readonly TextBlock _hint;

    private HotkeyGesture? _pendingGesture;
    private bool _recording;

    public SettingsWindow(SettingsStore store, HotkeyService hotkey, FileLog log, Action applied)
    {
        _store = store;
        _hotkey = hotkey;
        _log = log;
        _applied = applied;

        Title = "WowCapture 设置";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _hotkeyText = new TextBlock
        {
            Text = store.Current.GetHotkeyGesture().ToString(),
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
        };

        _recordButton = new Button { Content = "修改…", Width = 76, Height = 26, Margin = new Thickness(12, 0, 0, 0) };
        _recordButton.Click += (_, _) => ToggleRecording();

        _actionBox = new ComboBox { Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var (name, value) in new (string, AfterCaptureAction)[]
                 {
                     ("复制到剪贴板", AfterCaptureAction.Copy),
                     ("仅保存到磁盘", AfterCaptureAction.Save),
                     ("贴到屏幕上", AfterCaptureAction.Pin),
                     ("无动作", AfterCaptureAction.None),
                 })
        {
            _actionBox.Items.Add(new ComboBoxItem { Content = name, Tag = value });
        }

        _outputBox = new TextBox { Width = 216, VerticalAlignment = VerticalAlignment.Center };
        _outputBox.Text = store.Current.Output.Directory ?? string.Empty;

        var browse = new Button { Content = "浏览…", Width = 76, Height = 26, Margin = new Thickness(8, 0, 0, 0) };
        browse.Click += OnBrowseClick;

        _autoStartBox = new CheckBox
        {
            Content = "登录 Windows 后自动启动",
            IsChecked = store.Current.Startup.RunAtLogin,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _hint = new TextBlock
        {
            Text = string.Empty,
            Foreground = Brushes.DimGray,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        };

        SelectCurrentAction(store.Current.Capture.AfterCapture);

        var save = new Button { Content = "保存并关闭", Width = 110, Height = 30, IsDefault = true };
        save.Click += OnSaveClick;

        var cancel = new Button { Content = "取消", Width = 80, Height = 30, IsCancel = true };
        cancel.Click += (_, _) => Close();

        Content = BuildContent(save, cancel, browse);
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private UIElement BuildContent(Button save, Button cancel, Button browse)
    {
        var grid = new Grid { Margin = new Thickness(24, 20, 24, 20) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        for (var i = 0; i < 5; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddRow(grid, 0, "截图热键", HorizontalStack(_hotkeyText, _recordButton));
        AddRow(grid, 1, "截图后动作", _actionBox);
        AddRow(grid, 2, "输出目录", HorizontalStack(_outputBox, browse));
        AddRow(grid, 4, "开机自启", _autoStartBox);

        Grid.SetRow(_hint, 3);
        Grid.SetColumn(_hint, 1);
        _hint.Margin = new Thickness(0, 2, 0, 10);
        grid.Children.Add(_hint);

        var hint = new TextBlock
        {
            Text = "输出目录留空 = 图片\\WowCapture（按年月分目录）；命名模板等高级项仍可手改 settings.json。",
            Foreground = Brushes.DimGray,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(96, 2, 0, 12),
        };
        Grid.SetRow(hint, 5);
        Grid.SetColumnSpan(hint, 2);
        grid.Children.Add(hint);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        save.Margin = new Thickness(10, 0, 0, 0);

        Grid.SetRow(buttons, 6);
        Grid.SetColumnSpan(buttons, 2);
        buttons.Margin = new Thickness(0, 4, 0, 0);
        grid.Children.Add(buttons);

        return grid;
    }

    private static StackPanel HorizontalStack(params UIElement[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var child in children)
        {
            panel.Children.Add(child);
        }

        return panel;
    }

    private static void AddRow(Grid grid, int row, string label, FrameworkElement content)
    {
        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
        };
        Grid.SetRow(text, row);
        grid.Children.Add(text);

        content.VerticalAlignment = VerticalAlignment.Center;
        content.Margin = new Thickness(0, 0, 0, 14);
        Grid.SetRow(content, row);
        Grid.SetColumn(content, 1);
        grid.Children.Add(content);
    }

    private void SelectCurrentAction(AfterCaptureAction current)
    {
        foreach (ComboBoxItem item in _actionBox.Items)
        {
            if (item.Tag is AfterCaptureAction value && value == current)
            {
                _actionBox.SelectedItem = item;
                return;
            }
        }

        _actionBox.SelectedIndex = 0;
    }

    /// <summary>进入/退出热键录制。</summary>
    private void ToggleRecording()
    {
        if (_recording)
        {
            StopRecording();
            return;
        }

        _recording = true;
        _recordButton.Content = "取消录制";
        _hotkeyText.Text = "请按下新的组合键…";
        _hint.Text = "先按住 Ctrl / Alt / Shift / Win，再按主键；Esc 取消录制。";
    }

    private void StopRecording()
    {
        _recording = false;
        _recordButton.Content = "修改…";
        _hotkeyText.Text = (_pendingGesture ?? _store.Current.GetHotkeyGesture()).ToString();
        _hint.Text = string.Empty;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_recording)
        {
            return;
        }

        e.Handled = true;

        // 按住 Alt 时 WPF 把主键放在 SystemKey 里。
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            StopRecording();
            return;
        }

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System)
        {
            // 修饰键不是终点，等真正的主键。
            _hotkeyText.Text = "修饰键 + …（再按主键）";
            return;
        }

        var modifiers = ToHotkeyModifiers();
        if (modifiers == HotkeyModifiers.None)
        {
            _hint.Text = "至少按住一个修饰键（Ctrl / Alt / Shift / Win）。";
            return;
        }

        var gesture = new HotkeyGesture(modifiers, KeyInterop.VirtualKeyFromKey(key));
        if (!gesture.IsValid)
        {
            _hint.Text = $"这个键不能用作热键（VK 0x{gesture.VirtualKey:X2}）。";
            return;
        }

        _pendingGesture = gesture;
        _hint.Text = string.Empty;
        _recording = false;
        _recordButton.Content = "修改…";
        _hotkeyText.Text = gesture.ToString();
    }

    private static HotkeyModifiers ToHotkeyModifiers()
    {
        var modifiers = HotkeyModifiers.None;
        var keyboard = Keyboard.Modifiers;

        if (keyboard.HasFlag(ModifierKeys.Control))
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (keyboard.HasFlag(ModifierKeys.Alt))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (keyboard.HasFlag(ModifierKeys.Shift))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        // WPF 的 Keyboard.Modifiers 不包含 Win 键，这里用 Win32 补上。
        if (NativeMethods.GetAsyncKeyState(NativeMethods.VkLwin) < 0
            || NativeMethods.GetAsyncKeyState(NativeMethods.VkRwin) < 0)
        {
            modifiers |= HotkeyModifiers.Win;
        }

        return modifiers;
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "选择截图保存目录（留空使用默认的 图片\\WowCapture）",
            UseDescriptionForTitle = true,
        };

        if (Directory.Exists(_outputBox.Text))
        {
            dialog.SelectedPath = _outputBox.Text;
        }

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            _outputBox.Text = dialog.SelectedPath;
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var settings = _store.Current;
        var gesture = _pendingGesture ?? settings.GetHotkeyGesture();

        // Register 会先注销旧键；新键注册失败时必须把旧键注册回去，
        // 否则一次失败的尝试就把用户唯一的截屏入口弄没了。
        if (!gesture.Equals(settings.GetHotkeyGesture()))
        {
            var result = _hotkey.Register(gesture, out var win32Error);
            if (result != HotkeyRegisterResult.Success)
            {
                _hotkey.Register(settings.GetHotkeyGesture(), out _);
                _hint.Text = $"注册 {gesture} 失败：{result.Describe()}（Win32 错误码 {win32Error}），热键保持不变。";
                return;
            }
        }

        settings.Hotkey.Modifiers = gesture.Modifiers;
        settings.Hotkey.VirtualKey = gesture.VirtualKey;
        settings.Capture.AfterCapture = SelectedAction();

        var directory = _outputBox.Text.Trim();
        settings.Output.Directory = directory.Length == 0 ? null : directory;
        settings.Startup.RunAtLogin = _autoStartBox.IsChecked == true;

        if (!_store.Save())
        {
            _hint.Text = "配置保存失败，详情见日志。";
            return;
        }

        _log.Info(
            $"设置已更新：热键={gesture}，动作={settings.Capture.AfterCapture}，"
            + $"输出目录={settings.Output.Directory ?? "(默认)"}，自启={settings.Startup.RunAtLogin}");

        _applied?.Invoke();
        Close();
    }

    private AfterCaptureAction SelectedAction() =>
        _actionBox.SelectedItem is ComboBoxItem { Tag: AfterCaptureAction action } ? action : AfterCaptureAction.Copy;
}
