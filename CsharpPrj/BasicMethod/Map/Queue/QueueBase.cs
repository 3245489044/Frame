
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BasicMethod.Queue
{

    public abstract class QueueBase<TMessage> : IQueue<TMessage>, IDisposable
    {
        public string Name { get; }

        public string Description { get; }

        public abstract int Count { get; }

        public bool IsConsuming => _consumeCts != null && !_consumeCts.IsCancellationRequested;

        private CancellationTokenSource? _consumeCts;

        private Func<TMessage, CancellationToken, Task<QueueResult>>? _handler;

        private Task? _consumeTask;

        private readonly object _lock = new();

        private int _consecutiveErrors;

        public int MaxConsecutiveErrors { get; set; } = 10;

        public event EventHandler<Exception>? OnError;

        public event EventHandler<int>? OnMaxErrorsReached;

        protected QueueBase(string name, string? description = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description ?? string.Empty;
        }

        public abstract Task<QueueResult> EnqueueAsync(TMessage message, CancellationToken cancellationToken = default);

        public virtual async Task<QueueResult> EnqueueBatchAsync(IEnumerable<TMessage> messages, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(messages);

            int count = 0;
            foreach (var message in messages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await EnqueueAsync(message, cancellationToken);
                if (!result.IsSuccess)
                    return QueueResult.Fail($"批量入队失败（第 {count + 1} 条）: {result.Message}");
                count++;
            }

            return QueueResult.Ok($"批量入队完成，共 {count} 条");
        }

        public abstract Task<(bool Success, TMessage? Message)> TryDequeueAsync(CancellationToken cancellationToken = default);

        public virtual Task StartConsumingAsync(
            Func<TMessage, CancellationToken, Task<QueueResult>> handler,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(handler);

            lock (_lock)
            {
                if (IsConsuming)
                    throw new InvalidOperationException($"队列 '{Name}' 已在消费中");

                _handler = handler;
                _consumeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _consumeTask = ConsumeLoopAsync(_consumeCts.Token);
            }

            return Task.CompletedTask;
        }

        public virtual async Task StopConsumingAsync()
        {
            Task? consumeTask;
            lock (_lock)
            {
                _consumeCts?.Cancel();
                _consumeCts?.Dispose();
                _consumeCts = null;
                consumeTask = _consumeTask;
                _consumeTask = null;
                _handler = null;
            }

            if (consumeTask != null)
            {
                try
                {
                    await consumeTask;
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        public abstract void Clear();

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                var cts = _consumeCts;
                if (cts != null)
                {
                    try { cts.Cancel(); } catch { }
                    cts.Dispose();
                    _consumeCts = null;
                }
            }
        }

        protected virtual async Task ConsumeLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var (success, message) = await TryDequeueAsync(cancellationToken);

                    if (success && message != null && _handler != null)
                    {
                        await _handler(message, cancellationToken);
                        _consecutiveErrors = 0;
                    }
                    else if (!success)
                    {
                        await Task.Delay(10, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    OnError?.Invoke(this, ex);

                    _consecutiveErrors++;
                    if (_consecutiveErrors >= MaxConsecutiveErrors)
                    {
                        OnMaxErrorsReached?.Invoke(this, _consecutiveErrors);
                        break;
                    }

                    await Task.Delay(100, cancellationToken);
                }
            }
        }
    }


    public abstract class QueueHandlerBase<TMessage> : IQueueHandler<TMessage>
    {
        public string Name { get; }

        public string Description { get; }

        protected QueueHandlerBase(string name, string? description = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description ?? string.Empty;
        }

        public abstract Task<QueueResult> HandleAsync(TMessage message, CancellationToken cancellationToken = default);
    }


    public class DelegateQueueHandler<TMessage> : QueueHandlerBase<TMessage>
    {
        private readonly Func<TMessage, CancellationToken, Task<QueueResult>> _handleFunc;

        public DelegateQueueHandler(
            string name,
            Func<TMessage, CancellationToken, Task<QueueResult>> handleFunc,
            string? description = null)
            : base(name, description)
        {
            _handleFunc = handleFunc ?? throw new ArgumentNullException(nameof(handleFunc));
        }

        public DelegateQueueHandler(
            string name,
            Func<TMessage, QueueResult> handleFunc,
            string? description = null)
            : base(name, description)
        {
            ArgumentNullException.ThrowIfNull(handleFunc);
            _handleFunc = (msg, ct) => Task.FromResult(handleFunc(msg));
        }

        public override Task<QueueResult> HandleAsync(TMessage message, CancellationToken cancellationToken = default)
            => _handleFunc(message, cancellationToken);
    }


    public class QueuePipeline<TMessage> : IQueuePipeline<TMessage>
    {
        public string Name { get; }

        public string Description { get; }

        public IReadOnlyList<IQueueHandler<TMessage>> Handlers => _handlers.AsReadOnly();

        private readonly List<IQueueHandler<TMessage>> _handlers = new();

        public QueuePipeline(string name, string? description = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description ?? string.Empty;
        }

        public IQueuePipeline<TMessage> AddHandler(IQueueHandler<TMessage> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            _handlers.Add(handler);
            return this;
        }

        public IQueuePipeline<TMessage> InsertBefore(string name, IQueueHandler<TMessage> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            var index = FindIndexByName(name);
            if (index >= 0)
                _handlers.Insert(index, handler);
            else
                _handlers.Add(handler);
            return this;
        }

        public IQueuePipeline<TMessage> InsertAfter(string name, IQueueHandler<TMessage> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            var index = FindIndexByName(name);
            if (index >= 0)
                _handlers.Insert(index + 1, handler);
            else
                _handlers.Add(handler);
            return this;
        }

        public IQueuePipeline<TMessage> Remove(string name)
        {
            var index = FindIndexByName(name);
            if (index >= 0)
                _handlers.RemoveAt(index);
            return this;
        }

        public async Task<QueueResult> ExecuteAsync(TMessage message, CancellationToken cancellationToken = default)
        {
            foreach (var handler in _handlers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var result = await handler.HandleAsync(message, cancellationToken);
                    if (!result.IsSuccess)
                        return QueueResult.Fail($"处理器 '{handler.Name}' 失败: {result.Message}", result.Exception);
                }
                catch (Exception ex)
                {
                    return QueueResult.Fail($"处理器 '{handler.Name}' 异常: {ex.Message}", ex);
                }
            }

            return QueueResult.Ok($"管线 '{Name}' 处理完成，共 {_handlers.Count} 个处理器");
        }

        private int FindIndexByName(string name)
        {
            for (int i = 0; i < _handlers.Count; i++)
            {
                if (string.Equals(_handlers[i].Name, name, StringComparison.Ordinal))
                    return i;
            }
            return -1;
        }
    }


    public abstract class QueueDecoratorBase<TMessage> : QueueBase<TMessage>
    {
        protected readonly IQueue<TMessage> _inner;

        public override int Count => _inner.Count;

        protected QueueDecoratorBase(IQueue<TMessage> inner, string name, string? description = null)
            : base(name, description)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public override Task<QueueResult> EnqueueAsync(TMessage message, CancellationToken cancellationToken = default)
            => _inner.EnqueueAsync(message, cancellationToken);

        public override async Task<(bool Success, TMessage? Message)> TryDequeueAsync(CancellationToken cancellationToken = default)
            => await _inner.TryDequeueAsync(cancellationToken);

        public override void Clear() => _inner.Clear();
    }
}


