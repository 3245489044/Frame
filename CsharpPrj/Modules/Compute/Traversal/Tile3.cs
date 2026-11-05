namespace Modules.Compute.Traversal;

/// <summary>
/// 轴对齐的 tile 区间（<see cref="X"/>/<see cref="Y"/>/<see cref="Z"/> 为起点，Size* 为长度）。
/// <para>tile 间无写冲突：每个 tile 只写自己的输出区间，这是「worker 数不改变输出」的前提。</para>
/// </summary>
public readonly record struct Tile3(int X, int Y, int Z, int SizeX, int SizeY, int SizeZ)
{
    public int EndX => X + SizeX;

    public int EndY => Y + SizeY;

    public int EndZ => Z + SizeZ;

    public int Volume => SizeX * SizeY * SizeZ;

    public bool IsEmpty => SizeX <= 0 || SizeY <= 0 || SizeZ <= 0;

    /// <summary>向外扩 halo（单位：格）。</summary>
    public Tile3 Expand(int halo, int extentX, int extentY, int extentZ) => new(
        Math.Max(0, X - halo),
        Math.Max(0, Y - halo),
        Math.Max(0, Z - halo),
        Math.Min(extentX, EndX + halo) - Math.Max(0, X - halo),
        Math.Min(extentY, EndY + halo) - Math.Max(0, Y - halo),
        Math.Min(extentZ, EndZ + halo) - Math.Max(0, Z - halo));

    public override string ToString() => $"Tile3({X},{Y},{Z} +{SizeX}x{SizeY}x{SizeZ})";
}
