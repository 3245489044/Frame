
#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BasicMethod.Queue
{

    public class QueueDispatcher : IQueueDispatcher, IDisposable
    {
        public string Name { get; }

        public string Description { get; }

        private readonly ConcurrentDictionary<string, object> _routes = new();

        public event EventHandler<(string Key, Exception Error)>? OnConsumingError;

        public QueueDispatcher(string name, string? description = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description ?? string.Empty;
        }

        public IQueueDispatcher Register<TMessage>(string key, IQueue<TMessage> queue)
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(queue);

            _routes[key] = queue;
            return this;
        }

        public IQueueDispatcher Register<TMessage>(string key, IQueueHandler<TMessage> handler)
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(handler);

            var queue = new MemoryQueue<TMessage>($"{Name}.{key}", $"分发器 '{Name}' 的自动队列 '{key}'");
            _routes[key] = queue;

            var consumeTask = queue.StartConsumingAsync(handler.HandleAsync);
            consumeTask.ContinueWith(
                t => OnConsumingError?.Invoke(this, (key, t.Exception.InnerException ?? t.Exception)),
                TaskContinuationOptions.OnlyOnFaulted);

            return this;
        }

        private (IQueue<TMessage>? Queue, string? Error) ResolveQueue<TMessage>(string key)
        {
            if (_routes.TryGetValue(key, out var route))
            {
                if (route is IQueue<TMessage> queue)
                    return (queue, null);

                return (null, $"路由键 '{key}' 的类型不匹配（期望 IQueue<{typeof(TMessage).Name}>）");
            }

            return (null, $"未找到路由键 '{key}'，请先调用 Register 注册");
        }

        public async Task<QueueResult> DispatchAsync<TMessage>(string key, TMessage message, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(key);

            var (queue, error) = ResolveQueue<TMessage>(key);
            if (queue == null)
                return QueueResult.Fail(error);

            return await queue.EnqueueAsync(message, cancellationToken);
        }

        public async Task<QueueResult> DispatchBatchAsync<TMessage>(string key, IEnumerable<TMessage> messages, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(messages);

            var (queue, error) = ResolveQueue<TMessage>(key);
            if (queue == null)
                return QueueResult.Fail(error);

            return await queue.EnqueueBatchAsync(messages, cancellationToken);
        }

        public IReadOnlyCollection<string> GetRegisteredKeys() => _routes.Keys.ToArray();

        public bool IsRegistered(string key) => _routes.ContainsKey(key);

        public bool Unregister(string key)
        {
            if (_routes.TryRemove(key, out var route))
            {
                if (route is IDisposable disposable)
                    disposable.Dispose();
                return true;
            }
            return false;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var key in _routes.Keys.ToArray())
                {
                    Unregister(key);
                }
            }
        }
    }
}
