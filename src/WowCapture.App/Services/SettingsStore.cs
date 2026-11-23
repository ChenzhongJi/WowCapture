using System.IO;
using System.Text.Json;
using WowCapture.Core.Settings;

namespace WowCapture.App.Services;

/// <summary>
/// 配置读写。三条硬要求：
/// 原子替换（写坏不能留半个文件）、损坏可恢复（备份后回默认）、首次运行自动生成可手改的 json。
/// </summary>
internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string _filePath;
    private readonly FileLog _log;

    public SettingsStore(string filePath, FileLog log)
    {
        _filePath = filePath;
        _log = log;
        Current = Load(out var createdFromDefaults);
        WasCreatedFromDefaults = createdFromDefaults;
    }

    public AppSettings Current { get; private set; }

    /// <summary>本次启动时没有可用配置（首次运行或已损坏），调用方应尽快 Save 一份出来。</summary>
    public bool WasCreatedFromDefaults { get; }

    public string FilePath => _filePath;

    public bool Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temp = _filePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Current, Options));

            if (File.Exists(_filePath))
            {
                // File.Replace 是原子的：要么旧配置，要么新配置，不会出现"写了一半"。
                File.Replace(temp, _filePath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temp, _filePath);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error($"保存配置失败：{_filePath}", ex);
            return false;
        }
    }

    private AppSettings Load(out bool createdFromDefaults)
    {
        createdFromDefaults = true;

        try
        {
            if (!File.Exists(_filePath))
            {
                _log.Info($"未找到配置，使用默认值：{_filePath}");
                return AppSettings.CreateDefault();
            }

            var json = File.ReadAllText(_filePath);
            var parsed = JsonSerializer.Deserialize<AppSettings>(json, Options);
            if (parsed is null)
            {
                throw new JsonException("配置内容为空。");
            }

            createdFromDefaults = false;
            _log.Info($"已加载配置：{_filePath}");
            return parsed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // 手动改坏了 json 不应该让工具起不来：备份到 .bak，回默认值继续跑。
            var backup = $"{_filePath}.bak-{DateTime.Now:yyyyMMddHHmmss}";
            try
            {
                if (File.Exists(_filePath))
                {
                    File.Move(_filePath, backup, overwrite: true);
                }
            }
            catch (Exception moveEx) when (moveEx is IOException or UnauthorizedAccessException)
            {
                // 备份失败也不影响回退默认值。
            }

            _log.Warn($"配置不可用，已备份到 {backup} 并回退默认值：{ex.Message}");
            return AppSettings.CreateDefault();
        }
    }
}
