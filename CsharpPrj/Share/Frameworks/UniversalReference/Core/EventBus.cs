
using System.Collections.Concurrent;

namespace UniversalWorkflowFramework.Core
{
    public class EventBus
    {
        private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();

        public int GetSubscriberCount<TEvent>()
        {
            var eventType = typeof(TEvent);
            if (!_handlers.TryGetValue(eventType, out var handlers))
                return 0;
            lock (handlers) return handlers.Count;
        }

        public IEnumerable<Type> RegisteredEventTypes => _handlers.Keys;

        public void Subscribe<TEvent>(Func<TEvent, Task> handler)
        {
            var eventType = typeof(TEvent);
            var handlers = _handlers.GetOrAdd(eventType, _ => new List<Delegate>());
            lock (handlers)
            {
                handlers.Add(handler);
            }
        }

        public void SubscribeSync<TEvent>(Action<TEvent> handler)
        {
            var eventType = typeof(TEvent);
            Func<TEvent, Task> asyncHandler = e =>
            {
                handler(e);
                return Task.CompletedTask;
            };
            Subscribe(asyncHandler);
        }

        public void Unsubscribe<TEvent>(Func<TEvent, Task> handler)
        {
            var eventType = typeof(TEvent);
            if (_handlers.TryGetValue(eventType, out var handlers))
            {
                lock (handlers)
                {
                    handlers.Remove(handler);
                    if (handlers.Count == 0)
                        _handlers.TryRemove(eventType, out _);
                }
            }
        }

        public async Task PublishAsync<TEvent>(TEvent @event)
        {
            var eventType = typeof(TEvent);
            Delegate[] snapshot;
            if (!_handlers.TryGetValue(eventType, out var handlers))
                return;
            // 快照后派发：回调内 Subscribe/Unsubscribe 不得改变本次遍历（原实现直接遍历可变 List）。
            lock (handlers) snapshot = handlers.ToArray();

            if (snapshot.Length == 0)
                return;

            var tasks = new List<Task>(snapshot.Length);
            foreach (var handler in snapshot)
            {
                var typedHandler = (Func<TEvent, Task>)handler;
                tasks.Add(typedHandler(@event));
            }
            await Task.WhenAll(tasks);
        }

        public async Task PublishSequentialAsync<TEvent>(TEvent @event)
        {
            var eventType = typeof(TEvent);
            Delegate[] snapshot;
            if (!_handlers.TryGetValue(eventType, out var handlers))
                return;
            lock (handlers) snapshot = handlers.ToArray();

            foreach (var handler in snapshot)
            {
                var typedHandler = (Func<TEvent, Task>)handler;
                await typedHandler(@event);
            }
        }

        public void Clear()
        {
            _handlers.Clear();
        }

        public void Clear<TEvent>()
        {
            _handlers.TryRemove(typeof(TEvent), out _);
        }
    }
}
