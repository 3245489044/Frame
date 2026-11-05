using Modules.Compute.Layout;
using Modules.Compute.Traversal;

namespace Modules.Compute.Schedule;

/// <summary>
/// tile 编号 ⇄ 区间 的闭式映射 + 静态工作分割。
/// <para>顺序与 <see cref="Traversal.TileCursor3"/> 完全一致（z→y→x 的 tile 序、x 最快），
/// 保证「按编号分给 worker」与「按游标顺序执行」产出同一结果。</para>
/// </summary>
public static class WorkPartition
{
    public static int TileCount(Index3 extent, int tileSize) =>
        Count(extent.Nx, tileSize) * Count(extent.Ny, tileSize) * Count(extent.Nz, tileSize);

    public static Tile3 TileAt(Index3 extent, int tileSize, int ordinal)
    {
        if (tileSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(tileSize), "tile 边长必须为正");
        if (ordinal < 0)
            throw new ArgumentOutOfRangeException(nameof(ordinal));

        int tilesX = Count(extent.Nx, tileSize);
        int tilesY = Count(extent.Ny, tileSize);
        int tilesZ = Count(extent.Nz, tileSize);

        if (ordinal >= tilesX * tilesY * tilesZ)
            throw new ArgumentOutOfRangeException(nameof(ordinal), $"编号 {ordinal} 超出 tile 总数");

        int plane = tilesX * tilesY;
        int iz = ordinal / plane;
        int rem = ordinal % plane;
        int iy = rem / tilesX;
        int ix = rem % tilesX;

        int x = ix * tileSize;
        int y = iy * tileSize;
        int z = iz * tileSize;

        return new Traversal.Tile3(
            x, y, z,
            Math.Min(tileSize, extent.Nx - x),
            Math.Min(tileSize, extent.Ny - y),
            Math.Min(tileSize, extent.Nz - z));
    }

    /// <summary>把 <paramref name="total"/> 个 tile 均分给 <paramref name="partitions"/> 份（余数给前几份）。</summary>
    public static void Split(int total, int partitions, int partition, out int start, out int count)
    {
        if (total < 0)
            throw new ArgumentOutOfRangeException(nameof(total));
        if (partitions <= 0)
            throw new ArgumentOutOfRangeException(nameof(partitions), "份数必须为正");
        if ((uint)partition >= (uint)partitions)
            throw new ArgumentOutOfRangeException(nameof(partition));

        int baseSize = total / partitions;
        int remainder = total % partitions;

        start = partition * baseSize + Math.Min(partition, remainder);
        count = baseSize + (partition < remainder ? 1 : 0);
    }

    private static int Count(int extent, int tile) => extent <= 0 ? 0 : (extent + tile - 1) / tile;
}
