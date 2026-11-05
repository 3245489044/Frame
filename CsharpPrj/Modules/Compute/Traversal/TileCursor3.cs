using Modules.Compute.Layout;

namespace Modules.Compute.Traversal;

/// <summary>
/// 三维 tile 游标（struct、无委托、热循环可内联）。
/// <para>顺序固定：tile 外 z→y→x，tile 内由内核自行按 x 最快遍历 ⇒ 同一 extent 的 tile 顺序跨运行稳定。</para>
/// </summary>
public struct TileCursor3
{
    private readonly int _extentX;
    private readonly int _extentY;
    private readonly int _extentZ;
    private readonly int _tx;
    private readonly int _ty;
    private readonly int _tz;

    private int _ix;
    private int _iy;
    private int _iz;
    private Tile3 _current;
    private bool _done;

    public TileCursor3(Index3 extent, int tileSize)
        : this(extent.Nx, extent.Ny, extent.Nz, tileSize, tileSize, tileSize)
    {
    }

    public TileCursor3(Index3 extent, int tileX, int tileY, int tileZ)
        : this(extent.Nx, extent.Ny, extent.Nz, tileX, tileY, tileZ)
    {
    }

    public TileCursor3(int extentX, int extentY, int extentZ, int tileSize)
        : this(extentX, extentY, extentZ, tileSize, tileSize, tileSize)
    {
    }

    public TileCursor3(int extentX, int extentY, int extentZ, int tileX, int tileY, int tileZ)
    {
        if (extentX < 0 || extentY < 0 || extentZ < 0)
            throw new ArgumentOutOfRangeException(nameof(extentX), "extent 不能为负");
        if (tileX <= 0 || tileY <= 0 || tileZ <= 0)
            throw new ArgumentOutOfRangeException(nameof(tileX), "tile 边长必须为正");

        _extentX = extentX;
        _extentY = extentY;
        _extentZ = extentZ;
        _tx = tileX;
        _ty = tileY;
        _tz = tileZ;

        _ix = -1;
        _iy = 0;
        _iz = 0;
        _current = default;
        _done = false;
    }

    public Tile3 Current => _current;

    public bool MoveNext()
    {
        if (_done)
            return false;

        _ix++;
        if (_ix >= TileCount(_extentX, _tx))
        {
            _ix = 0;
            _iy++;
            if (_iy >= TileCount(_extentY, _ty))
            {
                _iy = 0;
                _iz++;
                if (_iz >= TileCount(_extentZ, _tz))
                {
                    _done = true;
                    return false;
                }
            }
        }

        int x = _ix * _tx;
        int y = _iy * _ty;
        int z = _iz * _tz;

        _current = new Tile3(
            x, y, z,
            Math.Min(_tx, _extentX - x),
            Math.Min(_ty, _extentY - y),
            Math.Min(_tz, _extentZ - z));
        return true;
    }

    private static int TileCount(int extent, int tile) => extent <= 0 ? 0 : (extent + tile - 1) / tile;
}
