using DataCenter.Abstractions.Cells;
using TConfig;
using TConfig.ContextFrame.Abstractions;

namespace Operation.StateBlock.Core.Compat;

/// <summary>
/// 兼容适配器：把既有引擎存储单元（<see cref="IStorageCell"/>，Plate/Modules 层设施）接入端口 <see cref="IDataCell"/>。
/// 迁移期用途 = 让 Core 的兼容构造接受旧后端，而 Core 内部实现只依赖端口；G5 后由端口原生后端（InMemoryDataCell）取代。
/// 事件桥接：变更类型一一对应；Tier 经探测（Removed 时键已不存在 → 回退 Session，仅作诊断信息）。
/// </summary>
internal sealed class StorageCellAdapter : IDataCell
{
    private readonly IStorageCell _inner;
    private StorageCellAdapter? _parentAdapter;

    internal StorageCellAdapter(IStorageCell inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _inner.DataChanged += OnInnerDataChanged;
    }

    internal IStorageCell Inner => _inner;

    public string Name => _inner.Name;

    public IDataCell? Parent
    {
        get => _inner.Parent is null ? null : (_parentAdapter ??= new StorageCellAdapter(_inner.Parent));
        set => _inner.Parent = value switch
        {
            null => null,
            StorageCellAdapter adapter => adapter._inner,
            _ => throw new NotSupportedException("兼容适配器的父链仅支持同类适配单元（StorageCellAdapter）。"),
        };
    }

    public event EventHandler<DataCellChangedEventArgs>? DataChanged;

    public void Set<T>(string key, T value, DataTier tier = DataTier.Session) => _inner.Set(key, value, tier.ToLayer());

    public void SetObject(string key, object? value, DataTier tier) => _inner.SetObject(key, value, tier.ToLayer());

    public void SetQuiet<T>(string key, T value, DataTier tier) => _inner.SetQuiet(key, value, tier.ToLayer());

    public T? Get<T>(string key, DataTier? tier = null) => _inner.Get<T>(key, tier?.ToLayer());

    public bool TryGet<T>(string key, out T? value, DataTier? tier = null) => _inner.TryGet(key, out value, tier?.ToLayer());

    public bool Contains(string key, DataTier? tier = null) => _inner.Contains(key, tier?.ToLayer());

    public bool Remove(string key, DataTier? tier = null) => _inner.Remove(key, tier?.ToLayer());

    public void ClearTier(DataTier tier) => _inner.ClearLayer(tier.ToLayer());

    public IEnumerable<string> GetKeys(DataTier tier) => _inner.GetKeys(tier.ToLayer());

    public IEnumerable<KeyValuePair<string, object?>> GetEntries(DataTier tier) => _inner.GetEntries(tier.ToLayer());

    private void OnInnerDataChanged(object? sender, ContextDataChangedEventArgs e)
        => DataChanged?.Invoke(this, new DataCellChangedEventArgs(e.Key, e.OldValue, e.NewValue, Map(e.ChangeType), ProbeTier(e.Key)));

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
