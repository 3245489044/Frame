
using System;
using System.Collections.Concurrent;
using TConfig.ContextFrame.Abstractions;

namespace BasicMethod.ContextFrame.Core
{
    public class StateStoreBase : IStateStore
    {
        private readonly ConcurrentDictionary<string, object?> _store = new();

        public T? Get<T>(string key)
        {
            if (_store.TryGetValue(key, out var value) && value is T t)
                return t;
            return default;
        }

        public void Set<T>(string key, T value)
        {
            _store[key] = value;
        }

        public bool Remove<T>(string key)
        {
            return _store.TryRemove(key, out _);
        }

        public bool Contains<T>(string key)
        {
            return _store.ContainsKey(key);
        }

        public T? GetOrCreate<T>(string key, Func<T> factory)
        {
            return (T?)_store.GetOrAdd(key, _ => factory()!);
        }

        public void Clear()
        {
            _store.Clear();
        }
    }
}
