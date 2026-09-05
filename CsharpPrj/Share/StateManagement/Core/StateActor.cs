
using System.Collections.Concurrent;

namespace Share.StateManagement
{
    public abstract class StateActor : IStateActor
    {
        private readonly ConcurrentDictionary<string, object?> _states = new(StringComparer.Ordinal);

        protected StateActor(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            Name = name;
        }

        public string Name { get; }

        public virtual Task InitializeAsync() => Task.CompletedTask;

        public virtual Task ShutdownAsync() => Task.CompletedTask;

        public TState? GetState<TState>(string key)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);

            if (_states.TryGetValue(key, out var value))
            {
                return value is TState state ? state : default;
            }
            return default;
        }

        public void SetState<TState>(string key, TState value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            _states[key] = value;
        }
    }
}
