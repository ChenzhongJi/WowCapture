using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WowCapture.Core.Output;

/// <summary>
/// 截图文件名的模板求值。纯逻辑、无 IO，可以像普通函数一样测试。
/// </summary>
/// <remarks>
/// <para>
/// 设计原则：<b>看不懂的占位符原样保留</b>。用户在模板里手滑写了 <c>{date}</c>，
/// 结果是文件名里出现 <c>{date}</c>，而不是程序崩溃或悄悄变成空字符串——
/// 前者一眼能看出问题，后者要一段时间后才发现文件名全是乱的。
/// </para>
/// <para>
/// 非法字符统一换成下划线：Windows 文件名不允许 <c>&lt; &gt; : " / \ | ? *</c> 和控制字符，
/// 而 <c>{yyyy}-{MM}-{dd}_{HHmmss}</c> 这类模板本身是安全的，危险来自用户自定义部分。
/// </para>
/// </remarks>
public static partial class FileNameTemplate
{
    public const string Default = "{yyyy}-{MM}-{dd}_{HHmmss}";

    private const int MaxLength = 100;

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    [GeneratedRegex(@"\{(?<token>[A-Za-z]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    /// <summary>
    /// 按模板生成不含扩展名与目录的文件名。
    /// 未知占位符保持原样；模板为空时用 <see cref="Default"/>。
    /// </summary>
    public static string Format(string? template, DateTimeOffset time, int width, int height)
    {
        var raw = string.IsNullOrWhiteSpace(template) ? Default : template;

        var replaced = TokenPattern().Replace(raw, match =>
        {
            var token = match.Groups["token"].Value;
            return Resolve(token, time, width, height) is { } value ? value : match.Value;
        });

        var sanitized = Sanitize(replaced);
        return sanitized.Length == 0 ? "screenshot" : sanitized;
    }

    private static string? Resolve(string token, DateTimeOffset time, int width, int height) => token switch
    {
        "yyyy" => time.ToString("yyyy", CultureInfo.InvariantCulture),
        "yy" => time.ToString("yy", CultureInfo.InvariantCulture),
        "MM" => time.ToString("MM", CultureInfo.InvariantCulture),
        "dd" => time.ToString("dd", CultureInfo.InvariantCulture),
        "HH" => time.ToString("HH", CultureInfo.InvariantCulture),
        "HHmmss" => time.ToString("HHmmss", CultureInfo.InvariantCulture),
        "mm" => time.ToString("mm", CultureInfo.InvariantCulture),
        "ss" => time.ToString("ss", CultureInfo.InvariantCulture),
        "fff" => time.ToString("fff", CultureInfo.InvariantCulture),
        "width" => width.ToString(CultureInfo.InvariantCulture),
        "height" => height.ToString(CultureInfo.InvariantCulture),
        _ => null,
    };

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);

        foreach (var ch in value)
        {
            if (Array.IndexOf(invalid, ch) >= 0 || char.IsControl(ch))
            {
                builder.Append('_');
                continue;
            }

            builder.Append(ch);
        }

        // Windows 会静默丢弃结尾的空格和点，"abc. " 与 "abc" 会撞名。
        var result = builder.ToString().TrimEnd(' ', '.');

        if (result.Length > MaxLength)
        {
            result = result[..MaxLength].TrimEnd(' ', '.');
        }

        // CON、NUL 这类设备名不能作文件名。
        return ReservedNames.Contains(result) ? result + "_" : result;
    }
}
