
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using TConfig.ContextFrame.Abstractions;

namespace ECS.Tests.Mocks;

public class MockStorageRegistry : IStorageRegistry
{
    private readonly Dictionary<string, object?> _store = new();
    private readonly Dictionary<string, int> _counters = new();
    private readonly Dictionary<string, Type> _registeredTypes = new();


    public void Register<T>(string key, T value) where T : notnull
    {
        _store[key] = value;
        _registeredTypes[key] = typeof(T);
    }

    public T? Resolve<T>(string key) where T : notnull
    {
        if (_store.TryGetValue(key, out var value) && value is T typedValue)
            return typedValue;
        return default;
    }

    public bool TryResolve<T>(string key, [MaybeNullWhen(false)] out T? value) where T : notnull
    {
        if (_store.TryGetValue(key, out var raw) && raw is T typed)
        {
            value = typed;
            return true;
        }
        value = default;
        return false;
    }

    public bool Contains(string key)
    {
        return _store.ContainsKey(key);
    }


    public void RegisterFromJson(Dictionary<string, object?> extensionData)
    {
        foreach (var kvp in extensionData)
        {
            _store[kvp.Key] = kvp.Value;
        }
    }

    public void RegisterFromJsonElement(Dictionary<string, JsonElement> extensionData)
    {
        foreach (var kvp in extensionData)
        {
            _store[kvp.Key] = kvp.Value.ToString();
        }
    }

    public void RegisterFromJsonElement<T>(string key, JsonElement element) where T : notnull
    {
        var value = JsonSerializer.Deserialize<T>(element.GetRawText());
        if (value != null)
        {
            _store[key] = value;
            _registeredTypes[key] = typeof(T);
        }
    }


    public int NextId(string counterName)
    {
        if (!_counters.ContainsKey(counterName))
            _counters[counterName] = 0;
        return ++_counters[counterName];
    }

    public int CurrentId(string counterName)
    {
        return _counters.TryGetValue(counterName, out var id) ? id : 0;
    }

    public void ResetId(string counterName)
    {
        _counters.Remove(counterName);
    }


    public IReadOnlyDictionary<string, Type> GetRegisteredTypes()
    {
        return _registeredTypes;
    }

    public void Clear()
    {
        _store.Clear();
        _counters.Clear();
        _registeredTypes.Clear();
    }
}
