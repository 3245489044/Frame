namespace TConfig.GameMath;

/// <summary>
/// 共享轴对齐包围盒基元。原定义位于 GameEditor.VirtualMatrix/Math/Geometry.cs，
/// 归位到 TConfig 基元层，消除跨层业务类型依赖。
/// </summary>
public readonly struct Aabb
{
    public readonly int MinX, MinY, MinZ;
    public readonly int MaxX, MaxY, MaxZ;

    public Aabb(int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
    {
        MinX = minX; MinY = minY; MinZ = minZ;
        MaxX = maxX; MaxY = maxY; MaxZ = maxZ;
    }

    public bool Contains(int x, int y, int z) =>
        x >= MinX && x <= MaxX && y >= MinY && y <= MaxY && z >= MinZ && z <= MaxZ;

    public bool Intersects(in Aabb other) =>
        MinX <= other.MaxX && MaxX >= other.MinX &&
        MinY <= other.MaxY && MaxY >= other.MinY &&
        MinZ <= other.MaxZ && MaxZ >= other.MinZ;
}
