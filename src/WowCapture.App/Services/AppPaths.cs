using System.IO;
using System.Security;

namespace WowCapture.App.Services;

/// <summary>
/// 所有落盘位置的唯一来源。
/// 便携模式（exe 同级存在 <c>portable.flag</c>）时，数据全部落在程序目录下的 <c>data\</c>。
/// </summary>
/// <remarks>
/// 目录按三级回退探测：
/// 1. 首选（<c>%LOCALAPPDATA%\WowCapture</c> 或便携目录）；
/// 2. <c>%TEMP%\WowCapture</c>；
/// 3. exe 同级的 <c>data\</c>。
/// 受控电脑、沙箱环境里前两级可能都不可写；回退到程序目录至少让配置和日志有落脚点，
/// 而不是"设置不生效、日志也找不到"。
/// </remarks>
internal static class AppPaths
{
    private const string PortableFlagName = "portable.flag";

    public static bool IsPortable =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, PortableFlagName));

    /// <summary>按配置规则算出的首选目录（尚未探测可写性）。</summary>
    public static string PreferredDataRoot { get; } = ResolvePreferredDataRoot();

    /// <summary>实际使用的数据根目录，由 <see cref="EnsureCreated"/> 探测后确定。</summary>
    public static string DataRoot { get; private set; } = PreferredDataRoot;

    /// <summary>是否探测到可写的数据目录。</summary>
    public static bool IsWritable { get; private set; }

    /// <summary>数据目录回退（说明首选目录不可写，通常意味着运行在受限环境）。</summary>
    public static bool IsFallback => !string.Equals(DataRoot, PreferredDataRoot, StringComparison.OrdinalIgnoreCase);

    /// <summary>每个候选目录的探测结果（含异常信息），用于启动日志诊断。</summary>
    public static IReadOnlyList<string> ProbeReport => _probeReport;

    private static readonly List<string> _probeReport = [];

    public static string SettingsFile => Path.Combine(DataRoot, "settings.json");

    public static string LogDirectory => Path.Combine(DataRoot, "logs");

    /// <summary>按启动日期命名的日志文件（运行跨天不切分，M0 够用）。</summary>
    public static string LogFile => Path.Combine(LogDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");

    public static void EnsureCreated()
    {
        _probeReport.Clear();

        foreach (var candidate in EnumerateCandidates())
        {
            var error = TryPrepare(candidate);
            if (error is null)
            {
                _probeReport.Add($"{candidate} => 可写");
                DataRoot = candidate;
                IsWritable = true;
                return;
            }

            _probeReport.Add($"{candidate} => 不可写：{error}");
        }

        // 三个目录都不可写：保持首选路径，程序仍能运行，只是配置/日志无法落盘。
        IsWritable = false;
    }

    private static IEnumerable<string> EnumerateCandidates()
    {
        yield return DataRoot;

        var temp = Path.Combine(Path.GetTempPath(), "WowCapture");
        if (!string.Equals(temp, DataRoot, StringComparison.OrdinalIgnoreCase))
        {
            yield return temp;
        }

        var besideExe = Path.Combine(AppContext.BaseDirectory, "data");
        if (!string.Equals(besideExe, DataRoot, StringComparison.OrdinalIgnoreCase))
        {
            yield return besideExe;
        }
    }

    /// <summary>尝试准备目录；成功返回 <c>null</c>，失败返回失败原因。</summary>
    private static string? TryPrepare(string root)
    {
        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "logs"));

            // 只 CreateDirectory 不够：目录可能"存在但不可写"，必须真的写一次。
            var probe = Path.Combine(root, ".write-probe");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return null;
        }
        catch (Exception ex)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    private static string ResolvePreferredDataRoot()
    {
        if (IsPortable)
        {
            return Path.Combine(AppContext.BaseDirectory, "data");
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrWhiteSpace(localAppData)
            ? Path.Combine(AppContext.BaseDirectory, "data")
            : Path.Combine(localAppData, "WowCapture");
    }
}
