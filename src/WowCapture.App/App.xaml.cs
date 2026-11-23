using System.IO;
using System.Threading;
using System.Windows.Media.Imaging;
using WowCapture.App.Benchmarks;
using WowCapture.App.Diagnostics;
using WowCapture.App.Imaging;
using WowCapture.App.Overlay;
using WowCapture.App.Output;
using WowCapture.App.Pin;
using WowCapture.App.Services;
using WowCapture.App.Tray;
using WowCapture.App.Windows;
using WowCapture.Capture;
using WowCapture.Core.Annotations;
using WowCapture.Core.Geometry;
using WowCapture.Core.Imaging;
using WowCapture.Core.Input;
using WowCapture.Core.Settings;
using WowCapture.Platform.Screen;

namespace WowCapture.App;

/// <summary>应用宿主：单实例、日志、配置、热键、托盘；热键触发冻结帧 → 遮罩 → 选区 → 剪贴板。</summary>
public partial class App : System.Windows.Application
{
    private const string MutexName = @"Local\WowCapture.SingleInstance";

    private Mutex? _instanceMutex;
    private bool _ownsMutex;
    private FileLog? _log;
    private TrayIcon? _tray;
    private SettingsStore? _settings;
    private HotkeyService? _hotkey;
    private PinManager? _pins;
    private FirstRunWindow? _firstRunWindow;
    private OverlaySession? _overlaySession;

    /// <summary>一次截图会话正在进行（含抓屏阶段）。只在 UI 线程读写。</summary>
    private bool _captureInProgress;

    /// <summary>`--selftest`：完整走一遍启动流程后立刻退出，供脚本与 CI 冒烟。</summary>
    private static bool IsSelfTest => HasFlag("--selftest");

    /// <summary>`--diagnose`：打印显示器/DPI/坐标往返/热键链路。</summary>
    private static bool IsDiagnose => HasFlag("--diagnose");

    /// <summary>`--overlay-test`：无人值守跑一遍遮罩与选区，落两张样图。</summary>
    private static bool IsOverlayTest => HasFlag("--overlay-test");

    /// <summary>`--snap-test`：验证窗口吸附的查找、排除与高亮渲染。</summary>
    private static bool IsSnapTest => HasFlag("--snap-test");

    /// <summary>`--annotate-test`：验证标注渲染与合成导出。</summary>
    private static bool IsAnnotateTest => HasFlag("--annotate-test");

    /// <summary>`--pin-test`：验证贴图窗口的尺寸、置顶样式、渲染、缩放与资源回收。</summary>
    private static bool IsPinTest => HasFlag("--pin-test");
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!AcquireSingleInstance())
        {
            return;
        }

        AppPaths.EnsureCreated();
        _log = new FileLog(AppPaths.LogFile);
        _pins = new PinManager(_log);
        HookGlobalExceptionHandlers();
        _log.Info($"WowCapture {AppInfo.Version} 启动，pid={Environment.ProcessId}，数据目录={AppPaths.DataRoot}"
                  + (AppPaths.IsWritable ? string.Empty : "（不可写，配置与日志将无法保存）"));
        _log.Info(
            $"运行环境：session={Platform.ShellInfo.SessionId}，任务栏={Platform.ShellInfo.IsTaskbarAvailable}，"
            + $"数据目录回退={AppPaths.IsFallback}，首选目录={AppPaths.PreferredDataRoot}");

        try
        {
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            _log.Info($"进程身份：{identity.Name}，IsSystem={identity.IsSystem}，"
                      + $"USERPROFILE={Environment.GetEnvironmentVariable("USERPROFILE")}，"
                      + $"COMPAT_LAYER={Environment.GetEnvironmentVariable("__COMPAT_LAYER") ?? "(无)"}");

            // AppContainer / 受限令牌会体现在令牌组里（S-1-15-2-* 之类），排查权限问题时很关键。
            var groups = identity.Groups is null
                ? "(无)"
                : string.Join(" | ", identity.Groups.Select(g =>
                {
                    try
                    {
                        return g.Translate(typeof(System.Security.Principal.NTAccount)).Value;
                    }
                    catch (Exception)
                    {
                        return g.Value;
                    }
                }));

            _log.Info($"令牌组：{groups}");
        }
        catch (Exception ex)
        {
            _log.Warn($"读取进程身份失败：{ex.Message}");
        }

        if (!Platform.ShellInfo.IsTaskbarAvailable)
        {
            _log.Warn("当前会话没有任务栏：托盘图标不会显示。请从资源管理器正常启动 WowCapture，而不是从服务/沙箱会话启动。");
        }
        else if (AppPaths.IsFallback)
        {
            _log.Warn("数据目录发生回退（首选目录不可写）：通常说明程序在受限/沙箱环境里启动，托盘图标可能同样无法注册。");

            foreach (var line in AppPaths.ProbeReport)
            {
                _log.Info($"数据目录探测：{line}");
            }
        }

        // `--open-data`：直接打开数据目录。托盘图标在 Win11 默认收在溢出区里，
        // 找不到图标时这个参数就是"我的配置在哪"的答案。
        if (HasFlag("--open-data"))
        {
            TryOpenDirectory(AppPaths.DataRoot);
            ShutdownLater(0);
            return;
        }

        if (TryParseBenchmark(e.Args, out var iterations))
        {
            var benchmarkCode = CaptureBenchmark.Run(iterations, _log);
            ShutdownLater(benchmarkCode);
            return;
        }

        _settings = new SettingsStore(AppPaths.SettingsFile, _log);
        if (_settings.WasCreatedFromDefaults)
        {
            // 首次运行就把默认配置落盘：用户"能看见就能改"，比藏着掖着强。
            _settings.Save();
        }

        _hotkey = new HotkeyService(_log);

        if (IsDiagnose)
        {
            RunDiagnostics();
            return;
        }

        if (IsOverlayTest)
        {
            RunOverlayTest();
            return;
        }

        if (IsSnapTest)
        {
            RunSnapTest();
            return;
        }

        if (IsAnnotateTest)
        {
            RunAnnotateTest();
            return;
        }

        if (IsPinTest)
        {
            RunPinTest();
            return;
        }

        if (TryParseStress(e.Args, out var stressIterations))
        {
            var stressCode = ResourceStressTest.Run(stressIterations, _log);
            ShutdownLater(stressCode);
            return;
        }

        _tray = new TrayIcon(_log);
        _tray.ExitRequested += (_, _) => Shutdown(0);
        _tray.CaptureRequested += OnHotkeyPressed;
        _tray.OpenOutputFolderRequested += (_, _) => OpenOutputFolder();
        _tray.CloseAllPinsRequested += (_, _) => _pins?.CloseAll();
        _tray.DisableClickThroughRequested += (_, _) =>
        {
            _pins?.DisableClickThroughAll();
            UpdateClickThroughState();
        };

        if (_pins is not null)
        {
            _pins.Changed += (_, _) => UpdateClickThroughState();

            _pins.ClickThroughChanged += (_, _) =>
            {
                UpdateClickThroughState();

                if (_pins.AnyClickThrough)
                {
                    _tray?.ShowBalloon("已开启点击穿透", "贴图不再挡鼠标；可在托盘菜单「取消贴图穿透」恢复。");
                }
            };
        }

        RegisterHotkeyOrWarn();
        SyncAutoStart();
        UpdateClickThroughState();
        ShowFirstRunGuideIfNeeded();

        _log.Info("托盘已就绪");
        _tray.ShowBalloon(
            "WowCapture 已启动",
            $"按 {_settings.Current.GetHotkeyGesture()} 框选截图；Esc 取消，Enter 确认并复制。");

        if (IsSelfTest)
        {
            var selectionLogicOk = SelectionLogicTests.Run(_log);
            var outputOk = OutputTests.Run(_log);
            var startupOk = StartupTests.Run(_log);
            var ok = selectionLogicOk && outputOk && startupOk;

            _log.Info(ok ? "自检完成，即将退出" : "自检失败");
            ShutdownLater(ok ? 0 : 6);
        }
    }

    /// <summary>
    /// 诊断模式：同步部分先打印，热键与消息通道要等消息泵跑起来才能验证，
    /// 所以把收尾挂到第一次 Idle 上。
    /// </summary>
    private void RunDiagnostics()
    {
        var diagnostics = new EnvironmentDiagnostics(_log!);
        diagnostics.PrintEnvironment(_settings!);
        diagnostics.RegisterHotkey(_hotkey!, _settings!.Current.GetHotkeyGesture());

        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            new Action(() => ShutdownLater(diagnostics.Finish())));
    }

    private async void RunOverlayTest()
    {
        var code = 4;

        try
        {
            code = await OverlaySelfTest.RunAsync(_log!);
        }
        catch (Exception ex)
        {
            _log!.Error("遮罩自检失败", ex);
        }

        ShutdownLater(code);
    }

    private async void RunSnapTest()
    {
        var code = 9;

        try
        {
            code = await SnapSelfTest.RunAsync(_log!);
        }
        catch (Exception ex)
        {
            _log!.Error("吸附自检失败", ex);
        }

        ShutdownLater(code);
    }

    private async void RunAnnotateTest()
    {
        var code = 11;

        try
        {
            code = await AnnotationSelfTest.RunAsync(_log!);
        }
        catch (Exception ex)
        {
            _log!.Error("标注自检失败", ex);
        }

        ShutdownLater(code);
    }

    private async void RunPinTest()
    {
        var code = 13;

        try
        {
            code = await PinSelfTest.RunAsync(_log!);
        }
        catch (Exception ex)
        {
            _log!.Error("贴图自检失败", ex);
        }

        ShutdownLater(code);
    }

    /// <summary>把截图贴到屏幕上（选区左上角往右下偏一点，避免完全盖住原位置）。</summary>
    private void PinSelection(BitmapSource image, ScreenRect selection)
    {
        var anchor = new ScreenPoint(selection.X + 24, selection.Y + 24);
        var monitors = new Win32MonitorService().GetMonitors();

        var monitor = monitors.FirstOrDefault(m => m.Bounds.Contains(anchor));
        var scale = monitor.Scale > 0 ? monitor.Scale : 1.0;

        _pins?.Pin(image, new ScreenRect(anchor.X, anchor.Y, selection.Width, selection.Height), scale);
    }

    private void RegisterHotkeyOrWarn()
    {
        var gesture = _settings!.Current.GetHotkeyGesture();
        var result = _hotkey!.Register(gesture, out var win32Error);

        if (result == HotkeyRegisterResult.Success)
        {
            _tray!.SetCaptureHotkeyText(gesture.ToString());
            _hotkey.Pressed += OnHotkeyPressed;
            return;
        }

        _tray!.ShowBalloon(
            "热键不可用",
            $"注册 {gesture} 失败（{result.Describe()}，Win32 错误码 {win32Error}）。可编辑 {AppPaths.SettingsFile} 改键后重启。",
            warning: true);
    }

    private void OnHotkeyPressed(object? sender, EventArgs e)
    {
        if (_overlaySession is not null || _captureInProgress)
        {
            _log?.Info("已有截图会话进行中，忽略本次触发");
            return;
        }

        _ = StartCaptureSessionAsync();
    }

    /// <summary>
    /// 一次截图会话。抓屏放在线程池（40–150ms，不能卡 UI），
    /// 遮罩必须在 UI 线程创建，所以这里用 await 回到 UI 线程。
    /// </summary>
    private async Task StartCaptureSessionAsync()
    {
        // 防重入必须在第一个 await 之前同步占住：抓屏的 40–150ms 窗口期内再次触发热键，
        // OnHotkeyPressed 靠这个标志挡下。若放行，两个会话并发跑，先结束的那个还会在
        // RunCaptureSessionAsync 的 finally 里把后者的 _overlaySession 引用误清掉。
        _captureInProgress = true;

        try
        {
            await RunCaptureSessionAsync();
        }
        finally
        {
            _captureInProgress = false;
        }
    }

    private async Task RunCaptureSessionAsync()
    {
        var capturer = new GdiScreenCapturer();

        CapturedFrame frame;
        try
        {
            frame = await Task.Run(capturer.CaptureVirtualScreen);
        }
        catch (Exception ex)
        {
            _log?.Error("抓屏失败", ex);
            _tray?.ShowBalloon("抓屏失败", ex.Message, warning: true);
            return;
        }

        // 先转成 BitmapSource（WPF 会复制一份），随后原始像素缓冲就可以被回收，
        // 避免 66MB 的冻结帧和它的位图副本同时长期占着。
        var frameBitmap = FrameBitmap.ToBitmapSource(frame);
        var bounds = frame.Bounds;
        var scan = BlankFrameDetector.Scan(frame);
        _log?.Info($"冻结帧 {bounds}，非黑采样 {scan.NonBlackCount}/{scan.SampledCount}，颜色一致={scan.IsUniform}");

        var session = OverlaySession.Create(
            frameBitmap,
            bounds,
            new Win32MonitorService().GetMonitors(),
            _settings?.Current.Capture.DimOpacity ?? 0.45);

        _overlaySession = session;

        try
        {
            session.Show();
            var outcome = await session.Result;

            if (outcome is null)
            {
                _log?.Info("截图已取消");
                return;
            }

            var rect = outcome.Selection;
            var cropped = SelectionOutput.Export(frameBitmap, bounds, outcome);

            // 工具栏上的"贴图"是显式选择，优先级高于配置里的默认动作。
            var action = session.RequestedAction == CaptureAction.Pin
                ? AfterCaptureAction.Pin
                : _settings?.Current.Capture.AfterCapture ?? AfterCaptureAction.Copy;

            switch (action)
            {
                case AfterCaptureAction.Pin:
                    PinSelection(cropped, rect);
                    _log?.Info($"选区 {rect} 已贴图（{outcome.Annotations.Count} 条标注）");
                    _tray?.ShowBalloon("已贴到屏幕", "拖动移动 · 滚轮缩放 · Alt+滚轮调透明度 · 双击复位 · 右键菜单");
                    return;

                case AfterCaptureAction.Save:
                    var savedPathOnly = await ScreenshotSaver.SaveAsync(cropped, _settings!.Current, _log!);
                    _log?.Info($"选区 {rect} 已确认，仅保存：{savedPathOnly ?? "失败"}");
                    _tray?.ShowBalloon(
                        savedPathOnly is null ? "保存失败" : "已保存",
                        savedPathOnly is null ? "详情见日志" : Path.GetFileName(savedPathOnly),
                        warning: savedPathOnly is null);
                    return;

                case AfterCaptureAction.None:
                    _log?.Info($"选区 {rect} 已确认（设置为不执行动作）");
                    return;

                default:
                    break;
            }

            var copied = await SelectionOutput.CopyToClipboardAsync(cropped, _log);

            string? savedPath = null;
            if (_settings?.Current.Output.AutoSave == true)
            {
                savedPath = await ScreenshotSaver.SaveAsync(cropped, _settings.Current, _log!);
            }

            _log?.Info(
                $"选区 {rect}，标注 {outcome.Annotations.Count} 条 → {cropped.PixelWidth}×{cropped.PixelHeight}，"
                + $"剪贴板={(copied ? "已写入" : "写入失败")}"
                + (savedPath is null ? string.Empty : $"，已保存 {savedPath}"));

            var detail = $"{rect.Width} × {rect.Height} 像素";
            if (savedPath is not null)
            {
                detail += $"\n已保存：{Path.GetFileName(savedPath)}";
            }

            var title = copied
                ? savedPath is null ? "已复制到剪贴板" : "已复制并保存"
                : savedPath is null ? "复制失败" : "已保存（剪贴板写入失败）";

            _tray?.ShowBalloon(title, detail, warning: !copied);
        }
        catch (Exception ex)
        {
            _log?.Error("截图会话异常", ex);
            _tray?.ShowBalloon("截图失败", ex.Message, warning: true);
        }
        finally
        {
            session.Dispose();

            // 只在引用仍属于本会话时清空，防止误清并发会话的引用（正常情况下防重入已挡住并发）。
            if (ReferenceEquals(_overlaySession, session))
            {
                _overlaySession = null;
            }
        }
    }

    private bool AcquireSingleInstance()
    {
        _instanceMutex = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);
        _ownsMutex = isFirstInstance;

        if (isFirstInstance)
        {
            return true;
        }

        _instanceMutex.Dispose();
        _instanceMutex = null;

        System.Windows.MessageBox.Show(
            "WowCapture 已在运行，请查看任务栏通知区域。",
            "WowCapture",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);

        Shutdown(0);
        return false;
    }

    private void HookGlobalExceptionHandlers()
    {
        // 三层兜底：任何一层漏掉都会让用户看到"程序闪一下就没了"，且没有日志可查。
        DispatcherUnhandledException += (_, args) =>
        {
            // 走到这里说明有预料之外的 bug。可以恢复的异常应该在调用点被接住
            // （例如遮罩会话用 try/catch 关掉窗口后继续常驻）；这里一律按致命处理：
            // 带着坏状态常驻（热键还占着、托盘还在，但功能已经不可用）比干净退出更糟。
            _log?.Error("UI 线程未处理异常（致命，准备退出）", args.Exception);
            args.Handled = true;
            Dispatcher.BeginInvoke(new Action(() => Shutdown(1)));
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            _log?.Error("AppDomain 未处理异常", args.ExceptionObject as Exception);

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _log?.Error("未观察的任务异常", args.Exception);
            args.SetObserved();
        };
    }

    /// <summary>
    /// 延后到启动流程结束再关。
    /// 必须走 <see cref="System.Windows.Application.Shutdown(int)"/> 而不是
    /// <c>Dispatcher.BeginInvokeShutdown</c>——后者只停调度器、不触发 OnExit，
    /// 托盘图标就不会 Dispose（会残留在通知区域，直到鼠标划过）。
    /// </summary>
    private void ShutdownLater(int exitCode)
    {
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            new Action(() => Shutdown(exitCode)));
    }

    private static bool TryParseBenchmark(string[] args, out int iterations)
    {
        iterations = 20;

        for (var i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], "--bench-capture", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 < args.Length && int.TryParse(args[i + 1], out var parsed))
            {
                iterations = parsed;
            }

            return true;
        }

        return false;
    }

    private static bool TryParseStress(string[] args, out int iterations)
    {
        iterations = 30;

        for (var i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], "--stress", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 < args.Length && int.TryParse(args[i + 1], out var parsed))
            {
                iterations = parsed;
            }

            return true;
        }

        return false;
    }

    /// <summary>开机自启与配置保持同步：配置说开就写注册表，说关就删。</summary>
    private void SyncAutoStart()
    {
        if (_settings is null)
        {
            return;
        }

        try
        {
            var want = _settings.Current.Startup.RunAtLogin;
            if (AutoStartManager.IsEnabled() != want)
            {
                var applied = AutoStartManager.SetEnabled(want);
                _log?.Info(applied
                    ? $"开机自启已{(want ? "开启" : "关闭")}"
                    : $"开机自启{(want ? "开启" : "关闭")}失败（注册表不可写）");
            }
        }
        catch (Exception ex)
        {
            _log?.Error("同步开机自启失败", ex);
        }
    }

    private void UpdateClickThroughState() =>
        _tray?.SetClickThroughAvailable(_pins?.AnyClickThrough == true);

    private static void TryOpenDirectory(string directory)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(directory) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // 打不开就算了：数据目录里本来就可能有日志不可写的情况。
        }
    }

    /// <summary>首次运行弹一页说明。自检模式和开机自启时跳过，避免打扰。</summary>
    private void ShowFirstRunGuideIfNeeded()
    {
        if (_settings is null || _settings.Current.FirstRunCompleted || IsSelfTest || HasFlag("--autostart"))
        {
            return;
        }

        try
        {
            _firstRunWindow = new FirstRunWindow(_settings.Current.GetHotkeyGesture().ToString());
            _firstRunWindow.Closed += (_, _) => _firstRunWindow = null;
            _firstRunWindow.Show();

            _settings.Current.FirstRunCompleted = true;
            _settings.Save();
            _log?.Info("已显示首次运行引导");
        }
        catch (Exception ex)
        {
            _log?.Error("显示首次运行引导失败", ex);
        }
    }

    private void OpenOutputFolder()
    {
        try
        {
            var directory = ScreenshotSaver.ResolveDirectory(_settings!.Current);
            Directory.CreateDirectory(directory);
            _tray?.OpenPath(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.Error("打开截图目录失败", ex);
        }
    }

    private static bool HasFlag(string flag) =>
        Environment.GetCommandLineArgs().Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _firstRunWindow?.Close();
        _firstRunWindow = null;

        _overlaySession?.Dispose();
        _overlaySession = null;

        _pins?.Dispose();
        _pins = null;

        _hotkey?.Dispose();
        _hotkey = null;

        _tray?.Dispose();
        _tray = null;

        _log?.Info("退出");
        _log?.Dispose();
        _log = null;

        if (_instanceMutex is not null)
        {
            if (_ownsMutex)
            {
                try
                {
                    _instanceMutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // 不属于本线程时忽略：进程退出时内核会回收。
                }
            }

            _instanceMutex.Dispose();
            _instanceMutex = null;
        }

        base.OnExit(e);
    }
}
