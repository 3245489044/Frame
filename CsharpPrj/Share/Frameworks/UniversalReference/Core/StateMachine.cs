
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
            // 读改写加锁：ConcurrentDictionary 只保证单次操作原子，条件迁移仍会 TOCTOU 覆盖他线程结果。
            lock (_states)
            {
                if (!_states.TryGetValue(id, out var currentState))
                    return false;

                var oldState = currentState;
                _states[id] = newState;
                OnStateChanged?.Invoke(id, oldState, newState);
                return true;
            }
        }

        public bool TryTransitionTo(string id, TState newState, Func<TState, bool> condition)
        {
            lock (_states)
            {
                if (!_states.TryGetValue(id, out var currentState) || !condition(currentState))
                    return false;

                var oldState = currentState;
                _states[id] = newState;
                OnStateChanged?.Invoke(id, oldState, newState);
                return true;
            }
        }

        public void SetState(string id, TState state)
        {
            // 统一走状态迁移语义：SetState 用于强制迁移，但仍应触发 OnStateChanged（原实现静默写入）。
            lock (_states)
            {
                if (_states.TryGetValue(id, out var oldState))
                {
                    _states[id] = state;
                    OnStateChanged?.Invoke(id, oldState, state);
                }
                else
                {
                    _states[id] = state;
                }
            }
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
