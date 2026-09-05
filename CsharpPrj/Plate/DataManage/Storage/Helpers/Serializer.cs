using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DataManage.Storage.Helpers
{
    public interface ISerializer
    {
        string Serialize<T>(T obj);
        T Deserialize<T>(string data);
        object Deserialize(string data, Type type);
    }

    public sealed class SystemTextJsonSerializer : ISerializer
    {
        private static readonly JsonSerializerOptions DefaultOptions = new()
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly JsonSerializerOptions _options;

        public SystemTextJsonSerializer(JsonSerializerOptions? options = null)
        {
            _options = options ?? DefaultOptions;
        }

        public string Serialize<T>(T obj) => System.Text.Json.JsonSerializer.Serialize(obj, _options);

        public T Deserialize<T>(string data)
        {
            var result = System.Text.Json.JsonSerializer.Deserialize<T>(data, _options);
            if (result == null && !typeof(T).IsValueType || Nullable.GetUnderlyingType(typeof(T)) != null)
            {
                return default!;
            }
            return result ?? throw new InvalidOperationException("反序列化返回 null。");
        }

        public object Deserialize(string data, Type type)
        {
            var result = System.Text.Json.JsonSerializer.Deserialize(data, type, _options);
            if (result == null && (!type.IsValueType || Nullable.GetUnderlyingType(type) != null))
            {
                return null!;
            }
            return result ?? throw new InvalidOperationException("反序列化返回 null。");
        }
    }

    public static class Serializer
    {
        public static ISerializer Default { get; set; } = new SystemTextJsonSerializer();
    }
}