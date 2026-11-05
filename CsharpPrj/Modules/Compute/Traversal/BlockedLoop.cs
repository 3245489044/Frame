using Modules.Compute.Layout;

namespace Modules.Compute.Traversal;

/// <summary>无委托 tile 执行器（struct 实现 ⇒ 泛型实例化后可内联，热路径不付接口派发）。</summary>
public interface ITileRunner
{
    void Run(in Tile3 tile);
}

/// <summary>
/// 3D cache-block 遍历：tile 默认 8×8×8（byte 平面 512B、float 平面 2KB，可进 L1/L2）。
/// <para><see cref="ForEachTile{TileRunner}(Index3,int,ref TileRunner)"/> 是热路径入口；
/// <see cref="ForEachTile(Index3,int,Action{Tile3})"/> 只用于冷路径（调度/调试）。</para>
/// </summary>
public static class BlockedLoop
{
    public static void ForEachTile(Index3 extent, int tileSize, Action<Tile3> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var cursor = new TileCursor3(extent, tileSize);
        while (cursor.MoveNext())
            body(cursor.Current);
    }

    public static void ForEachTile(Index3 extent, int tileX, int tileY, int tileZ, Action<Tile3> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var cursor = new TileCursor3(extent, tileX, tileY, tileZ);
        while (cursor.MoveNext())
            body(cursor.Current);
    }

    /// <summary>热路径：struct runner 经泛型约束直接调用，不产生委托/虚调用。</summary>
    public static void ForEachTile<TRunner>(Index3 extent, int tileSize, ref TRunner runner)
        where TRunner : ITileRunner
    {
        var cursor = new TileCursor3(extent, tileSize);
        while (cursor.MoveNext())
        {
            Tile3 tile = cursor.Current;
            runner.Run(in tile);
        }
    }

    public static int TileCount(Index3 extent, int tileSize)
    {
        if (tileSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(tileSize), "tile 边长必须为正");

        return Count(extent.Nx, tileSize) * Count(extent.Ny, tileSize) * Count(extent.Nz, tileSize);
    }

    private static int Count(int extent, int tile) => extent <= 0 ? 0 : (extent + tile - 1) / tile;
}
