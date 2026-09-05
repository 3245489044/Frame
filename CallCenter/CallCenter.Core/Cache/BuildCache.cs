namespace CallCenter.Core.Cache;

/// <summary>
/// 编译缓存（P4）：以工具 fingerprint 为 key 缓存已构建的可执行产物元数据。命中 → 直接执行（Runtime 轨），
/// 未命中 → 走 Build 轨后写入。仅缓存逻辑，产物形态（IL/bundle/模块分析）由各轨 Build 提供；本类线程安全。
/// </summary>
public sealed class BuildCache
{
    private readonly Dictionary<string, object?> _items = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>当前缓存条目数。</summary>
    public int Count
    {
        get { lock (_gate) return _items.Count; }
    }

    /// <summary>按 fingerprint 命中查询（未命中返回 false，不抛）。</summary>
    public bool TryGet(string fingerprint, out object? artifact)
    {
        lock (_gate) return _items.TryGetValue(fingerprint, out artifact);
    }

    /// <summary>写入产物（key=fingerprint）。覆盖同 key 旧产物。</summary>
    public void Put(string fingerprint, object? artifact)
    {
        lock (_gate) _items[fingerprint] = artifact;
    }

    /// <summary>移除指定条目，返回是否命中。</summary>
    public bool Remove(string fingerprint)
    {
        lock (_gate) return _items.Remove(fingerprint);
    }

    /// <summary>清空缓存。</summary>
    public void Clear()
    {
        lock (_gate) _items.Clear();
    }
}
