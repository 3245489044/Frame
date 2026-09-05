
using System.Collections.Concurrent;

namespace Modules.Transformers
{
    public class CacheTransformer<TInput, TOutput> : ITransformer<TInput, TOutput>
        where TInput : notnull, IEquatable<TInput>
    {
        private readonly ITransformer<TInput, TOutput> _inner;
        private readonly ConcurrentDictionary<TInput, Lazy<TOutput>> _cache = new();

        public string Name => $"Cached({_inner.Name})";

        public int CacheSize => _cache.Count;

        public CacheTransformer(ITransformer<TInput, TOutput> inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public TOutput Transform(TInput input)
        {
            return _cache.GetOrAdd(input, key =>
                new Lazy<TOutput>(() => _inner.Transform(key), LazyThreadSafetyMode.ExecutionAndPublication)
            ).Value;
        }

        public void ClearCache()
        {
            _cache.Clear();
        }

        public bool RemoveCache(TInput input)
        {
            return _cache.TryRemove(input, out _);
        }
    }
}
