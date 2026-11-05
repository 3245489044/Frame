
using System.Collections.Concurrent;
using System.Text.Json;
using TConfig.ContextFrame.Abstractions;

namespace Modules.InternalStorage.Core;

public class StorageRegistry : IStorageRegistry
{
    private readonly ConcurrentDictionary<string, (Type Type, object? Value)> _registry = new();


    public void Register<T>(string key, T value) where T : notnull
    {
        _registry[key] = (typeof(T), value);
    }

    public T? Resolve<T>(string key) where T : notnull
    {
        return TryResolve<T>(key, out var value) ? value : default;
    }

    public bool TryResolve<T>(string key, out T? value) where T : notnull
    {
        if (_registry.TryGetValue(key, out var entry) && entry.Value is T t)
        {
            value = t;
            return true;
        }
        value = default;
        return false;
    }

    public bool Contains(string key)
    {
        return _registry.ContainsKey(key);
    }


    public void RegisterFromJson(Dictionary<string, object?> extensionData)
    {
        if (extensionData == null) return;

        foreach (var kvp in extensionData)
        {
            if (kvp.Value != null)
            {
                var valueType = kvp.Value.GetType();
                _registry[kvp.Key] = (valueType, kvp.Value);
            }
        }
    }

    public void RegisterFromJsonElement(Dictionary<string, JsonElement> extensionData)
    {
        if (extensionData == null) return;

        foreach (var kvp in extensionData)
        {
            _registry[kvp.Key] = (typeof(JsonElement), kvp.Value);
        }
    }

    public void RegisterFromJsonElement<T>(string key, JsonElement element) where T : notnull
    {
        try
        {
            var value = JsonSerializer.Deserialize<T>(element.GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (value != null)
            {
                _registry[key] = (typeof(T), value);
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"从 JsonElement 反序列化为 {typeof(T).Name} 失败（键: '{key}'）: {ex.Message}", ex);
        }
    }



    private readonly ConcurrentDictionary<string, int> _counters = new();

    public int NextId(string counterName)
    {
        return _counters.AddOrUpdate(counterName, _ => 0, (_, id) => id + 1);
    }

    public int CurrentId(string counterName)
    {
        return _counters.TryGetValue(counterName, out var id) ? id : 0;
    }

    public void ResetId(string counterName)
    {
        _counters.TryRemove(counterName, out _);
    }


    public IReadOnlyDictionary<string, Type> GetRegisteredTypes()
    {
        return _registry.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Type);
    }
}
