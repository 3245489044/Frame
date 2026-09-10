
using Modules.StateManagement;

namespace Share.StateManagement
{
    public class VirtualStore : IVirtualStore
    {
        private readonly StringKvStoreBacking _backing = new();

        public void SetValue(string controlName, string? value)
        {
            if (string.IsNullOrWhiteSpace(controlName))
                throw new ArgumentException("Control name cannot be empty", nameof(controlName));

            _backing.Set(controlName, value);
        }

        public string? GetValue(string controlName)
        {
            if (string.IsNullOrWhiteSpace(controlName))
                return null;

            return _backing.Get(controlName);
        }

        public bool HasControl(string controlName)
        {
            if (string.IsNullOrWhiteSpace(controlName))
                return false;

            return _backing.Has(controlName);
        }

        public void RemoveControl(string controlName)
        {
            if (!string.IsNullOrWhiteSpace(controlName))
                _backing.Remove(controlName);
        }

        public void RemoveByPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return;

            _backing.RemoveByPrefix(prefix);
        }

        public void ClearAll() => _backing.Clear();

        public IReadOnlyCollection<string> GetAllControlNames() => _backing.Keys;

        public Dictionary<string, string?> GetAllValues() => _backing.Snapshot();

        public string? GetOrAdd(string controlName, string? defaultValue)
            => _backing.GetOrAdd(controlName, defaultValue);

        public void SetRange(IEnumerable<KeyValuePair<string, string?>> values)
        {
            ArgumentNullException.ThrowIfNull(values);

            foreach (var kvp in values)
            {
                SetValue(kvp.Key, kvp.Value);
            }
        }

        public int Count => _backing.Count;

        public IReadOnlyDictionary<string, string?> AsReadOnlyDictionary()
            => _backing.Snapshot();
    }
}
