using System.Collections.Concurrent;
using DataCenter.Abstractions.Cells;
using TConfig;
using TConfig.ContextFrame.Abstractions;

namespace Operation.StateBlock.Core.Compat;

/// <summary>
/// 兼容视图：把端口数据单元（<see cref="IDataCell"/>）暴露为既有引擎存储契约（<see cref="IStorageCell"/>），
/// 使 <c>IStateCenter.Cell</c> / 事务 / 宿主存储（如 ECS Storage）在端口后端上继续工作。
/// 语义对齐 Modules.InternalStorage.StorageCell：集合族/数组族为容器就地变更（不触发变更通知）；
/// 引擎特有的快照行为（GetSnapshotBehavior）在端口层无对应语义 → 恒为 null。
/// </summary>
internal sealed class DataCellStorageView : IStorageCell
{
    private readonly IDataCell _inner;
    private IStorageCell? _parentView;
    private StorageCellAdapter? _parentAdapter;

    internal DataCellStorageView(IDataCell inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _inner.DataChanged += OnInnerDataChanged;
    }

    internal IDataCell Inner => _inner;

    public string Name => _inner.Name;

    public IStorageCell? Parent
    {
        get => _inner.Parent switch
        {
            null => null,
            StorageCellAdapter adapter => adapter.Inner,
            var parent => _parentView ??= new DataCellStorageView(parent),
        };
        set
        {
            if (value is null)
            {
                _inner.Parent = null;
                _parentAdapter = null;
                return;
            }

            // 引擎存储作为父单元：经适配器接入端口（保持读回退链一致）
            _parentAdapter = new StorageCellAdapter(value);
            _inner.Parent = _parentAdapter;
        }
    }

    public event EventHandler<ContextDataChangedEventArgs>? DataChanged;

    // ── 键值（读写路径与端口一致） ──

    public void Set<T>(string key, T value, StorageLayer layer = StorageLayer.Session) => _inner.Set(key, value, layer.ToTier());

    public void SetObject(string key, object? value, StorageLayer layer) => _inner.SetObject(key, value, layer.ToTier());

    public void SetQuiet<T>(string key, T value, StorageLayer layer) => _inner.SetQuiet(key, value, layer.ToTier());

    public T? Get<T>(string key, StorageLayer? layer = null) => _inner.Get<T>(key, layer?.ToTier());

    public bool TryGet<T>(string key, out T? value, StorageLayer? layer = null) => _inner.TryGet(key, out value, layer?.ToTier());

    public bool Contains(string key, StorageLayer? layer = null) => _inner.Contains(key, layer?.ToTier());

    public bool Remove(string key, StorageLayer? layer = null) => _inner.Remove(key, layer?.ToTier());

    public void ClearLayer(StorageLayer layer) => _inner.ClearTier(layer.ToTier());

    public IEnumerable<string> GetKeys(StorageLayer layer) => _inner.GetKeys(layer.ToTier());

    public IEnumerable<KeyValuePair<string, object?>> GetEntries(StorageLayer layer) => _inner.GetEntries(layer.ToTier());

    /// <summary>端口层无「快照行为」语义（引擎特有）→ 恒 null。</summary>
    public int? GetSnapshotBehavior(string key, StorageLayer layer) => null;

    // ── 集合族（容器就地变更，镜像 StorageCell 语义：不触发变更通知） ──

    public void SetAdd<T>(string key, T item, StorageLayer layer) => GetOrCreateSet<T>(key, layer).TryAdd(item, 0);

    public bool SetRemove<T>(string key, T item, StorageLayer layer) => GetSet<T>(key, layer)?.TryRemove(item, out _) ?? false;

    public bool SetContains<T>(string key, T item, StorageLayer layer) => GetSet<T>(key, layer)?.ContainsKey(item) ?? false;

    public IEnumerable<T> SetGetAll<T>(string key, StorageLayer layer) => GetSet<T>(key, layer)?.Keys.ToList() ?? Enumerable.Empty<T>();

    public int SetCount(string key, StorageLayer layer)
        => _inner.Get<object>(key, layer.ToTier()) is System.Collections.ICollection collection ? collection.Count : 0;

    public T? SetFirst<T>(string key, StorageLayer layer)
        => GetSet<T>(key, layer) is { } set ? set.Keys.FirstOrDefault() : default;

    // ── 数组族（就地变更，镜像 StorageCell 语义） ──

    public void SetArray<T>(string key, T[] array, StorageLayer layer) => Set(key, array, layer);

    public T[]? GetArray<T>(string key, StorageLayer layer) => Get<T[]>(key, layer);

    public T? GetArrayAt<T>(string key, int index, StorageLayer layer)
        => Get<T[]>(key, layer) is { } array && index >= 0 && index < array.Length ? array[index] : default;

    public void SetArrayAt<T>(string key, int index, T value, StorageLayer layer)
    {
        if (Get<T[]>(key, layer) is { } array && index >= 0 && index < array.Length)
            array[index] = value;
    }

    public void SwapArrayAt<T>(string key, int index1, int index2, StorageLayer layer)
    {
        if (Get<T[]>(key, layer) is { } array &&
            index1 >= 0 && index1 < array.Length && index2 >= 0 && index2 < array.Length)
        {
            (array[index1], array[index2]) = (array[index2], array[index1]);
        }
    }

    public int GetArrayLength(string key, StorageLayer layer) => Get<Array>(key, layer)?.Length ?? 0;

    private ConcurrentDictionary<T, byte>? GetSet<T>(string key, StorageLayer layer)
        => _inner.Get<ConcurrentDictionary<T, byte>>(key, layer.ToTier());

    private ConcurrentDictionary<T, byte> GetOrCreateSet<T>(string key, StorageLayer layer)
    {
        var tier = layer.ToTier();
        if (_inner.Get<ConcurrentDictionary<T, byte>>(key, tier) is { } existing)
            return existing;

        var created = new ConcurrentDictionary<T, byte>();
        _inner.SetQuiet(key, created, tier);
        return created;
    }

    private void OnInnerDataChanged(object? sender, DataCellChangedEventArgs e)
        => DataChanged?.Invoke(this, new ContextDataChangedEventArgs(e.Key, e.OldValue, e.NewValue, Map(e.ChangeKind)));

    private static DataChangeType Map(DataChangeKind kind) => kind switch
    {
        DataChangeKind.Added => DataChangeType.Added,
        DataChangeKind.Removed => DataChangeType.Removed,
        DataChangeKind.Cleared => DataChangeType.Cleared,
        _ => DataChangeType.Updated,
    };
}
