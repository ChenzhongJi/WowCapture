using System.Windows.Media.Imaging;

namespace WowCapture.App.Windows;

/// <summary>
/// 从 WPF Resource 里加载应用图标给窗口标题栏用。
/// 加载失败（图标缺失、URI 解析异常）时返回 <c>null</c>——窗口用默认图标，
/// 这绝不该成为打开窗口的障碍。
/// </summary>
internal static class WindowIcons
{
    public static BitmapFrame? TryLoadAppIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/app.ico"));
            return resource is null ? null : BitmapFrame.Create(resource.Stream);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
