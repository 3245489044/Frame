
namespace Share.StateManagement
{
    public class ObservableStateStore : IStateStore
    {
        private readonly IStateStore _innerStore;

        public event EventHandler<StateChangedEventArgs>? StateChanged;

        public ObservableStateStore(IStateStore innerStore)
        {
            _innerStore = PortGuard.Required(innerStore, nameof(innerStore));
        }

        public string? Get(string key) => _innerStore.Get(key);

        public bool Has(string key) => _innerStore.Has(key);

        public IReadOnlyCollection<string> GetAllKeys() => _innerStore.GetAllKeys();

        public int Count => _innerStore.Count;

        public bool IsEmpty => _innerStore.IsEmpty;

        public string? GetOrAdd(string key, string? defaultValue)
            => _innerStore.GetOrAdd(key, defaultValue);

        public Dictionary<string, string?> GetAll() => _innerStore.GetAll();

        private void RaiseStateChanged(StateChangedEventArgs args)
        {
            StateChanged?.Invoke(this, args);
        }

        public void Set(string key, string? value)
        {
            var oldValue = _innerStore.Get(key);
            _innerStore.Set(key, value);
            RaiseStateChanged(new StateChangedEventArgs(key, oldValue, value, StateChangeType.Set));
        }

        public void Remove(string key)
        {
            var oldValue = _innerStore.Get(key);
            _innerStore.Remove(key);
            RaiseStateChanged(new StateChangedEventArgs(key, oldValue, null, StateChangeType.Remove));
        }

        public void RemoveByPrefix(string prefix)
        {
            var keysToRemove = _innerStore.GetAllKeys()
                .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (keysToRemove.Count == 0) return;

            foreach (var key in keysToRemove)
            {
                var oldValue = _innerStore.Get(key);
                _innerStore.Remove(key);
                RaiseStateChanged(new StateChangedEventArgs(key, oldValue, null, StateChangeType.Remove));
            }
        }

        public void SetRange(IEnumerable<KeyValuePair<string, string?>> values)
        {
            PortGuard.Required(values, nameof(values));

            foreach (var kvp in values)
            {
                Set(kvp.Key, kvp.Value);
            }
        }

        public void Clear()
        {
            _innerStore.Clear();

            StateChanged?.Invoke(this, new StateChangedEventArgs("*", null, null, StateChangeType.Clear));
        }
    }
}
