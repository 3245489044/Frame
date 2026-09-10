
using System.Collections.Concurrent;

namespace UniversalWorkflowFramework.Core
{
    public class StateMachine<TState> where TState : struct, Enum
    {
        private readonly ConcurrentDictionary<string, TState> _states = new();

        public IEnumerable<string> AllIds => _states.Keys;

        public int Count => _states.Count;

        public bool Register(string id, TState initialState)
        {
            return _states.TryAdd(id, initialState);
        }

        public TState? GetState(string id)
        {
            return _states.TryGetValue(id, out var state) ? state : null;
        }

        public bool TryTransitionTo(string id, TState newState)
        {
            if (_states.TryGetValue(id, out var currentState))
            {
                var oldState = currentState;
                _states[id] = newState;
                OnStateChanged?.Invoke(id, oldState, newState);
                return true;
            }
            return false;
        }

        public bool TryTransitionTo(string id, TState newState, Func<TState, bool> condition)
        {
            if (_states.TryGetValue(id, out var currentState) && condition(currentState))
            {
                var oldState = currentState;
                _states[id] = newState;
                OnStateChanged?.Invoke(id, oldState, newState);
                return true;
            }
            return false;
        }

        public void SetState(string id, TState state)
        {
            _states[id] = state;
        }

        public bool Remove(string id)
        {
            return _states.TryRemove(id, out _);
        }

        public int CountByState(TState state)
        {
            return _states.Values.Count(s => s.Equals(state));
        }

        public IEnumerable<string> GetIdsByState(TState state)
        {
            return _states.Where(kv => kv.Value.Equals(state)).Select(kv => kv.Key);
        }

        public bool Contains(string id)
        {
            return _states.ContainsKey(id);
        }

        public void Clear()
        {
            _states.Clear();
        }

        public event Action<string, TState, TState>? OnStateChanged;
    }
}
