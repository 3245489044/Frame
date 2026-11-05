namespace Modules.Compute.Adapters;

/// <summary>
/// 二维网格矩形（<b>闭区间</b>）：<c>MinX..MaxX</c>、<c>MinZ..MaxZ</c>。
/// <para>列偏移为 <c>(z-MinZ)*Width + (x-MinX)</c>（x 最快），与 <c>Modules.Compute.Layout.Index3</c> 的扁平契约一致。</para>
/// <para>用于把现有「列/区块」坐标（厘米或格）原样交给基底，不产生第二套坐标约定。</para>
/// </summary>
public readonly record struct GridRect(int MinX, int MinZ, int MaxX, int MaxZ)
{
    public int Width => MaxX - MinX + 1;

    public int Height => MaxZ - MinZ + 1;

    public int Count => Width <= 0 || Height <= 0 ? 0 : Width * Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>列偏移（x 最快）；调用方必须保证 (x,z) 在矩形内。</summary>
    public int Offset(int x, int z) => (z - MinZ) * Width + (x - MinX);

    public bool Contains(int x, int z) => x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;

    /// <summary>按格对齐到给定步长向外取整（保持闭区间）。</summary>
    public GridRect AlignOut(int step)
    {
        if (step <= 0)
            throw new ArgumentOutOfRangeException(nameof(step));

        int minX = FloorToMultiple(MinX, step);
        int minZ = FloorToMultiple(MinZ, step);
        int maxX = FloorToMultiple(MaxX, step) + step - 1;
        int maxZ = FloorToMultiple(MaxZ, step) + step - 1;
        return new GridRect(minX, minZ, maxX, maxZ);
    }

    /// <summary>与另一矩形求交；无交集返回空矩形。</summary>
    public GridRect Intersect(GridRect other)
    {
        int minX = Math.Max(MinX, other.MinX);
        int minZ = Math.Max(MinZ, other.MinZ);
        int maxX = Math.Min(MaxX, other.MaxX);
        int maxZ = Math.Min(MaxZ, other.MaxZ);
        return maxX < minX || maxZ < minZ ? new GridRect(minX, minZ, minX - 1, minZ - 1) : new GridRect(minX, minZ, maxX, maxZ);
    }

    public override string ToString() => $"GridRect[{MinX}..{MaxX}]x[{MinZ}..{MaxZ}]";

    private static int FloorToMultiple(int value, int step)
    {
        int q = value / step;
        if (value < 0 && value % step != 0)
            q--;
        return q * step;
    }
}
