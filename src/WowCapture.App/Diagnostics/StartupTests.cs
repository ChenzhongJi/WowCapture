using WowCapture.App.Services;

namespace WowCapture.App.Diagnostics;

/// <summary>
/// 开机自启的断言。分两层：
/// 1) 命令行拼装：纯逻辑，任何环境都能验（路径含空格时必须被引号包住）；
/// 2) 注册表往返：写测试专用值名、读回、删除。受限环境（沙箱、只读配置）会写不进去，
///    这种情况标记为"跳过"并记录原因，而不是判失败——否则自检在很多机器上都会假红。
/// </summary>
internal static class StartupTests
{
    private const string TestValueName = "WowCapture-SelfTest";

    public static bool Run(FileLog log)
    {
        var command = AutoStartManager.BuildRunCommand(@"C:\Program Files\WowCapture\WowCapture.exe");
        var formatOk = command == @"""C:\Program Files\WowCapture\WowCapture.exe"" --autostart";
        log.Info($"自启命令行：{(formatOk ? "通过" : $"失败（{command}）")}");

        var registryOk = true;

        try
        {
            var (writeOk, error) = AutoStartManager.SetEnabledDetailed(true, TestValueName, @"C:\WowCapture\WowCapture.exe");

            if (!writeOk)
            {
                log.Info($"开机自启注册表：跳过（当前环境不允许写入 HKCU Run：{error}）");
            }
            else
            {
                var registered = AutoStartManager.IsEnabled(TestValueName);
                AutoStartManager.SetEnabled(enabled: false, TestValueName);
                var removed = !AutoStartManager.IsEnabled(TestValueName);

                registryOk = registered && removed;
                log.Info($"开机自启注册表：{(registryOk ? "通过" : "失败")}（写入={registered}，删除={removed}）");
            }
        }
        catch (Exception ex)
        {
            log.Error("开机自启测试失败", ex);
            registryOk = false;
        }
        finally
        {
            try
            {
                AutoStartManager.SetEnabled(enabled: false, TestValueName);
            }
            catch (Exception)
            {
                // 清理失败不影响结论。
            }
        }

        return formatOk && registryOk;
    }
}
