
#nullable enable
using System.Collections.Concurrent;

namespace Modules.Framework
{
    public interface ILayoutStrategy<TContainer, TConfig, TContext>
    {
        string ContainerType { get; }

        void Execute(TContainer target, TConfig config, TContext context);
    }

    public class LayoutStrategyRegistry<TContainer, TConfig, TContext>
    {
        private readonly ConcurrentDictionary<string, ILayoutStrategy<TContainer, TConfig, TContext>> _strategies = new();

        public void Register(ILayoutStrategy<TContainer, TConfig, TContext> strategy)
        {
            _strategies[strategy.ContainerType] = strategy;
        }

        public bool TryGet(string key, out ILayoutStrategy<TContainer, TConfig, TContext>? strategy)
            => _strategies.TryGetValue(key, out strategy);

        public bool Unregister(string key)
            => _strategies.TryRemove(key, out _);

        public void Clear()
            => _strategies.Clear();

        public IEnumerable<string> RegisteredTypes => _strategies.Keys;

        public int Count => _strategies.Count;
    }
}
