
#nullable enable
using System.Collections.Concurrent;
using Share.TaskStatusPool.Abstractions;

namespace Share.TaskStatusPool.Core
{
    public class PoolRegistry<TKey, TContext> : IPoolRegistry<TKey, TContext>
        where TKey : notnull
    {
        protected readonly ConcurrentDictionary<TKey, IMorphism<TContext, TContext>> _store;

        public string Name { get; }

        public int Count => _store.Count;

        public IEnumerable<TKey> Keys => _store.Keys;

        public PoolRegistry(string? name = null, IEqualityComparer<TKey>? comparer = null)
        {
            Name = name ?? $"Registry<{typeof(TKey).Name},{typeof(TContext).Name}>";
            _store = new ConcurrentDictionary<TKey, IMorphism<TContext, TContext>>(comparer ?? EqualityComparer<TKey>.Default);
        }

        public void Register(TKey key, IMorphism<TContext, TContext> morphism)
        {
            ArgumentNullException.ThrowIfNull(key);
            PortGuard.Required(morphism, nameof(morphism));

            if (!_store.TryAdd(key, morphism))
                throw new ArgumentException($"键 '{key}' 已注册。如需覆盖请使用 RegisterOrUpdate。", nameof(key));
        }

        public void RegisterOrUpdate(TKey key, IMorphism<TContext, TContext> morphism)
        {
            ArgumentNullException.ThrowIfNull(key);
            PortGuard.Required(morphism, nameof(morphism));

            _store[key] = morphism;
        }

        public IMorphism<TContext, TContext> Get(TKey key)
        {
            ArgumentNullException.ThrowIfNull(key);

            if (_store.TryGetValue(key, out var morphism))
                return morphism;

            throw new KeyNotFoundException($"键 '{key}' 未注册。");
        }

        public bool TryGet(TKey key, out IMorphism<TContext, TContext>? morphism)
        {
            ArgumentNullException.ThrowIfNull(key);
            return _store.TryGetValue(key, out morphism);
        }

        public bool ContainsKey(TKey key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return _store.ContainsKey(key);
        }

        public bool Remove(TKey key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return _store.TryRemove(key, out _);
        }

        public void Clear() => _store.Clear();

        public TContext Execute(TKey key, TContext context)
        {
            var morphism = Get(key);
            return morphism.Morph(context);
        }

        public bool TryExecute(TKey key, TContext context, out TContext result)
        {
            if (_store.TryGetValue(key, out var morphism))
            {
                result = morphism.Morph(context);
                return true;
            }

            result = default!;
            return false;
        }

        public override string ToString()
            => $"PoolRegistry[{Name}] Count={_store.Count}";
    }
}
