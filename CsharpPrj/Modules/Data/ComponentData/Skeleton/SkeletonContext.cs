
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BasicMethod.ContextFrame.Core;

namespace Modules.ComponentData.Skeleton
{
    public class SkeletonContext : IDisposable
    {
        private readonly IContext _context;
        private readonly bool _ownsContext;
        private volatile CancellationTokenSource _linkedCts;
        private readonly List<CancellationTokenSource> _allCts = new();
        private readonly ConcurrentDictionary<string, object> _items;
        private readonly object _lock = new();
        private bool _disposed;
        private const string ItemsKey = "__SkeletonContextItems";
        private IServiceProvider _serviceProvider;

        private readonly Dictionary<Type, object> _services = new();

        public IContext Context => _context;
        public IServiceProvider ServiceProvider { get => _serviceProvider; set => _serviceProvider = value; }
        public IReadOnlyDictionary<string, object> Items => _items;

        public void RegisterService<T>(T service) where T : class
        {
            _services[typeof(T)] = service;
        }

        public void RegisterServices(IReadOnlyDictionary<Type, object> services)
        {
            foreach (var kvp in services)
                _services[kvp.Key] = kvp.Value;
        }

        public T? ResolveService<T>() where T : class
        {
            return _services.TryGetValue(typeof(T), out var service)
                ? service as T
                : null;
        }

        public T ResolveRequiredService<T>() where T : class
        {
            return ResolveService<T>()
                ?? throw new InvalidOperationException($"Service {typeof(T).Name} is not registered in SkeletonContext");
        }

        public void SetItem(string key, object value) => _items[key] = value;

        public bool RemoveItem(string key) => _items.TryRemove(key, out _);

        public CancellationToken CancellationToken
        {
            get => _linkedCts.Token;
            set
            {
                lock (_lock)
                {
                    if (value == _linkedCts.Token) return;
                    if (_disposed) throw new ObjectDisposedException(nameof(SkeletonContext));
                    var newCts = CancellationTokenSource.CreateLinkedTokenSource(_context.CancellationToken, value);
                    _allCts.Add(newCts);
                    var oldCts = Interlocked.Exchange(ref _linkedCts, newCts);
                    oldCts?.Dispose();
                }
            }
        }

        public SkeletonContext(IContext context = null)
        {
            _context = context ?? new ContextBase();
            _ownsContext = context == null;
            _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_context.CancellationToken);
            _allCts.Add(_linkedCts);

            if (_context.TryGetData<ConcurrentDictionary<string, object>>(out var existing, ItemsKey))
            {
                _items = existing;
            }
            else if (_context.ContainsData<object>(ItemsKey))
            {
                throw new InvalidOperationException($"Context already has data with key '{ItemsKey}' but of incompatible type.");
            }
            else
            {
                _items = new ConcurrentDictionary<string, object>();
                _context.SetData(_items, ItemsKey);
            }
        }

        public T? GetData<T>(string key = null) where T : class => _context.GetData<T>(key);
        public bool TryGetData<T>(out T data, string key = null) where T : class => _context.TryGetData(out data, key);
        public IEnumerable<T> GetAllData<T>() where T : class => _context.GetAllData<T>();
        public void SetData<T>(T data, string key = null) where T : class => _context.SetData(data, key);
        public void RemoveData<T>(string key = null) where T : class => _context.RemoveData<T>(key);

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                lock (_lock)
                {
                    _allCts.ForEach(cts => cts.Dispose());
                    _allCts.Clear();
                    if (_ownsContext) _context.Dispose();
                }
            }
            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
