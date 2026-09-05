
namespace Modules.Transformers
{
    public interface ITransformLifecycle
    {
        void OnBefore<TInput>(TInput input);

        void OnAfter<TInput, TOutput>(TInput input, TOutput output);

        void OnError<TInput>(TInput input, Exception exception);

        Task OnBeforeAsync<TInput>(TInput input, CancellationToken cancellationToken = default);

        Task OnAfterAsync<TInput, TOutput>(TInput input, TOutput output, CancellationToken cancellationToken = default);

        Task OnErrorAsync<TInput>(TInput input, Exception exception, CancellationToken cancellationToken = default);

        void OnDispose();
    }

    public class LifecycleTransformer<TInput, TOutput> : ITransformer<TInput, TOutput>, ITransformLifecycle
    {
        private readonly ITransformer<TInput, TOutput> _inner;
        private readonly ITransformLifecycle? _lifecycle;

        public string Name => _inner.Name;

        public LifecycleTransformer(ITransformer<TInput, TOutput> inner, ITransformLifecycle? lifecycle = null)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _lifecycle = lifecycle;
        }

        public TOutput Transform(TInput input)
        {
            OnBefore(input);

            try
            {
                var output = _inner.Transform(input);

                OnAfter(input, output);

                return output;
            }
            catch (Exception ex)
            {
                OnError(input, ex);
                throw;
            }
        }


        public void OnBefore<T>(T input)
        {
            _lifecycle?.OnBefore(input);
            if (_inner is ITransformLifecycle innerLifecycle)
                innerLifecycle.OnBefore(input);
        }

        public void OnAfter<TInput1, TOutput1>(TInput1 input, TOutput1 output)
        {
            _lifecycle?.OnAfter(input, output);
            if (_inner is ITransformLifecycle innerLifecycle)
                innerLifecycle.OnAfter(input, output);
        }

        public void OnError<T>(T input, Exception exception)
        {
            _lifecycle?.OnError(input, exception);
            if (_inner is ITransformLifecycle innerLifecycle)
                innerLifecycle.OnError(input, exception);
        }


        public async Task OnBeforeAsync<T>(T input, CancellationToken cancellationToken = default)
        {
            if (_lifecycle != null)
                await _lifecycle.OnBeforeAsync(input, cancellationToken);

            if (_inner is ITransformLifecycle innerLifecycle)
                await innerLifecycle.OnBeforeAsync(input, cancellationToken);
        }

        public async Task OnAfterAsync<TInput1, TOutput1>(TInput1 input, TOutput1 output, CancellationToken cancellationToken = default)
        {
            if (_lifecycle != null)
                await _lifecycle.OnAfterAsync(input, output, cancellationToken);

            if (_inner is ITransformLifecycle innerLifecycle)
                await innerLifecycle.OnAfterAsync(input, output, cancellationToken);
        }

        public async Task OnErrorAsync<T>(T input, Exception exception, CancellationToken cancellationToken = default)
        {
            if (_lifecycle != null)
                await _lifecycle.OnErrorAsync(input, exception, cancellationToken);

            if (_inner is ITransformLifecycle innerLifecycle)
                await innerLifecycle.OnErrorAsync(input, exception, cancellationToken);
        }

        public void OnDispose()
        {
            _lifecycle?.OnDispose();
            (_inner as ITransformLifecycle)?.OnDispose();
        }
    }

    public class AsyncLifecycleTransformer<TInput, TOutput> : IAsyncTransformer<TInput, TOutput>, ITransformLifecycle
    {
        private readonly IAsyncTransformer<TInput, TOutput> _inner;
        private readonly ITransformLifecycle? _lifecycle;

        public string Name => _inner.Name;

        public AsyncLifecycleTransformer(IAsyncTransformer<TInput, TOutput> inner, ITransformLifecycle? lifecycle = null)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _lifecycle = lifecycle;
        }

        public async Task<TOutput> TransformAsync(TInput input, CancellationToken cancellationToken = default)
        {
            await OnBeforeAsync(input, cancellationToken);

            try
            {
                var output = await _inner.TransformAsync(input, cancellationToken);

                await OnAfterAsync(input, output, cancellationToken);

                return output;
            }
            catch (Exception ex)
            {
                await OnErrorAsync(input, ex, cancellationToken);
                throw;
            }
        }


        public void OnBefore<T>(T input)
        {
            _lifecycle?.OnBefore(input);
            if (_inner is ITransformLifecycle innerLifecycle)
                innerLifecycle.OnBefore(input);
        }

        public void OnAfter<TInput1, TOutput1>(TInput1 input, TOutput1 output)
        {
            _lifecycle?.OnAfter(input, output);
            if (_inner is ITransformLifecycle innerLifecycle)
                innerLifecycle.OnAfter(input, output);
        }

        public void OnError<T>(T input, Exception exception)
        {
            _lifecycle?.OnError(input, exception);
            if (_inner is ITransformLifecycle innerLifecycle)
                innerLifecycle.OnError(input, exception);
        }


        public async Task OnBeforeAsync<T>(T input, CancellationToken cancellationToken = default)
        {
            if (_lifecycle != null)
                await _lifecycle.OnBeforeAsync(input, cancellationToken);

            if (_inner is ITransformLifecycle innerLifecycle)
                await innerLifecycle.OnBeforeAsync(input, cancellationToken);
        }

        public async Task OnAfterAsync<TInput1, TOutput1>(TInput1 input, TOutput1 output, CancellationToken cancellationToken = default)
        {
            if (_lifecycle != null)
                await _lifecycle.OnAfterAsync(input, output, cancellationToken);

            if (_inner is ITransformLifecycle innerLifecycle)
                await innerLifecycle.OnAfterAsync(input, output, cancellationToken);
        }

        public async Task OnErrorAsync<T>(T input, Exception exception, CancellationToken cancellationToken = default)
        {
            if (_lifecycle != null)
                await _lifecycle.OnErrorAsync(input, exception, cancellationToken);

            if (_inner is ITransformLifecycle innerLifecycle)
                await innerLifecycle.OnErrorAsync(input, exception, cancellationToken);
        }

        public void OnDispose()
        {
            _lifecycle?.OnDispose();
            (_inner as ITransformLifecycle)?.OnDispose();
        }
    }
}
