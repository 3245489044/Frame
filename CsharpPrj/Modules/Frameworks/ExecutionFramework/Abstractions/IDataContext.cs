namespace ExecutionFramework.Abstractions;

public interface IDataContext
{
    T GetItem<T>(string key);

    void SetItem<T>(string key, T value);

    bool ContainsKey(string key);

    bool RemoveItem(string key);

    void Clear();

    IEnumerable<string> GetKeys();

    int Count { get; }

    void Merge(IDataContext other, bool overwrite = true);
}
