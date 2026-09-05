#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Modules.Common
{
    public class WeakStateManager<TKey, TState>
        where TKey : class
        where TState : class, new()
    {
        private readonly ConditionalWeakTable<TKey, TState> _table = new();

        public event Action<TKey, TState>? OnStateCreated;

        public event Action<TKey, TState>? OnStateRemoved;

        public TState GetOrCreate(TKey key)
        {
            var state = _table.GetOrCreateValue(key);
            OnStateCreated?.Invoke(key, state);
            return state;
        }

        public bool TryGet(TKey key, [MaybeNullWhen(false)] out TState state)
            => _table.TryGetValue(key, out state);

        public void Remove(TKey key)
        {
            if (_table.TryGetValue(key, out var state))
            {
                _table.Remove(key);
                OnStateRemoved?.Invoke(key, state);
            }
        }

        public void Clear()
        {
            _table.Clear();
        }

        public ConditionalWeakTable<TKey, TState> InnerTable => _table;
    }
}
