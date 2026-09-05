
namespace Modules.Transformers
{

    public interface IMatchStrategy<in T, out TKey>
        where TKey : notnull
    {
        TKey GetMatchKey(T item);
    }

    public class IdentityMatch<T> : IMatchStrategy<T, T>
        where T : notnull
    {
        public T GetMatchKey(T item) => item;
    }

    public class KeySelectorMatch<T, TKey> : IMatchStrategy<T, TKey>
        where TKey : notnull
    {
        private readonly Func<T, TKey> _keySelector;

        public KeySelectorMatch(Func<T, TKey> keySelector)
        {
            _keySelector = keySelector ?? throw new ArgumentNullException(nameof(keySelector));
        }

        public TKey GetMatchKey(T item) => _keySelector(item);
    }


    public interface IReduceStrategy<T>
    {
        T Reduce(T existing, T newItem);
    }

    public class KeepFirstReduce<T> : IReduceStrategy<T>
    {
        public T Reduce(T existing, T newItem) => existing;
    }

    public class KeepLastReduce<T> : IReduceStrategy<T>
    {
        public T Reduce(T existing, T newItem) => newItem;
    }

    public class FuncReduce<T> : IReduceStrategy<T>
    {
        private readonly Func<T, T, T> _reduceFunc;

        public FuncReduce(Func<T, T, T> reduceFunc)
        {
            _reduceFunc = reduceFunc ?? throw new ArgumentNullException(nameof(reduceFunc));
        }

        public T Reduce(T existing, T newItem) => _reduceFunc(existing, newItem);
    }
}
