namespace WowCapture.Core.Geometry;

/// <summary>
/// 虚拟屏幕坐标系中的矩形，单位是物理像素；左上角可以为负（副屏在主屏左侧或上方时）。
/// </summary>
/// <remarks>
/// <para>
/// 约定 <see cref="Right"/> / <see cref="Bottom"/> 是开区间（不含该边界），因此
/// <c>Right - Left == Width</c> 恒成立。这条约定与 GDI 的 RECT 一致，
/// 跨屏拼接时不需要到处做 ±1 修正——这是自研截图工具最常见的偏移来源。
/// </para>
/// <para>
/// 本类型是纯值类型，不依赖 WPF / Win32，可直接用于单元测试。
/// </para>
/// </remarks>
public readonly record struct ScreenRect(int X, int Y, int Width, int Height)
{
    public static readonly ScreenRect Empty = new(0, 0, 0, 0);

    public int Left => X;

    public int Top => Y;

    public int Right => X + Width;

    public int Bottom => Y + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public long Area => IsEmpty ? 0 : (long)Width * Height;

    public ScreenPoint TopLeft => new(X, Y);

    public ScreenPoint BottomRight => new(Right, Bottom);

    public ScreenPoint Center => new(X + (Width / 2), Y + (Height / 2));

    /// <summary>由任意两个对角点构造，自动处理反向拖拽（从右下往左上拖）。</summary>
    public static ScreenRect FromLtrb(int left, int top, int right, int bottom) =>
        new(Math.Min(left, right), Math.Min(top, bottom), Math.Abs(right - left), Math.Abs(bottom - top));

    public static ScreenRect FromPoints(ScreenPoint a, ScreenPoint b) => FromLtrb(a.X, a.Y, b.X, b.Y);

    public bool Contains(ScreenPoint point) =>
        !IsEmpty && point.X >= X && point.Y >= Y && point.X < Right && point.Y < Bottom;

    public bool Contains(ScreenRect other) =>
        !other.IsEmpty && other.X >= X && other.Y >= Y && other.Right <= Right && other.Bottom <= Bottom;

    public bool IntersectsWith(ScreenRect other) => !Intersect(other).IsEmpty;

    /// <summary>交集；无交集时返回 <see cref="Empty"/>（不会返回负宽高）。</summary>
    public ScreenRect Intersect(ScreenRect other)
    {
        var left = Math.Max(X, other.X);
        var top = Math.Max(Y, other.Y);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);

        return right <= left || bottom <= top ? Empty : new ScreenRect(left, top, right - left, bottom - top);
    }

    /// <summary>并集；任一为空时返回另一个。</summary>
    public ScreenRect Union(ScreenRect other)
    {
        if (IsEmpty)
        {
            return other;
        }

        if (other.IsEmpty)
        {
            return this;
        }

        return FromLtrb(
            Math.Min(Left, other.Left),
            Math.Min(Top, other.Top),
            Math.Max(Right, other.Right),
            Math.Max(Bottom, other.Bottom));
    }

    public ScreenRect Inflate(int dx, int dy) => new(X - dx, Y - dy, Width + (dx * 2), Height + (dy * 2));

    public ScreenRect Offset(int dx, int dy) => new(X + dx, Y + dy, Width, Height);

    public override string ToString() => $"({X},{Y}) {Width}x{Height}";
}
