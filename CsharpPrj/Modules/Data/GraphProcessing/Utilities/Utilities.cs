
#nullable enable

using System.Text.Json;

namespace Modules.GraphProcessing.Utilities
{
    public static class JsonOperations
    {
        private static readonly JsonSerializerOptions _options = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public static string Serialize<T>(T obj)
            => JsonSerializer.Serialize(obj, _options);

        public static T? Deserialize<T>(string json)
            => JsonSerializer.Deserialize<T>(json, _options);

        public static bool TryDeserialize<T>(string json, out T? result)
        {
            try
            {
                result = Deserialize<T>(json);
                return result != null;
            }
            catch
            {
                result = default;
                return false;
            }
        }
    }

    public static class ReferenceResolver
    {
        public static object? ResolvePath(object root, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return root;

            var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
            object? current = root;

            foreach (var segment in segments)
            {
                if (current == null)
                    return null;

                var type = current.GetType();
                var prop = type.GetProperty(segment);
                if (prop == null)
                {
                    var field = type.GetField(segment);
                    if (field == null)
                        return null;
                    current = field.GetValue(current);
                }
                else
                {
                    current = prop.GetValue(current);
                }
            }

            return current;
        }

        public static bool TryResolvePath<T>(object root, string path, out T? value)
        {
            var result = ResolvePath(root, path);
            if (result is T typed)
            {
                value = typed;
                return true;
            }
            value = default;
            return false;
        }
    }
}
