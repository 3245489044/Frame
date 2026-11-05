
using System.Collections.Concurrent;
using TConfig.ContextFrame.Abstractions;
namespace Modules.InternalStorage.Core;

public class StorageCell : IStorageCell
{
    private readonly ConcurrentDictionary<string, StorageEntry> _configStore = new();
    private readonly ConcurrentDictionary<string, StorageEntry> _sessionStore = new();
    private readonly ConcurrentDictionary<string, StorageEntry> _transientStore = new();

    public string Name { get; }

    public IStorageCell? Parent { get; set; }

    public event EventHandler<ContextDataChangedEventArgs>? DataChanged;

    public StorageCell(string? name = null, IStorageCell? parent = null)
    {
        Name = name ?? $"StorageCell-{Guid.NewGuid():N}";
        Parent = parent;
    }


    public void Set<T>(string key, T value, StorageLayer layer = StorageLayer.Session)
    {
        var store = GetStore(layer);
        var exists = store.TryGetValue(key, out var oldEntry);

        var isValidOld = exists && oldEntry != null && !oldEntry.IsExpired;
        var changeType = isValidOld
            ? DataChangeType.Updated
            : DataChangeType.Added;

        var entry = new StorageEntry(value, layer);
        store[key] = entry;

        RaiseDataChanged(key, isValidOld ? oldEntry.Value : null, value, changeType);
    }

    public void SetObject(string key, object? value, StorageLayer layer)
    {
        Set<object?>(key, value, layer);
    }

    public void SetQuiet<T>(string key, T value, StorageLayer layer)
    {
        // 静默写：与 Set 同层持久化（读路径一致），仅省略 RaiseDataChanged 通知副作用（轮内瞬态隔离写）。
        var store = GetStore(layer);
        store[key] = new StorageEntry(value, layer);
    }

    public T? Get<T>(string key, StorageLayer? layer = null)
    {
        return TryGet(key, out T? value, layer) ? value : default;
    }

    public bool TryGet<T>(string key, out T? value, StorageLayer? layer = null)
    {
        if (layer.HasValue)
        {
            return TryGetFromStore(GetStore(layer.Value), key, out value);
        }

        if (TryGetFromStore(_configStore, key, out value)) return true;
        if (TryGetFromStore(_sessionStore, key, out value)) return true;
        if (TryGetFromStore(_transientStore, key, out value)) return true;

        if (Parent != null)
            return Parent.TryGet<T>(key, out value, null);

        value = default;
        return false;
    }

    public bool Remove(string key, StorageLayer? layer = null)
    {
        if (layer.HasValue)
        {
            var store = GetStore(layer.Value);
            if (store.TryRemove(key, out var oldEntry))
            {
                RaiseDataChanged(key, oldEntry.Value, null, DataChangeType.Removed);
                return true;
            }
            return false;
        }

        var removed = false;
        foreach (var store in GetAllStores())
        {
            if (store.TryRemove(key, out var oldEntry))
            {
                RaiseDataChanged(key, oldEntry.Value, null, DataChangeType.Removed);
                removed = true;
            }
        }
        return removed;
    }

    public bool Contains(string key, StorageLayer? layer = null)
    {
        if (layer.HasValue)
            return GetStore(layer.Value).ContainsKey(key);

        return _configStore.ContainsKey(key)
            || _sessionStore.ContainsKey(key)
            || _transientStore.ContainsKey(key);
    }


    public void ClearLayer(StorageLayer layer)
    {
        var store = GetStore(layer);

        var snapshot = store.ToArray();
        store.Clear();

        foreach (var kvp in snapshot)
        {
            RaiseDataChanged(kvp.Key, kvp.Value.Value, null, DataChangeType.Removed);
        }
    }

    public IEnumerable<string> GetKeys(StorageLayer layer)
    {
        return GetStore(layer).Keys;
    }

    public IEnumerable<KeyValuePair<string, object?>> GetEntries(StorageLayer layer)
    {
        return GetStore(layer).Select(kvp => new KeyValuePair<string, object?>(kvp.Key, kvp.Value.Value));
    }

    public int? GetSnapshotBehavior(string key, StorageLayer layer)
    {
        var store = GetStore(layer);
        if (store.TryGetValue(key, out var entry))
        {
            return (int)entry.SnapshotBehavior;
        }
        return null;
    }


    public void SetAdd<T>(string key, T item, StorageLayer layer)
    {
        var store = GetStore(layer);
        var entry = store.GetOrAdd(key, _ => new StorageEntry(new ConcurrentDictionary<T, byte>(), layer));
        if (entry.Value is ConcurrentDictionary<T, byte> set)
        {
            set.TryAdd(item, 0);
        }
    }

    public bool SetRemove<T>(string key, T item, StorageLayer layer)
    {
        var store = GetStore(layer);
        if (store.TryGetValue(key, out var entry) && entry.Value is ConcurrentDictionary<T, byte> set)
        {
            return set.TryRemove(item, out _);
        }
        return false;
    }

    public bool SetContains<T>(string key, T item, StorageLayer layer)
    {
        var store = GetStore(layer);
        if (store.TryGetValue(key, out var entry) && entry.Value is ConcurrentDictionary<T, byte> set)
        {
            return set.ContainsKey(item);
        }
        return false;
    }

    public IEnumerable<T> SetGetAll<T>(string key, StorageLayer layer)
    {
        var store = GetStore(layer);
        if (store.TryGetValue(key, out var entry) && entry.Value is ConcurrentDictionary<T, byte> set)
        {
            return set.Keys.ToList();
        }
        return Enumerable.Empty<T>();
    }

    public int SetCount(string key, StorageLayer layer)
    {
        var store = GetStore(layer);
        if (store.TryGetValue(key, out var entry) && entry.Value is System.Collections.ICollection collection)
        {
            return collection.Count;
        }
        return 0;
    }

    public T? SetFirst<T>(string key, StorageLayer layer)
    {
        var store = GetStore(layer);
        if (store.TryGetValue(key, out var entry) && entry.Value is ConcurrentDictionary<T, byte> set)
        {
            return set.Keys.FirstOrDefault();
        }
        return default;
    }


    public void SetArray<T>(string key, T[] array, StorageLayer layer)
    {
        Set(key, array, layer);
    }

    public T[]? GetArray<T>(string key, StorageLayer layer)
    {
        return Get<T[]>(key, layer);
    }

    public T? GetArrayAt<T>(string key, int index, StorageLayer layer)
    {
        var store = GetStore(layer);
        if (store.TryGetValue(key, out var entry) && entry.Value is T[] arr && index >= 0 && index < arr.Length)
        {
            return arr[index];
        }
        return default;
    }

    public void SetArrayAt<T>(string key, int index, T value, StorageLayer layer)
    {
        var store = GetStore(layer);
        if (store.TryGetValue(key, out var entry) && entry.Value is T[] arr && index >= 0 && index < arr.Length)
        {
            arr[index] = value;
        }
    }

    public void SwapArrayAt<T>(string key, int index1, int index2, StorageLayer layer)
    {
        var store = GetStore(layer);
        if (store.TryGetValue(key, out var entry) && entry.Value is T[] arr &&
            index1 >= 0 && index1 < arr.Length && index2 >= 0 && index2 < arr.Length)
        {
            (arr[index1], arr[index2]) = (arr[index2], arr[index1]);
        }
    }

    public int GetArrayLength(string key, StorageLayer layer)
    {
        var store = GetStore(layer);
        if (store.TryGetValue(key, out var entry) && entry.Value is Array arr)
        {
            return arr.Length;
        }
        return 0;
    }


    private ConcurrentDictionary<string, StorageEntry> GetStore(StorageLayer layer) => layer switch
    {
        StorageLayer.Config => _configStore,
        StorageLayer.Session => _sessionStore,
        StorageLayer.Transient => _transientStore,
        _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, $"不支持的存储层级: {layer}")
    };

    private IEnumerable<ConcurrentDictionary<string, StorageEntry>> GetAllStores()
    {
        yield return _configStore;
        yield return _sessionStore;
        yield return _transientStore;
    }

    private static bool TryGetFromStore<T>(
        ConcurrentDictionary<string, StorageEntry> store,
        string key,
        out T? value)
    {
        if (store.TryGetValue(key, out var entry) && !entry.IsExpired && entry.Value is T t)
        {
            value = t;
            return true;
        }
        value = default;
        return false;
    }

    protected virtual void RaiseDataChanged(string key, object? oldValue, object? newValue, DataChangeType changeType)
    {
        DataChanged?.Invoke(this, new ContextDataChangedEventArgs(key, oldValue, newValue, changeType));
    }
}
