#region USINGS
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TConfig.ContextFrame.Abstractions;
#endregion

namespace WorkFlowMethod.Orchestration
{
    public class DefaultBridgeContext : IContext
    {
        private readonly Dictionary<string, object> _store = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<Type, object> _services = new();
        private EventHandler<ContextDataChangedEventArgs>? _dataChanged;

        public string Id { get; } = $"OrchestrationBridgeContext-{Guid.NewGuid():N}";
        public IContext? Parent => null;
        public bool IsDisposed { get; private set; }
        public DateTime CreatedAt { get; } = DateTime.UtcNow;
        public CancellationToken CancellationToken => CancellationToken.None;

        public T? GetData<T>(string? key = null) where T : notnull =>
            _store.TryGetValue(key ?? typeof(T).Name, out var val) && val is T typed ? typed : default;

        public void SetData<T>(T value, string? key = null) where T : notnull
        {
            var actualKey = key ?? typeof(T).Name;
            var oldValue = _store.TryGetValue(actualKey, out var existing) ? existing : null;
            _store[actualKey] = value;
            OnDataChanged(actualKey, oldValue, value, DataChangeType.Updated);
        }

        public bool TryGetData<T>(out T? value, string? key = null) where T : notnull
        {
            if (_store.TryGetValue(key ?? typeof(T).Name, out var val) && val is T typed)
            {
                value = typed;
                return true;
            }
            value = default;
            return false;
        }

        public bool RemoveData<T>(string? key = null) where T : notnull
        {
            var actualKey = key ?? typeof(T).Name;
            if (_store.TryGetValue(actualKey, out var oldValue) && _store.Remove(actualKey))
            {
                OnDataChanged(actualKey, oldValue, null, DataChangeType.Removed);
                return true;
            }
            return false;
        }

        public bool ContainsData<T>(string? key = null) where T : notnull =>
            _store.ContainsKey(key ?? typeof(T).Name);

        public IEnumerable<T> GetAllData<T>() where T : notnull =>
            _store.Values.OfType<T>();

        public IEnumerable<object> GetAllData() =>
            _store.Values;

        public event EventHandler<ContextDataChangedEventArgs>? DataChanged
        {
            add => _dataChanged += value;
            remove => _dataChanged -= value;
        }

        private void OnDataChanged(string key, object? oldValue, object? newValue, DataChangeType changeType)
        {
            _dataChanged?.Invoke(this, new ContextDataChangedEventArgs(key, oldValue, newValue, changeType));
        }

        public T GetOrAddService<T>(Func<T> factory) where T : notnull
        {
            return (T)_services.GetOrAdd(typeof(T), _ => factory());
        }

        public bool TryGetService<T>(out T? service) where T : notnull
        {
            if (_services.TryGetValue(typeof(T), out var existing))
            {
                service = (T)existing;
                return true;
            }
            service = default;
            return false;
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            _store.Clear();
            _services.Clear();
        }
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}