using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Modules.StateMachine
{
    #region 状态机存储

    public class StateMachineStore<TState> : IStateMachineStore<TState>
        where TState : struct, Enum
    {
        private readonly ConcurrentDictionary<string, ConditionalStateMachine<TState>> _states = new(StringComparer.OrdinalIgnoreCase);

        public int MaxCapacity { get; set; } = 1000;

        public TimeSpan StateRetentionPeriod { get; set; } = TimeSpan.FromMinutes(5);

        public int Count => _states.Count;

        public IConditionalStateMachine<TState> GetOrCreate(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("ID cannot be empty", nameof(id));

            CleanupIfNeeded();

            return _states.GetOrAdd(id, _ => new ConditionalStateMachine<TState>());
        }

        public IConditionalStateMachine<TState>? Get(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;

            return _states.TryGetValue(id, out var state) ? state : null;
        }

        public bool Remove(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return false;

            return _states.TryRemove(id, out _);
        }

        public IEnumerable<string> GetActiveIds()
        {
            return _states
                .Where(kv => kv.Value.IsActive)
                .Select(kv => kv.Key);
        }

        public IEnumerable<string> GetAllIds() => _states.Keys.ToArray();

        public int CleanupFinished()
        {
            var cutoff = DateTime.UtcNow - StateRetentionPeriod;
            var finishedIds = _states
                .Where(kv =>
                {
                    var state = kv.Value;
                    return state.IsFinished && state.EndTime.HasValue && state.EndTime.Value < cutoff;
                })
                .Select(kv => kv.Key)
                .ToList();

            foreach (var id in finishedIds)
            {
                _states.TryRemove(id, out _);
            }

            return finishedIds.Count;
        }

        private void CleanupIfNeeded()
        {
            if (_states.Count >= MaxCapacity)
            {
                CleanupFinished();
            }
        }
    }

    #endregion
}
