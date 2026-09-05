
namespace Modules.Transformers
{
    public interface ITransformer { }

    public interface ITransformer<in TInput, out TOutput> : ITransformer
    {
        string Name { get; }

        TOutput Transform(TInput input);
    }

    public interface IAsyncTransformer<in TInput, TOutput>
    {
        string Name { get; }

        Task<TOutput> TransformAsync(TInput input, CancellationToken cancellationToken = default);
    }

    public class AsyncTransformerAdapter<TInput, TOutput> : IAsyncTransformer<TInput, TOutput>
    {
        private readonly ITransformer<TInput, TOutput> _inner;

        public string Name => $"Async({_inner.Name})";

        public AsyncTransformerAdapter(ITransformer<TInput, TOutput> inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public Task<TOutput> TransformAsync(TInput input, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_inner.Transform(input));
        }
    }

    internal class ComposedAsyncTransformer<TInput, TMiddle, TOutput> : IAsyncTransformer<TInput, TOutput>
    {
        private readonly IAsyncTransformer<TInput, TMiddle> _first;
        private readonly IAsyncTransformer<TMiddle, TOutput> _second;

        public string Name => $"{_first.Name} ∘ {_second.Name}";

        public ComposedAsyncTransformer(
            IAsyncTransformer<TInput, TMiddle> first,
            IAsyncTransformer<TMiddle, TOutput> second)
        {
            _first = first;
            _second = second;
        }

        public async Task<TOutput> TransformAsync(TInput input, CancellationToken cancellationToken = default)
        {
            var middle = await _first.TransformAsync(input, cancellationToken);
            return await _second.TransformAsync(middle, cancellationToken);
        }
    }
}
