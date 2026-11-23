using System.IO;
using Microsoft.Win32;

namespace WowCapture.App.Services;

/// <summary>
/// 开机自启：写 <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>。
/// </summary>
/// <remarks>
/// 用 HKCU 而不是计划任务或服务：不需要管理员权限，卸载时删掉值就算干净移除，
/// 也不会像计划任务那样在任务计划程序里留下残影。
/// </remarks>
internal static class AutoStartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string DefaultValueName = "WowCapture";

    public static bool IsEnabled(string valueName = DefaultValueName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(valueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>构造 Run 项里的命令行。带引号，避免路径空格被拆开。</summary>
    public static string BuildRunCommand(string executablePath) => $"\"{executablePath}\" --autostart";

    public static bool SetEnabled(bool enabled, string valueName = DefaultValueName, string? executablePath = null) =>
        SetEnabledDetailed(enabled, valueName, executablePath).Success;

    /// <summary>带失败原因的重载，供自检在受限环境里区分"实现错了"和"环境不让写"。</summary>
    public static (bool Success, string? Error) SetEnabledDetailed(
        bool enabled,
        string valueName,
        string? executablePath = null)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return (false, "无法打开 Run 注册表项");
            }

            if (enabled)
            {
                var path = executablePath ?? Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(path))
                {
                    return (false, "拿不到可执行文件路径");
                }

                key.SetValue(valueName, BuildRunCommand(path));
            }
            else
            {
                key.DeleteValue(valueName, throwOnMissingValue: false);
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
