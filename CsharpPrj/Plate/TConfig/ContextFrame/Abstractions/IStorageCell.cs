
using System.Diagnostics.CodeAnalysis;

namespace TConfig.ContextFrame.Abstractions;

public interface IStorageCell
{
    string Name { get; }

    IStorageCell? Parent { get; set; }


    void Set<T>(string key, T value, StorageLayer layer = StorageLayer.Session);

    T? Get<T>(string key, StorageLayer? layer = null);

    bool TryGet<T>(string key, [MaybeNullWhen(false)] out T? value, StorageLayer? layer = null);

    bool Remove(string key, StorageLayer? layer = null);

    bool Contains(string key, StorageLayer? layer = null);

    void SetObject(string key, object? value, StorageLayer layer);

    /// <summary>
    /// 静默写：持久化值但不触发 DataChanged（轮内瞬态 / 广播隔离语义的写入口）。
    /// 读路径与 Set 一致（同层可见可读），仅省略通知副作用——不 raise DataChanged、不进订阅转发。
    /// </summary>
    void SetQuiet<T>(string key, T value, StorageLayer layer);


    void ClearLayer(StorageLayer layer);

    IEnumerable<string> GetKeys(StorageLayer layer);

    IEnumerable<KeyValuePair<string, object?>> GetEntries(StorageLayer layer);

    int? GetSnapshotBehavior(string key, StorageLayer layer);


    void SetAdd<T>(string key, T item, StorageLayer layer);

    bool SetRemove<T>(string key, T item, StorageLayer layer);

    bool SetContains<T>(string key, T item, StorageLayer layer);

    IEnumerable<T> SetGetAll<T>(string key, StorageLayer layer);

    int SetCount(string key, StorageLayer layer);

    T? SetFirst<T>(string key, StorageLayer layer);


    void SetArray<T>(string key, T[] array, StorageLayer layer);

    T[]? GetArray<T>(string key, StorageLayer layer);

    T? GetArrayAt<T>(string key, int index, StorageLayer layer);

    void SetArrayAt<T>(string key, int index, T value, StorageLayer layer);

    void SwapArrayAt<T>(string key, int index1, int index2, StorageLayer layer);

    int GetArrayLength(string key, StorageLayer layer);


    event EventHandler<ContextDataChangedEventArgs>? DataChanged;
}
