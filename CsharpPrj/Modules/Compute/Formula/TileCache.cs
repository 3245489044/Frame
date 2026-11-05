using System.Collections.Concurrent;

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

    private readonly ConcurrentDictionary<Key, object?> _entries;
    private readonly int _capacity;

    public TileCache(int capacity = 4096)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "容量必须为正");

        _capacity = capacity;
        _entries = new ConcurrentDictionary<Key, object?>();
    }

    public int Count => _entries.Count;

    public int Capacity => _capacity;

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
        if (_entries.TryGetValue(key, out value))
        {
            Hits++;
            return true;
        }

        Misses++;
        return false;
    }

    public bool TryGet<T>(Key key, out T? value)
    {
        if (_entries.TryGetValue(key, out object? raw) && raw is T typed)
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
        if (_entries.Count >= _capacity)
            _entries.Clear();

        _entries[key] = value;
    }

    /// <summary>参数/内容签名变化：只保留新签名的条目。</summary>
    public int Invalidate(long signature)
    {
        int removed = 0;
        foreach (var key in _entries.Keys)
        {
            if (key.Signature != signature && _entries.TryRemove(key, out _))
                removed++;
        }

        return removed;
    }

    public void Clear()
    {
        _entries.Clear();
        Hits = 0;
        Misses = 0;
    }
}
