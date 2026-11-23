using System.Globalization;
using System.IO;
using System.Text;

namespace WowCapture.App.Services;

/// <summary>
/// 极简文件日志：单文件追加、行级加锁、逐行刷新。
/// 刻意不引日志框架——常驻工具只需要"出事时能查到那几百行"。
/// </summary>
internal sealed class FileLog : IDisposable
{
    private readonly object _gate = new();
    private readonly StreamWriter? _writer;
    private bool _disposed;

    public FileLog(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true,
            };

            // 常驻工具一跑几个月，日志不能无限累积：启动时清掉 14 天前的旧日志。
            PurgeOldLogs(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // 日志写不了也不能让主流程崩：静默降级为无日志。
            _writer = null;
        }
    }

    public void Info(string message) => Write("INF", message);

    public void Warn(string message) => Write("WRN", message);

    public void Error(string message, Exception? exception = null) =>
        Write("ERR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    private void Write(string level, string message)
    {
        var writer = _writer;
        if (writer is null || _disposed)
        {
            return;
        }

        lock (_gate)
        {
            try
            {
                writer.WriteLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}");
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // 磁盘满 / 文件被占用：丢掉这一行，不影响程序。
            }
        }
    }

    /// <summary>
    /// 清理过期日志：只匹配同前缀的 <c>app-yyyyMMdd.log</c>，目录里的其它文件一律不碰。
    /// 日志子系统的任何失败都必须静默——它绝不能成为启动失败的原因。
    /// </summary>
    private static void PurgeOldLogs(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory))
            {
                return;
            }

            var prefix = Path.GetFileNameWithoutExtension(path) + "-";
            var cutoff = DateTime.UtcNow.AddDays(-14);

            foreach (var file in Directory.EnumerateFiles(directory, "*.log"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    || name.Length != prefix.Length + 8)
                {
                    continue;
                }

                if (!DateTime.TryParseExact(
                        name.AsSpan(prefix.Length),
                        "yyyyMMdd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var date)
                    || date >= cutoff)
                {
                    continue;
                }

                try
                {
                    File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // 单个文件删不掉就算了。
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 目录不可枚举等：跳过清理，不影响启动。
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _writer?.Dispose();
        }
    }
}
