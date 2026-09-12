using System.Collections.Concurrent;
using DataCenter.Abstractions.Cells;

namespace Operation.StateBlock.Core;

/// <summary>
/// 内存分层数据单元 —— Core 的默认 tier 后端（零引擎设施依赖）。
/// 语义对齐 Modules.InternalStorage.StorageCell：三 tier 独立字典、父链读回退、Set 触发变更通知、
/// SetQuiet 静默持久化、ClearTier 逐键 Removed 通知（GetSnapshotBehavior/TTL 等引擎特有语义不在端口内）。
/// </summary>
public sealed class InMemoryDataCell : IDataCell
{
    private readonly ConcurrentDictionary<string, object?> _config = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, object?> _session = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, object?> _transient = new(StringComparer.Ordinal);

    public InMemoryDataCell(string? name = null, IDataCell? parent = null)
    {
        Name = string.IsNullOrWhiteSpace(name) ? $"InMemoryDataCell-{Guid.NewGuid():N}" : name;
        Parent = parent;
    }

    public string Name { get; }

    public IDataCell? Parent { get; set; }

    public event EventHandler<DataCellChangedEventArgs>? DataChanged;

    public void Set<T>(string key, T value, DataTier tier = DataTier.Session)
    {
        var store = GetStore(tier);

        // 原子读-改-写（F-44）：原「先 TryGetValue 再索引赋值」在并发写同键时会误判
        // ChangeKind（Added/Updated）并回传失真 OldValue；AddOrUpdate 的工厂在键锁内执行 ⇒ 判定与写入同源。
        object? oldValue = null;
        var kind = DataChangeKind.Added;
        store.AddOrUpdate(
            key,
            _ => { kind = DataChangeKind.Added; oldValue = null; return value; },
            (_, existing) => { kind = DataChangeKind.Updated; oldValue = existing; return value; });

        Raise(key, oldValue, value, kind, tier);
    }

    public void SetObject(string key, object? value, DataTier tier) => Set<object?>(key, value, tier);

    public void SetQuiet<T>(string key, T value, DataTier tier) => GetStore(tier)[key] = value;

    public T? Get<T>(string key, DataTier? tier = null) => TryGet<T>(key, out var value, tier) ? value : default;

    public bool TryGet<T>(string key, out T? value, DataTier? tier = null)
    {
        if (tier.HasValue)
            return TryGetFrom(GetStore(tier.Value), key, out value);

        if (TryGetFrom(_config, key, out value)) return true;
        if (TryGetFrom(_session, key, out value)) return true;
        if (TryGetFrom(_transient, key, out value)) return true;

        if (Parent != null)
            return Parent.TryGet<T>(key, out value, null);

        value = default;
        return false;
    }

    public bool Contains(string key, DataTier? tier = null)
    {
        if (tier.HasValue)
            return GetStore(tier.Value).ContainsKey(key);

        return _config.ContainsKey(key) || _session.ContainsKey(key) || _transient.ContainsKey(key);
    }

    public bool Remove(string key, DataTier? tier = null)
    {
        if (tier.HasValue)
        {
            var store = GetStore(tier.Value);
            if (store.TryRemove(key, out var oldValue))
            {
                Raise(key, oldValue, null, DataChangeKind.Removed, tier.Value);
                return true;
            }
            return false;
        }

        var removed = false;
        foreach (var (t, store) in Stores())
        {
            if (store.TryRemove(key, out var oldValue))
            {
                Raise(key, oldValue, null, DataChangeKind.Removed, t);
                removed = true;
            }
        }
        return removed;
    }

    public void ClearTier(DataTier tier)
    {
        var store = GetStore(tier);
        var snapshot = store.ToArray();
        store.Clear();
        foreach (var kvp in snapshot)
            Raise(kvp.Key, kvp.Value, null, DataChangeKind.Removed, tier);
    }

    public IEnumerable<string> GetKeys(DataTier tier) => GetStore(tier).Keys;

    public IEnumerable<KeyValuePair<string, object?>> GetEntries(DataTier tier)
        => GetStore(tier).Select(kvp => new KeyValuePair<string, object?>(kvp.Key, kvp.Value));

    private void Raise(string key, object? oldValue, object? newValue, DataChangeKind kind, DataTier tier)
        => DataChanged?.Invoke(this, new DataCellChangedEventArgs(key, oldValue, newValue, kind, tier));

    private ConcurrentDictionary<string, object?> GetStore(DataTier tier) => tier switch
    {
        DataTier.Config => _config,
        DataTier.Session => _session,
        DataTier.Transient => _transient,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, $"不支持的分层标识: {tier}"),
    };

    private IEnumerable<(DataTier Tier, ConcurrentDictionary<string, object?> Store)> Stores()
    {
        yield return (DataTier.Config, _config);
        yield return (DataTier.Session, _session);
        yield return (DataTier.Transient, _transient);
    }

    private static bool TryGetFrom<T>(ConcurrentDictionary<string, object?> store, string key, out T? value)
    {
        if (store.TryGetValue(key, out var raw) && raw is T typed)
        {
            value = typed;
            return true;
        }
        value = default;
        return false;
    }
}
