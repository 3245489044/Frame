
using System.Reflection;
using System.Text.Json;

namespace Common.PlaceholderFramework.Core
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public class ConfigPropertyAttribute : Attribute
    {
        public string Path { get; }

        public object? DefaultValue { get; set; }

        public bool Required { get; set; }

        public ConfigPropertyAttribute(string path)
        {
            Path = path;
        }
    }

    public static class ConfigInjector
    {
        public static T Inject<T>(string json) where T : new()
        {
            using var doc = JsonDocument.Parse(json);
            return Inject<T>(doc);
        }

        public static T Inject<T>(JsonDocument doc) where T : new()
        {
            var instance = new T();
            InjectInto(instance, doc.RootElement);
            return instance;
        }

        public static void InjectInto<T>(T instance, string json)
        {
            using var doc = JsonDocument.Parse(json);
            InjectInto(instance, doc.RootElement);
        }

        public static void InjectInto<T>(T instance, JsonElement rootElement)
        {
            var type = typeof(T);
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var property in properties)
            {
                var attr = property.GetCustomAttribute<ConfigPropertyAttribute>();
                if (attr == null)
                    continue;

                var value = ResolveValue(rootElement, attr.Path, property.PropertyType, attr.DefaultValue);
                if (value != null)
                {
                    property.SetValue(instance, value);
                }
                else if (attr.Required)
                {
                    throw new InvalidOperationException(
                        $"Required config property '{property.Name}' not found at path '{attr.Path}'");
                }
            }
        }

        private static object? ResolveValue(JsonElement root, string path, Type targetType, object? defaultValue)
        {
            var element = NavigateTo(root, path);
            if (element == null)
                return defaultValue;

            return ConvertValue(element.Value, targetType);
        }

        private static object? ConvertValue(JsonElement element, Type targetType)
        {
            var underlyingType = Nullable.GetUnderlyingType(targetType);
            if (underlyingType != null)
            {
                if (element.ValueKind == JsonValueKind.Null)
                    return null;
                targetType = underlyingType;
            }

            if (targetType == typeof(string))
                return element.GetString() ?? string.Empty;

            if (targetType == typeof(int))
                return element.GetInt32();

            if (targetType == typeof(long))
                return element.GetInt64();

            if (targetType == typeof(double))
                return element.GetDouble();

            if (targetType == typeof(float))
                return (float)element.GetDouble();

            if (targetType == typeof(decimal))
                return element.GetDecimal();

            if (targetType == typeof(bool))
                return element.GetBoolean();

            if (targetType == typeof(short))
                return element.GetInt16();

            if (targetType == typeof(byte))
                return element.GetByte();

            if (targetType == typeof(int[]))
                return element.EnumerateArray().Select(e => e.GetInt32()).ToArray();

            if (targetType == typeof(long[]))
                return element.EnumerateArray().Select(e => e.GetInt64()).ToArray();

            if (targetType == typeof(double[]))
                return element.EnumerateArray().Select(e => e.GetDouble()).ToArray();

            if (targetType == typeof(string[]))
                return element.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToArray();

            if (targetType == typeof(bool[]))
                return element.EnumerateArray().Select(e => e.GetBoolean()).ToArray();

            if (targetType == typeof(List<int>))
                return element.EnumerateArray().Select(e => e.GetInt32()).ToList();

            if (targetType == typeof(List<double>))
                return element.EnumerateArray().Select(e => e.GetDouble()).ToList();

            if (targetType == typeof(List<string>))
                return element.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList();

            if (targetType == typeof(Dictionary<string, string>))
            {
                var dict = new Dictionary<string, string>();
                foreach (var prop in element.EnumerateObject())
                {
                    dict[prop.Name] = prop.Value.GetString() ?? string.Empty;
                }
                return dict;
            }

            try
            {
                var json = element.GetRawText();
                return JsonSerializer.Deserialize(json, targetType);
            }
            catch
            {
                return null;
            }
        }

        internal static JsonElement? NavigateTo(JsonElement root, string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            var segments = path.Split('.');
            JsonElement current = root;

            foreach (var segment in segments)
            {
                if (!current.TryGetProperty(segment, out current))
                    return null;
            }

            return current;
        }
    }
}
