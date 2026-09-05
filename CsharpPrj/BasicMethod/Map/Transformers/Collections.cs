
namespace Modules.Transformers
{
    public class Aggregator<TInput, TOutput> : ITransformer<IEnumerable<TInput>, TOutput>
    {
        private readonly Func<IEnumerable<TInput>, TOutput> _aggregateFunc;

        public string Name { get; }

        public Aggregator(string name, Func<IEnumerable<TInput>, TOutput> aggregateFunc)
        {
            Name = name;
            _aggregateFunc = aggregateFunc ?? throw new ArgumentNullException(nameof(aggregateFunc));
        }

        public TOutput Transform(IEnumerable<TInput> input)
            => _aggregateFunc(input);
    }

    public class Filter<T> : ITransformer<IEnumerable<T>, IEnumerable<T>>
    {
        private readonly Func<T, bool> _predicate;

        public string Name { get; }

        public Filter(string name, Func<T, bool> predicate)
        {
            Name = name;
            _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        }

        public IEnumerable<T> Transform(IEnumerable<T> input)
            => input.Where(_predicate);
    }

    public class Mapper<TInput, TOutput> : ITransformer<IEnumerable<TInput>, IEnumerable<TOutput>>
    {
        private readonly Func<TInput, TOutput> _mapFunc;

        public string Name { get; }

        public Mapper(string name, Func<TInput, TOutput> mapFunc)
        {
            Name = name;
            _mapFunc = mapFunc ?? throw new ArgumentNullException(nameof(mapFunc));
        }

        public IEnumerable<TOutput> Transform(IEnumerable<TInput> input)
            => input.Select(_mapFunc);
    }
}
