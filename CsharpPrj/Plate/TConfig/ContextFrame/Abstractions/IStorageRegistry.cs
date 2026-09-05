
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace TConfig.ContextFrame.Abstractions;

public interface IStorageRegistry
{

    void Register<T>(string key, T value) where T : notnull;

    T? Resolve<T>(string key) where T : notnull;

    bool TryResolve<T>(string key, [MaybeNullWhen(false)] out T? value) where T : notnull;

    bool Contains(string key);


    void RegisterFromJson(Dictionary<string, object?> extensionData);

    void RegisterFromJsonElement(Dictionary<string, JsonElement> extensionData);

    void RegisterFromJsonElement<T>(string key, JsonElement element) where T : notnull;


    int NextId(string counterName);

    int CurrentId(string counterName);

    void ResetId(string counterName);


    IReadOnlyDictionary<string, Type> GetRegisteredTypes();
}
