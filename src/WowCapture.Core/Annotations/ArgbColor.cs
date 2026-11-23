namespace WowCapture.Core.Annotations;

/// <summary>
/// 颜色值。刻意不用 WPF 的 <c>Color</c> 或 GDI 的 <c>Color</c>：
/// 标注模型属于 Core，必须能在没有 UI 框架的环境里构造与断言。
/// </summary>
public readonly record struct ArgbColor(byte A, byte R, byte G, byte B)
{
    public static readonly ArgbColor Transparent = new(0, 0, 0, 0);

    public static readonly ArgbColor Black = new(255, 0, 0, 0);

    public static readonly ArgbColor White = new(255, 255, 255, 255);

    /// <summary>默认标注色：鲜红，在任何背景上都跳。</summary>
    public static readonly ArgbColor Red = new(255, 0xFF, 0x3B, 0x30);

    /// <summary>高亮黄，用于圈重点。</summary>
    public static readonly ArgbColor Yellow = new(255, 0xFF, 0xD6, 0x0A);

    /// <summary>标注蓝，适合在浅色文档上圈注。</summary>
    public static readonly ArgbColor Blue = new(255, 0x0A, 0x84, 0xFF);

    public static ArgbColor FromRgb(byte r, byte g, byte b) => new(255, r, g, b);
}
