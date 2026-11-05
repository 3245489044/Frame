using TConfig.GameMath;
using MathematicalAlgorithms.Culling;

namespace MathematicalAlgorithms.Spatial;

/// <summary>
/// 分块网格坐标/寻址唯一实现：世界坐标 → 分块 → 局部坐标的换算、分块键、Floor 语义。
/// <para>自 Engine VoxelMath 提取，去 <c>Matrix</c> 前缀；<b>键打包位宽（21 位/轴，offset 1&lt;&lt;20）、
/// Floor 语义、chunkSize 2 的幂校验逐字节保持原实现</b>（GPU 编辑上传与脏块比较依赖该协议）。</para>
/// </summary>
public static class ChunkSpace
{
    /// <summary>未指定时的默认分块边长（须为 2 的幂，与编辑存储的分块划分一致）。</summary>
    public const int DefaultChunkSize = 32;

    /// <summary>浮点世界坐标 → 体素索引（向负无穷取整，负坐标不向零截断）。</summary>
    public static int ToVoxel(float world) => (int)MathF.Floor(world);

    public static int ToVoxelX(in Vec3 p) => ToVoxel(p.X);
    public static int ToVoxelY(in Vec3 p) => ToVoxel(p.Y);
    public static int ToVoxelZ(in Vec3 p) => ToVoxel(p.Z);

    /// <summary>分块边长的位移位数；非 2 的幂 ⇒ 抛（不静默近似）。</summary>
    public static int ShiftOf(int chunkSize)
    {
        if (chunkSize <= 0 || (chunkSize & (chunkSize - 1)) != 0)
            throw new ArgumentException($"chunkSize 必须是 2 的幂，实际 {chunkSize}", nameof(chunkSize));
        return System.Numerics.BitOperations.Log2((uint)chunkSize);
    }

    /// <summary>体素所在分块索引（算术右移 = 向下取整除法，负坐标同样正确）。</summary>
    public static int ChunkIndex(int voxel, int chunkSize) => voxel >> ShiftOf(chunkSize);

    /// <summary>体素在分块内的局部索引 [0, chunkSize)。</summary>
    public static int LocalIndex(int voxel, int chunkSize) => voxel & (chunkSize - 1);

    /// <summary>分块起始体素坐标。</summary>
    public static int ChunkOrigin(int chunkIndex, int chunkSize) => chunkIndex * chunkSize;

    /// <summary>分块三维键（21 位/轴打包，负索引经偏移后落位）。</summary>
    public static long ChunkKey(int chunkX, int chunkY, int chunkZ)
    {
        const int Offset = 1 << 20;
        const long Mask = 0x1FFFFF;
        long ax = (long)(chunkX + Offset) & Mask;
        long ay = (long)(chunkY + Offset) & Mask;
        long az = (long)(chunkZ + Offset) & Mask;
        return ax | (ay << 21) | (az << 42);
    }

    /// <summary>键 → 分块索引（与 <see cref="ChunkKey"/> 逐位对应）。</summary>
    public static ChunkCoord ChunkOfKey(long key) => new(
        (int)((key & 0x1FFFFF) - (1 << 20)),
        (int)(((key >> 21) & 0x1FFFFF) - (1 << 20)),
        (int)(((key >> 42) & 0x1FFFFF) - (1 << 20)));
}

/// <summary>分块坐标（世界按 chunkSize 划分后的整数网格）。</summary>
public readonly struct ChunkCoord : IEquatable<ChunkCoord>
{
    public readonly int X, Y, Z;

    public ChunkCoord(int x, int y, int z) => (X, Y, Z) = (x, y, z);

    public long Key => ChunkSpace.ChunkKey(X, Y, Z);

    /// <summary>体素所属分块。</summary>
    public static ChunkCoord Of(int voxelX, int voxelY, int voxelZ, int chunkSize) => new(
        ChunkSpace.ChunkIndex(voxelX, chunkSize),
        ChunkSpace.ChunkIndex(voxelY, chunkSize),
        ChunkSpace.ChunkIndex(voxelZ, chunkSize));

    public static ChunkCoord Of(in Vec3 world, int chunkSize) => Of(
        ChunkSpace.ToVoxelX(world), ChunkSpace.ToVoxelY(world), ChunkSpace.ToVoxelZ(world), chunkSize);

    /// <summary>分块覆盖的世界包围盒（含端点）。</summary>
    public Aabb Bounds(int chunkSize)
    {
        var ox = ChunkSpace.ChunkOrigin(X, chunkSize);
        var oy = ChunkSpace.ChunkOrigin(Y, chunkSize);
        var oz = ChunkSpace.ChunkOrigin(Z, chunkSize);
        return new Aabb(ox, oy, oz, ox + chunkSize - 1, oy + chunkSize - 1, oz + chunkSize - 1);
    }

    public bool Equals(ChunkCoord other) => X == other.X && Y == other.Y && Z == other.Z;
    public override bool Equals(object? obj) => obj is ChunkCoord c && Equals(c);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    public static bool operator ==(ChunkCoord a, ChunkCoord b) => a.Equals(b);
    public static bool operator !=(ChunkCoord a, ChunkCoord b) => !a.Equals(b);
    public override string ToString() => $"chunk({X},{Y},{Z})";
}

/// <summary>轴对齐整数区域（含端点）——脏区、编辑范围、切片范围共用一种区域表示。</summary>
public readonly struct ChunkRegion
{
    public readonly int MinX, MinY, MinZ, MaxX, MaxY, MaxZ;

    public ChunkRegion(int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
    {
        MinX = System.Math.Min(minX, maxX); MinY = System.Math.Min(minY, maxY); MinZ = System.Math.Min(minZ, maxZ);
        MaxX = System.Math.Max(minX, maxX); MaxY = System.Math.Max(minY, maxY); MaxZ = System.Math.Max(minZ, maxZ);
    }

    public int Width => MaxX - MinX + 1;
    public int Height => MaxY - MinY + 1;
    public int Depth => MaxZ - MinZ + 1;
    public long VoxelCount => (long)Width * Height * Depth;

    public static ChunkRegion FromBounds(in Aabb aabb) =>
        new(aabb.MinX, aabb.MinY, aabb.MinZ, aabb.MaxX, aabb.MaxY, aabb.MaxZ);

    /// <summary>以某体素为中心、各轴半径 radius 的立方区域。</summary>
    public static ChunkRegion Cube(int x, int y, int z, int radius) =>
        new(x - radius, y - radius, z - radius, x + radius, y + radius, z + radius);

    /// <summary>世界球（按体素 AABB 保守包围）。</summary>
    public static ChunkRegion Sphere(in Vec3 center, float radius)
    {
        int r = System.Math.Max(1, (int)MathF.Ceiling(radius));
        return Cube(ChunkSpace.ToVoxelX(center), ChunkSpace.ToVoxelY(center), ChunkSpace.ToVoxelZ(center), r);
    }

    public bool Contains(int x, int y, int z) =>
        x >= MinX && x <= MaxX && y >= MinY && y <= MaxY && z >= MinZ && z <= MaxZ;

    public bool Intersects(in ChunkRegion other) =>
        MinX <= other.MaxX && MaxX >= other.MinX &&
        MinY <= other.MaxY && MaxY >= other.MinY &&
        MinZ <= other.MaxZ && MaxZ >= other.MinZ;

    public Aabb ToAabb() => new(MinX, MinY, MinZ, MaxX, MaxY, MaxZ);

    /// <summary>区域覆盖的全部分块（按 chunkSize 网格）。</summary>
    public IEnumerable<ChunkCoord> Chunks(int chunkSize)
    {
        int cx0 = ChunkSpace.ChunkIndex(MinX, chunkSize), cx1 = ChunkSpace.ChunkIndex(MaxX, chunkSize);
        int cy0 = ChunkSpace.ChunkIndex(MinY, chunkSize), cy1 = ChunkSpace.ChunkIndex(MaxY, chunkSize);
        int cz0 = ChunkSpace.ChunkIndex(MinZ, chunkSize), cz1 = ChunkSpace.ChunkIndex(MaxZ, chunkSize);

        for (int cx = cx0; cx <= cx1; cx++)
        for (int cy = cy0; cy <= cy1; cy++)
        for (int cz = cz0; cz <= cz1; cz++)
            yield return new ChunkCoord(cx, cy, cz);
    }
}

/// <summary>
/// 流式/可见性查询参数（内核统一口径，渲染与 ECS 共用一个范围）。
/// <para><b>可见口径</b>（<see cref="MaxVisibleChunks"/>）只决定"这一帧提交多少块"；
/// <b>驻留口径</b>（<see cref="MaxResidentChunks"/>）决定"内存里保留多少块"。两者不再共用一个数。</para>
/// </summary>
public readonly record struct ChunkStreamOptions(
    int HorizontalChunks,
    int VerticalChunks,
    int MaxVisibleChunks,
    float MaxDistance)
{
    /// <summary>驻留上限；≤ 0 ⇒ 与可见上限一致（离开视野即释放，不额外放大）。</summary>
    public int MaxResidentChunks { get; init; }

    /// <summary>实际驻留上限（可见上限为 int.MaxValue 时同样无上限）。</summary>
    public int ResidentChunks => MaxResidentChunks > 0
        ? MaxResidentChunks
        : (MaxVisibleChunks > 0 ? MaxVisibleChunks : int.MaxValue);

    /// <summary>
    /// 物理范围 → 区块个数：配置层用米/厘米描述覆盖距离，区块边长由内核 chunkSize 决定。
    /// <para>1 cm 世界下同一组「区块个数」常量会随 chunkSize 缩小覆盖 100 倍，因此流式参数必须以物理距离表达。</para>
    /// <para>上下限只为阻止异常的候选枚举（例如 256 m 半径 + 1 cm 区块），不代表裁剪语义。</para>
    /// </summary>
    public static ChunkStreamOptions FromPhysical(
        float horizontalCm, float verticalCm, int maxVisibleChunks, float maxDistanceCm,
        int chunkSize, int maxHorizontalChunks = 64, int maxVerticalChunks = 32,
        int maxResidentChunks = 0)
    {
        ChunkSpace.ShiftOf(chunkSize);

        int horizontal = CeilChunks(horizontalCm, chunkSize, 0, maxHorizontalChunks);
        int vertical = CeilChunks(verticalCm, chunkSize, 1, maxVerticalChunks);
        return new ChunkStreamOptions(horizontal, vertical, maxVisibleChunks, maxDistanceCm)
        {
            MaxResidentChunks = maxResidentChunks,
        };
    }

    private static int CeilChunks(float physicalCm, int chunkSize, int min, int max)
    {
        if (physicalCm <= 0f) return min;
        int count = (int)MathF.Ceiling(physicalCm / chunkSize);
        return Math.Clamp(count, min, max);
    }
}

/// <summary>一帧的分块可见性快照：视锥剔除后的可见分块（近→远）+ 保守渲染域包围盒。</summary>
/// <param name="VisibleBounds">与 <paramref name="VisibleChunks"/> 同序的 cm 包围盒；规划时已算出，消费侧直接复用。</param>
/// <param name="VisibleCenters">与 <paramref name="VisibleChunks"/> 同序的中心点（cm），避免消费侧重复求中。</param>
/// <param name="ViewFrustum">本帧规划所用视锥；绘制端直接复用，避免同一帧再按姿态构造一次。</param>
/// <param name="CandidateCoords">候选域全集（与 <paramref name="CandidateChunks"/> 同一次枚举派生）；缺省 null 表示旧快照未采集。</param>
/// <param name="AdmittedCoords">候选集中通过视锥 + 距离上限的分块（配额截断<b>之前</b>）；配额排除量 = 本集合 − 可见集合。</param>
public readonly record struct ChunkStreamSnapshot(
    IReadOnlyList<ChunkCoord> VisibleChunks,
    Aabb Bounds,
    int CandidateChunks,
    IReadOnlyList<Aabb>? VisibleBounds = null,
    IReadOnlyList<Vec3>? VisibleCenters = null,
    Frustum ViewFrustum = default,
    IReadOnlyList<ChunkCoord>? CandidateCoords = null,
    IReadOnlyList<ChunkCoord>? AdmittedCoords = null)
{
    /// <summary>几何通过（视锥 + 距离）的分块数；未采集时退化为可见数（不静默造假值）。</summary>
    public int AdmittedChunks => AdmittedCoords?.Count ?? VisibleChunks.Count;
}

/// <summary>脏分块（编辑改动的更新调度单元，版本号用于消费侧判断是否需要重建）。</summary>
public readonly record struct DirtyChunk(ChunkCoord Coord, long Version);
