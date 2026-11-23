using System.IO;
using System.Windows.Media.Imaging;
using WowCapture.App.Services;
using WowCapture.Core.Output;
using WowCapture.Core.Settings;

namespace WowCapture.App.Output;

/// <summary>把裁剪结果落盘：目录解析、命名模板、格式编码、同名自增。</summary>
internal static class ScreenshotSaver
{
    public static string? Save(BitmapSource bitmap, AppSettings settings, FileLog log)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            var directory = ResolveDirectory(settings);
            Directory.CreateDirectory(directory);

            var baseName = FileNameTemplate.Format(
                settings.Output.FileNameTemplate,
                DateTimeOffset.Now,
                bitmap.PixelWidth,
                bitmap.PixelHeight);

            var extension = NormalizeFormat(settings.Output.Format);
            var path = BuildUniquePath(directory, baseName, extension);

            BitmapEncoder encoder = extension == "jpg"
                ? new JpegBitmapEncoder { QualityLevel = 92 }
                : new PngBitmapEncoder();

            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using var stream = File.Create(path);
            encoder.Save(stream);

            log.Info($"已保存截图：{path}");
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            log.Error("保存截图失败", ex);
            return null;
        }
    }

    /// <summary>
    /// <see cref="Save"/> 的异步版，截图主流程用它：编码与落盘（4K PNG 可达数百毫秒）
    /// 放到线程池，避免在用户刚按完 Enter 的瞬间卡住整个 dispatcher。位图必须已冻结。
    /// </summary>
    public static Task<string?> SaveAsync(BitmapSource bitmap, AppSettings settings, FileLog log) =>
        Task.Run(() => Save(bitmap, settings, log));

    /// <summary>
    /// 解析输出目录。未配置时落在 <c>图片\WowCapture\yyyy-MM\</c>，
    /// 按年月分目录，时间久了也不至于把资源管理器塞满。
    /// </summary>
    public static string ResolveDirectory(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Output.Directory))
        {
            // 允许写 %USERPROFILE%\Pictures\Shots 这样的环境变量。
            return Environment.ExpandEnvironmentVariables(settings.Output.Directory);
        }

        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrWhiteSpace(pictures))
        {
            pictures = AppContext.BaseDirectory;
        }

        return Path.Combine(pictures, "WowCapture", DateTime.Now.ToString("yyyy-MM"));
    }

    private static string NormalizeFormat(string? format) =>
        string.Equals(format, "jpg", StringComparison.OrdinalIgnoreCase)
        || string.Equals(format, "jpeg", StringComparison.OrdinalIgnoreCase)
            ? "jpg"
            : "png";

    private static string BuildUniquePath(string directory, string baseName, string extension)
    {
        var path = Path.Combine(directory, $"{baseName}.{extension}");

        // 同一秒连截多张是常态，必须自增而不是覆盖。
        var index = 1;
        while (File.Exists(path))
        {
            path = Path.Combine(directory, $"{baseName}_{index}.{extension}");
            index++;
        }

        return path;
    }
}
