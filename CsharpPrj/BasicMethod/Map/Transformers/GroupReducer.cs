
namespace Modules.Transformers
{
    public class GroupReducer<T> : ITransformer<IEnumerable<T>, IEnumerable<T>>
    {
        private readonly Func<T, object> _keyExtractor;
        private readonly IReduceStrategy<T> _reduceStrategy;
        private readonly IEqualityComparer<object>? _keyComparer;

        public string Name { get; }

        public GroupReducer(
            string name,
            IMatchStrategy<T, object>? matchStrategy = null,
            IReduceStrategy<T>? reduceStrategy = null)
        {
            Name = name;
            _keyExtractor = matchStrategy != null
                ? matchStrategy.GetMatchKey
                : (Func<T, object>)(item => item!);
            _reduceStrategy = reduceStrategy ?? new KeepFirstReduce<T>();
            _keyComparer = null;
        }

        public GroupReducer(
            string name,
            IEqualityComparer<T>? comparer,
            Func<T, T, T>? reduceFunc = null)
        {
            Name = name;
            _reduceStrategy = reduceFunc != null
                ? new FuncReduce<T>(reduceFunc)
                : new KeepFirstReduce<T>();
            _keyExtractor = item => item!;
            _keyComparer = comparer != null
                ? new ComparerEqualityAdapter<T>(comparer)
                : null;
        }

        public IEnumerable<T> Transform(IEnumerable<T> input)
        {
            var map = _keyComparer != null
                ? new Dictionary<object, T>(_keyComparer)
                : new Dictionary<object, T>();

            foreach (var item in input)
            {
                var key = _keyExtractor(item);
                if (map.TryGetValue(key, out var existing))
                    map[key] = _reduceStrategy.Reduce(existing, item);
                else
                    map[key] = item;
            }

            return map.Values;
        }
    }

    internal class ComparerEqualityAdapter<T> : IEqualityComparer<object>
    {
        private readonly IEqualityComparer<T> _inner;

        public ComparerEqualityAdapter(IEqualityComparer<T> inner)
        {
            _inner = inner;
        }

        public new bool Equals(object? x, object? y)
        {
            if (x is T tx && y is T ty)
                return _inner.Equals(tx, ty);
            return EqualityComparer<object>.Default.Equals(x, y);
        }

        public int GetHashCode(object obj)
        {
            if (obj is T t)
                return _inner.GetHashCode(t);
            return obj?.GetHashCode() ?? 0;
        }
    }
}
