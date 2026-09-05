
namespace Share.StateManagement
{
    public interface IStateStore
    {

        void Set(string key, string? value);

        string? Get(string key);

        bool Has(string key);

        void Clear();

        IReadOnlyCollection<string> GetAllKeys();


        string? GetOrAdd(string key, string? defaultValue);

        void Remove(string key);

        void RemoveByPrefix(string prefix);


        void SetRange(IEnumerable<KeyValuePair<string, string?>> values);

        Dictionary<string, string?> GetAll();


        int Count { get; }

        bool IsEmpty { get; }
    }
}
