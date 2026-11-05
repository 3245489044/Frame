namespace Modules.Compute.Formula;

/// <summary>
/// tile 结果缓存：键 = <c>(signature, tileX, tileY, tileZ)</c>，signature 由 <see cref="SignatureSet"/> 给出。
/// <para>语义：同一签名同一 tile 位置的结果可跨 chunk/跨编辑复用；签名变化（参数改动）⇒ 该签名条目失效。</para>
/// <para>并发：内部为 <see cref="ConcurrentDictionary{TKey,TValue}"/>，可被多个 worker 共享（容量是软上限）。</para>
/// <para>容量策略：超过上限整表清空（简单、可预期）；P5 再换成按 signature 分桶 + LRU。</para>
/// </summary>
public sealed class TileCache
{
    public readonly record struct Key(long Signature, int X, int Y, int Z);

    private readonly BoundedCache<Key, object?> _entries;

    public TileCache(int capacity = 4096)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "容量必须为正");

        // 淘汰语义单源：整表清空是显式策略（EvictionPolicy.ClearAll），禁本类自建写计数。
        _entries = new BoundedCache<Key, object?>(capacity, policy: EvictionPolicy.ClearAll);
    }

    public int Count => _entries.Count;

    public int Capacity => _entries.Capacity;

    public long Hits { get; private set; }

    public long Misses { get; private set; }

    public double HitRate
    {
        get
        {
            long total = Hits + Misses;
            return total == 0 ? 0.0 : (double)Hits / total;
        }
    }

    public bool TryGet(Key key, out object? value)
    {
        if (_entries.TryGet(key, out value))
        {
            Hits++;
            return true;
        }

        Misses++;
        return false;
    }

    public bool TryGet<T>(Key key, out T? value)
    {
        if (_entries.TryGet(key, out object? raw) && raw is T typed)
        {
            value = typed;
            Hits++;
            return true;
        }

        value = default;
        Misses++;
        return false;
    }

    public void Put(Key key, object? value)
    {
        // 写计数 + 整表清空由 BoundedCache(EvictionPolicy.ClearAll) 单点负责。
        _entries.Set(key, value);
    }

    /// <summary>参数/内容签名变化：只保留新签名的条目。</summary>
    public int Invalidate(long signature)
        => _entries.RemoveWhere(key => key.Signature != signature);

    public void Clear()
    {
        _entries.Clear();
        Hits = 0;
        Misses = 0;
    }
}
