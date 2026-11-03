using MathematicalAlgorithms.Noise;

namespace MathematicalAlgorithms.Spatial;

/// <summary>
/// L1 高度瓦片缓存：按水平瓦片缓存「可能非空气」的 <see cref="HeightSpan"/>，按公式签名失效。
/// <ul>
///   <li>纯派生视图：不拥有世界内容，唯一真源仍是 <see cref="TerrainHeightModel"/>。</li>
///   <li>世界不变 ⇒ 命中；换 seed/地形参数 ⇒ 签名变化 ⇒ 整表失效（懒重建）。</li>
///   <li>瓦片粒度固定为模板常数，不做逐体素缓存（避免 <c>10¹⁴</c> 级条目）。</li>
/// </ul>
/// </summary>
public sealed class TerrainHeightTiles
{
    private sealed class Entry
    {
        public long Signature;
        public HeightSpan Span;
    }

    // 覆盖 16 m 半径的 chunk 对齐瓦片（101×101 ≈ 1.0 万）仍远小于上限；上限只防极端半径把内存撑爆。
    private const int MaxResidentTiles = 32768;

    private readonly TerrainHeightModel _model;
    private readonly int _tileSize;
    private readonly int _margin;
    private readonly Dictionary<long, Entry> _tiles = new();

    /// <summary>
    /// 瓦片边长与余量（cm）。<paramref name="tileSizeCm"/> ≤ 0 取模型米当量；<paramref name="marginCm"/> 传 0 表示
    /// 「与直算同值、不留余量」，传负数取模型米当量。
    /// <para>把 <paramref name="tileSizeCm"/> 设为 chunkSize 即「按 chunk 对齐」：一个 chunk footprint 恰好命中一个瓦片，
    /// 查询值与逐 chunk 直算完全一致，但只算一次。</para>
    /// </summary>
    public TerrainHeightTiles(TerrainHeightModel model, int tileSizeCm = 0, int marginCm = -1)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _tileSize = tileSizeCm > 0 ? tileSizeCm : model.UnitsPerMeter;
        _margin = marginCm >= 0 ? marginCm : model.UnitsPerMeter;
    }

    public int TileSize => _tileSize;
    public int ResidentTiles => _tiles.Count;
    public long Hits { get; private set; }
    public long Misses { get; private set; }

    /// <summary>footprint → 高度区间（跨多个瓦片时取并集）。</summary>
    public HeightSpan Query(int minX, int minZ, int maxX, int maxZ)
    {
        int x0 = Math.Min(minX, maxX), x1 = Math.Max(minX, maxX);
        int z0 = Math.Min(minZ, maxZ), z1 = Math.Max(minZ, maxZ);
        int tx0 = (int)MathF.Floor(x0 / (float)_tileSize);
        int tx1 = (int)MathF.Floor(x1 / (float)_tileSize);
        int tz0 = (int)MathF.Floor(z0 / (float)_tileSize);
        int tz1 = (int)MathF.Floor(z1 / (float)_tileSize);

        var span = HeightSpan.Empty;
        for (int tz = tz0; tz <= tz1; tz++)
        for (int tx = tx0; tx <= tx1; tx++)
            span = span.Union(Tile(tx, tz));
        return span;
    }

    private HeightSpan Tile(int tileX, int tileZ)
    {
        long key = ChunkSpace.ChunkKey(tileX, 0, tileZ);
        if (_tiles.TryGetValue(key, out var entry) && entry.Signature == _model.Signature)
        {
            Hits++;
            return entry.Span;
        }

        if (_tiles.Count > MaxResidentTiles)
            _tiles.Clear();

        int x0 = tileX * _tileSize;
        int z0 = tileZ * _tileSize;
        var span = _model.SurfaceSpan(x0, z0, x0 + _tileSize - 1, z0 + _tileSize - 1, _margin);

        Misses++;
        _tiles[key] = new Entry { Signature = _model.Signature, Span = span };
        return span;
    }

    public void Clear()
    {
        _tiles.Clear();
        Hits = 0;
        Misses = 0;
    }
}
