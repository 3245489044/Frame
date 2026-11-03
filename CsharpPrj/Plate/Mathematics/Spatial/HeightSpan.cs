namespace MathematicalAlgorithms.Spatial;

/// <summary>
/// 垂直区间（含端点，单位由调用方决定；体素世界为 cm）。
/// <para>只表示「某个水平 footprint 内可能存在实心/液体内容的高度范围」，是加速提示，不是世界状态。
/// 空区间用 <see cref="MinY"/> &gt; <see cref="MaxY"/> 表示。</para>
/// </summary>
public readonly record struct HeightSpan(int MinY, int MaxY)
{
    public static HeightSpan Empty => new(1, 0);

    public static HeightSpan Of(int y) => new(y, y);

    public bool IsEmpty => MaxY < MinY;

    public HeightSpan Expand(int amount) =>
        IsEmpty ? this : new HeightSpan(MinY - amount, MaxY + amount);

    public HeightSpan Union(in HeightSpan other) =>
        other.IsEmpty ? this : IsEmpty ? other : new HeightSpan(
            Math.Min(MinY, other.MinY), Math.Max(MaxY, other.MaxY));

    /// <summary>与 [minY,maxY] 是否相交（保守判定）。</summary>
    public bool Intersects(int minY, int maxY) => !IsEmpty && MinY <= maxY && MaxY >= minY;

    public bool Contains(int y) => !IsEmpty && y >= MinY && y <= MaxY;
}
