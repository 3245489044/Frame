
namespace Modules.Transformers
{
    public static class TransformerExtensions
    {

        public static ITransformer<TInput, TOutput> Then<TInput, TMiddle, TOutput>(
            this ITransformer<TInput, TMiddle> first,
            ITransformer<TMiddle, TOutput> second)
            => new ComposedTransformer<TInput, TMiddle, TOutput>(first, second);

        public static ITransformer<TInput, TOutput> ToTransformer<TInput, TOutput>(
            this Func<TInput, TOutput> func,
            string name)
            => new FuncTransformer<TInput, TOutput>(name, func);


        public static ITransformer<TInput, TOutput> WithLifecycle<TInput, TOutput>(
            this ITransformer<TInput, TOutput> transformer,
            ITransformLifecycle? lifecycle = null)
            => new LifecycleTransformer<TInput, TOutput>(transformer, lifecycle);

        public static ITransformer<TInput, TOutput> WithLifecycle<TInput, TOutput>(
            this ITransformer<TInput, TOutput> transformer,
            Action<string>? onBefore = null,
            Action<string>? onAfter = null,
            Action<string>? onError = null)
            => new LifecycleTransformer<TInput, TOutput>(transformer,
                new DelegateLifecycle(onBefore, onAfter, onError));


        public static ITransformer<TInput, TOutput> WithMetadata<TInput, TOutput>(
            this ITransformer<TInput, TOutput> transformer,
            ITransformMetadata metadata)
            => new MetadataTransformer<TInput, TOutput>(transformer, metadata);

        public static ITransformer<TInput, TOutput> WithMetadata<TInput, TOutput>(
            this ITransformer<TInput, TOutput> transformer,
            string? description = null,
            string version = "1.0.0",
            Dictionary<string, string>? properties = null)
        {
            var metadata = new TransformMetadataBuilder(transformer.Name, typeof(TInput), typeof(TOutput))
                .WithDescription(description)
                .WithVersion(version);
            if (properties != null)
                metadata.WithProperties(properties);
            return new MetadataTransformer<TInput, TOutput>(transformer, metadata.Build());
        }


        public static ITransformer<TInput, TOutput> Decorate<TInput, TOutput>(
            this ITransformer<TInput, TOutput> transformer,
            DecoratorChain decorators)
            => decorators.Apply(transformer);

        public static ITransformer<TInput, TOutput> Decorate<TInput, TOutput>(
            this ITransformer<TInput, TOutput> transformer,
            ITransformDecorator decorator)
            => decorator.Apply(transformer);


        public static IEnumerable<T> GroupReduce<T>(
            this IEnumerable<T> source,
            IMatchStrategy<T, object> matchStrategy,
            IReduceStrategy<T>? reduceStrategy = null,
            string? name = null)
        {
            var reducer = new GroupReducer<T>(
                name ?? $"GroupReduce<{typeof(T).Name}>",
                matchStrategy,
                reduceStrategy);
            return reducer.Transform(source);
        }

        public static IEnumerable<T> DistinctBy<T, TKey>(
            this IEnumerable<T> source,
            Func<T, TKey> keySelector)
            where TKey : notnull
        {
            var map = new Dictionary<TKey, T>();
            foreach (var item in source)
            {
                var key = keySelector(item);
                if (!map.ContainsKey(key))
                    map[key] = item;
            }
            return map.Values;
        }

        public static IEnumerable<T> ReduceBy<T, TKey>(
            this IEnumerable<T> source,
            Func<T, TKey> keySelector,
            Func<T, T, T> reduceFunc)
            where TKey : notnull
        {
            var map = new Dictionary<TKey, T>();
            foreach (var item in source)
            {
                var key = keySelector(item);
                if (map.TryGetValue(key, out var existing))
                    map[key] = reduceFunc(existing, item);
                else
                    map[key] = item;
            }
            return map.Values;
        }
    }


    internal class ComposedTransformer<TInput, TMiddle, TOutput> : ITransformer<TInput, TOutput>
    {
        private readonly ITransformer<TInput, TMiddle> _first;
        private readonly ITransformer<TMiddle, TOutput> _second;

        public string Name => $"{_first.Name} ∘ {_second.Name}";

        public ComposedTransformer(
            ITransformer<TInput, TMiddle> first,
            ITransformer<TMiddle, TOutput> second)
        {
            _first = first;
            _second = second;
        }

        public TOutput Transform(TInput input)
            => _second.Transform(_first.Transform(input));
    }

    internal class FuncTransformer<TInput, TOutput> : ITransformer<TInput, TOutput>
    {
        private readonly Func<TInput, TOutput> _func;

        public string Name { get; }

        public FuncTransformer(string name, Func<TInput, TOutput> func)
        {
            Name = name;
            _func = func;
        }

        public TOutput Transform(TInput input) => _func(input);
    }

    internal class DelegateLifecycle : ITransformLifecycle
    {
        private readonly Action<string>? _onBefore;
        private readonly Action<string>? _onAfter;
        private readonly Action<string>? _onError;

        public DelegateLifecycle(
            Action<string>? onBefore,
            Action<string>? onAfter,
            Action<string>? onError)
        {
            _onBefore = onBefore;
            _onAfter = onAfter;
            _onError = onError;
        }


        public void OnBefore<T>(T input)
        {
            _onBefore?.Invoke($"[Before] {input}");
        }

        public void OnAfter<TInput, TOutput>(TInput input, TOutput output)
        {
            _onAfter?.Invoke($"[After] {input} → {output}");
        }

        public void OnError<T>(T input, Exception exception)
        {
            _onError?.Invoke($"[Error] {input}: {exception.Message}");
        }


        public Task OnBeforeAsync<T>(T input, CancellationToken cancellationToken = default)
        {
            OnBefore(input);
            return Task.CompletedTask;
        }

        public Task OnAfterAsync<TInput, TOutput>(TInput input, TOutput output, CancellationToken cancellationToken = default)
        {
            OnAfter(input, output);
            return Task.CompletedTask;
        }

        public Task OnErrorAsync<T>(T input, Exception exception, CancellationToken cancellationToken = default)
        {
            OnError(input, exception);
            return Task.CompletedTask;
        }

        public void OnDispose() { }
    }
}
