
using System.Collections.Concurrent;

namespace Share.FrameworkAbstractions.Configuration
{
    public interface IServiceContext : IDisposable
    {
        string Id { get; }

        IServiceContext? Parent { get; }

        bool IsDisposed { get; }

        DateTime CreatedAt { get; }

        CancellationToken CancellationToken { get; }

        T? GetData<T>(string? key = null) where T : class;

        void SetData<T>(T value, string? key = null) where T : class;

        bool TryGetData<T>(out T? value, string? key = null) where T : class;

        bool RemoveData<T>(string? key = null) where T : class;

        bool ContainsData<T>(string? key = null) where T : class;

        IEnumerable<T> GetAllData<T>() where T : class;

        IEnumerable<object> GetAllData();

        T GetOrAddService<T>(Func<T> factory) where T : class;

        bool TryGetService<T>(out T? service) where T : class;

        event EventHandler<ContextDataChangedEventArgs>? DataChanged;

        IServiceContext CreateChild(string? id = null);
    }

    public class ContextDataChangedEventArgs : EventArgs
    {
        public string Key { get; }

        public object? OldValue { get; }

        public object? NewValue { get; }

        public ContextDataChangeType ChangeType { get; }

        public ContextDataChangedEventArgs(string key, object? oldValue, object? newValue, ContextDataChangeType changeType)
        {
            Key = key;
            OldValue = oldValue;
            NewValue = newValue;
            ChangeType = changeType;
        }
    }

    public enum ContextDataChangeType
    {
        Added,
        Updated,
        Removed
    }

    public class ServiceContext : IServiceContext
    {
        private readonly ConcurrentDictionary<string, object?> _store = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<Type, object> _services = new();
        private readonly CancellationTokenSource? _cts;
        private bool _disposed;

        public string Id { get; }

        public IServiceContext? Parent { get; }

        public bool IsDisposed => _disposed;

        public DateTime CreatedAt { get; } = DateTime.UtcNow;

        public CancellationToken CancellationToken => _cts?.Token ?? CancellationToken.None;

        public event EventHandler<ContextDataChangedEventArgs>? DataChanged;

        public ServiceContext(string? id = null, IServiceContext? parent = null, CancellationToken? cancellationToken = null)
        {
            Id = id ?? $"ctx-{Guid.NewGuid():N}";
            Parent = parent;

            if (cancellationToken.HasValue && cancellationToken.Value.CanBeCanceled)
            {
                _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken.Value);
            }
        }

        public T? GetData<T>(string? key = null) where T : class
        {
            var dataKey = key ?? typeof(T).Name;
            return _store.TryGetValue(dataKey, out var val) && val is T typed ? typed : null;
        }

        public void SetData<T>(T value, string? key = null) where T : class
        {
            PortGuard.Required(value, nameof(value));

            var dataKey = key ?? typeof(T).Name;
            var oldValue = _store.TryGetValue(dataKey, out var existing) ? existing : null;
            var changeType = oldValue == null ? ContextDataChangeType.Added : ContextDataChangeType.Updated;

            _store[dataKey] = value;

            OnDataChanged(new ContextDataChangedEventArgs(dataKey, oldValue, value, changeType));
        }

        public bool TryGetData<T>(out T? value, string? key = null) where T : class
        {
            var dataKey = key ?? typeof(T).Name;
            if (_store.TryGetValue(dataKey, out var val) && val is T typed)
            {
                value = typed;
                return true;
            }
            value = null;
            return false;
        }

        public bool RemoveData<T>(string? key = null) where T : class
        {
            var dataKey = key ?? typeof(T).Name;
            if (_store.TryRemove(dataKey, out var oldValue))
            {
                OnDataChanged(new ContextDataChangedEventArgs(dataKey, oldValue, null, ContextDataChangeType.Removed));
                return true;
            }
            return false;
        }

        public bool ContainsData<T>(string? key = null) where T : class
        {
            return _store.ContainsKey(key ?? typeof(T).Name);
        }

        public IEnumerable<T> GetAllData<T>() where T : class
        {
            return _store.Values.OfType<T>();
        }

        public IEnumerable<object> GetAllData()
        {
            return _store.Values.Where(v => v != null)!;
        }

        public T GetOrAddService<T>(Func<T> factory) where T : class
        {
            PortGuard.Required(factory, nameof(factory));
            return (T)_services.GetOrAdd(typeof(T), _ => factory()!);
        }

        public bool TryGetService<T>(out T? service) where T : class
        {
            if (_services.TryGetValue(typeof(T), out var obj) && obj is T typed)
            {
                service = typed;
                return true;
            }
            service = null;
            return false;
        }

        public IServiceContext CreateChild(string? id = null)
        {
            return new ServiceContext(id ?? $"{Id}/{Guid.NewGuid():N}", this, CancellationToken);
        }

        protected virtual void OnDataChanged(ContextDataChangedEventArgs e)
        {
            DataChanged?.Invoke(this, e);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cts?.Cancel();
            _cts?.Dispose();
            _store.Clear();
            _services.Clear();
        }
    }
}
