
#nullable enable

using System.Collections.Concurrent;

namespace Modules.ComponentData.Reactor
{
    public interface IReactorWrapper
    {
        float Priority { get; }
        bool ShouldReact(object data);
        void React(object data, object? context);
    }

    public abstract class ReactorSystemBase : IDisposable
    {
        private readonly ConcurrentDictionary<Type, PriorityChain<IReactorWrapper>> _reactorChains = new();
        private volatile bool _disposed;

        protected void RegisterCore(IReactorWrapper wrapper, Type dataType)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var chain = _reactorChains.GetOrAdd(dataType, _ => new PriorityChain<IReactorWrapper>());
            chain.Add(wrapper.Priority, wrapper);
        }

        protected bool UnregisterCore(IReactorWrapper wrapper, Type dataType)
        {
            if (_reactorChains.TryGetValue(dataType, out var chain))
            {
                bool removed = chain.Remove(w => ReferenceEquals(w, wrapper));

                if (chain.Count == 0)
                    _reactorChains.TryRemove(dataType, out _);

                return removed;
            }
            return false;
        }

        protected void ExecuteCore(object data, object? context)
        {
            if (data == null) return;

            var dataType = data.GetType();

            if (_reactorChains.TryGetValue(dataType, out var chain))
            {
                chain.ForEach(wrapper =>
                {
                    try
                    {
                        if (wrapper.ShouldReact(data))
                        {
                            wrapper.React(data, context);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ReactorSystemBase] Reactor error for {dataType.Name}: {ex.Message}");
                    }
                });
            }
        }

        public void Clear()
        {
            _reactorChains.Clear();
        }

        public int ChainCount => _reactorChains.Count;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Clear();
        }
    }
}
