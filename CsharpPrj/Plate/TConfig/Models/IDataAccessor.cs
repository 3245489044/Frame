
namespace TConfig;

public enum StorageLayer
{
    Config,
    Session,
    Transient
}

public interface IDataAccessor
{
    T? GetConfig<T>(string key) where T : class;

    void SetConfig<T>(string key, T value) where T : class;

    T? GetSession<T>(string key) where T : class;

    void SetSession<T>(string key, T value) where T : class;

    T? GetTransient<T>(string key) where T : class;

    void SetTransient<T>(string key, T value) where T : class;

    void SetRange(IDictionary<string, object?> data, StorageLayer layer);

    IDictionary<string, object?> CollectAll(StorageLayer layer);
}
