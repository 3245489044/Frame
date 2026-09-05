
using System.Text.Json;
using System.Text.Json.Nodes;
using Plate.JsonMethod.Serialization;

namespace Plate.JsonMethod.Extensions
{
    public static class JsonExtensions
    {
        private static readonly JsonSerializationService _serializer = JsonSerializationService.Instance;


        public static T? FromJson<T>(this string json, JsonSerializerOptions? options = null)
        {
            if (options != null)
                return _serializer.Deserialize<T>(json, options);
            return _serializer.Deserialize<T>(json);
        }

        public static bool IsValidJson(this string json)
        {
            try { JsonDocument.Parse(json); return true; }
            catch { return false; }
        }

        public static string PrettyPrintJson(this string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return json;
            try
            {
                using var doc = JsonDocument.Parse(json);
                using var stream = new MemoryStream();
                using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
                doc.WriteTo(writer);
                writer.Flush();
                return System.Text.Encoding.UTF8.GetString(stream.ToArray());
            }
            catch { return json; }
        }

        public static string MinifyJson(this string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return json;
            try
            {
                using var doc = JsonDocument.Parse(json);
                using var stream = new MemoryStream();
                using var writer = new Utf8JsonWriter(stream);
                doc.WriteTo(writer);
                writer.Flush();
                return System.Text.Encoding.UTF8.GetString(stream.ToArray());
            }
            catch { return json; }
        }


        public static string ToJson<T>(this T obj, bool writeIndented = true)
        {
            var options = writeIndented
                ? JsonSerializerFactory.Contexts.PrettyPrint
                : JsonSerializerFactory.Contexts.Default;
            return _serializer.Serialize(obj, options);
        }

        public static string ToSafeJson<T>(this T obj)
        {
            try
            {
                return _serializer.Serialize(obj);
            }
            catch
            {
                return "{}";
            }
        }

        public static JsonNode? ToJsonNode<T>(this T obj)
            => JsonSerializer.SerializeToNode(obj, JsonSerializerFactory.Contexts.Default);

        public static T? DeepCloneViaJson<T>(this T obj)
        {
            var json = _serializer.Serialize(obj);
            return _serializer.Deserialize<T>(json);
        }
    }
}
