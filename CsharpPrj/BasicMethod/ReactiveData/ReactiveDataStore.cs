
#nullable enable

using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Modules.ReactiveData.Abstractions;

namespace Modules.ReactiveData
{
    public class ReactiveDataStore : IReactiveDataStore
    {
        private readonly ConcurrentDictionary<string, object?> _data = new(StringComparer.Ordinal);
        private readonly Subject<DataChangedEventArgs> _subject = new();
        private bool _disposed;

        public IObservable<DataChangedEventArgs> DataChanged => _subject.AsObservable();

        public IObservable<DataChangedEventArgs> ObserveKey(string key)
        {
            return DataChanged.Where(e => string.Equals(e.Key, key, StringComparison.Ordinal));
        }

        public IObservable<DataChangedEventArgs> ObserveType<T>()
        {
            return DataChanged.Where(e => e.NewValue is T || e.OldValue is T);
        }

        public T? Get<T>(string key)
        {
            if (_data.TryGetValue(key, out var value) && value is T typed)
                return typed;
            return default;
        }

        public bool TryGet<T>(string key, out T? value)
        {
            if (_data.TryGetValue(key, out var raw) && raw is T typed)
            {
                value = typed;
                return true;
            }
            value = default;
            return false;
        }

        public void Set<T>(string key, T value)
        {
            var exists = _data.TryGetValue(key, out var oldValue);
            _data[key] = value;

            var changeType = exists ? DataChangeType.Updated : DataChangeType.Added;
            Publish(key, oldValue, value, changeType);
        }

        public bool Remove(string key)
        {
            if (_data.TryRemove(key, out var oldValue))
            {
                Publish(key, oldValue, null, DataChangeType.Removed);
                return true;
            }
            return false;
        }

        public void Clear()
        {
            _data.Clear();
            Publish("*", null, null, DataChangeType.Cleared);
        }

        public bool ContainsKey(string key) => _data.ContainsKey(key);

        public IEnumerable<string> Keys => _data.Keys;

        public int Count => _data.Count;

        public IEnumerable<KeyValuePair<string, object?>> GetAllData() => _data;

        private void Publish(string key, object? oldValue, object? newValue, DataChangeType changeType)
        {
            if (!_disposed)
            {
                _subject.OnNext(new DataChangedEventArgs(key, oldValue, newValue, changeType));
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _subject.OnCompleted();
                _subject.Dispose();
                _data.Clear();
            }
        }
    }
}
