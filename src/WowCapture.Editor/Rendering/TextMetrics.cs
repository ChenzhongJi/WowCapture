using System.Globalization;
using System.Windows;
using System.Windows.Media;
using WowCapture.Core.Geometry;

namespace WowCapture.Editor.Rendering;

/// <summary>
/// 文字度量。预览、内联输入框、导出三处必须用<b>同一套字体与字号</b>，
/// 否则会出现"输入时框这么大、导出后字挤到外面"的错位。
/// </summary>
/// <remarks>
/// 字号单位是物理像素，<c>pixelsPerDip</c> 固定 1.0：标注空间本身就是 96 DPI 语义的像素空间，
/// 屏幕上由外层矩阵负责缩放，导出时则直接 1:1。这样两边算出来的排版尺寸完全一致。
/// </remarks>
public static class TextMetrics
{
    public const string FontFamilyName = "Segoe UI";

    public static Typeface Typeface { get; } = new(FontFamilyName);

    public static ScreenRect Measure(string text, double fontSize, ScreenPoint position)
    {
        var formatted = CreateFormattedText(text, fontSize, Brushes.Black);

        return new ScreenRect(
            position.X,
            position.Y,
            Math.Max(4, (int)Math.Ceiling(formatted.Width)),
            Math.Max(4, (int)Math.Ceiling(formatted.Height)));
    }

    public static FormattedText CreateFormattedText(string text, double fontSize, Brush brush) => new(
        text,
        CultureInfo.CurrentCulture,
        FlowDirection.LeftToRight,
        Typeface,
        fontSize,
        brush,
        pixelsPerDip: 1.0);
}
