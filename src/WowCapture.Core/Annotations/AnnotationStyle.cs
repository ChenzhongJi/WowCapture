namespace WowCapture.Core.Annotations;

/// <summary>
/// 标注样式。宽度单位是<b>物理像素</b>，与标注坐标同一套单位，
/// 这样同一份标注在屏幕预览与导出时像素尺寸完全一致。
/// </summary>
/// <param name="Stroke">描边颜色。</param>
/// <param name="StrokeWidth">描边宽度（物理像素）。</param>
/// <param name="Fill">填充颜色，<c>null</c> 表示不填充。</param>
public readonly record struct AnnotationStyle(ArgbColor Stroke, double StrokeWidth, ArgbColor? Fill)
{
    public static AnnotationStyle Default => new(ArgbColor.Red, 4, null);

    public AnnotationStyle WithStroke(ArgbColor stroke) => this with { Stroke = stroke };

    public AnnotationStyle WithWidth(double width) => this with { StrokeWidth = width };
}
