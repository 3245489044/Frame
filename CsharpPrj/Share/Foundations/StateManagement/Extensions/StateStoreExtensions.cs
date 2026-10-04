
using System.Text.Json;
using Share.StateManagement.Diagnostics;

namespace Share.StateManagement
{
    public static class StateStoreExtensions
    {
        public static T? Get<T>(this IStateStore store, string key)
        {
            PortGuard.Required(store, nameof(store));

            var value = store.Get(key);
            if (value == null)
                return default;

            try
            {
                if (typeof(T) == typeof(string))
                    return (T)(object)value;

                return JsonSerializer.Deserialize<T>(value);
            }
            catch (Exception ex)
            {
                StateManagementDiagnosticContext.Record("state_store.deserialize_failed",
                    new { key, error = ex.Message });
                return default;
            }
        }

        public static void Set<T>(this IStateStore store, string key, T value)
        {
            PortGuard.Required(store, nameof(store));

            if (value == null)
            {
                store.Set(key, null);
                return;
            }

            if (value is string strValue)
            {
                store.Set(key, strValue);
                return;
            }

            var json = JsonSerializer.Serialize(value);
            store.Set(key, json);
        }

        public static T GetOrSet<T>(this IStateStore store, string key, T defaultValue)
        {
            PortGuard.Required(store, nameof(store));

            if (store.Has(key))
            {
                var existing = store.Get(key);
                if (existing != null)
                {
                    try
                    {
                        if (typeof(T) == typeof(string))
                            return (T)(object)existing;

                        return JsonSerializer.Deserialize<T>(existing) ?? defaultValue;
                    }
                    catch (Exception ex)
                    {
                        StateManagementDiagnosticContext.Record("state_store.deserialize_failed",
                            new { key, error = ex.Message, reason = "existing_corrupt" });
                        return defaultValue;
                    }
                }
                return default!;
            }

            var stringValue = defaultValue == null ? null
                : (defaultValue is string strVal ? strVal
                : JsonSerializer.Serialize(defaultValue));

            var result = store.GetOrAdd(key, stringValue);

            if (result == null)
                return default!;

            try
            {
                if (typeof(T) == typeof(string))
                    return (T)(object)result;

                return JsonSerializer.Deserialize<T>(result) ?? defaultValue;
            }
            catch (Exception ex)
            {
                StateManagementDiagnosticContext.Record("state_store.deserialize_failed",
                    new { key, error = ex.Message, reason = "getoradd_corrupt" });
                return defaultValue;
            }
        }

        public static bool TryGet(this IStateStore store, string key, out string? value)
        {
            PortGuard.Required(store, nameof(store));

            value = store.Get(key);
            return store.Has(key);
        }

        public static bool ContainsAll(this IStateStore store, IEnumerable<string> keys)
        {
            PortGuard.Required(store, nameof(store));
            PortGuard.Required(keys, nameof(keys));

            return keys.All(k => store.Has(k));
        }

        public static Dictionary<string, string?> FindByPrefix(this IStateStore store, string prefix)
        {
            PortGuard.Required(store, nameof(store));

            if (string.IsNullOrEmpty(prefix))
                return store.GetAll();

            var result = new Dictionary<string, string?>();
            foreach (var key in store.GetAllKeys())
            {
                if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    result[key] = store.Get(key);
                }
            }
            return result;
        }

        public static void CopyTo(this IStateStore source, IStateStore destination, bool overwrite = true)
        {
            PortGuard.Required(source, nameof(source));
            PortGuard.Required(destination, nameof(destination));

            foreach (var key in source.GetAllKeys())
            {
                if (overwrite || !destination.Has(key))
                {
                    destination.Set(key, source.Get(key));
                }
            }
        }

        public static IReadOnlyDictionary<string, string?> AsReadOnlyDictionary(this IStateStore store)
        {
            PortGuard.Required(store, nameof(store));
            return store.GetAll();
        }


        public static string? GetOrDefault(this IStateStore store, string key, string? defaultValue = null)
        {
            var value = store.Get(key);
            return value ?? defaultValue;
        }

        public static bool TrySet(this IStateStore store, string key, string? value)
        {
            if (store.Has(key))
                return false;

            store.Set(key, value);
            return true;
        }

        public static bool TryUpdate(this IStateStore store, string key, string? value)
        {
            if (!store.Has(key))
                return false;

            store.Set(key, value);
            return true;
        }

        public static string? Pop(this IStateStore store, string key)
        {
            var value = store.Get(key);
            store.Remove(key);
            return value;
        }

        public static bool HasPrefix(this IStateStore store, string prefix)
        {
            return store.GetAllKeys().Any(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        public static Dictionary<string, string?> GetByPrefix(this IStateStore store, string prefix)
        {
            return store.GetAll()
                .Where(kvp => kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
        }
    }
}
