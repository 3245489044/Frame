#region USINGS
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using ExecutionFramework.Abstractions;
#endregion

namespace WorkFlowMethod.Execution.Session
{
    public class DataContext : IDataContext
    {
        private readonly ConcurrentDictionary<string, object> _store = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _sessionId;

        public int Count => _store.Count;

        public ICollection<string> Keys => _store.Keys;

        public DataContext(string sessionId)
        {
            _sessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        }

        public void Set(string key, object value)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("键不能为空", nameof(key));
            _store[key] = value;
        }

        public object Get(string key)
        {
            _store.TryGetValue(key, out var value);
            return value;
        }

        public T Get<T>(string key)
        {
            if (_store.TryGetValue(key, out var value) && value is T typedValue)
                return typedValue;
            return default;
        }

        public bool TryGet(string key, out object value)
        {
            return _store.TryGetValue(key, out value);
        }

        public bool TryGet<T>(string key, out T value)
        {
            if (_store.TryGetValue(key, out var raw) && raw is T typed)
            {
                value = typed;
                return true;
            }
            value = default;
            return false;
        }

        public bool ContainsKey(string key)
        {
            return _store.ContainsKey(key);
        }

        public bool Remove(string key)
        {
            return _store.TryRemove(key, out _);
        }

        public void Clear()
        {
            _store.Clear();
        }

        public void SetByPath(string path, object value)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("路径不能为空", nameof(path));
            _store[path] = value;
        }

        public Dictionary<string, object> CreateSnapshot()
        {
            return new Dictionary<string, object>(_store, StringComparer.OrdinalIgnoreCase);
        }

        public void RestoreFromSnapshot(Dictionary<string, object> snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            _store.Clear();
            foreach (var kvp in snapshot)
            {
                _store[kvp.Key] = kvp.Value;
            }
        }

        public void MergeFrom(DataContext other, bool overwrite = true)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            foreach (var kvp in other._store)
            {
                if (overwrite || !_store.ContainsKey(kvp.Key))
                {
                    _store[kvp.Key] = kvp.Value;
                }
            }
        }


        T IDataContext.GetItem<T>(string key)
        {
            if (_store.TryGetValue(key, out var value) && value is T typedValue)
                return typedValue;
            return default;
        }

        void IDataContext.SetItem<T>(string key, T value)
        {
            Set(key, value);
        }

        bool IDataContext.RemoveItem(string key)
        {
            return Remove(key);
        }

        IEnumerable<string> IDataContext.GetKeys()
        {
            return Keys.ToArray();
        }

        void IDataContext.Merge(IDataContext other, bool overwrite)
        {
            if (other is DataContext opOther)
            {
                MergeFrom(opOther, overwrite);
                return;
            }

            foreach (var key in other.GetKeys())
            {
                if (overwrite || !_store.ContainsKey(key))
                {
                    _store[key] = other.GetItem<object>(key);
                }
            }
        }

        public override string ToString()
        {
            return $"DataContext [Session={_sessionId}, Items={Count}]";
        }
    }
}
