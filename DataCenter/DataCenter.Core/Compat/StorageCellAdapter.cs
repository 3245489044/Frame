using System.Collections.Concurrent;
using DataCenter.Abstractions.Cells;
using TConfig;
using TConfig.ContextFrame.Abstractions;

namespace Operation.StateBlock.Core.Compat;

/// <summary>
/// 兼容适配器：把既有引擎存储单元（<see cref="IStorageCell"/>，Plate/Modules 层设施）接入端口 <see cref="IDataCell"/>。
/// 迁移期用途 = 让 Core 的兼容构造接受旧后端，而 Core 内部实现只依赖端口；G5 后由端口原生后端（InMemoryDataCell）取代。
/// 事件桥接：变更类型一一对应；Tier 经探测（Removed 时键已不存在 → 回退 Session，仅作诊断信息）。
/// <para>
/// 可见性：<c>public</c> —— 供持有引擎实现的上层（<c>DataCenter.Adapters</c>，F-35 方案 A）把引擎会话子单元接入端口；
/// Core 自身不依赖引擎实现程序集（G2）。
/// </para>
/// </summary>
public sealed class StorageCellAdapter : IDataCell
{
    private readonly IStorageCell _inner;
    private StorageCellAdapter? _parentAdapter;

    /// <summary>
    /// 键→层登记旁表（F-38）—— 删除路径上键已不存在，探测必然失真（原实现回退 Session）；
    /// 以写入时登记的层还原变更事件 <see cref="DataCellChangedEventArgs.Tier"/>，命中时探测次数为 0（F-31）。
    /// </summary>
    private readonly ConcurrentDictionary<string, DataTier> _tierByKey = new(StringComparer.Ordinal);

    public StorageCellAdapter(IStorageCell inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _inner.DataChanged += OnInnerDataChanged;
    }

    internal IStorageCell Inner => _inner;

    public string Name => _inner.Name;

    /// <summary>
    /// 父单元（读回退链）。父适配器经 CAS 原子发布（F-31：<c>??=</c> 非原子 → 并发下多重订阅/事件放大）；
    /// 适配器与 inner 同寿，不做退订（迁移期语义，G5 后整体退役）。
    /// </summary>
    public IDataCell? Parent
    {
        get
        {
            var parent = _inner.Parent;
            if (parent is null)
                return null;

            var existing = Volatile.Read(ref _parentAdapter);
            if (existing is not null && ReferenceEquals(existing.Inner, parent))
                return existing;

            var created = new StorageCellAdapter(parent);
            return Interlocked.CompareExchange(ref _parentAdapter, created, existing) ?? created;
        }
        set => _inner.Parent = value switch
        {
            null => null,
            StorageCellAdapter adapter => adapter._inner,
            _ => throw new NotSupportedException("兼容适配器的父链仅支持同类适配单元（StorageCellAdapter）。"),
        };
    }

    public event EventHandler<DataCellChangedEventArgs>? DataChanged;

    public void Set<T>(string key, T value, DataTier tier = DataTier.Session)
    {
        _tierByKey[key] = tier;   // 先登记：inner 的变更通知同步回调，回调需立即可见（F-38）
        _inner.Set(key, value, tier.ToLayer());
    }

    public void SetObject(string key, object? value, DataTier tier)
    {
        _tierByKey[key] = tier;
        _inner.SetObject(key, value, tier.ToLayer());
    }

    public void SetQuiet<T>(string key, T value, DataTier tier)
    {
        _tierByKey[key] = tier;
        _inner.SetQuiet(key, value, tier.ToLayer());
    }

    public T? Get<T>(string key, DataTier? tier = null) => _inner.Get<T>(key, tier?.ToLayer());

    public bool TryGet<T>(string key, out T? value, DataTier? tier = null) => _inner.TryGet(key, out value, tier?.ToLayer());

    public bool Contains(string key, DataTier? tier = null) => _inner.Contains(key, tier?.ToLayer());

    public bool Remove(string key, DataTier? tier = null)
    {
        var removed = _inner.Remove(key, tier?.ToLayer());   // 事件回调在删除过程中读取登记值
        _tierByKey.TryRemove(key, out _);
        return removed;
    }

    public void ClearTier(DataTier tier)
    {
        _inner.ClearLayer(tier.ToLayer());
        foreach (var kvp in _tierByKey.ToArray())
        {
            if (kvp.Value == tier)
                _tierByKey.TryRemove(kvp.Key, out _);
        }
    }

    public IEnumerable<string> GetKeys(DataTier tier) => _inner.GetKeys(tier.ToLayer());

    public IEnumerable<KeyValuePair<string, object?>> GetEntries(DataTier tier) => _inner.GetEntries(tier.ToLayer());

    private void OnInnerDataChanged(object? sender, ContextDataChangedEventArgs e)
    {
        var kind = Map(e.ChangeType);

        // F-38：删除/清空路径上键已不存在，探测必然失真（原实现回退 Session）→ 优先用写入时登记的层；
        // 未命中（外部直接写 inner 等）才回退探测（F-31：命中时探测次数为 0）
        var tier = _tierByKey.TryGetValue(e.Key, out var registered) ? registered : ProbeTier(e.Key);

        DataChanged?.Invoke(this, new DataCellChangedEventArgs(e.Key, e.OldValue, e.NewValue, kind, tier));
    }

    private static DataChangeKind Map(DataChangeType changeType) => changeType switch
    {
        DataChangeType.Added => DataChangeKind.Added,
        DataChangeType.Removed => DataChangeKind.Removed,
        DataChangeType.Cleared => DataChangeKind.Cleared,
        _ => DataChangeKind.Updated,
    };

    private DataTier ProbeTier(string key)
        => _inner.Contains(key, StorageLayer.Config) ? DataTier.Config
         : _inner.Contains(key, StorageLayer.Session) ? DataTier.Session
         : _inner.Contains(key, StorageLayer.Transient) ? DataTier.Transient
         : DataTier.Session;
}
