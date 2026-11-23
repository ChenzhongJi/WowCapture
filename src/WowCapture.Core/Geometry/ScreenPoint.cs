namespace WowCapture.Core.Geometry;

/// <summary>虚拟屏幕坐标系中的一个点，单位是物理像素。</summary>
public readonly record struct ScreenPoint(int X, int Y)
{
    public static ScreenPoint operator +(ScreenPoint a, ScreenPoint b) => new(a.X + b.X, a.Y + b.Y);

    public static ScreenPoint operator -(ScreenPoint a, ScreenPoint b) => new(a.X - b.X, a.Y - b.Y);

    public override string ToString() => $"({X}, {Y})";
}
