
namespace Modules.Transformers
{
    public interface ITransformDecorator
    {
        string DecoratorType { get; }

        string? Description { get; }

        ITransformer<TInput, TOutput> Apply<TInput, TOutput>(ITransformer<TInput, TOutput> inner);

        bool CanHandle(object? configValue);
    }

    public interface IAsyncTransformDecorator
    {
        string DecoratorType { get; }

        string? Description { get; }

        IAsyncTransformer<TInput, TOutput> Apply<TInput, TOutput>(
            IAsyncTransformer<TInput, TOutput> inner);

        bool CanHandle(object? configValue);
    }


    public class LogDecorator : ITransformDecorator
    {
        public string DecoratorType => "log";
        public string? Description => "在转换前后通过 TransformContext 记录日志";

        public ITransformer<TInput, TOutput> Apply<TInput, TOutput>(
            ITransformer<TInput, TOutput> inner)
            => new LoggingTransformer<TInput, TOutput>(inner);

        public bool CanHandle(object? configValue)
            => configValue is bool b && b || configValue is string;
    }

    internal class LoggingTransformer<TInput, TOutput> : ITransformer<TInput, TOutput>
    {
        private readonly ITransformer<TInput, TOutput> _inner;

        public string Name => $"Log({_inner.Name})";

        public LoggingTransformer(ITransformer<TInput, TOutput> inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public TOutput Transform(TInput input)
        {
            var ctx = TransformContext.Current;
            ctx?.Log($"[{_inner.Name}] 开始转换: {input}");

            try
            {
                var output = _inner.Transform(input);
                ctx?.Log($"[{_inner.Name}] 转换完成: {output}");
                return output;
            }
            catch (Exception ex)
            {
                ctx?.Log($"[{_inner.Name}] 转换失败: {ex.Message}");
                throw;
            }
        }
    }

    public class TimeoutDecorator : ITransformDecorator
    {
        private readonly TimeSpan _timeout;

        public string DecoratorType => "timeout";
        public string? Description => $"设置转换超时时间: {_timeout}";

        public TimeoutDecorator() : this(TimeSpan.FromSeconds(30)) { }

        public TimeoutDecorator(TimeSpan timeout)
        {
            _timeout = timeout;
        }

        public ITransformer<TInput, TOutput> Apply<TInput, TOutput>(
            ITransformer<TInput, TOutput> inner)
            => new TimeoutTransformer<TInput, TOutput>(inner, _timeout);

        public bool CanHandle(object? configValue)
            => configValue is int || configValue is double || configValue is TimeSpan;
    }

    internal class TimeoutTransformer<TInput, TOutput> : ITransformer<TInput, TOutput>
    {
        private readonly ITransformer<TInput, TOutput> _inner;
        private readonly TimeSpan _timeout;

        public string Name => $"Timeout({_inner.Name},{_timeout})";

        public TimeoutTransformer(ITransformer<TInput, TOutput> inner, TimeSpan timeout)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _timeout = timeout;
        }

        public TOutput Transform(TInput input)
        {
            using var cts = new CancellationTokenSource(_timeout);
            try
            {
                var task = Task.Run(() => _inner.Transform(input), cts.Token);
                if (task.Wait(_timeout, cts.Token))
                    return task.Result;
                throw new TimeoutException(
                    $"Transformer '{_inner.Name}' timed out after {_timeout}");
            }
            catch (AggregateException ae) when (ae.InnerException is OperationCanceledException)
            {
                throw new TimeoutException(
                    $"Transformer '{_inner.Name}' timed out after {_timeout}");
            }
        }
    }

    public class RetryDecorator : ITransformDecorator
    {
        private readonly int _maxRetries;
        private readonly TimeSpan _baseDelay;

        public string DecoratorType => "retry";
        public string? Description => $"失败时重试 {_maxRetries} 次";

        public RetryDecorator() : this(3, TimeSpan.FromMilliseconds(100)) { }

        public RetryDecorator(int maxRetries, TimeSpan? baseDelay = null)
        {
            _maxRetries = Math.Max(1, maxRetries);
            _baseDelay = baseDelay ?? TimeSpan.FromMilliseconds(100);
        }

        public ITransformer<TInput, TOutput> Apply<TInput, TOutput>(
            ITransformer<TInput, TOutput> inner)
            => new RetryTransformer<TInput, TOutput>(inner, _maxRetries, _baseDelay);

        public bool CanHandle(object? configValue)
            => configValue is int || configValue is long;
    }

    internal class RetryTransformer<TInput, TOutput> : ITransformer<TInput, TOutput>
    {
        private readonly ITransformer<TInput, TOutput> _inner;
        private readonly int _maxRetries;
        private readonly TimeSpan _baseDelay;

        public string Name => $"Retry({_inner.Name},{_maxRetries})";

        public RetryTransformer(ITransformer<TInput, TOutput> inner, int maxRetries, TimeSpan baseDelay)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _maxRetries = maxRetries;
            _baseDelay = baseDelay;
        }

        public TOutput Transform(TInput input)
        {
            var lastException = (Exception?)null;

            for (int attempt = 1; attempt <= _maxRetries; attempt++)
            {
                try
                {
                    return _inner.Transform(input);
                }
                catch (Exception ex) when (attempt < _maxRetries)
                {
                    lastException = ex;
                    var ctx = TransformContext.Current;
                    ctx?.Log($"[{_inner.Name}] 第 {attempt}/{_maxRetries} 次重试: {ex.Message}");

                    var delayMs = (int)(_baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
                    Thread.Sleep(delayMs);
                }
            }

            throw new InvalidOperationException(
                $"Transformer '{_inner.Name}' failed after {_maxRetries} retries",
                lastException);
        }
    }

    public class DecoratorChain
    {
        private readonly List<ITransformDecorator> _decorators = new();

        public DecoratorChain Add(ITransformDecorator decorator)
        {
            _decorators.Add(decorator);
            return this;
        }

        public DecoratorChain AddRange(IEnumerable<ITransformDecorator> decorators)
        {
            _decorators.AddRange(decorators);
            return this;
        }

        public ITransformer<TInput, TOutput> Apply<TInput, TOutput>(
            ITransformer<TInput, TOutput> transformer)
        {
            var result = transformer;
            for (int i = _decorators.Count - 1; i >= 0; i--)
                result = _decorators[i].Apply(result);
            return result;
        }

        public void Clear() => _decorators.Clear();

        public int Count => _decorators.Count;
    }
}
