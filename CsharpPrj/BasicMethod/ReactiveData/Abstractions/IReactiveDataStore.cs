
#nullable enable

namespace Modules.ReactiveData.Abstractions
{
    public interface IReactiveDataStore : IDisposable
    {
        IObservable<DataChangedEventArgs> DataChanged { get; }

        IObservable<DataChangedEventArgs> ObserveKey(string key);

        IObservable<DataChangedEventArgs> ObserveType<T>();

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
