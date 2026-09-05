using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Modules.StateMachine
{
    #region 作用域状态管理器

    public class ScopeStateManager<TState> : IScopeStateManager<TState>
        where TState : struct, Enum
    {
        private readonly ConditionalStateMachine<TState> _global;
        private readonly ConcurrentDictionary<string, ConditionalStateMachine<TState>> _scopes = new(StringComparer.OrdinalIgnoreCase);

        public IConditionalStateMachine<TState> Global => _global;

        public ScopeStateManager(TState initialState = default)
        {
            _global = new ConditionalStateMachine<TState>(initialState);
        }

        public IConditionalStateMachine<TState> CreateScope(string scopeName)
        {
            if (string.IsNullOrWhiteSpace(scopeName))
                throw new ArgumentException("Scope name cannot be empty", nameof(scopeName));

            return _scopes.GetOrAdd(scopeName, _ => new ConditionalStateMachine<TState>());
        }

        public IConditionalStateMachine<TState>? GetScope(string scopeName)
        {
            if (string.IsNullOrWhiteSpace(scopeName))
                return null;

            return _scopes.TryGetValue(scopeName, out var scope) ? scope : null;
        }

        public bool ReleaseScope(string scopeName)
        {
            if (string.IsNullOrWhiteSpace(scopeName))
                return false;

            return _scopes.TryRemove(scopeName, out _);
        }

        public IReadOnlyCollection<string> GetScopeNames()
        {
            return (IReadOnlyCollection<string>)_scopes.Keys;
        }
    }

    #endregion
}
