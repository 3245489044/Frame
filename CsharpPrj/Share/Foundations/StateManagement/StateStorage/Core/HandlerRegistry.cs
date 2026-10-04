
using System.Collections.Concurrent;

namespace Modules.StateManagement
{
    public class HandlerRegistry<TKey, THandler> : IHandlerRegistry<TKey, THandler>
        where TKey : notnull
        where THandler : class
    {
        private readonly ConcurrentDictionary<TKey, THandler> _handlers = new();

        public int Count => _handlers.Count;

        public void Register(TKey key, THandler handler)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            handler = PortGuard.Required(handler, nameof(handler));

            _handlers[key] = handler;
        }

        public void RegisterRange(IEnumerable<KeyValuePair<TKey, THandler>> handlers)
        {
            PortGuard.Required(handlers, nameof(handlers));

            foreach (var kvp in handlers)
            {
                Register(kvp.Key, kvp.Value);
            }
        }

        public THandler? Get(TKey key)
        {
            if (key == null) return null;
            return _handlers.TryGetValue(key, out var handler) ? handler : null;
        }

        public bool TryGet(TKey key, out THandler? handler)
        {
            if (key == null)
            {
                handler = null;
                return false;
            }
            return _handlers.TryGetValue(key, out handler);
        }

        public bool Remove(TKey key)
        {
            if (key == null) return false;
            return _handlers.TryRemove(key, out _);
        }

        public void Clear() => _handlers.Clear();

        public IEnumerable<TKey> GetKeys() => _handlers.Keys.ToArray();

        public IEnumerable<KeyValuePair<TKey, THandler>> GetAll() => _handlers.ToArray();

        public async Task ForEachAsync(Func<THandler, Task> action)
        {
            PortGuard.Required(action, nameof(action));

            foreach (var handler in _handlers.Values)
            {
                await action(handler).ConfigureAwait(false);
            }
        }
    }
}
