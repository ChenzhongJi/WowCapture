using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using WowCapture.App.Services;

namespace WowCapture.App.Tray;

/// <summary>托盘图标与右键菜单。</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly FileLog _log;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _captureItem;
    private readonly ToolStripMenuItem _disableClickThroughItem;

    public event EventHandler? ExitRequested;

    /// <summary>用户从菜单点了"截图"（与热键走同一条链路）。</summary>
    public event EventHandler? CaptureRequested;

    /// <summary>用户要打开截图保存目录。</summary>
    public event EventHandler? OpenOutputFolderRequested;

    /// <summary>用户要打开设置窗口。</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>用户要关闭所有贴图。</summary>
    public event EventHandler? CloseAllPinsRequested;

    /// <summary>用户要取消所有贴图的点击穿透。</summary>
    public event EventHandler? DisableClickThroughRequested;

    public TrayIcon(FileLog log)
    {
        _log = log;

        _captureItem = new ToolStripMenuItem("截图");
        _captureItem.Click += (_, _) => CaptureRequested?.Invoke(this, EventArgs.Empty);

        var settings = new ToolStripMenuItem("设置…");
        settings.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);

        var openData = new ToolStripMenuItem("打开数据目录");
        var openOutput = new ToolStripMenuItem("打开截图目录");
        var closePins = new ToolStripMenuItem("关闭全部贴图");
        var disableClickThrough = new ToolStripMenuItem("取消贴图穿透")
        {
            Enabled = false,
            ToolTipText = "贴图开启点击穿透后，用它恢复",
        };
        var openLog = new ToolStripMenuItem("打开日志");
        var exit = new ToolStripMenuItem("退出");

        openData.Click += (_, _) => Open(AppPaths.DataRoot);
        openOutput.Click += (_, _) => OpenOutputFolderRequested?.Invoke(this, EventArgs.Empty);
        closePins.Click += (_, _) => CloseAllPinsRequested?.Invoke(this, EventArgs.Empty);
        disableClickThrough.Click += (_, _) => DisableClickThroughRequested?.Invoke(this, EventArgs.Empty);
        openLog.Click += (_, _) => Open(AppPaths.LogFile);
        exit.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        _disableClickThroughItem = disableClickThrough;

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(
        [
            _captureItem,
            settings,
            new ToolStripSeparator(),
            openOutput,
            closePins,
            disableClickThrough,
            openData,
            openLog,
            new ToolStripSeparator(),
            exit,
        ]);

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = $"WowCapture {AppInfo.Version}",
            Visible = true,
            ContextMenuStrip = menu,
        };

        _notifyIcon.DoubleClick += (_, _) => ShowAbout();
    }

    /// <summary>把当前生效的热键显示在菜单项上——用户改完配置能立刻看到是否生效。</summary>
    public void SetCaptureHotkeyText(string hotkeyText) => _captureItem.Text = $"截图\t{hotkeyText}";

    /// <summary>有贴图处于穿透状态时才启用"取消贴图穿透"。</summary>
    public void SetClickThroughAvailable(bool available) => _disableClickThroughItem.Enabled = available;

    public void ShowBalloon(string title, string text, bool warning = false) =>        _notifyIcon.ShowBalloonTip(
            timeout: 4000,
            tipTitle: title,
            tipText: text,
            tipIcon: warning ? ToolTipIcon.Warning : ToolTipIcon.Info);

    private void ShowAbout()
    {
        MessageBox.Show(
            $"""
             WowCapture {AppInfo.Version}

             截图：
               按热键或用托盘菜单"截图"
               拖动框选（悬停可吸附整个窗口）
               选区内拖动画标注：A/R/E/L/P/H 切换，Ctrl+Z 撤销
               方向键微调选区 · Enter 确认复制 · Esc 取消

             数据目录：{AppPaths.DataRoot}
             """,
            "关于 WowCapture",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void Open(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            _log.Warn($"打开失败，目标不存在：{path}");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.Error($"打开失败：{path}", ex);
        }
    }

    /// <summary>供宿主打开一个已经解析好的路径（例如截图目录）。</summary>
    public void OpenPath(string path) => Open(path);

    public void Dispose()
    {
        // 必须先隐藏再释放，否则托盘会残留一个死图标，直到鼠标划过去才消失。
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
