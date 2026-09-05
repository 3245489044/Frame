
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TConfig.ContextFrame.Abstractions;

namespace BasicMethod.ContextFrame.Core
{
    public class ContextBase : TConfig.ContextFrame.Abstractions.IContext
    {

        private readonly ConcurrentDictionary<string, object?> _data = new();
        private readonly ConcurrentDictionary<Type, object> _services = new();
        private readonly CancellationTokenSource _cts = new();
        private bool _disposed;


        public string Id { get; }

        public IContext? Parent { get; }

        public bool IsDisposed => Volatile.Read(ref _disposed);

        public DateTime CreatedAt { get; }

        public CancellationToken CancellationToken => _cts.Token;


        public event EventHandler<ContextDataChangedEventArgs>? DataChanged;


        public ContextBase(string? id = null, IContext? parent = null)
        {
            Id = id ?? Guid.NewGuid().ToString("N");
            Parent = parent;
            CreatedAt = DateTime.UtcNow;
        }


        public void SetData<T>(T value, string? key = null) where T : notnull
        {
            ThrowIfDisposed();
            var resolvedKey = ResolveKey<T>(key);

            var exists = _data.TryGetValue(resolvedKey, out var oldValue);
            _data[resolvedKey] = value;

            var changeType = exists ? DataChangeType.Updated : DataChangeType.Added;
            RaiseDataChanged(resolvedKey, oldValue, value, changeType);
        }

        public T? GetData<T>(string? key = null) where T : notnull
        {
            ThrowIfDisposed();
            var resolvedKey = ResolveKey<T>(key);

            if (_data.TryGetValue(resolvedKey, out var value) && value is T t)
                return t;

            if (Parent is IContext parent)
                return parent.GetData<T>(key);

            return default;
        }

        public bool TryGetData<T>([MaybeNullWhen(false)] out T? value, string? key = null) where T : notnull
        {
            ThrowIfDisposed();
            var resolvedKey = ResolveKey<T>(key);

            if (_data.TryGetValue(resolvedKey, out var raw) && raw is T t)
            {
                value = t;
                return true;
            }

            if (Parent is IContext parent)
                return parent.TryGetData(out value, key);

            value = default;
            return false;
        }

        public bool RemoveData<T>(string? key = null) where T : notnull
        {
            ThrowIfDisposed();
            var resolvedKey = ResolveKey<T>(key);

            if (_data.TryRemove(resolvedKey, out var oldValue))
            {
                RaiseDataChanged(resolvedKey, oldValue, null, DataChangeType.Removed);
                return true;
            }
            return false;
        }

        public bool ContainsData<T>(string? key = null) where T : notnull
        {
            ThrowIfDisposed();
            var resolvedKey = ResolveKey<T>(key);

            if (_data.ContainsKey(resolvedKey))
                return true;

            return Parent?.ContainsData<T>(key) ?? false;
        }

        public IEnumerable<T> GetAllData<T>() where T : notnull
        {
            ThrowIfDisposed();
            return _data.Values.OfType<T>().ToList();
        }

        public IEnumerable<object> GetAllData()
        {
            ThrowIfDisposed();
            return _data.Values.Where(v => v != null).ToList()!;
        }


        public T GetOrAddService<T>(Func<T> factory) where T : notnull
        {
            ThrowIfDisposed();
            return (T)_services.GetOrAdd(typeof(T), _ => factory()!);
        }

        public bool TryGetService<T>([MaybeNullWhen(false)] out T? service) where T : notnull
        {
            ThrowIfDisposed();
            if (_services.TryGetValue(typeof(T), out var raw) && raw is T t)
            {
                service = t;
                return true;
            }

            if (Parent is IContext parent)
                return parent.TryGetService(out service);

            service = default;
            return false;
        }


        public virtual void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, true))
                return;

            _cts.Cancel();
            _cts.Dispose();
            _data.Clear();
            _services.Clear();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }


        protected virtual void RaiseDataChanged(string key, object? oldValue, object? newValue, DataChangeType changeType)
        {
            DataChanged?.Invoke(this, new ContextDataChangedEventArgs(key, oldValue, newValue, changeType));
        }

        protected void ThrowIfDisposed()
        {
            if (IsDisposed)
                throw new ObjectDisposedException(GetType().FullName, "上下文已释放");
        }

        protected static string ResolveKey<T>(string? key)
            => key ?? typeof(T).FullName ?? typeof(T).Name;
    }
}
