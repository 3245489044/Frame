namespace MathematicalAlgorithms.Spatial;

/// <summary>
/// 分块载荷缓存：按 21 位分块键保存 (chunkSize³ 载荷数组, 内容签名) 对；签名相等即返回同一数组实例。
/// <para>纯机械层：不认体素/节点/编辑语义，只负责"签名命中 → 复用、签名变化 → 重建"。
/// 载荷数组由缓存独占，调用方只读；仅在主线程读写，后台只能读取调用方另行产出的副本。</para>
/// </summary>
public sealed class ChunkPayloadCache<T>
{
    private sealed class Entry
    {
        public T[] Payload = Array.Empty<T>();
        public long Signature;
    }

    private readonly int _size;
    private readonly int _volume;
    private readonly Dictionary<long, Entry> _entries = new();

    public ChunkPayloadCache(int chunkSize)
    {
        ChunkSpace.ShiftOf(chunkSize);
        _size = chunkSize;
        _volume = chunkSize * chunkSize * chunkSize;
    }

    public int ChunkSize => _size;
    public int ResidentCount => _entries.Count;

    /// <summary>命中且签名一致 ⇒ 返回载荷；否则返回 null（不触发重建）。</summary>
    public T[]? TryGet(in ChunkCoord chunk, long signature)
    {
        if (_entries.TryGetValue(chunk.Key, out var entry) && entry.Signature == signature)
            return entry.Payload;
        return null;
    }

    public bool IsCurrent(in ChunkCoord chunk, long signature) => TryGet(chunk, signature) is not null;

    /// <summary>签名命中直接返回；否则对同一数组调用 <paramref name="fill"/> 重建并更新签名。</summary>
    public T[] GetOrBuild(
        in ChunkCoord chunk, long signature, Action<ChunkCoord, T[]> fill, out bool built)
    {
        ArgumentNullException.ThrowIfNull(fill);

        long key = chunk.Key;
        if (_entries.TryGetValue(key, out var entry) && entry.Signature == signature)
        {
            built = false;
            return entry.Payload;
        }

        if (entry is null)
        {
            entry = new Entry { Payload = new T[_volume] };
            _entries[key] = entry;
        }
        else if (entry.Payload.Length != _volume)
        {
            entry.Payload = new T[_volume];
        }

        fill(chunk, entry.Payload);
        entry.Signature = signature;
        built = true;
        return entry.Payload;
    }

    public void Invalidate(in ChunkCoord chunk) => _entries.Remove(chunk.Key);
    public void Remove(in ChunkCoord chunk) => _entries.Remove(chunk.Key);
    public void Clear() => _entries.Clear();
}

/// <summary>
/// 分块载荷游标：连续读取同一分块的多个世界格时，只在跨块时向 <see cref="ChunkPayloadCache{T}"/> 取一次载荷，
/// 之后按局部行主序索引直读，避免逐格字典查询。
/// <para>失效语义与缓存一致：游标不主动重建；调用方按需 <see cref="ChunkPayloadCache{T}.Remove"/>/<see cref="ChunkPayloadCache{T}.Clear"/>
/// 后，游标在下一次跨块时自然取到新数组。</para>
/// </summary>
public sealed class ChunkPayloadCursor<T>
{
    private readonly ChunkPayloadCache<T> _cache;
    private readonly int _size;
    private readonly int _shift;
    private readonly Func<ChunkCoord, long> _signatureOf;
    private readonly Action<ChunkCoord, T[]> _fill;

    private long _chunkKey = long.MinValue;
    private T[] _payload = Array.Empty<T>();

    public ChunkPayloadCursor(
        ChunkPayloadCache<T> cache,
        Func<ChunkCoord, long> signatureOf,
        Action<ChunkCoord, T[]> fill)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _signatureOf = signatureOf ?? throw new ArgumentNullException(nameof(signatureOf));
        _fill = fill ?? throw new ArgumentNullException(nameof(fill));
        _size = cache.ChunkSize;
        _shift = ChunkSpace.ShiftOf(_size);
    }

    /// <summary>读取世界格载荷（局部索引 = x | y&lt;&lt;shift | z&lt;&lt;2shift，行主序）。</summary>
    public T Read(int x, int y, int z)
    {
        var chunk = ChunkCoord.Of(x, y, z, _size);
        long key = chunk.Key;
        if (key != _chunkKey)
        {
            long signature = _signatureOf(chunk);
            _payload = _cache.TryGet(chunk, signature)
                ?? _cache.GetOrBuild(chunk, signature, _fill, out _);
            _chunkKey = key;
        }

        int local = ChunkSpace.LocalIndex(x, _size)
                  | (ChunkSpace.LocalIndex(y, _size) << _shift)
                  | (ChunkSpace.LocalIndex(z, _size) << (2 * _shift));
        return _payload[local];
    }
}
