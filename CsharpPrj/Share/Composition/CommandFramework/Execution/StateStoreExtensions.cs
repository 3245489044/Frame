
using System.Text.Json;
using CommandFramework.Abstractions;

namespace CommandFramework.Execution;

public static class StateStoreExtensions
{
    private static string? SerializeValue<T>(T value)
    {
        if (value == null) return null;
        if (typeof(T) == typeof(string)) return (string)(object)value;
        return JsonSerializer.Serialize(value);
    }

    private static bool TryDeserialize<T>(string? value, out T? result)
    {
        result = default;
        if (value == null) return false;
        if (typeof(T) == typeof(string)) { result = (T)(object)value; return true; }
        try { result = JsonSerializer.Deserialize<T>(value); return true; }
        catch { return false; }
    }

    public static T? Get<T>(this IStateStore store, string key)
    {
        ArgumentNullException.ThrowIfNull(store);
        return TryDeserialize<T>(store.Get(key), out var result) ? result : default;
    }

    public static void Set<T>(this IStateStore store, string key, T value)
    {
        ArgumentNullException.ThrowIfNull(store);
        store.Set(key, SerializeValue(value));
    }

    public static T GetOrSet<T>(this IStateStore store, string key, T defaultValue)
    {
        ArgumentNullException.ThrowIfNull(store);

        var stringValue = SerializeValue(defaultValue);
        var result = store.GetOrAdd(key, stringValue);

        return TryDeserialize<T>(result, out var deserialized) ? deserialized! : defaultValue;
    }

    public static void SetRange(this IStateStore store, IEnumerable<KeyValuePair<string, string?>> values)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(values);

        foreach (var kvp in values)
        {
            store.Set(kvp.Key, kvp.Value);
        }
    }

    public static Dictionary<string, string?> GetAll(this IStateStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        var result = new Dictionary<string, string?>();
        foreach (var key in store.GetAllKeys())
        {
            result[key] = store.Get(key);
        }
        return result;
    }
}

public class StateChangedEventArgs : EventArgs
{
    public string Key { get; }

    public string? OldValue { get; }

    public string? NewValue { get; }

    public StateChangeType ChangeType { get; }

    public StateChangedEventArgs(string key, string? oldValue, string? newValue, StateChangeType changeType)
    {
        Key = key;
        OldValue = oldValue;
        NewValue = newValue;
        ChangeType = changeType;
    }
}

public enum StateChangeType
{
    Set,
    Remove,
    Clear
}

public class ObservableStateStore : IStateStore
{
    private readonly IStateStore _innerStore;
    private readonly object _eventLock = new();

    public event EventHandler<StateChangedEventArgs>? StateChanged;

    public ObservableStateStore(IStateStore innerStore)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
    }

    public string? Get(string key) => _innerStore.Get(key);

    public bool Has(string key) => _innerStore.Has(key);

    public IReadOnlyCollection<string> GetAllKeys() => _innerStore.GetAllKeys();

    public int Count => _innerStore.Count;

    public string? GetOrAdd(string key, string? defaultValue)
    {
        return _innerStore.GetOrAdd(key, defaultValue);
    }

    private void RaiseStateChanged(StateChangedEventArgs args)
    {
        var handler = StateChanged;
        handler?.Invoke(this, args);
    }

    public void Set(string key, string? value)
    {
        StateChangedEventArgs args;
        lock (_eventLock)
        {
            var oldValue = _innerStore.Get(key);
            _innerStore.Set(key, value);
            args = new StateChangedEventArgs(key, oldValue, value, StateChangeType.Set);
        }
        RaiseStateChanged(args);
    }

    public void Remove(string key)
    {
        StateChangedEventArgs args;
        lock (_eventLock)
        {
            var oldValue = _innerStore.Get(key);
            _innerStore.Remove(key);
            args = new StateChangedEventArgs(key, oldValue, null, StateChangeType.Remove);
        }
        RaiseStateChanged(args);
    }

    public void RemoveByPrefix(string prefix)
    {
        List<StateChangedEventArgs> eventArgsList;
        lock (_eventLock)
        {
            var keysToRemove = _innerStore.GetAllKeys()
                .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            eventArgsList = new List<StateChangedEventArgs>(keysToRemove.Count);
            foreach (var key in keysToRemove)
            {
                var oldValue = _innerStore.Get(key);
                _innerStore.Remove(key);
                eventArgsList.Add(new StateChangedEventArgs(key, oldValue, null, StateChangeType.Remove));
            }
        }

        foreach (var args in eventArgsList)
        {
            RaiseStateChanged(args);
        }
    }

    public void Clear()
    {
        StateChangedEventArgs args;
        lock (_eventLock)
        {
            _innerStore.Clear();
            args = new StateChangedEventArgs("*", null, null, StateChangeType.Clear);
        }
        RaiseStateChanged(args);
    }
}
