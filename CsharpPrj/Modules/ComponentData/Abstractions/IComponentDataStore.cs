
#nullable enable

namespace Modules.ComponentData.Abstractions
{
    public interface IComponentDataStore : IDisposable
    {
        IObservable<ComponentDataChangedEventArgs> DataChanged { get; }

        IObservable<ComponentDataChangedEventArgs> ObserveKey(string key);

        IObservable<ComponentDataChangedEventArgs> ObserveType<T>();

        T? Get<T>(string key);

        bool TryGet<T>(string key, out T? value);

        void Set<T>(string key, T value);

        bool Remove(string key);

        void Clear();

        bool ContainsKey(string key);

        IEnumerable<string> Keys { get; }

        int Count { get; }

        IEnumerable<KeyValuePair<string, object?>> GetAllData();
    }
}
