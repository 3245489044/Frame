
#nullable enable

using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Modules.ComponentData.Abstractions;

namespace Modules.ComponentData.Core
{
    public class ComponentDataStore : IComponentDataStore
    {
        private readonly ConcurrentDictionary<string, object?> _data = new();
        private readonly Subject<ComponentDataChangedEventArgs> _subject = new();
        private readonly IObservable<ComponentDataChangedEventArgs> _dataChanged;
        private volatile bool _disposed;

        public ComponentDataStore()
        {
            _dataChanged = _subject.AsObservable();
        }

        public IObservable<ComponentDataChangedEventArgs> DataChanged => _dataChanged;

        public IObservable<ComponentDataChangedEventArgs> ObserveKey(string key)
        {
            return _dataChanged.Where(e => e.Key == key);
        }

        public IObservable<ComponentDataChangedEventArgs> ObserveType<T>()
        {
            return _dataChanged.Where(e => e.NewValue is T || e.OldValue is T);
        }

        private static T? ConvertValue<T>(object? raw)
        {
            if (raw is T typed)
                return typed;
            if (raw != null && !typeof(T).IsAssignableFrom(raw.GetType()))
            {
                try
                {
                    return (T)System.Convert.ChangeType(raw, typeof(T));
                }
                catch
                {
                }
            }
            return default;
        }

        public T? Get<T>(string key)
        {
            if (_data.TryGetValue(key, out var value))
                return ConvertValue<T>(value);
            return default;
        }

        public bool TryGet<T>(string key, out T? value)
        {
            if (_data.TryGetValue(key, out var raw))
            {
                value = ConvertValue<T>(raw);
                return value is not null || raw is null;
            }
            value = default;
            return false;
        }

        public void Set<T>(string key, T value)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var exists = _data.TryGetValue(key, out var oldValue);
            var changeType = exists ? ComponentDataChangeType.Updated : ComponentDataChangeType.Added;

            _data[key] = value;
            var args = new ComponentDataChangedEventArgs(key, oldValue, value, changeType);
            PublishEvent(args);
        }

        public bool Remove(string key)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_data.TryRemove(key, out var oldValue))
            {
                var args = new ComponentDataChangedEventArgs(key, oldValue, null, ComponentDataChangeType.Removed);
                PublishEvent(args);
                return true;
            }
            return false;
        }

        public void Clear()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _data.Clear();
            var args = new ComponentDataChangedEventArgs("*", null, null, ComponentDataChangeType.Cleared);
            PublishEvent(args);
        }

        public bool ContainsKey(string key) => _data.ContainsKey(key);

        public IEnumerable<string> Keys => _data.Keys;

        public int Count => _data.Count;

        public IEnumerable<KeyValuePair<string, object?>> GetAllData() => _data;

        private void PublishEvent(ComponentDataChangedEventArgs args)
        {
            try
            {
                _subject.OnNext(args);
            }
            catch (Exception ex) when (!_disposed)
            {
                System.Diagnostics.Debug.WriteLine($"[ComponentDataStore] PublishEvent error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _subject.OnCompleted();
                _subject.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ComponentDataStore] Dispose error: {ex.Message}");
            }

            _data.Clear();
        }
    }
}
