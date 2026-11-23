using System.IO;
using System.Text;
using WowCapture.App.Services;
using WowCapture.Core.Input;
using WowCapture.Core.Layout;
using WowCapture.Platform;
using WowCapture.Platform.Screen;
namespace WowCapture.App.Diagnostics;

/// <summary>
/// <c>--diagnose</c>：把"我这台机器上坐标/DPI/热键到底对不对"变成一段可复制的文本。
/// 这是 S2（跨屏遮罩像素对齐）在没有第二块屏时的可执行验收方式：
/// 打印每块屏的遮罩摆放 + 物理↔DIP 往返自检，任何换算错误都会在这里变成失败计数。
/// </summary>
internal sealed class EnvironmentDiagnostics
{
    private readonly FileLog _log;
    private readonly StringBuilder _report = new();
    private readonly bool _console = ConsoleBridge.TryAttachToParentConsole();
    private int _hotkeyPresses;
    private int _hotkeyFailures;

    public EnvironmentDiagnostics(FileLog log) => _log = log;

    public void PrintEnvironment(SettingsStore settings)
    {
        Line("== WowCapture 环境诊断 ==");
        Line($"时间      : {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        Line($"版本      : {AppInfo.Version}");
        Line($"数据目录  : {AppPaths.DataRoot}{(AppPaths.IsPortable ? "（便携模式）" : string.Empty)}");
        Line($"首选目录  : {AppPaths.PreferredDataRoot}{(AppPaths.IsFallback ? "  ← 发生了回退（受限环境）" : string.Empty)}");
        Line($"运行会话  : session={ShellInfo.SessionId}，任务栏={ShellInfo.IsTaskbarAvailable}（False 时托盘图标不会显示）");

        var monitors = new Win32MonitorService();
        var list = monitors.GetMonitors();
        var virtualBounds = monitors.GetVirtualScreenBounds();

        Line($"虚拟屏    : {virtualBounds}");
        Line($"显示器数  : {list.Count}");

        var roundTripFailures = 0;

        foreach (var monitor in list)
        {
            var placement = OverlayPlacement.For(monitor);
            var failures = placement.VerifyRoundTrip(BuildSamplePoints(placement));
            roundTripFailures += failures;

            Line(string.Empty);
            Line($"  [{monitor.Index}] {(monitor.IsPrimary ? "主屏" : "副屏")}  dpi={monitor.Dpi}（{monitor.Scale:P0}）");
            Line($"      bounds  : {monitor.Bounds}");
            Line($"      workarea: {monitor.WorkArea}");
            Line($"      遮罩窗口: 物理 {monitor.Bounds.Width}x{monitor.Bounds.Height} → DIP {placement.WidthDip:F1}x{placement.HeightDip:F1}");
            Line($"      坐标往返: {(failures == 0 ? "通过" : $"失败 {failures} 点")}");
        }

        var probes = monitors.LastProbes;
        if (probes.Count > 0)
        {
            Line(string.Empty);
            Line("DPI 交叉验证（shcore）:");

            foreach (var probe in probes)
            {
                var shellPercent = probe.ShellScale > 0 ? probe.ShellScale : (int)Math.Round(probe.Dpi / 96.0 * 100);
                var dpiPercent = (int)Math.Round(probe.Dpi / 96.0 * 100);
                var consistent = Math.Abs(shellPercent - dpiPercent) <= 25;

                Line($"  {probe.DeviceName.Trim()}: dpi={probe.Dpi} → {dpiPercent}%；系统缩放 {shellPercent}%；{(consistent ? "一致" : "不一致，需查 DPI 查询方式")}");
            }
        }

        Line(string.Empty);
        Line($"设置文件  : {settings.FilePath}{(settings.WasCreatedFromDefaults ? "（本次新建，使用默认值）" : "（已加载）")}");
        Line($"默认热键  : {settings.Current.GetHotkeyGesture()}");

        Line(string.Empty);
        Line($"S2 结论   : {(roundTripFailures == 0 ? "坐标换算往返全部通过" : $"坐标换算存在 {roundTripFailures} 处失败，先修换算再写遮罩")}");
    }

    public void RegisterHotkey(HotkeyService hotkey, HotkeyGesture gesture)
    {
        var result = hotkey.Register(gesture, out var win32Error);

        Line(string.Empty);
        Line($"热键注册  : {gesture} → {result.Describe()}{(win32Error != 0 ? $"（Win32 错误码 {win32Error}）" : string.Empty)}");

        if (result != HotkeyRegisterResult.Success)
        {
            _hotkeyFailures++;
            return;
        }

        hotkey.Pressed += (_, _) => _hotkeyPresses++;

        if (!hotkey.SimulatePress())
        {
            _hotkeyFailures++;
            Line("消息通道  : 投递 WM_HOTKEY 失败");
        }
    }

    /// <summary>消息泵跑过一轮之后调用：核对回调是否真的收到，落盘并给出退出码。</summary>
    public int Finish()
    {
        Line($"消息通道  : 合成 WM_HOTKEY 后收到 {_hotkeyPresses} 次事件");
        Line(string.Empty);

        var exitCode = _hotkeyPresses == 1 && _hotkeyFailures == 0 ? 0 : 2;

        Line(exitCode == 0
            ? "结果      : 全部通过"
            : "结果      : 存在失败项，见上面的标记");

        Flush();
        return exitCode;
    }

    private void Flush()
    {
        var text = _report.ToString();

        try
        {
            var path = Path.Combine(AppPaths.DataRoot, "diagnose.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, Encoding.UTF8);
            _log.Info($"诊断报告已写入：{path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"诊断报告写入失败：{ex.Message}");
        }
    }

    private void Line(string text)
    {
        _report.AppendLine(text);

        if (text.Length > 0)
        {
            _log.Info(text);
        }

        if (_console)
        {
            Console.WriteLine(text);
        }
    }

    /// <summary>取四角与中心，覆盖取整误差最容易暴露的位置。</summary>
    private static IReadOnlyList<(double X, double Y)> BuildSamplePoints(OverlayPlacement placement) =>
    [
        (0, 0),
        (placement.WidthDip - 1, 0),
        (0, placement.HeightDip - 1),
        (placement.WidthDip - 1, placement.HeightDip - 1),
        (placement.WidthDip / 2, placement.HeightDip / 2),
        (13, 7),
        (255, 129),
    ];
}
