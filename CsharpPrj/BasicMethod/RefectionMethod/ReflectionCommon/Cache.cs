#nullable enable

using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Modules.ReflectionCommon.Caching
{
    public interface ICache<TKey, TValue> where TKey : notnull
    {
        TValue GetOrAdd(TKey key, Func<TKey, TValue> factory);
        bool TryGetValue(TKey key, out TValue value);
        void Set(TKey key, TValue value);
        void Remove(TKey key);
        void Clear();
        int Count { get; }
    }

    public class Cache<TKey, TValue> : ICache<TKey, TValue> where TKey : notnull
    {
        private readonly ConcurrentDictionary<TKey, TValue> _cache;
        private readonly Func<TKey, TValue>? _defaultFactory;

        public Cache(Func<TKey, TValue>? defaultFactory = null)
        {
            _cache = new ConcurrentDictionary<TKey, TValue>();
            _defaultFactory = defaultFactory;
        }

        public Cache(IEqualityComparer<TKey> comparer, Func<TKey, TValue>? defaultFactory = null)
        {
            _cache = new ConcurrentDictionary<TKey, TValue>(comparer);
            _defaultFactory = defaultFactory;
        }

        public TValue GetOrAdd(TKey key, Func<TKey, TValue> factory)
        {
            return _cache.GetOrAdd(key, factory);
        }

        public TValue GetOrAdd(TKey key)
        {
            if (_defaultFactory == null)
                throw new InvalidOperationException("No default factory configured. Use GetOrAdd(key, factory) instead.");

            return _cache.GetOrAdd(key, _defaultFactory);
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            return _cache.TryGetValue(key, out value!);
        }

        public void Set(TKey key, TValue value)
        {
            _cache[key] = value;
        }

        public void Remove(TKey key)
        {
            _cache.TryRemove(key, out _);
        }

        public void Clear()
        {
            _cache.Clear();
        }

        public int Count => _cache.Count;
    }

    public class AsyncCache<TKey, TValue> where TKey : notnull
    {
        private readonly ConcurrentDictionary<TKey, Lazy<Task<TValue>>> _cache;
        private readonly Func<TKey, CancellationToken, Task<TValue>> _factory;

        public AsyncCache(Func<TKey, CancellationToken, Task<TValue>> factory)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _cache = new ConcurrentDictionary<TKey, Lazy<Task<TValue>>>();
        }

        public AsyncCache(IEqualityComparer<TKey> comparer, Func<TKey, CancellationToken, Task<TValue>> factory)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _cache = new ConcurrentDictionary<TKey, Lazy<Task<TValue>>>(comparer);
        }

        public Task<TValue> GetOrAddAsync(TKey key, CancellationToken cancellationToken = default)
        {
            var lazy = _cache.GetOrAdd(key, k => new Lazy<Task<TValue>>(
                () => _factory(k, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));

            return lazy.Value;
        }

        public async Task<(bool Found, TValue? Value)> TryGetValueAsync(TKey key)
        {
            if (_cache.TryGetValue(key, out var lazy) && lazy.IsValueCreated)
            {
                var value = await lazy.Value.ConfigureAwait(false);
                return (true, value);
            }

            return (false, default);
        }

        public void Clear()
        {
            _cache.Clear();
        }

        public int Count => _cache.Count;
    }
}
