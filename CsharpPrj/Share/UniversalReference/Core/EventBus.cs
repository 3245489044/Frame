
using System.Collections.Concurrent;

namespace UniversalWorkflowFramework.Core
{
    public class EventBus
    {
        private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();

        public int GetSubscriberCount<TEvent>()
        {
            var eventType = typeof(TEvent);
            return _handlers.TryGetValue(eventType, out var handlers) ? handlers.Count : 0;
        }

        public IEnumerable<Type> RegisteredEventTypes => _handlers.Keys;

        public void Subscribe<TEvent>(Func<TEvent, Task> handler)
        {
            var eventType = typeof(TEvent);
            _handlers.AddOrUpdate(
                eventType,
                _ => new List<Delegate> { handler },
                (_, handlers) =>
                {
                    handlers.Add(handler);
                    return handlers;
                }
            );
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
                handlers.Remove(handler);
                if (handlers.Count == 0)
                {
                    _handlers.TryRemove(eventType, out _);
                }
            }
        }

        public async Task PublishAsync<TEvent>(TEvent @event)
        {
            var eventType = typeof(TEvent);
            if (_handlers.TryGetValue(eventType, out var handlers) && handlers.Count > 0)
            {
                var tasks = new List<Task>(handlers.Count);
                foreach (var handler in handlers)
                {
                    var typedHandler = (Func<TEvent, Task>)handler;
                    tasks.Add(typedHandler(@event));
                }
                await Task.WhenAll(tasks);
            }
        }

        public async Task PublishSequentialAsync<TEvent>(TEvent @event)
        {
            var eventType = typeof(TEvent);
            if (_handlers.TryGetValue(eventType, out var handlers))
            {
                foreach (var handler in handlers)
                {
                    var typedHandler = (Func<TEvent, Task>)handler;
                    await typedHandler(@event);
                }
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
