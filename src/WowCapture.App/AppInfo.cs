namespace WowCapture.App;

/// <summary>版本号来源唯一，托盘菜单、日志、关于框都读这里。</summary>
internal static class AppInfo
{
    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
