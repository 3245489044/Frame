
using System.Collections.Concurrent;

namespace Common.PlaceholderFramework.SchemaValidation;

public class SchemaRegistry<TKey>
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, JsonSchema> _schemas = new();
    private readonly TKey _defaultKey;

    public SchemaRegistry(TKey defaultKey)
    {
        _defaultKey = defaultKey;
    }

    public void Register(TKey key, JsonSchema schema)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        if (schema == null)
            throw new ArgumentNullException(nameof(schema));

        _schemas[key] = schema;
    }

    public JsonSchema GetSchema(TKey key)
    {
        if (key == null)
            return GetDefaultSchema();

        return _schemas.TryGetValue(key, out var schema)
            ? schema
            : GetDefaultSchema();
    }

    public JsonSchema GetDefaultSchema()
    {
        return _schemas.TryGetValue(_defaultKey, out var schema)
            ? schema
            : JsonSchema.CreateDefault();
    }

    public bool HasSchema(TKey key)
    {
        return key != null && _schemas.ContainsKey(key);
    }

    [Obsolete("请直接使用 HasSchema(TKey) 替代，IsValidKey 的语义与 HasSchema 重复")]
    public bool IsValidKey(TKey key)
    {
        return HasSchema(key) && !Equals(key, _defaultKey);
    }

    public IEnumerable<TKey> GetRegisteredKeys()
    {
        return _schemas.Keys.Where(k => !Equals(k, _defaultKey));
    }

    public void Clear()
    {
        _schemas.Clear();
    }

    public int Count => _schemas.Count;
}
